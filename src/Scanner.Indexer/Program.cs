/*
 * ROBLOX MODEL SCANNER — CATALOGUE INDEXER HOST
 * ============================================================================
 * PURPOSE
 *   Migrates the scanner's JSON catalogue into the SQLite index and is the only
 *   writer of that database. It exists as its own service so that the publishing
 *   API can open the index read-only: that split removes lock contention between
 *   a migration and a request, makes it impossible for a request to corrupt the
 *   index, and keeps an expensive full migration off the request path.
 *
 * EXPOSURE
 *   The service has two very different halves:
 *     1. The migration loop, which touches only the file system.
 *     2. An internal control plane on /internal/{health,status,rebuild}, which is
 *        what an operator or the publishing API calls.
 *   Only the second half is reachable over a socket, and it is deliberately hard
 *   to reach:
 *     - It binds a configured address, loopback by default. A wildcard bind is
 *       refused unless the operator explicitly opts in.
 *     - The socket peer must fall inside the configured CIDR allow-list. The peer
 *       address is read from the connection and never from a forwarding header,
 *       because a header is attacker-controlled.
 *     - Every request must carry X-Indexer-Token, compared over its raw bytes in
 *       fixed time. A token shorter than 32 characters disables the surface
 *       rather than being accepted.
 *     - Requests are rate limited per peer, so a buggy caller cannot keep the
 *       writer saturated.
 *     - When no token is configured the endpoints answer 404, so an unconfigured
 *       deployment does not advertise the surface.
 *   No endpoint accepts a path or returns catalogue data, so the control plane
 *   cannot be used to read files or exfiltrate the index.
 *
 * DEPLOYMENT
 *   This service is never published through the public edge. Do not add it to the
 *   reverse proxy or tunnelled hostnames that front rms.vtilserver.com and
 *   rms-api.vtilserver.com; it is meant to be reached only from the host itself
 *   or from the private network.
 *
 * OPERATING NOTES
 *   - Set SCANNER_INDEXER_TOKEN to a random value of at least 32 characters.
 *   - Point SCANNER_MODELS_PATH at the catalogue if it does not sit in Files/.
 *   - Point SCANNER_INDEX_PATH at the index if it should live elsewhere; the
 *     publishing API must be given the same path.
 *   - Set SCANNER_INDEXER_NETWORKS when the control plane must be reachable from
 *     a private subnet, for example "127.0.0.1/32;10.20.0.0/16".
 */

using System.Net;

using Scanner.Indexer.Enums;
using Scanner.Indexer.Extensions;
using Scanner.Indexer.Filters;
using Scanner.Indexer.Implementation;
using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Models;
using Scanner.Indexer.Options;

using System.Text.Json;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

/*
 * The listener is configured from raw configuration rather than from the bound
 * options object, because the address has to be known before the host is built.
 */
IConfigurationSection indexerSection = builder.Configuration.GetSection(IndexerOptions.SectionName);
string configuredBindAddress = indexerSection["BindAddress"] ?? "127.0.0.1";
int controlPort = int.TryParse(indexerSection["ControlPort"], out int parsedPort) && parsedPort is > 0 and <= 65535
    ? parsedPort
    : 5081;

IPAddress bindAddress = ResolveBindAddress(configuredBindAddress, builder.Environment.IsDevelopment());

builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP/1 only: the control plane is called by a local process or an internal
    // tool, and terminating TLS here would imply it is reachable from somewhere
    // that needs it. Anything crossing a network boundary must be tunnelled.
    options.Listen(bindAddress, controlPort, listen => listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);

    // The control plane has no bodies; refusing large ones removes a class of
    // memory-pressure request before any handler sees it.
    options.Limits.MaxRequestBodySize = 4 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScannerIndexer(builder.Configuration);

/* Enum values are published by name so a rule category or an outcome is readable in a log. */
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

WebApplication app = builder.Build();

/*
 * The rate limiter must be in the pipeline for the policy attached to the
 * control-plane endpoints to have any effect.
 */
app.UseRateLimiter();

/*
 * A probe endpoint outside the guarded group, so a supervisor can tell the
 * process is alive without holding the control-plane token. It reports nothing
 * about the catalogue, the index or the configuration.
 */
app.MapGet("/", static () => Results.Ok(new { service = "scanner-indexer", status = "ok" }))
    .WithName("GetIndexerRoot")
    .WithSummary("Reports that the indexing service process is running.");

/*
 * The internal control plane. Every endpoint in this group runs the guard filter
 * before its handler, and every one is rate limited per peer.
 */
RouteGroupBuilder internalApi = app
    .MapGroup("/internal")
    .AddEndpointFilter<InternalCallFilter>()
    .RequireRateLimiting(ServiceCollectionExtensions.InternalRateLimitPolicy);

internalApi.MapGet("/health", static (IIndexWriter writer) =>
        Results.Ok(new { status = "ok", schemaVersion = writer.GetSchemaVersion() }))
    .WithName("GetIndexerHealth")
    .WithSummary("Reports that the control plane is reachable and the schema is understood.");

/*
 * The status surface the publishing API relays and an operator watches: whether
 * the catalogue exists, how big it is, what the index reflects, whether the two
 * disagree, and how the last migration attempt ended. Distinguishing "nothing
 * changed" from "every attempt is failing" is the whole point of reporting the
 * failure text alongside the staleness flag.
 */
internalApi.MapGet("/status", static (IIndexStatusProvider status) => Results.Ok(status.GetStatus()))
    .WithName("GetIndexerStatus")
    .WithSummary("Reports the current catalogue revision, index revision and migration health.");

/*
 * Inspect the catalogue immediately instead of waiting for the next tick. The
 * index is only rewritten when the catalogue revision actually differs, so this
 * cannot be used to force needless work.
 */
internalApi.MapPost("/rebuild", static async (IIndexWriter writer, CancellationToken cancellationToken) =>
    {
        IndexBuildOutcome outcome = await writer
            .EnsureCurrentAsync(ignoreThrottle: true, cancellationToken)
            .ConfigureAwait(false);

        int statusCode = outcome switch
        {
            IndexBuildOutcome.Busy => StatusCodes.Status409Conflict,
            IndexBuildOutcome.CatalogueInvalid or IndexBuildOutcome.StorageFailed => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status200OK
        };

        return Results.Json(new { outcome = outcome.ToString() }, statusCode: statusCode);
    })
    .WithName("RequestIndexRebuild")
    .WithSummary("Inspects the catalogue now and migrates it if it has changed.");

/*
 * Report the guard's state at start-up. A disabled control plane is a deployment
 * mistake that would otherwise surface only as an unexplained 404, and the reason
 * is safe to log because it never contains the token itself.
 */
IInternalCallGuard guard = app.Services.GetRequiredService<IInternalCallGuard>();
if (!guard.IsEnabled)
{
    app.Logger.LogWarning(
        "The internal control plane is disabled: {Reason} The migration loop still runs, so the index stays " +
        "current, but nothing can query or nudge this service.",
        guard.DisabledReason);
}
else
{
    app.Logger.LogInformation(
        "The internal control plane is listening on {Address}:{Port} and is restricted to the configured networks.",
        bindAddress,
        controlPort);
}

app.Run();

/*
 * Resolve the listener address, refusing a wildcard unless the operator has
 * explicitly asked for one.
 *
 * A wildcard bind puts the control plane on every network the host is attached
 * to, including any that are not meant to see it. The token and the allow-list
 * still protect it, but a defence that depends on two other defences is not a
 * defence, so the default is to refuse and tell the operator which variable
 * unlocks the behaviour if that is truly what they want.
 */
static IPAddress ResolveBindAddress(string configured, bool isDevelopment)
{
    string value = configured.Trim();

    if (value is "0.0.0.0" or "::" or "*" or "+")
    {
        bool optedIn = IsTruthy(Environment.GetEnvironmentVariable("SCANNER_INDEXER_ALLOW_WILDCARD"));

        if (!optedIn)
        {
            return IPAddress.Loopback;
        }
    }

    // A development machine may legitimately name the loopback host.
    if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return IPAddress.Loopback;
    }

    return IPAddress.TryParse(value, out IPAddress? address) ? address : IPAddress.Loopback;
}

/*
 * Interpret an environment variable as an explicit opt-in. Only a positive
 * spelling counts: an unset or unrecognised value must leave the safe default in
 * place, because an operator who typed something unexpected did not consent.
 */
static bool IsTruthy(string? value) =>
    value is not null &&
    (value.Equals("1", StringComparison.Ordinal) ||
     value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
     value.Equals("yes", StringComparison.OrdinalIgnoreCase));
