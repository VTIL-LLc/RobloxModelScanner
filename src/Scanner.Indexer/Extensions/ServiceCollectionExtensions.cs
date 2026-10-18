using Microsoft.Extensions.Options;

using System.Threading.RateLimiting;

using Scanner.Indexer.Implementation;
using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Models;
using Scanner.Indexer.Options;

namespace Scanner.Indexer.Extensions;

/// <summary>
/// Registers the catalogue indexing services on the application's service collection.
/// </summary>
/// <remarks>
/// <para>
/// Every dependency is a singleton. The writer holds the migration gate and the bookkeeping that the
/// status endpoint reports, so two instances would mean two components racing to write the same database;
/// the reader is stateless and cheap to construct; the guard caches its parsed allow-list. Per-request
/// lifetimes would buy nothing and would break the single-writer invariant.
/// </para>
/// <para>
/// Secrets are not read from the committed settings file. The control-plane token in particular must come
/// from the environment, because a token in source control is a token that is already compromised. On a
/// deployed host it arrives through a file the environment names instead, so that the value is not also
/// written into the service definition, which is a file anything able to read the deployment directory can
/// read.
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
    /// The file name of the SQLite index.
    /// </summary>
    private const string IndexFileName = "scanner-index.db";

    /// <summary>
    /// The environment variable that supplies the control-plane token.
    /// </summary>
    private const string TokenEnvironmentVariable = "SCANNER_INDEXER_TOKEN";

    /// <summary>
    /// The environment variable naming a file the control-plane token is read from.
    /// </summary>
    private const string TokenFileEnvironmentVariable = "SCANNER_INDEXER_TOKEN_FILE";

    /// <summary>
    /// The environment variable that overrides the catalogue location.
    /// </summary>
    private const string ModelsPathEnvironmentVariable = "SCANNER_MODELS_PATH";

    /// <summary>
    /// The environment variable that overrides the index database location.
    /// </summary>
    private const string IndexPathEnvironmentVariable = "SCANNER_INDEX_PATH";

    /// <summary>
    /// The environment variable that overrides the control-plane network allow-list.
    /// </summary>
    private const string AllowedNetworksEnvironmentVariable = "SCANNER_INDEXER_NETWORKS";

    /// <summary>
    /// Registers every service the indexing service depends on.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configuration">The host configuration, used for options and environment fallbacks.</param>
    /// <returns>The same collection, so registration calls can be chained.</returns>
    public static IServiceCollection AddScannerIndexer(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<IndexerOptions>()
            .Bind(configuration.GetSection(IndexerOptions.SectionName))
            .PostConfigure(options =>
            {
                // Only fill a gap: an explicitly configured value always wins. Empty and whitespace both
                // count as "not configured" so that a settings file carrying an empty placeholder cannot
                // shadow the environment, which is the only place a credential belongs on a deployed host.
                options.Token = ResolveToken(options.Token, configuration);
                options.ModelsPath = Coalesce(options.ModelsPath, configuration[ModelsPathEnvironmentVariable]);
                options.IndexPath = Coalesce(options.IndexPath, configuration[IndexPathEnvironmentVariable]);
                options.AllowedNetworks = Coalesce(options.AllowedNetworks, configuration[AllowedNetworksEnvironmentVariable]);
            });

        services.AddSingleton(static provider => ResolvePaths(provider.GetRequiredService<IOptions<IndexerOptions>>().Value));

        services.AddSingleton<ICatalogueReader, CatalogueFileReader>();
        services.AddSingleton<SqliteIndexWriter>();
        services.AddSingleton<IIndexWriter>(static provider => provider.GetRequiredService<SqliteIndexWriter>());
        services.AddSingleton<IIndexStatusProvider, IndexStatusProvider>();
        services.AddSingleton<IInternalCallGuard, InternalCallGuard>();

        services.AddHostedService<IndexMaintenanceLoop>();

        // The control plane can trigger a full migration, so it is rate limited as well as authenticated.
        // A bug in an allowed caller must not be able to keep the writer saturated.
        services.AddRateLimiter(static options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(InternalRateLimitPolicy, static context =>
            {
                // Partitioned on the socket peer, never on a forwarded header: the peer address has already
                // passed the allow-list, whereas a header is attacker-controlled.
                string partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partition,
                    static _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// The name of the rate-limit policy applied to the internal control plane.
    /// </summary>
    public const string InternalRateLimitPolicy = "internal-control-plane";

    /// <summary>
    /// Resolves the absolute file locations the indexing service operates on.
    /// </summary>
    /// <param name="options">The bound indexer options.</param>
    /// <returns>The resolved catalogue and index paths.</returns>
    /// <remarks>
    /// The index is derived from the catalogue's directory, so a deployment cannot accidentally point the
    /// writer at one scanner and the readers at another.
    /// </remarks>
    private static IndexerPaths ResolvePaths(IndexerOptions options)
    {
        string filesDirectory = Path.Combine(AppContext.BaseDirectory, FilesDirectoryName);

        string modelsPath = ResolvePath(options.ModelsPath, Path.Combine(filesDirectory, CatalogueFileName));

        string defaultIndexPath = Path.Combine(
            Path.GetDirectoryName(modelsPath) ?? filesDirectory,
            IndexFileName);

        string indexPath = ResolvePath(options.IndexPath, defaultIndexPath);

        return new IndexerPaths(modelsPath, indexPath);
    }

    /// <summary>
    /// Resolves the control-plane token from the settings, from a file, or from the environment.
    /// </summary>
    /// <param name="configured">The token from the settings section, which may be absent.</param>
    /// <param name="configuration">The host configuration, read for the environment variables.</param>
    /// <returns>The token, or a value that is empty or whitespace when none is configured.</returns>
    /// <remarks>
    /// <para>
    /// The order is the settings section, then the file named by <c>SCANNER_INDEXER_TOKEN_FILE</c>, then
    /// <c>SCANNER_INDEXER_TOKEN</c>. An explicit setting is the most specific statement of intent and wins;
    /// the file comes next because it is what a deployment supplies, and it is preferred to the variable so
    /// that naming a file is enough to override a value left in the environment of the shell an operator
    /// happened to start the service from.
    /// </para>
    /// <para>
    /// A file that is named but cannot be read yields no token rather than falling through to the variable.
    /// A deployment whose credential file is missing a folder, or unreadable by the service account, loses
    /// the control plane instead of silently acquiring one from somewhere else. Nothing is configured when
    /// no token is found, and the control plane is disabled in that case, so this is the safe direction for
    /// the failure to travel in.
    /// </para>
    /// </remarks>
    private static string? ResolveToken(string? configured, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        string? path = configuration[TokenFileEnvironmentVariable];

        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                // A token file written by an editor ends with a newline. Trimming is not cosmetic here: the
                // shorter-than-minimum rule that disables the surface counts characters, and a length check
                // that included a terminator would be one character more forgiving than the rule intends.
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

        return configuration[TokenEnvironmentVariable];
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
