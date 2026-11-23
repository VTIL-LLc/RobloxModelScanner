using System.Globalization;

using RobloxModelScanner.Enums;
using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Factories;

/// <summary>
/// Builds the scanner's settings from the process environment and the environment file.
/// </summary>
/// <remarks>
/// <para>
/// Reading settings and bounding them are done together here, on purpose. A scanner configured with a
/// batch size of zero, a concurrency of minus one or a relative catalogue path would fail somewhere far
/// from the setting that caused it -- usually inside a background task, which is the hardest place to
/// diagnose -- so every value is clamped or replaced at the point it is read. The scanner's response to
/// nonsense configuration is to run with a defensible value rather than to refuse to start, because an
/// unattended scanner that will not run is worse than one running with a batch size of a hundred.
/// </para>
/// <para>
/// Because the clamping is quiet, the scanner writes its effective configuration to the log at
/// start-up. That line is the record of what was actually accepted, so a value that was ignored is
/// visible rather than merely absent.
/// </para>
/// <para>
/// Nothing here writes to the environment file. The one value the scanner does persist, the sweep
/// cursor, is written through the same abstraction these values are read from.
/// </para>
/// </remarks>
/// <seealso cref="IEnvironmentFile"/>
public static class ScannerSettingsFactory
{
    /// <summary>
    /// The default first identifier.
    /// </summary>
    /// <remarks>
    /// Preserved from the previous revision. It sits above the range the existing catalogue already
    /// covers, so a fresh installation starts where the earlier work stopped rather than walking the
    /// identifier space again.
    /// </remarks>
    private const long DefaultStartId = 16848923941L;

    /// <summary>
    /// The default number of identifiers per details request.
    /// </summary>
    private const int DefaultBatchSize = 99;

    /// <summary>
    /// The default number of details requests in flight.
    /// </summary>
    private const int DefaultScanConcurrency = 16;

    /// <summary>
    /// The default number of asset downloads in flight.
    /// </summary>
    private const int DefaultAssetConcurrency = 64;

    /// <summary>
    /// The default number of seconds between catalogue rewrites.
    /// </summary>
    private const int DefaultFlushSeconds = 15;

    /// <summary>
    /// The default number of unsaved rows that forces a catalogue rewrite.
    /// </summary>
    private const int DefaultFlushRows = 250;

    /// <summary>
    /// The default score at which a payload is kept.
    /// </summary>
    private const double DefaultKeepScore = 3;

    /// <summary>
    /// The largest accepted batch size.
    /// </summary>
    /// <remarks>
    /// A batch far beyond this is refused or times out at the catalogue endpoint, and either way the
    /// sweep would spend its time retrying one request instead of scanning.
    /// </remarks>
    private const int MaximumBatchSize = 250;

    /// <summary>
    /// The largest accepted concurrency, for either stage.
    /// </summary>
    /// <remarks>
    /// The bound exists to stop a typo from opening thousands of connections, which would be a denial of
    /// service against Roblox and against this host's socket table rather than a faster scan.
    /// </remarks>
    private const int MaximumConcurrency = 512;

    /// <summary>
    /// The default set of creators whose assets are skipped.
    /// </summary>
    /// <remarks>
    /// Carried over from the previous revision, which held it in the source. It is a setting now because
    /// a list of account identifiers compiled into a program is a list nobody can change without
    /// rebuilding it.
    /// </remarks>
    private static readonly long[] DefaultIgnoredCreators = [1];

    /// <summary>
    /// Reads and bounds the scanner's settings.
    /// </summary>
    /// <param name="environment">The environment, consulted in that order.</param>
    /// <returns>The resolved settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="environment"/> is null.</exception>
    public static ScannerSettings Read(IEnvironmentFile environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        string applicationDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return new ScannerSettings
        {
            Mode = ReadMode(environment),
            StartId = environment.GetLong("START_ID", DefaultStartId),
            BatchSize = environment.GetInt("BATCH_SIZE", DefaultBatchSize, 1, MaximumBatchSize),
            ScanConcurrency = environment.GetInt("SCAN_CONCURRENCY", DefaultScanConcurrency, 1, MaximumConcurrency),
            AssetConcurrency = environment.GetInt("ASSET_CONCURRENCY", DefaultAssetConcurrency, 1, MaximumConcurrency),
            FlushInterval = TimeSpan.FromSeconds(environment.GetInt("FLUSH_SECONDS", DefaultFlushSeconds, 1, 3600)),
            RequestInterval = TimeSpan.FromMilliseconds(environment.GetInt("REQUEST_INTERVAL_MS", 0, 0, 60_000)),
            FlushRows = environment.GetInt("FLUSH_ROWS", DefaultFlushRows, 1, 1_000_000),
            KeepScore = environment.GetDouble("KEEP_SCORE", DefaultKeepScore, 0, 1000),
            KeepModels = environment.GetFlag("KEEP_MODELS", true),
            ModelDirectory = ResolvePath(environment, "MODEL_DIRECTORY", applicationDirectory),
            CataloguePath = ResolvePath(
                environment,
                "CATALOGUE_PATH",
                Path.Combine(applicationDirectory, "Files", "catalogue.db")),
            ControlPath = ResolvePath(
                environment,
                "CONTROL_PATH",
                Path.Combine(applicationDirectory, "Files", "control.json")),
            FoundWebhook = environment.Get("MAIN_WEBHOOK").Trim(),
            DownloadWebhook = environment.Get("AUTODOWNLOAD_WEBHOOK").Trim(),
            RobloxCookie = environment.Get("ROBLOX_COOKIE").Trim(),
            RobloxCookieFilePath = ResolvePublishedCookiePath(environment),
            IgnoredCreators = ReadIgnoredCreators(environment.Get("IGNORED_CREATORS")),
            Verbose = environment.GetFlag("VERBOSE", false)
        };
    }

    /// <summary>
    /// Resolves the file the session cookie is published to.
    /// </summary>
    /// <param name="environment">The environment.</param>
    /// <returns>An absolute path, or an empty string when nothing named one.</returns>
    /// <remarks>
    /// <para>
    /// The variable is the same one the API reads the cookie from, and that is deliberate: the two ends
    /// are one file, written by the program that owns the session and read by the program that needs it,
    /// and two names for one path is how a deployment ends up writing a credential to one place and
    /// looking for it in another.
    /// </para>
    /// <para>
    /// Absent is a legitimate configuration rather than a mistake. It means this installation has nothing
    /// that needs the session besides the scanner, and the variable is unset so that nothing is published.
    /// A path that was named but is relative is resolved the same way every other path is.
    /// </para>
    /// </remarks>
    private static string ResolvePublishedCookiePath(IEnvironmentFile environment)
    {
        string configured = environment.Get("ROBLOX_COOKIE_FILE").Trim();

        return configured.Length == 0 ? string.Empty : Path.GetFullPath(configured);
    }

    /// <summary>
    /// Reads the requested pass.
    /// </summary>
    /// <param name="environment">The environment.</param>
    /// <returns>The requested mode, or a sweep when the request is absent or unrecognised.</returns>
    /// <remarks>
    /// An unrecognised value falls back to a sweep rather than stopping. A sweep is additive and
    /// idempotent; a backfill is a long walk over the whole catalogue that rewrites what it finds, and
    /// starting one by accident would be a surprising amount of work to perform because a word was
    /// misspelled.
    /// </remarks>
    private static ScannerMode ReadMode(IEnvironmentFile environment) =>
        environment.Get("SCAN_MODE").Trim().ToLowerInvariant() switch
        {
            "backfill" => ScannerMode.Backfill,
            _ => ScannerMode.Sweep
        };

    /// <summary>
    /// Resolves a path-valued setting.
    /// </summary>
    /// <param name="environment">The environment.</param>
    /// <param name="key">The setting name.</param>
    /// <param name="fallback">The absolute path to use when the setting is absent.</param>
    /// <returns>An absolute path.</returns>
    /// <remarks>
    /// A relative value is resolved against the process's current directory, because that is what a
    /// relative path means everywhere else. The result is absolute so that a later change of directory,
    /// or a background task with a different notion of the working directory, cannot send the scanner
    /// looking for its catalogue somewhere else.
    /// </remarks>
    private static string ResolvePath(IEnvironmentFile environment, string key, string fallback)
    {
        string configured = environment.Get(key).Trim();
        return configured.Length == 0 ? fallback : Path.GetFullPath(configured);
    }

    /// <summary>
    /// Reads the creators whose assets are skipped.
    /// </summary>
    /// <param name="value">The configured list, separated by commas, semicolons or whitespace.</param>
    /// <returns>The identifiers to skip.</returns>
    /// <remarks>
    /// A configured list replaces the default rather than extending it, so that configuring the list is
    /// the only way to change it and re-including one of the defaults is possible. An empty value means
    /// the default set, because leaving an optional setting blank is how it is left alone.
    /// </remarks>
    private static IReadOnlySet<long> ReadIgnoredCreators(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new HashSet<long>(DefaultIgnoredCreators);

        HashSet<long> creators = [];
        foreach (string entry in value.Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(entry, NumberStyles.Integer, CultureInfo.InvariantCulture, out long creator))
            {
                creators.Add(creator);
            }
        }

        return creators;
    }
}
