using Microsoft.Data.Sqlite;

using Microsoft.Extensions.Logging;

using RobloxModelScanner.Enums;
using RobloxModelScanner.Factories;
using RobloxModelScanner.Features.AssetDelivery;
using RobloxModelScanner.Features.Tray;
using RobloxModelScanner.Implementation;
using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;
using RobloxModelScanner.Operations;

namespace RobloxModelScanner;

/// <summary>
/// The scanner's entry point.
/// </summary>
/// <remarks>
/// <para>
/// Everything the scanner is made of is assembled here and nowhere else. There is no container and no
/// ambient lookup: the objects are few, the graph is a line, and being able to read the whole of what the
/// scanner depends on in one method is worth more than the indirection a container would add.
/// </para>
/// <para>
/// Two cancellation signals are established, because \"stop\" means two different things. The first
/// interrupt stops the pass from starting another block and lets the work already in flight finish, which
/// is the ordinary way to stop: an inspection that has already downloaded a payload has paid for it, and
/// throwing it away to exit a second sooner is a poor trade. A second interrupt abandons that work, and
/// is there for the case where the scanner must actually go now.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The exit code returned when the catalogue cannot be read.
    /// </summary>
    private const int CatalogueUnreadableExitCode = 2;

    /// <summary>
    /// The exit code returned when the scan ends in a way it did not expect.
    /// </summary>
    private const int UnexpectedFailureExitCode = 3;

    /// <summary>
    /// The scanner's entry point.
    /// </summary>
    /// <param name="args">The command line, which is unused; the scanner is configured by its environment.</param>
    /// <returns>The process exit code.</returns>
    private static async Task<int> Main(string[] args)
    {
        IEnvironmentFile environment = new EnvironmentFile(EnvironmentFile.ResolveDefaultPath());
        ScannerSettings settings = ScannerSettingsFactory.Read(environment);

        using ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(settings.Verbose ? LogLevel.Debug : LogLevel.Information)
            .AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            }));

        ILogger logger = factory.CreateLogger("scanner");
        Describe(logger, settings, environment);
        PublishSessionCookie(settings, logger);

        using CancellationTokenSource stop = new();
        using CancellationTokenSource abandon = new();

        Console.CancelKeyPress += (_, arguments) =>
        {
            if (!stop.IsCancellationRequested)
            {
                // The default handler is suppressed so that the process survives to drain its work. A
                // scanner killed mid-download leaves no row for the asset it was holding.
                arguments.Cancel = true;
                logger.LogInformation("Stopping. Work in flight will finish; press Ctrl+C again to abandon it.");
                stop.Cancel();
                return;
            }

            logger.LogWarning("Abandoning work in flight.");
            abandon.Cancel();
            arguments.Cancel = false;
        };

        using HttpClient roblox = CreateRobloxClient(logger, settings.RobloxCookie);
        using HttpClient webhooks = new() { Timeout = TimeSpan.FromSeconds(20) };

        IModelCatalogue catalogue = new SqliteModelCatalogue(
            settings.CataloguePath,
            settings.FlushInterval,
            settings.FlushRows);

        try
        {
            await catalogue.LoadAsync(stop.Token);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Refusing to start is the whole point of this branch. A catalogue that cannot be read is
            // still the operator's only copy of a hundred thousand findings, and starting a sweep beside it
            // would commit over the database on the first flush with a catalogue that had loaded as empty.
            logger.LogCritical(
                exception,
                "The catalogue at {Path} could not be read. Nothing was changed.",
                settings.CataloguePath);

            return CatalogueUnreadableExitCode;
        }

        logger.LogInformation("Loaded {Count} row(s) from {Path}.", catalogue.Count, settings.CataloguePath);

        IAssetDeliveryResolver resolver = new AssetDeliveryResolver(roblox);
        IRobloxAssetSource source = new RobloxAssetSource(
            resolver,
            roblox,
            factory.CreateLogger<RobloxAssetSource>());

        IRobloxToolbox toolbox = new ToolboxClient(
            roblox,
            settings.RequestInterval,
            factory.CreateLogger<ToolboxClient>());
        IScanReporter reporter = new DiscordReporter(webhooks, settings, factory.CreateLogger<DiscordReporter>());

        AssetInspectionOperation inspection = new(
            source,
            reporter,
            settings,
            factory.CreateLogger<AssetInspectionOperation>());

        InspectionDispatcher dispatcher = new(
            catalogue,
            inspection,
            settings.AssetConcurrency,
            abandon.Token,
            factory.CreateLogger<InspectionDispatcher>());

        IScanControl control = new FileScanControl(settings.ControlPath, factory.CreateLogger<FileScanControl>());

        MaintenanceOperation maintenance = new(
            catalogue,
            environment,
            settings,
            factory.CreateLogger<MaintenanceOperation>());

        using CancellationTokenSource maintenanceStop =
            CancellationTokenSource.CreateLinkedTokenSource(stop.Token);

        Task maintaining = maintenance.RunAsync(maintenanceStop.Token);

        if (settings.KeepModels) Directory.CreateDirectory(settings.ModelDirectory);

        StartTray(logger);

        int exitCode = 0;

        try
        {
            if (settings.Mode == ScannerMode.Backfill)
            {
                BackfillOperation backfill = new(
                    catalogue,
                    toolbox,
                    dispatcher,
                    control,
                    settings,
                    factory.CreateLogger<BackfillOperation>());

                await backfill.RunAsync(stop.Token);
            }
            else
            {
                SweepOperation sweep = new(
                    catalogue,
                    environment,
                    toolbox,
                    dispatcher,
                    control,
                    settings,
                    factory.CreateLogger<SweepOperation>());

                await sweep.RunAsync(stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("The scan was cancelled.");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "The scan ended unexpectedly.");
            exitCode = UnexpectedFailureExitCode;
        }
        finally
        {
            // The maintenance loop is stopped and awaited rather than merely abandoned, because it is the
            // only thing that saves the catalogue. Ending the process without waiting for its final,
            // forced write would discard every row found since the last tick.
            maintenanceStop.Cancel();

            try
            {
                await maintaining;
            }
            catch (OperationCanceledException)
            {
                // Expected: the loop was just cancelled.
            }
        }

        logger.LogInformation("Stopped with {Count} row(s) in the catalogue.", catalogue.Count);
        return exitCode;
    }

    /// <summary>
    /// Writes the effective configuration to the log.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="settings">The resolved settings.</param>
    /// <param name="environment">The environment file the settings were read from.</param>
    /// <remarks>
    /// Settings are bounded rather than rejected, so this line is the record of what was accepted. Without
    /// it, a value that was clamped would be visible only as behaviour that does not match the file.
    /// </remarks>
    private static void Describe(ILogger logger, ScannerSettings settings, IEnvironmentFile environment)
    {
        logger.LogInformation(
            "Mode {Mode}, cursor {Cursor}, batch {Batch}, scan concurrency {ScanConcurrency}, asset concurrency {AssetConcurrency}.",
            settings.Mode,
            settings.StartId,
            settings.BatchSize,
            settings.ScanConcurrency,
            settings.AssetConcurrency);

        logger.LogInformation(
            "Catalogue {Catalogue}, control {Control}, payloads {Payloads} ({KeepModels}), environment {Environment}.",
            settings.CataloguePath,
            settings.ControlPath,
            settings.ModelDirectory,
            settings.KeepModels ? "kept" : "not kept",
            environment is EnvironmentFile file ? file.Path : "unknown");

        logger.LogInformation(
            "Flushing every {Seconds} second(s) or {Rows} row(s); keeping payloads scoring {KeepScore} or above; {Ignored} creator(s) ignored.",
            settings.FlushInterval.TotalSeconds,
            settings.FlushRows,
            settings.KeepScore,
            settings.IgnoredCreators.Count);

        if (settings.RequestInterval > TimeSpan.Zero)
        {
            logger.LogInformation(
                "Catalogue requests are paced at one every {Milliseconds} ms.",
                settings.RequestInterval.TotalMilliseconds);
        }

        if (settings.RobloxCookie.Length == 0)
        {
            // The template name matches RMS_ENVIRONMENT_TEMPLATE_NAME in deploy/rmsdeploy.c, which is what
            // places the file this message tells the operator to copy.
            logger.LogWarning(
                "No session cookie is configured, so no payload will be downloaded: asset delivery refuses unauthenticated requests. Set ROBLOX_COOKIE in the .env file beside this program, or copy .env.template to .env and fill it in.");
        }

        if (settings.FoundWebhook.Length == 0 && settings.DownloadWebhook.Length == 0)
        {
            logger.LogInformation("No webhook is configured; findings will be recorded without being published.");
        }
    }

    /// <summary>
    /// Builds the client used for every Roblox request.
    /// </summary>
    /// <param name="logger">The logger that records the client's configuration.</param>
    /// <param name="cookie">The session cookie's value, which may be empty.</param>
    /// <returns>The configured client.</returns>
    /// <remarks>
    /// <para>
    /// One client serves the catalogue lookups, the asset delivery resolution and the content downloads.
    /// That is what lets the credential be attached once, and it is also what gives all three the same
    /// connection pool -- a pool that would otherwise be three, each with its own set of connections to
    /// the same hosts.
    /// </para>
    /// <para>
    /// Pooled connections are retired after five minutes. Roblox balances and addresses change from time
    /// to time, and a pooled connection held for the life of a process that runs for days would eventually
    /// be one of the addresses that stopped answering.
    /// </para>
    /// </remarks>
    private static HttpClient CreateRobloxClient(ILogger logger, string cookie)
    {
        SocketsHttpHandler handler = new()
        {
            MaxConnectionsPerServer = 128,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxModelScanner/1.0");

        if (cookie.Length == 0) return client;

        // The value stored in the environment file is the cookie's value alone, because that is the form
        // the browser and every Roblox tool present it in. The name is attached here rather than being
        // asked of the operator, who would then have to know it.
        client.DefaultRequestHeaders.Add("Cookie", string.Concat(".ROBLOSECURITY=", cookie));
        logger.LogDebug("The session cookie was attached to the Roblox client.");

        return client;
    }

    /// <summary>
    /// Publishes the session cookie to the file the rest of the stack reads it from.
    /// </summary>
    /// <param name="settings">The settings, which hold the cookie and the path to publish it to.</param>
    /// <param name="logger">The logger that records what happened.</param>
    /// <remarks>
    /// <para>
    /// The scanner is the only program in this stack that holds a Roblox session. It is the one that reads
    /// the environment file an operator pastes a cookie into, and it is the one whose traffic Roblox sees,
    /// so its session is the deployment's session. The API needs the same one to resolve a download, and it
    /// reads it from a file because that is where this stack keeps credentials -- but a second copy written
    /// out by hand is a copy that goes stale the first time the session is replaced, and the failure looks
    /// like the asset having been removed.
    /// </para>
    /// <para>
    /// So it is published here instead, at start-up, to the path the environment names. One file, one
    /// writer: the operator edits the environment file, the scanner writes the credential file, and every
    /// reader of that file sees the session the scanner is actually using.
    /// </para>
    /// <para>
    /// A file that cannot be written is reported and the scan continues. Publishing serves the other
    /// services; it must not be able to stop the catalogue from being swept.
    /// </para>
    /// </remarks>
    private static void PublishSessionCookie(ScannerSettings settings, ILogger logger)
    {
        if (settings.RobloxCookieFilePath.Length == 0)
        {
            logger.LogDebug(
                "No ROBLOX_COOKIE_FILE was named, so the session cookie is not published for other services.");
            return;
        }

        if (settings.RobloxCookie.Length == 0)
        {
            logger.LogWarning(
                "No session cookie is configured, so nothing was published to {Path}. Asset delivery refuses "
                + "unauthenticated requests, so downloads will not resolve until one is set.",
                settings.RobloxCookieFilePath);
            return;
        }

        try
        {
            File.WriteAllText(settings.RobloxCookieFilePath, settings.RobloxCookie);

            logger.LogInformation(
                "The session cookie was published to {Path} for the services that read it.",
                settings.RobloxCookieFilePath);
        }
        catch (Exception exception) when (exception is IOException
                                          || exception is UnauthorizedAccessException
                                          || exception is NotSupportedException
                                          || exception is ArgumentException)
        {
            // Reported rather than fatal: the session the scanner itself uses is unaffected, and a machine
            // whose downloads cannot resolve is still a machine whose catalogue can be swept.
            logger.LogWarning(
                exception,
                "The session cookie could not be published to {Path}.",
                settings.RobloxCookieFilePath);
        }
    }

    /// <summary>
    /// Shows the tray indicator when there is a desktop to show it on.
    /// </summary>
    /// <param name="logger">The logger that records a tray that could not be created.</param>
    /// <remarks>
    /// The tray belongs to an interactive run. Under the service host there is no desktop, and an
    /// indicator that cannot be created would end a scan that was otherwise perfectly able to run -- so
    /// the failure is reported and the scan continues without it.
    /// </remarks>
    private static void StartTray(ILogger logger)
    {
        if (!Environment.UserInteractive) return;

        try
        {
            TrayController.Start();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The tray indicator could not be created; the scan continues without it.");
        }
    }
}
