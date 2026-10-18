/*
 * ROBLOX MODEL SCANNER — MICROAPI HOST
 * ============================================================================
 * PURPOSE
 *   Composition root for the scanner's HTTP API. It wires the service
 *   registrations, applies the API-key gate to every endpoint, and exposes the
 *   catalogue, analytics, rule-reference, download and scanner-control surfaces.
 *
 * COMPATIBILITY
 *   The published contract is preserved exactly: /api/health, /api/models and
 *   /api/scanner/{status,pause,resume,stop} keep their paths, their query
 *   parameters and their response shapes, and every one of them still requires
 *   the `apiKey` query parameter. Analytics, rule reference and download
 *   resolution are additive.
 *
 * SECURITY MODEL
 *   Callers supply a GUID in the `apiKey` query parameter. The configured secret
 *   comes from Scanner:ApiKey or the SCANNER_API_KEY environment variable, never
 *   from the committed settings file. The comparison runs over the sixteen raw
 *   bytes of the parsed GUID with a fixed-time routine, so a guess cannot be
 *   narrowed by timing and a difference in GUID formatting cannot change the
 *   outcome. Because query strings are recorded by proxies and access logs, the
 *   request line is kept out of this process's own logging, and the browser-facing
 *   Web Forms application proxies these calls server-side so the secret is never
 *   embedded into client-side JavaScript.
 *
 *   Forwarding headers are believed only from the addresses on the configured
 *   trusted list, and the request logger that would otherwise record the whole
 *   URL including the key is silenced. Every endpoint is rate limited per caller
 *   address. HSTS is sent on any response this process serves over HTTPS, though
 *   the public name's HSTS policy belongs to the edge that terminates TLS for it.
 *
 * DATA MODEL AND STORAGE
 *   The indexing service migrates the scanner's JSON catalogue into SQLite and is
 *   the only writer of that database. This process opens it read-only and creates
 *   nothing, so a request can never wait for a migration, corrupt the index or set
 *   one building. An index that is absent, unreadable or built to an unfamiliar
 *   schema is refused with a sentence naming the reason rather than repaired.
 *   Pause, resume and stop are communicated through an atomically replaced JSON
 *   control file that the scanner process polls.
 *
 * OPERATING NOTES
 *   - Point Scanner:ModelsPath at the catalogue and Scanner:IndexPath at the index
 *     database if they do not sit in Files/.
 *   - Set Scanner:ApiKey or SCANNER_API_KEY to a freshly generated GUID and
 *     configure the same value in the website's server-side settings.
 *   - Set SCANNER_TRUSTED_PROXIES to the edge's ranges, or to "none" when nothing
 *     is in front of this process; the default is Cloudflare's published ranges.
 *   - Set ROBLOX_COOKIE so download resolution can authenticate; Roblox requires
 *     a session for the asset delivery endpoints.
 *   - Restrict network access to trusted callers and require HTTPS off-loopback.
 */

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using RobloxModelScanner.Features.AssetDelivery;

using Scanner.MicroApi.Enums;
using Scanner.MicroApi.Exceptions;
using Scanner.MicroApi.Extensions;
using Scanner.MicroApi.Filters;
using Scanner.MicroApi.Implementation;
using Scanner.MicroApi.Interfaces;
using Scanner.MicroApi.Models;
using Scanner.MicroApi.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

/*
 * The framework's hosting logger writes the request line, and the request line carries the API key in
 * the query string. Suppressing that category removes those lines while leaving every other category --
 * including the failures an operator actually reads -- at the configured level. Nothing else in this
 * process logs a query string: the endpoints log their own messages, and the framework's diagnostics
 * middleware logs the exception without the URL.
 */
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

/*
 * The banner names the server. It is one of the two facts a caller needs to start guessing at a version
 * to exploit, and this process is behind an edge that does not pass response headers from the origin's
 * name anyway, so the header has no value here.
 */
builder.WebHost.ConfigureKestrel(static options => options.AddServerHeader = false);

/*
 * HSTS is configured so that any response this process serves directly over HTTPS tells a browser never
 * to try it over HTTP again. The public name is terminated at the edge, which is where the header that
 * matters for it is produced; this covers the case of an operator or a monitor reaching the origin
 * itself. Subdomains are deliberately not included and preloading is deliberately not requested: both
 * make the promise harder to withdraw than a deployment on a machine that may be re-purposed deserves.
 */
builder.Services.AddHsts(static options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

/*
 * Register the API's services. Secrets are pulled from the environment when the
 * configuration section leaves them empty, so nothing sensitive has to be
 * committed to appsettings.json.
 */
builder.Services.AddScannerApi(builder.Configuration);

/*
 * Serialize responses as camelCase, which is what the website's API client
 * already expects, and publish enum values by name. A rule category or severity
 * that arrives as "Obfuscator" is self-describing in a browser or a log, where a
 * bare integer would require the reader to hold the enum's numbering in mind.
 */
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

WebApplication app = builder.Build();

/*
 * Resolve the trusted-proxy list now, before anything is listening. A malformed entry stops the process
 * with a message naming it, which is where a mistake in a security-relevant list belongs: the service is
 * not yet serving, so no request has been judged on the strength of a list that was quietly ignored.
 */
ForwardedHeadersOptions forwarded = app.Services
    .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
    .Value;

/*
 * Forwarding headers first, so that everything after it -- the rate limiter's partition key, the scheme
 * HSTS reads, every log line about a caller -- sees the real client rather than the edge.
 */
app.UseForwardedHeaders();
app.UseHsts();

/*
 * Compressing the catalogue, analytics and rule payloads cuts what the website
 * proxy has to move per page render, and these responses are highly repetitive
 * JSON. EnableForHttps is required because response compression is otherwise
 * skipped on encrypted connections, which is the only kind a real deployment has.
 */
app.UseResponseCompression();

/*
 * The limit is per caller address and covers the whole API, including the health probe. Leaving the
 * probe unlimited would let it be the one endpoint a caller can loop, and a monitor that cannot tell a
 * refused probe from a slow one is not much of a monitor.
 */
app.UseRateLimiter();

/*
 * An endpoint that needs the index is answered with a reason when the index is not readable, instead of
 * the framework's generic server error. It sits here, before the endpoints, because it is a property of
 * every endpoint that reads the index rather than a decision each one makes for itself.
 */
app.UseMiddleware<IndexUnavailableMiddleware>();

/*
 * Every endpoint sits behind the key gate. A deployment that starts without a
 * configured key therefore rejects all traffic rather than silently serving the
 * catalogue to anyone who can reach the port.
 */
RouteGroupBuilder api = app
    .MapGroup("/api")
    .AddEndpointFilter<ApiKeyEndpointFilter>()
    .RequireRateLimiting(ServiceCollectionExtensions.RateLimitPolicyName);

/* Liveness probe for the deployment's health check. */
api.MapGet("/health", static () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth")
    .WithSummary("Reports that the API process is serving requests.");

/*
 * The published rule reference: what rules exist, what they look for, the score
 * they contribute and the category and severity they carry. It is projected from
 * the same registry the scanner evaluates, so the documentation cannot drift
 * away from the behaviour.
 */
api.MapGet("/rules", static (IRuleCatalogService rules) => Results.Ok(rules.GetRules()))
    .WithName("ListDetectionRules")
    .WithSummary("Lists every detection rule with its score, category, severity and purpose.");

/*
 * The analytics view of the indexed catalogue: totals, the per-rule breakdown
 * including rules that never matched, category roll-ups, a score histogram, the
 * discovery timeline and the creator leaderboard. Every figure is an indexed
 * aggregate, so the cost does not grow with the catalogue.
 */
api.MapGet("/analytics/summary", static async (IAnalyticsService analytics, CancellationToken cancellationToken) =>
        Results.Ok(await analytics.GetSummaryAsync(cancellationToken).ConfigureAwait(false)))
    .WithName("GetAnalyticsSummary")
    .WithSummary("Returns aggregate analytics over the indexed catalogue.");

/*
 * Search and paginate the catalogue. Filtering happens inside the database, so
 * `total` describes the complete matching set rather than the current page.
 * Sorting is honoured through a closed set of column names: `sortBy` can only
 * select a known column and can never be concatenated into the statement, and
 * the page size is clamped so a single caller cannot request an unbounded slice.
 *
 * `q` is the historical parameter name and `search` is accepted as an alias, so
 * existing callers keep working while new ones can use the clearer name.
 */
api.MapGet("/models", static async (
        IModelCatalogRepository repository,
        CancellationToken cancellationToken,
        string? q,
        string? search,
        bool? hasScripts,
        bool? hasObfuscation,
        string? ruleTag,
        int page = 1,
        int pageSize = 50,
        string? sortBy = null,
        string? sortDirection = null) =>
    {
        string? term = string.IsNullOrWhiteSpace(search) ? q : search;

        ModelQuery query = ModelQuery.Normalize(
            term,
            hasScripts,
            hasObfuscation,
            ruleTag,
            page,
            pageSize,
            ParseSortField(sortBy),
            ParseDescending(sortDirection));

        return Results.Ok(await repository.SearchAsync(query, cancellationToken).ConfigureAwait(false));
    })
    .WithName("SearchModels")
    .WithSummary("Searches, sorts and pages the model catalogue.");

/*
 * Resolve an asset's current download location. Roblox requires an authenticated
 * session for asset delivery and answers with a signed, time-limited content URL,
 * so the location is resolved on demand and never cached. The asset must already
 * be in the catalogue: that check is what stops the endpoint from being usable as
 * an open proxy for arbitrary Roblox asset identifiers.
 *
 * By default the caller is redirected to the resolved location, which is what a
 * browser link needs. Passing redirect=false returns the location as JSON, which
 * is what a script wants.
 */
api.MapGet("/models/{model:long}/download", static async (
        long model,
        bool? redirect,
        IModelCatalogRepository repository,
        IAssetDeliveryResolver resolver,
        IOptions<ScannerOptions> options,
        CancellationToken cancellationToken) =>
    {
        ModelEntry? entry = await repository.GetByIdAsync(model, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Results.NotFound(new
            {
                error = "not_indexed",
                message = "The asset is not present in the catalogue, so no download link can be issued for it."
            });
        }

        AssetDeliveryResult result = await resolver.ResolveAsync(model, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            /*
             * Upstream failures are surfaced as a bad gateway rather than as an empty result, so a caller
             * can tell "Roblox would not give us the asset" apart from "the asset is not indexed".
             *
             * The word beside the sentence separates the one cause this service knows about from the ones
             * only Roblox knows: a session Roblox would not accept reads the same from here whether there
             * was no cookie to send or a cookie that has stopped working, and the remedies are different
             * enough that guessing would send an operator to the wrong place. So when no cookie is
             * configured at all, that is what the caller is told.
             */
            bool configured = !string.IsNullOrWhiteSpace(options.Value.RobloxCookie);

            return Results.Json(
                new
                {
                    error = configured ? result.FailureCode ?? "unavailable" : "nocookie",
                    message = configured
                        ? result.FailureReason
                        : "This API was started without a Roblox session cookie, which asset delivery has "
                          + "required since April 2025.",
                },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return redirect == false
            ? Results.Ok(new { model, location = result.Location })
            : Results.Redirect(result.Location!);
    })
    .WithName("ResolveModelDownload")
    .WithSummary("Resolves the signed download location for an indexed asset.");

/*
 * Read the persisted control state. This only reports intent; whether a pause or
 * stop has taken effect depends on the scanner reaching its next poll, so the
 * catalogue's presence is reported alongside it to make the distinction visible.
 * The index is reported the same way: "the scanner has produced nothing yet" and
 * "the indexer has not run yet" look identical from a caller's side and have
 * different fixes.
 */
api.MapGet("/scanner/status", static async (
        IScannerControlService control,
        IScannerIndexReader reader,
        CancellationToken cancellationToken) =>
    {
        ControlState state = await control.ReadAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            paused = state.Paused,
            stopRequested = state.StopRequested,
            modelsFile = reader.Paths.ModelsPath,
            catalogueExists = reader.CatalogueExists,
            indexFile = reader.Paths.IndexPath,
            indexExists = reader.IndexExists
        });
    })
    .WithName("GetScannerStatus")
    .WithSummary("Reports the persisted scanner control state.");

/* Control endpoints persist intent; the scanner process performs the action. */
api.MapPost("/scanner/pause", static (IScannerControlService control, CancellationToken cancellationToken) =>
        SetControlAsync(control, cancellationToken, paused: true, stopRequested: false))
    .WithName("PauseScanner")
    .WithSummary("Requests that the scanner wait before starting its next batch.");

api.MapPost("/scanner/resume", static (IScannerControlService control, CancellationToken cancellationToken) =>
        SetControlAsync(control, cancellationToken, paused: false, stopRequested: false))
    .WithName("ResumeScanner")
    .WithSummary("Clears a pending pause so the scanner continues.");

api.MapPost("/scanner/stop", static (IScannerControlService control, CancellationToken cancellationToken) =>
        SetControlAsync(control, cancellationToken, paused: false, stopRequested: true))
    .WithName("StopScanner")
    .WithSummary("Requests that the scanner terminate once in-flight work completes.");

/*
 * A deployment without a usable key rejects every authenticated request. Saying so
 * at start-up turns a confusing 401 into an actionable message.
 */
IApiKeyValidator keyValidator = app.Services.GetRequiredService<IApiKeyValidator>();
if (!keyValidator.IsConfigured)
{
    app.Logger.LogWarning(
        "No usable API key is configured. Set Scanner:ApiKey or the {Variable} environment variable to a GUID; " +
        "until then every request is rejected.",
        "SCANNER_API_KEY");
}

/*
 * What the trusted-proxy list resolved to is worth saying out loud. An empty list is a legitimate
 * configuration in front of no proxy and also what a deployment ends up with after an edge changes its
 * published ranges, and the two look identical from the outside: the rate limit starts counting the
 * edge instead of the caller. One line at start-up is enough to tell them apart.
 */
app.Logger.LogInformation(
    "Trusting forwarding headers from {TrustedProxyCount} network(s).",
    forwarded.KnownNetworks.Count);

/*
 * An unreadable index is not a reason to refuse to start. The indexer may simply not have run yet, and
 * a process that exits would leave the site showing a connection refusal rather than the sentence that
 * explains what to do. The condition is still reported here so that it appears once, at start-up, and
 * not only in the body of a refused request.
 */
IScannerIndexReader indexReader = app.Services.GetRequiredService<IScannerIndexReader>();
if (!indexReader.IndexExists)
{
    app.Logger.LogWarning(
        "No index exists at {IndexPath}. Every endpoint that reads the catalogue will answer 503 until " +
        "the indexing service has run.",
        indexReader.Paths.IndexPath);
}
else
{
    try
    {
        using SqliteConnection probe = indexReader.OpenConnection();
    }
    catch (IndexUnavailableException exception)
    {
        app.Logger.LogWarning(
            "The index at {IndexPath} cannot be read: {Reason}",
            indexReader.Paths.IndexPath,
            exception.Message);
    }
}

/*
 * The asset delivery resolver is checked too, because a deployment that omits the
 * session cookie still serves the catalogue but silently fails every download.
 */
ScannerOptions scannerOptions = app.Services.GetRequiredService<IOptions<ScannerOptions>>().Value;
if (string.IsNullOrWhiteSpace(scannerOptions.RobloxCookie))
{
    app.Logger.LogWarning(
        "No Roblox session cookie is configured. Catalogue and analytics endpoints work, but download " +
        "resolution will fail because Roblox requires an authenticated session for asset delivery.");
}

app.Run();

/*
 * Persist a control state and echo exactly what was stored. Echoing the persisted
 * value rather than the requested one means a caller cannot be misled by a failed
 * write that still returned a success-shaped body.
 */
static async Task<IResult> SetControlAsync(
    IScannerControlService control,
    CancellationToken cancellationToken,
    bool paused,
    bool stopRequested)
{
    ControlState state = await control
        .WriteAsync(new ControlState(paused, stopRequested), cancellationToken)
        .ConfigureAwait(false);

    return Results.Ok(new { paused = state.Paused, stopRequested = state.StopRequested });
}

/*
 * Translate the `sortBy` parameter into a whitelisted field. An unknown value
 * falls back to the asset identifier, which is the historical ordering, rather
 * than producing a bad-request response that would break a caller over a
 * cosmetic parameter.
 */
static ModelSortField ParseSortField(string? sortBy) =>
    Enum.TryParse(sortBy, ignoreCase: true, out ModelSortField field) ? field : ModelSortField.Model;

/*
 * Translate the `sortDirection` parameter. An absent direction keeps the
 * historical newest-first ordering; anything that is not "asc" is treated as
 * descending.
 */
static bool ParseDescending(string? sortDirection) =>
    sortDirection is null || !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
