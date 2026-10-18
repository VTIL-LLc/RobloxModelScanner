using Microsoft.Extensions.Logging;

using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Operations;

/// <summary>
/// Revisits the catalogue rows that carry no metadata and rebuilds them.
/// </summary>
/// <remarks>
/// <para>
/// A row with no name, no description, no creator and no store address is the signature of a revision
/// that recorded only the identifier. Those rows exist, they are the majority of the catalogue, and no
/// amount of sweeping will improve them: a sweep asks about identifiers it has not seen, and these have
/// been seen. Repairing them means asking about the same identifiers again and replacing what is there.
/// </para>
/// <para>
/// The pass is written to be interrupted. Rows are replaced as they are rebuilt and the catalogue is
/// flushed on its usual timer, so stopping part way through leaves the work done so far on disk. A second
/// run picks up the rows that are still empty and walks only those, which is also how the pass converges:
/// a row Roblox will not describe stays empty and is retried by the next run, and a row that is repaired
/// is never considered again.
/// </para>
/// <para>
/// Repaired rows are not published to the channels. They are assets the operator already has; announcing
/// them would bury the day's real findings under a re-run of the previous year's catalogue. The payload
/// is still kept when it scores highly enough, because the bytes are evidence rather than news.
/// </para>
/// </remarks>
public sealed class BackfillOperation
{
    /// <summary>
    /// How many blocks are examined between progress lines.
    /// </summary>
    /// <remarks>
    /// The pass can run for hours, so it reports periodically rather than only at the end. Reporting every
    /// block would produce one line per hundred rows, which over a catalogue of this size is a great many
    /// lines that say the same thing.
    /// </remarks>
    private const int BlocksBetweenReports = 50;

    /// <summary>
    /// How long to wait before asking again about a block that could not be answered.
    /// </summary>
    private static readonly TimeSpan UnansweredBlockDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The catalogue the rows are read from and replaced in.
    /// </summary>
    private readonly IModelCatalogue _catalogue;

    /// <summary>
    /// The catalogue the identifiers are asked about.
    /// </summary>
    private readonly IRobloxToolbox _toolbox;

    /// <summary>
    /// The dispatcher the models are handed to.
    /// </summary>
    private readonly IInspectionDispatcher _dispatcher;

    /// <summary>
    /// The operator's pause and stop request.
    /// </summary>
    private readonly IScanControl _control;

    /// <summary>
    /// The settings that carry the batch size and the ignored creators.
    /// </summary>
    private readonly ScannerSettings _settings;

    /// <summary>
    /// The logger that records the pass's progress.
    /// </summary>
    private readonly ILogger<BackfillOperation> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackfillOperation"/> class.
    /// </summary>
    /// <param name="catalogue">The catalogue the rows are read from and replaced in.</param>
    /// <param name="toolbox">The catalogue the identifiers are asked about.</param>
    /// <param name="dispatcher">The dispatcher the models are handed to.</param>
    /// <param name="control">The operator's pause and stop request.</param>
    /// <param name="settings">The settings that carry the batch size and the ignored creators.</param>
    /// <param name="logger">The logger that records the pass's progress.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public BackfillOperation(
        IModelCatalogue catalogue,
        IRobloxToolbox toolbox,
        IInspectionDispatcher dispatcher,
        IScanControl control,
        ScannerSettings settings,
        ILogger<BackfillOperation> logger)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _catalogue = catalogue;
        _toolbox = toolbox;
        _dispatcher = dispatcher;
        _control = control;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Repairs every row that carries no metadata, once.
    /// </summary>
    /// <param name="cancellationToken">A token used to abandon the pass.</param>
    /// <returns>A task that completes when the pass has wound down and its inspections have drained.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// One pass, not a loop. Rows that remain empty at the end are rows Roblox does not describe, and
    /// repeating the pass immediately would ask the same questions to receive the same silence. Running
    /// the pass again later, by hand, is how they are retried.
    /// </remarks>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogueRecord> rows = _catalogue.RowsWithoutMetadata();

        _logger.LogInformation(
            "Repairing {Count} row(s) that carry no metadata, in blocks of {BatchSize}.",
            rows.Count,
            _settings.BatchSize);

        if (rows.Count == 0)
        {
            _logger.LogInformation("No row needs repair.");
            return;
        }

        List<Task> window = new(_settings.ScanConcurrency);
        int position = 0;
        int blocks = 0;
        int handedOver = 0;
        int ignored = 0;

        while (position < rows.Count && !cancellationToken.IsCancellationRequested)
        {
            await _control.WaitWhilePausedAsync(cancellationToken);

            if (_control.StopRequested)
            {
                _logger.LogInformation("A stop was requested; the repair will not start another block.");
                break;
            }

            List<long> block = BuildBlock(rows, position);

            IReadOnlyList<ToolboxItem>? models = await _toolbox.GetModelsAsync(block, cancellationToken);

            if (models is null)
            {
                // The block is not stepped over. These rows are the whole point of the pass, and moving on
                // would leave them exactly as they were while reporting that the pass had finished.
                _logger.LogWarning(
                    "Identifiers {First} to {Last} could not be looked up. They will be asked for again.",
                    block[0],
                    block[^1]);

                await Task.Delay(UnansweredBlockDelay, cancellationToken);
                continue;
            }

            position += block.Count;
            blocks++;

            foreach (ToolboxItem item in models)
            {
                if (IsIgnored(item))
                {
                    ignored++;
                    continue;
                }

                window.Add(_dispatcher.DispatchAsync(item, replace: true));
                handedOver++;

                if (window.Count >= _settings.ScanConcurrency)
                {
                    Task completed = await Task.WhenAny(window);
                    window.Remove(completed);
                }
            }

            if (blocks % BlocksBetweenReports == 0)
            {
                _logger.LogInformation(
                    "Repair progress: {Position} of {Total} row(s) looked up, {HandedOver} handed over.",
                    position,
                    rows.Count,
                    handedOver);
            }
        }

        await _dispatcher.DrainAsync();

        // Read back rather than counted. The catalogue is the record of what was achieved, and it accounts
        // for a row that Roblox described but that produced no row of its own -- which happens when an
        // asset is refused by the content host even though the catalogue still lists it.
        IReadOnlyList<CatalogueRecord> remaining = _catalogue.RowsWithoutMetadata();

        _logger.LogInformation(
            "Repair finished after {Blocks} block(s): {HandedOver} row(s) handed over, {Ignored} ignored, {Remaining} still without metadata.",
            blocks,
            handedOver,
            ignored,
            remaining.Count);
    }

    /// <summary>
    /// Builds the next block of identifiers to look up.
    /// </summary>
    /// <param name="rows">The rows being repaired.</param>
    /// <param name="position">The first row of the block.</param>
    /// <returns>The identifiers to ask about.</returns>
    private List<long> BuildBlock(IReadOnlyList<CatalogueRecord> rows, int position)
    {
        int count = Math.Min(_settings.BatchSize, rows.Count - position);

        List<long> block = new(count);
        for (int index = 0; index < count; index++) block.Add(rows[position + index].Model);

        return block;
    }

    /// <summary>
    /// Determines whether an asset belongs to an ignored creator.
    /// </summary>
    /// <param name="item">The asset to test.</param>
    /// <returns><see langword="true"/> when the creator is on the ignore list.</returns>
    /// <remarks>
    /// Applied for the same reason the sweep applies it -- to save the download -- and with the same
    /// caveat: the ignored creator's row is left empty rather than filled in, so an exclude list also
    /// excludes a row from being repaired.
    /// </remarks>
    private bool IsIgnored(ToolboxItem item) =>
        long.TryParse(
            item.CreatorId,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out long creator) &&
        _settings.IgnoredCreators.Contains(creator);
}
