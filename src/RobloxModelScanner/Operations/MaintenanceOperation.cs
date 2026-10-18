using Microsoft.Extensions.Logging;

using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Operations;

/// <summary>
/// Saves the catalogue and the cursor on a timer, and reports what has accumulated.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue and the cursor are both written lazily, which keeps a hot loop from rewriting the
/// document for every row it finds, and both therefore need something that decides when the accumulated
/// change is worth committing. That is this operation, and it is the only component that knows the
/// passage of time.
/// </para>
/// <para>
/// It is also the scanner's heartbeat. A sweep can run for hours without finding anything -- the
/// identifier space is mostly places -- and a log that only speaks when something is found is
/// indistinguishable from one belonging to a scanner that has stopped. A line whenever the catalogue
/// grows gives the operator that answer without one whenever it would not.
/// </para>
/// <para>
/// Nothing here is allowed to throw its way out. A disk that has filled up would otherwise end the
/// operation, and with it the flushing that a full disk makes most necessary; instead the failure is
/// reported and the timer starts again, so the scanner keeps scanning and starts saving the moment space
/// is available again.
/// </para>
/// </remarks>
public sealed class MaintenanceOperation
{
    /// <summary>
    /// The catalogue to save.
    /// </summary>
    private readonly IModelCatalogue _catalogue;

    /// <summary>
    /// The environment file to save.
    /// </summary>
    private readonly IEnvironmentFile _environment;

    /// <summary>
    /// The settings that carry the interval.
    /// </summary>
    private readonly ScannerSettings _settings;

    /// <summary>
    /// The logger that reports progress and write failures.
    /// </summary>
    private readonly ILogger<MaintenanceOperation> _logger;

    /// <summary>
    /// The catalogue size at the previous report.
    /// </summary>
    private int _reportedCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaintenanceOperation"/> class.
    /// </summary>
    /// <param name="catalogue">The catalogue to save.</param>
    /// <param name="environment">The environment file to save.</param>
    /// <param name="settings">The settings that carry the interval.</param>
    /// <param name="logger">The logger that reports progress and write failures.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public MaintenanceOperation(
        IModelCatalogue catalogue,
        IEnvironmentFile environment,
        ScannerSettings settings,
        ILogger<MaintenanceOperation> logger)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _catalogue = catalogue;
        _environment = environment;
        _settings = settings;
        _logger = logger;
        _reportedCount = catalogue.Count;
    }

    /// <summary>
    /// Saves on a timer until cancelled, then saves once more.
    /// </summary>
    /// <param name="cancellationToken">A token used to end the loop.</param>
    /// <returns>A task that completes once the final save has been attempted.</returns>
    /// <remarks>
    /// The final save is forced and runs after the loop rather than inside it, because a stop request
    /// cancels the loop and the changes made since the last tick are exactly the ones a stop would
    /// otherwise throw away.
    /// </remarks>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_settings.FlushInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await SaveAsync(force: false, cancellationToken);
            Report();
        }

        await SaveAsync(force: true, CancellationToken.None);
    }

    /// <summary>
    /// Commits both files.
    /// </summary>
    /// <param name="force">Whether to write the catalogue regardless of how long the changes have waited.</param>
    /// <param name="cancellationToken">A token used to abandon the write.</param>
    /// <returns>A task that completes when the write has been attempted.</returns>
    private async Task SaveAsync(bool force, CancellationToken cancellationToken)
    {
        try
        {
            await _catalogue.FlushAsync(force, cancellationToken);
            await _environment.FlushAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            _logger.LogError(
                exception,
                "The catalogue at {Path} could not be saved. Scanning continues; the next attempt is in {Seconds} second(s).",
                _settings.CataloguePath,
                _settings.FlushInterval.TotalSeconds);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(
                exception,
                "The catalogue at {Path} could not be saved. Scanning continues; the next attempt is in {Seconds} second(s).",
                _settings.CataloguePath,
                _settings.FlushInterval.TotalSeconds);
        }
    }

    /// <summary>
    /// Reports the catalogue's growth since the previous report.
    /// </summary>
    /// <remarks>
    /// The growth is signed. A catalogue that shrinks -- which happens when rows are deduplicated while
    /// loading, or when an operator edits the file between runs -- should say so rather than report a
    /// negative number as an increase.
    /// </remarks>
    private void Report()
    {
        int count = _catalogue.Count;
        int growth = count - _reportedCount;

        if (growth != 0)
        {
            _logger.LogInformation(
                "Catalogue holds {Count} row(s), {Growth} since the last report.",
                count,
                growth.ToString("+#;-#;0", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            _logger.LogDebug("Catalogue holds {Count} row(s), unchanged.", count);
        }

        _reportedCount = count;
    }
}
