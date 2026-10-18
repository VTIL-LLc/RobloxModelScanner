using System.Globalization;

using Microsoft.Extensions.Logging;

using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Operations;

/// <summary>
/// Walks the asset identifier space upward, looking for models that are not yet recorded.
/// </summary>
/// <remarks>
/// <para>
/// Identifiers are allocated from one sequence, so the sweep simply asks about the next block of them and
/// repeats. Most blocks contain nothing interesting -- the identifiers of places, audio and images are
/// interleaved with those of models -- and the pass is written to make that unremarkable: an empty block
/// costs one request and produces no log line, because a sweep that reported every empty block would
/// write a hundred lines for every model it found.
/// </para>
/// <para>
/// The cursor is the pass's only durable state and it is stored in the environment file, so an
/// interrupted sweep resumes where it stopped instead of re-walking a range it has already paid for. It
/// advances only past a block that was actually answered, which is why the toolbox distinguishes an empty
/// answer from no answer at all: advancing past an unanswered block would step over a hundred identifiers
/// that were never looked at, and nothing would ever return to them.
/// </para>
/// <para>
/// The pass does not wait for the inspections it submits. The dispatcher bounds how many run at once and
/// hands back a task for each; a fixed number of the most recent are held so that the pass cannot run
/// arbitrarily far ahead of the work it has created, and when that window is full the pass waits for the
/// oldest submission rather than for any particular one.
/// </para>
/// </remarks>
public sealed class SweepOperation
{
    /// <summary>
    /// The setting the cursor is persisted under.
    /// </summary>
    /// <remarks>
    /// Preserved exactly from the previous revision, because the value already in every installation's
    /// environment file is the whole of the scanner's resume state.
    /// </remarks>
    private const string StartIdKey = "START_ID";

    /// <summary>
    /// How long to wait before asking again about a block that could not be answered.
    /// </summary>
    /// <remarks>
    /// The wait exists so that an unreachable endpoint becomes a slow scan rather than a busy loop. It is
    /// deliberately not a retry-and-forget: the identifiers in the block were never examined, so the block
    /// is asked again and the cursor does not move.
    /// </remarks>
    private static readonly TimeSpan UnansweredBlockDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The catalogue the pass writes into and deduplicates against.
    /// </summary>
    private readonly IModelCatalogue _catalogue;

    /// <summary>
    /// The environment file the cursor is persisted to.
    /// </summary>
    private readonly IEnvironmentFile _environment;

    /// <summary>
    /// The catalogue the identifiers are asked about.
    /// </summary>
    private readonly IRobloxToolbox _toolbox;

    /// <summary>
    /// The dispatcher the candidates are handed to.
    /// </summary>
    private readonly IInspectionDispatcher _dispatcher;

    /// <summary>
    /// The operator's pause and stop request.
    /// </summary>
    private readonly IScanControl _control;

    /// <summary>
    /// The settings that carry the cursor, the batch size and the ignored creators.
    /// </summary>
    private readonly ScannerSettings _settings;

    /// <summary>
    /// The logger that records the pass's progress.
    /// </summary>
    private readonly ILogger<SweepOperation> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SweepOperation"/> class.
    /// </summary>
    /// <param name="catalogue">The catalogue the pass writes into and deduplicates against.</param>
    /// <param name="environment">The environment file the cursor is persisted to.</param>
    /// <param name="toolbox">The catalogue the identifiers are asked about.</param>
    /// <param name="dispatcher">The dispatcher the candidates are handed to.</param>
    /// <param name="control">The operator's pause and stop request.</param>
    /// <param name="settings">The settings that carry the cursor, the batch size and the ignored creators.</param>
    /// <param name="logger">The logger that records the pass's progress.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SweepOperation(
        IModelCatalogue catalogue,
        IEnvironmentFile environment,
        IRobloxToolbox toolbox,
        IInspectionDispatcher dispatcher,
        IScanControl control,
        ScannerSettings settings,
        ILogger<SweepOperation> logger)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _catalogue = catalogue;
        _environment = environment;
        _toolbox = toolbox;
        _dispatcher = dispatcher;
        _control = control;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Runs the pass until a stop is requested or the identifier space is exhausted.
    /// </summary>
    /// <param name="cancellationToken">A token used to abandon the pass.</param>
    /// <returns>A task that completes when the pass has wound down and its inspections have drained.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        long cursor = Math.Max(1, _settings.StartId);
        long blocks = 0;
        long examined = 0;
        long ignored = 0;

        List<Task> window = new(_settings.ScanConcurrency);

        _logger.LogInformation(
            "Sweeping identifiers from {Cursor} in blocks of {BatchSize}.",
            cursor,
            _settings.BatchSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            await _control.WaitWhilePausedAsync(cancellationToken);

            if (_control.StopRequested)
            {
                _logger.LogInformation("A stop was requested; the sweep will not start another block.");
                break;
            }

            if (cursor > long.MaxValue - _settings.BatchSize)
            {
                _logger.LogInformation("The identifier space is exhausted at {Cursor}.", cursor);
                break;
            }

            List<long> block = BuildBlock(cursor);

            IReadOnlyList<ToolboxItem>? models = await _toolbox.GetModelsAsync(block, cancellationToken);

            if (models is null)
            {
                _logger.LogWarning(
                    "Identifiers {First} to {Last} could not be looked up. They will be asked for again.",
                    block[0],
                    block[^1]);

                await Task.Delay(UnansweredBlockDelay, cancellationToken);
                continue;
            }

            cursor += block.Count;
            blocks++;

            foreach (ToolboxItem item in models)
            {
                if (IsIgnored(item))
                {
                    ignored++;
                    continue;
                }

                window.Add(_dispatcher.DispatchAsync(item, replace: false));
                examined++;

                if (window.Count >= _settings.ScanConcurrency)
                {
                    // The oldest submission is awaited rather than the newest. Waiting for the newest would
                    // let the window hold a task that has only just started while the ones before it have
                    // already finished, which would keep more work alive than the bound intends.
                    Task completed = await Task.WhenAny(window);
                    window.Remove(completed);
                }
            }

            PersistCursor(cursor);

            if (models.Count > 0)
            {
                _logger.LogInformation(
                    "Identifiers {First} to {Last}: {Count} model(s) returned, {Recorded} recorded in total.",
                    block[0],
                    block[^1],
                    models.Count,
                    _catalogue.Count);
            }
            else
            {
                _logger.LogDebug("Identifiers {First} to {Last}: nothing.", block[0], block[^1]);
            }
        }

        await _dispatcher.DrainAsync();

        _logger.LogInformation(
            "Sweep finished after {Blocks} block(s): {Examined} model(s) handed over, {Ignored} ignored, cursor at {Cursor}.",
            blocks,
            examined,
            ignored,
            cursor);
    }

    /// <summary>
    /// Builds the next block of identifiers.
    /// </summary>
    /// <param name="cursor">The first identifier of the block.</param>
    /// <returns>The identifiers to ask about.</returns>
    private List<long> BuildBlock(long cursor)
    {
        List<long> block = new(_settings.BatchSize);
        for (int index = 0; index < _settings.BatchSize; index++) block.Add(cursor + index);

        return block;
    }

    /// <summary>
    /// Determines whether an asset belongs to an ignored creator.
    /// </summary>
    /// <param name="item">The asset to test.</param>
    /// <returns><see langword="true"/> when the creator is on the ignore list.</returns>
    /// <remarks>
    /// The filter is applied before the asset is downloaded rather than after it is analysed, because the
    /// cost being saved is the download. An identifier that cannot be read as a number is not ignored:
    /// a creator Roblox does not describe is not the same as a creator the operator excluded.
    /// </remarks>
    private bool IsIgnored(ToolboxItem item) =>
        long.TryParse(item.CreatorId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long creator) &&
        _settings.IgnoredCreators.Contains(creator);

    /// <summary>
    /// Records the cursor for the next run.
    /// </summary>
    /// <param name="cursor">The next identifier to ask about.</param>
    /// <remarks>
    /// The write itself happens on the next flush, which is what makes this cheap enough to call once per
    /// block. It is set here rather than at the end of the pass so that a crash costs at most the block in
    /// progress.
    /// </remarks>
    private void PersistCursor(long cursor) =>
        _environment.Set(StartIdKey, cursor.ToString(CultureInfo.InvariantCulture));
}
