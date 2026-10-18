using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

using RobloxModelScanner.Features.AssetDelivery;

using Scanner.MicroApi.Implementation;
using Scanner.MicroApi.Interfaces;
using Scanner.MicroApi.Models;
using Scanner.MicroApi.Options;

/*
 * The framework's forwarded-header options take the overrides assembly's own network type, and the
 * platform now has a second type of the same name. The alias says which one this file means everywhere.
 */
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Scanner.MicroApi.Extensions;

/// <summary>
/// Registers the scanner API services on the application's service collection.
/// </summary>
/// <remarks>
/// <para>
/// Registration is kept in one place so the composition root stays a readable description of the
/// endpoints rather than a list of dependencies. Everything the API needs is a singleton: the index
/// reader holds a connection string, the rule catalogue is immutable, and the control file is guarded
/// internally, so per-request lifetimes would only add overhead.
/// </para>
/// <para>
/// Secrets are deliberately not read from the committed settings file. The configuration section may
/// leave them empty, in which case they are taken from the environment, which is the only place a
/// credential should live on a deployed host. On a deployment the key arrives through a file that the
/// environment names instead, so that the value is not also written into the service definition, which
/// is a file anything able to read the deployment directory can read.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The application-relative directory that holds the scanner's working files.
    /// </summary>
    private const string FilesDirectoryName = "Files";

    /// <summary>
    /// The file name of the scanner's JSON catalogue.
    /// </summary>
    private const string CatalogueFileName = "models.json";

    /// <summary>
    /// The file name of the SQLite read model.
    /// </summary>
    private const string IndexFileName = "scanner-index.db";

    /// <summary>
    /// The file name of the scanner's control file.
    /// </summary>
    private const string ControlFileName = "control.json";

    /// <summary>
    /// The <c>Set-Cookie</c>-style header name used to carry the Roblox session.
    /// </summary>
    private const string CookieHeaderName = "Cookie";

    /// <summary>
    /// The name of the Roblox session cookie.
    /// </summary>
    private const string RobloxSessionCookieName = ".ROBLOSECURITY";

    /// <summary>
    /// The user agent sent to Roblox. Identifying the caller is what allows a deployment to be traced
    /// should Roblox ever need to contact the operator about its traffic.
    /// </summary>
    private const string ProductUserAgent = "RobloxModelScanner/1.0 (+asset-detection)";

    /// <summary>
    /// The environment variable that overrides the configured API key.
    /// </summary>
    private const string ApiKeyEnvironmentVariable = "SCANNER_API_KEY";

    /// <summary>
    /// The environment variable naming a file the API key is read from.
    /// </summary>
    private const string ApiKeyFileEnvironmentVariable = "SCANNER_API_KEY_FILE";

    /// <summary>
    /// The environment variable that supplies the Roblox session cookie.
    /// </summary>
    private const string RobloxCookieEnvironmentVariable = "ROBLOX_COOKIE";

    /// <summary>
    /// The environment variable naming a file the Roblox session cookie is read from.
    /// </summary>
    /// <remarks>
    /// The cookie is a credential, and a deployment writes credentials to files rather than into a
    /// service definition, for the reason the API key is read the same way: the definition is a file
    /// anything able to read the deployment directory can read. The variable names the file and is not
    /// itself a secret.
    /// </remarks>
    private const string RobloxCookieFileEnvironmentVariable = "ROBLOX_COOKIE_FILE";

    /// <summary>
    /// The environment variable that overrides the catalogue location.
    /// </summary>
    private const string ModelsPathEnvironmentVariable = "SCANNER_MODELS_PATH";

    /// <summary>
    /// The environment variable that overrides the index database location.
    /// </summary>
    private const string IndexPathEnvironmentVariable = "SCANNER_INDEX_PATH";

    /// <summary>
    /// The environment variable that overrides the trusted proxy addresses.
    /// </summary>
    private const string TrustedProxiesEnvironmentVariable = "SCANNER_TRUSTED_PROXIES";

    /// <summary>
    /// The environment variable that overrides the per-client request limit.
    /// </summary>
    private const string RequestsPerMinuteEnvironmentVariable = "SCANNER_REQUESTS_PER_MINUTE";

    /// <summary>
    /// The environment variable that overrides the operational webhook.
    /// </summary>
    private const string StatusWebhookEnvironmentVariable = "SCANNER_STATUS_WEBHOOK";

    /// <summary>
    /// The value that means no proxy is trusted, whatever the defaults would have been.
    /// </summary>
    private const string TrustNothing = "none";

    /// <summary>
    /// The name of the rate-limit policy applied to every endpoint.
    /// </summary>
    /// <remarks>
    /// Named rather than inline because the policy is applied in the composition root while its shape is
    /// decided here, and the two have to refer to the same policy.
    /// </remarks>
    public const string RateLimitPolicyName = "scanner";

    /// <summary>
    /// The reverse proxies whose forwarding headers are trusted when nothing is configured.
    /// </summary>
    /// <remarks>
    /// These are Cloudflare's published ranges, which is the edge this API is deployed behind. They are
    /// defaults rather than an assumption: a deployment behind something else replaces the list through
    /// <see cref="ScannerOptions.TrustedProxies"/>, and a deployment addressed directly disables the
    /// behaviour entirely with <c>none</c>. Trusting a stale range is not dangerous in itself -- it only
    /// means a forwarded header stops being believed, so the rate limit partitions by the edge's address
    /// instead of the caller's -- which is why a default list is acceptable here where a default
    /// credential would not be.
    /// </remarks>
    private static readonly string[] DefaultTrustedProxies =
    [
        "173.245.48.0/20",
        "103.21.244.0/22",
        "103.22.200.0/22",
        "103.31.4.0/22",
        "141.101.64.0/18",
        "108.162.192.0/18",
        "190.93.240.0/20",
        "188.114.96.0/20",
        "197.234.240.0/22",
        "198.41.128.0/17",
        "162.158.0.0/15",
        "104.16.0.0/13",
        "104.24.0.0/14",
        "172.64.0.0/13",
        "131.0.72.0/22",
        "2400:cb00::/32",
        "2606:4700::/32",
        "2803:f800::/32",
        "2405:b500::/32",
        "2405:8100::/32",
        "2a06:98c0::/29",
        "2c0f:f248::/32"
    ];

    /// <summary>
    /// The separators accepted in a configured proxy list.
    /// </summary>
    private static readonly char[] ProxyListSeparators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// The partition key used for a request that arrived without a peer address.
    /// </summary>
    private const string UnaddressedPartition = "unaddressed";

    /// <summary>
    /// The partition key used for a caller on this machine, whose requests are not limited.
    /// </summary>
    private const string LocalPartition = "local";

    /// <summary>
    /// The media type of the body written when a request is refused for exceeding the limit.
    /// </summary>
    private const string ContentType = "application/json; charset=utf-8";

    /// <summary>
    /// Registers every service the scanner API depends on.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configuration">The host configuration, used for options and environment fallbacks.</param>
    /// <returns>The same collection, so registration calls can be chained.</returns>
    public static IServiceCollection AddScannerApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ScannerOptions>()
            .Bind(configuration.GetSection(ScannerOptions.SectionName))
            .PostConfigure(options =>
            {
                // Only fill a gap: an explicitly configured value always wins, and the environment is
                // consulted solely so that a deployment does not have to persist a credential to disk.
                // Empty and whitespace are both treated as "not configured", because a settings file that
                // carries an empty placeholder must not shadow the environment.
                options.ApiKey = ResolveApiKey(options.ApiKey, configuration);
                options.RobloxCookie = ResolveRobloxCookie(options.RobloxCookie, configuration);
                options.ModelsPath = Coalesce(options.ModelsPath, configuration[ModelsPathEnvironmentVariable]);
                options.IndexPath = Coalesce(options.IndexPath, configuration[IndexPathEnvironmentVariable]);
                options.TrustedProxies = Coalesce(options.TrustedProxies, configuration[TrustedProxiesEnvironmentVariable]);
                options.StatusWebhook = Coalesce(options.StatusWebhook, configuration[StatusWebhookEnvironmentVariable]);

                // A limit that is absent, unparsable or not positive leaves the default in place rather
                // than disabling the limit, because a typo in an environment variable must not silently
                // remove the only thing standing between the API and an unbounded caller.
                if (int.TryParse(
                        configuration[RequestsPerMinuteEnvironmentVariable],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int requestsPerMinute) &&
                    requestsPerMinute > 0)
                {
                    options.RequestsPerMinute = requestsPerMinute;
                }
            });

        services.AddSingleton(static provider => ResolvePaths(provider.GetRequiredService<IOptions<ScannerOptions>>().Value));

        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();
        services.AddSingleton<IRuleCatalogService, RuleCatalogService>();
        services.AddSingleton<IScannerIndexReader, SqliteIndexReader>();
        services.AddSingleton<IModelCatalogRepository, SqliteModelCatalogRepository>();
        services.AddSingleton<IAnalyticsService, SqliteAnalyticsService>();
        services.AddSingleton<IScannerControlService, ScannerControlService>();

        services.AddHttpClient<IAssetDeliveryResolver, AssetDeliveryResolver>(static (provider, client) =>
        {
            // Roblox answers or fails a resolve request quickly, so a short timeout keeps a stuck upstream
            // from occupying a caller. The default handler already follows redirects and pools sockets.
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ProductUserAgent);

            ScannerOptions options = provider.GetRequiredService<IOptions<ScannerOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.RobloxCookie))
            {
                // Roblox requires an authenticated session for asset delivery, so the cookie is attached
                // here and never leaves the server. It is not returned by any endpoint.
                client.DefaultRequestHeaders.Add(
                    CookieHeaderName,
                    $"{RobloxSessionCookieName}={options.RobloxCookie}");
            }
        });

        // Catalogue and analytics responses are repetitive JSON; compressing them cuts the bytes the
        // website proxy has to move, which matters because every page render fans out to several calls.
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes =
            [
                "application/json",
                "application/problem+json",
                "text/json"
            ];
        });

        /*
         * Believe the forwarding headers only when they came from an address on the trusted list, and
         * take exactly one hop of them. The header is set by the client and is trivially forged, so
         * honouring it from an arbitrary peer would let a caller choose its own apparent address -- which
         * is to say, choose how the rate limit below partitions and what every log line records.
         *
         * Host and proto are forwarded so that a redirected download location is built with the scheme
         * the caller actually used. The forwarded host is deliberately not consumed: the API has one
         * name, it is not this process's business to believe another one, and a forged host is the input
         * to a cache-poisoning or password-reset attack in applications that do trust it.
         */
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ScannerOptions>>((forwarded, scanner) =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                forwarded.ForwardLimit = 1;

                // The framework seeds these with loopback. Clearing them means the list below is the
                // entire set of addresses whose headers are believed, and a deployment that configures
                // "none" trusts nobody rather than falling back to a default.
                forwarded.KnownNetworks.Clear();
                forwarded.KnownProxies.Clear();

                foreach (IPNetwork network in ParseNetworks(scanner.Value.TrustedProxies))
                {
                    forwarded.KnownNetworks.Add(network);
                }
            });

        /*
         * One limit per caller address, counted in a fixed window. This is the only defence in front of
         * a process whose every endpoint does real work -- a catalogue search scans text columns, a
         * download resolution waits on Roblox -- so it is applied to the whole API rather than to the
         * endpoints that look expensive today.
         *
         * The partition key is the socket peer, which forwarding headers have already replaced with the
         * real caller when the peer is a known proxy. It is never taken from a header directly, so a
         * caller cannot split itself across keys by inventing addresses.
         */
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicyName, static context =>
            {
                IPAddress? client = context.Connection.RemoteIpAddress;

                // A caller on this machine is not limited, and that exemption is not a hole: it can only
                // be reached from this machine, where a process could open the index database itself.
                // What it does buy is that the console's own front end -- which fans one page render out
                // into several calls and refreshes on a timer, all from the loopback address -- is never
                // the thing the limit is aimed at.
                if (client is not null && IPAddress.IsLoopback(client))
                {
                    return RateLimitPartition.GetNoLimiter(LocalPartition);
                }

                int permitLimit = context.RequestServices
                    .GetRequiredService<IOptions<ScannerOptions>>()
                    .Value
                    .RequestsPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter(
                    client?.ToString() ?? UnaddressedPartition,

                    // No queue: a request held behind a full window has already spent its caller's
                    // patience, and answering it late is worse than answering it with a refusal.
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.OnRejected = static async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = ContentType;

                // The refusal names the limit rather than the caller. Echoing the address back would put
                // a caller-supplied value into a response body, and repeating it in one's own output is
                // how a value gets treated as trustworthy.
                await context.HttpContext.Response
                    .WriteAsync(
                        """{"error":"rate_limited","message":"Too many requests. Wait a moment and try again."}""",
                        cancellationToken)
                    .ConfigureAwait(false);
            };
        });

        return services;
    }

    /// <summary>
    /// Parses the configured trusted proxy list.
    /// </summary>
    /// <param name="configured">The configured value, which may be empty or the word <c>none</c>.</param>
    /// <returns>The networks whose forwarding headers may be believed.</returns>
    /// <exception cref="InvalidOperationException">An entry is not an address or a CIDR range.</exception>
    /// <remarks>
    /// A malformed entry stops the process rather than being skipped. The list decides whose claim about
    /// a caller's address is believed, and quietly dropping from it would leave a deployment apparently
    /// configured while trusting nothing -- the kind of failure that is only noticed when a limit has to
    /// be read. The message names the entry, so the fix is a one-line change.
    /// </remarks>
    private static IEnumerable<IPNetwork> ParseNetworks(string? configured)
    {
        if (string.Equals(configured?.Trim(), TrustNothing, StringComparison.OrdinalIgnoreCase)) yield break;

        IEnumerable<string> entries = string.IsNullOrWhiteSpace(configured)
            ? DefaultTrustedProxies
            : configured.Split(ProxyListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string entry in entries)
        {
            if (!TryParseNetwork(entry, out IPNetwork network))
            {
                throw new InvalidOperationException(
                    $"The trusted proxy list contains \"{entry}\", which is not an IP address or a CIDR "
                    + "range. Fix the value, or set the list to \"none\" to trust no proxy.");
            }

            yield return network;
        }
    }

    /// <summary>
    /// Parses one address or CIDR range.
    /// </summary>
    /// <param name="text">The entry to parse.</param>
    /// <param name="network">The parsed range, when the entry is valid.</param>
    /// <returns><see langword="true"/> when the entry is an address or a CIDR range.</returns>
    private static bool TryParseNetwork(string text, out IPNetwork network)
    {
        network = null!;

        int slash = text.IndexOf('/');
        string addressText = slash < 0 ? text : text[..slash];

        if (!IPAddress.TryParse(addressText, out IPAddress? address)) return false;

        int maximumPrefix =
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;

        int prefix = maximumPrefix;

        if (slash >= 0 &&
            (!int.TryParse(text[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out prefix) ||
             prefix < 0 ||
             prefix > maximumPrefix))
        {
            return false;
        }

        network = new IPNetwork(address, prefix);
        return true;
    }

    /// <summary>
    /// Resolves the absolute file locations the API operates on.
    /// </summary>
    /// <param name="options">The bound scanner options.</param>
    /// <returns>The resolved catalogue, index and control paths.</returns>
    /// <remarks>
    /// Resolving once, at start-up, is what removes the class of bug where the API reads a different
    /// <c>models.json</c> depending on the working directory the host was started from.
    /// </remarks>
    private static ScannerPaths ResolvePaths(ScannerOptions options)
    {
        string filesDirectory = Path.Combine(AppContext.BaseDirectory, FilesDirectoryName);

        string modelsPath = ResolvePath(options.ModelsPath, Path.Combine(filesDirectory, CatalogueFileName));
        string indexPath = ResolvePath(options.IndexPath, Path.Combine(filesDirectory, IndexFileName));

        // The control file always sits beside the catalogue: that is the directory the scanner polls, and
        // deriving it means a deployment cannot point the two at different scanners by accident.
        string controlPath = Path.Combine(Path.GetDirectoryName(modelsPath) ?? filesDirectory, ControlFileName);

        return new ScannerPaths(modelsPath, indexPath, controlPath);
    }

    /// <summary>
    /// Resolves the API key from the settings, from a file, or from the environment.
    /// </summary>
    /// <param name="configured">The key from the settings section, which may be absent.</param>
    /// <param name="configuration">The host configuration, read for the environment variables.</param>
    /// <returns>The key, or a value that is empty or whitespace when none is configured.</returns>
    /// <remarks>
    /// <para>
    /// The order is the settings section, then the file named by <c>SCANNER_API_KEY_FILE</c>, then
    /// <c>SCANNER_API_KEY</c>. An explicit setting is the most specific statement of intent and wins; the
    /// file comes next because it is what a deployment supplies, and it is preferred to the variable so
    /// that naming a file is enough to override a value left in the environment of the shell an operator
    /// happened to start the API from. The scanner's own site names the same file, so the two halves of
    /// one shared secret cannot drift apart.
    /// </para>
    /// <para>
    /// A file that is named but cannot be read yields no key rather than falling through to the variable.
    /// A deployment whose key file is missing a folder, or unreadable by the service account, refuses
    /// every caller instead of silently accepting one the file was meant to replace.
    /// </para>
    /// <para>
    /// A key that is not a GUID is not rejected here. <see cref="Implementation.ApiKeyValidator"/> refuses
    /// it, so the caller is told its key is wrong rather than being met by a process that came up and
    /// reports nothing it can act on.
    /// </para>
    /// </remarks>
    private static string? ResolveApiKey(string? configured, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        string? path = configuration[ApiKeyFileEnvironmentVariable];

        if (!string.IsNullOrWhiteSpace(path)) return ReadSecretFile(path);

        return configuration[ApiKeyEnvironmentVariable];
    }

    /// <summary>
    /// Resolves the Roblox session cookie from the settings, from a file, or from the environment.
    /// </summary>
    /// <param name="configured">The cookie from the settings section, which may be absent.</param>
    /// <param name="configuration">The host configuration, read for the environment variables.</param>
    /// <returns>The cookie, or a value that is empty or whitespace when none is configured.</returns>
    /// <remarks>
    /// <para>
    /// The order is the settings section, then the file named by <c>ROBLOX_COOKIE_FILE</c>, then
    /// <c>ROBLOX_COOKIE</c>, which is the order the API key is resolved in and for the same reasons.
    /// This route used to read the variable alone, and that left the cookie the one credential in the
    /// stack a deployment had to write into a service definition -- which the plan says is not where
    /// secrets go, so a deployment that followed the plan had no way to give the API its cookie and
    /// every download answered 502 while the catalogue worked.
    /// </para>
    /// <para>
    /// A cookie that is absent is neither fatal nor treated as a fault. Asset delivery refuses an
    /// unauthenticated request, so without this value nothing downloads, which the log states at
    /// start-up and the download route states per request, while every other endpoint serves the
    /// catalogue the API was deployed to serve.
    /// </para>
    /// </remarks>
    private static string? ResolveRobloxCookie(string? configured, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        string? path = configuration[RobloxCookieFileEnvironmentVariable];

        if (!string.IsNullOrWhiteSpace(path)) return ReadSecretFile(path);

        return configuration[RobloxCookieEnvironmentVariable];
    }

    /// <summary>
    /// Reads a credential from the file a variable names.
    /// </summary>
    /// <param name="path">The path named by the environment.</param>
    /// <returns>The file's contents, trimmed, or an empty string when the file cannot be read.</returns>
    /// <remarks>
    /// A file written by an editor ends with a newline. Trimming is not cosmetic: a key parsed as a GUID,
    /// or a cookie compared against what Roblox issued, fails on a value that carries a terminator, and
    /// it fails for a reason that is invisible in the operator's view of the file. Every failure answers
    /// with an empty value rather than an exception, because a named file that cannot be read has to
    /// leave the surface it protects disabled instead of taking the process down with it.
    /// </remarks>
    private static string ReadSecretFile(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Chooses the first value that is actually present.
    /// </summary>
    /// <param name="configured">The configured value, which may be null, empty or whitespace.</param>
    /// <param name="fallback">The environment-supplied value.</param>
    /// <returns>The configured value when it carries content, otherwise the fallback.</returns>
    private static string? Coalesce(string? configured, string? fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;

    /// <summary>
    /// Turns a configured path into an absolute one.
    /// </summary>
    /// <param name="configured">The configured value, which may be empty.</param>
    /// <param name="fallback">The absolute default used when nothing is configured.</param>
    /// <returns>An absolute path.</returns>
    private static string ResolvePath(string? configured, string fallback)
    {
        if (string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(fallback);

        return Path.GetFullPath(
            Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured));
    }
}
