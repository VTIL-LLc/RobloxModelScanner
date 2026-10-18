using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using RobloxModelScanner.Enums;
using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;
using RobloxModelScanner.Operations;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// Runs inspections concurrently and commits their rows to the catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The two passes that use this are both bursts of work followed by long silences: a sweep submits a
/// hundred candidates the moment a batch is answered, and a repair submits a hundred the moment a batch
/// of empty rows is read. Running them inline would leave the network idle for the whole of a download
/// and then stall the pass that found them; running them without a bound would open as many connections
/// as there are candidates and be throttled by Roblox within seconds.
/// </para>
/// <para>
/// A model is inspected once at a time. The same identifier can arrive twice -- overlapping batches, or
/// a repair that reads a row while a sweep is finding it -- and the second arrival is dropped rather than
/// queued. Queueing it would mean downloading the same asset twice to produce the same row, and the two
/// rows would then race to be written.
/// </para>
/// <para>
/// The dispatcher owns the decision to append or replace, because it is the only component that sees
/// both the purpose and the catalogue at the moment the row is ready.
/// </para>
/// </remarks>
/// <seealso cref="IInspectionDispatcher"/>
public sealed class InspectionDispatcher : IInspectionDispatcher
{
    /// <summary>
    /// The catalogue rows are committed to.
    /// </summary>
    private readonly IModelCatalogue _catalogue;

    /// <summary>
    /// The operation that examines one asset.
    /// </summary>
    private readonly AssetInspectionOperation _inspection;

    /// <summary>
    /// The bound on how many examinations may run at once.
    /// </summary>
    private readonly SemaphoreSlim _slots;

    /// <summary>
    /// The identifiers currently being examined.
    /// </summary>
    private readonly ConcurrentDictionary<long, bool> _inFlight = new();

    /// <summary>
    /// The examinations that have been started and have not yet finished.
    /// </summary>
    private readonly ConcurrentDictionary<long, Task> _running = new();

    /// <summary>
    /// The token that abandons work already in flight.
    /// </summary>
    private readonly CancellationToken _abandon;

    /// <summary>
    /// The logger that records a failure that escaped an inspection.
    /// </summary>
    private readonly ILogger<InspectionDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InspectionDispatcher"/> class.
    /// </summary>
    /// <param name="catalogue">The catalogue rows are committed to.</param>
    /// <param name="inspection">The operation that examines one asset.</param>
    /// <param name="assetConcurrency">The greatest number of examinations that may run at once.</param>
    /// <param name="abandon">The token that abandons work already in flight.</param>
    /// <param name="logger">The logger that records a failure that escaped an inspection.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="assetConcurrency"/> is not positive.</exception>
    /// <remarks>
    /// The token is deliberately separate from the scan's own cancellation. A stop request means \"finish
    /// what you have and exit\", and discarding a downloaded payload because the operator asked the
    /// scanner to wind down would waste work that is already paid for. This token is for the other
    /// situation -- a second interrupt, or a process that must go now -- where the work is forfeited
    /// rather than completed.
    /// </remarks>
    public InspectionDispatcher(
        IModelCatalogue catalogue,
        AssetInspectionOperation inspection,
        int assetConcurrency,
        CancellationToken abandon,
        ILogger<InspectionDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetConcurrency);

        _catalogue = catalogue;
        _inspection = inspection;
        _slots = new SemaphoreSlim(assetConcurrency, assetConcurrency);
        _abandon = abandon;
        _logger = logger;
    }

    /// <summary>
    /// Gets the number of examinations currently running or waiting for a slot.
    /// </summary>
    public int PendingCount => _running.Count;

    /// <inheritdoc />
    public Task DispatchAsync(ToolboxItem item, bool replace)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Already inspected by an earlier pass, and a discovery adds nothing by repeating it. A repair is
        // the exception: it exists precisely to revisit a row that is already recorded.
        if (!replace && _catalogue.Contains(item.ModelId)) return Task.CompletedTask;

        if (!_inFlight.TryAdd(item.ModelId, true)) return Task.CompletedTask;

        Task task = RunAsync(item, replace);
        _running[item.ModelId] = task;

        return task;
    }

    /// <inheritdoc />
    public async Task DrainAsync()
    {
        // Looped rather than awaited once. A finishing examination removes itself from the running set,
        // and a snapshot taken while that is happening can miss an entry that was added a moment later;
        // repeating until the set is empty costs nothing when it already is.
        while (!_running.IsEmpty)
        {
            Task[] pending = [.. _running.Values];
            if (pending.Length == 0) break;

            // No examination is allowed to surface an exception -- each one records its own failure -- so
            // waiting for all of them cannot throw here, which is what makes this safe to call during a
            // shutdown.
            await Task.WhenAll(pending);
        }
    }

    /// <summary>
    /// Examines one asset, within the concurrency bound, and commits its row.
    /// </summary>
    /// <param name="item">The asset's catalogue metadata.</param>
    /// <param name="replace">Whether the row replaces an existing one rather than being appended.</param>
    /// <returns>A task that completes when the examination and its bookkeeping are finished.</returns>
    /// <remarks>
    /// The waiting for a slot happens inside the task rather than before it, so that a pass submitting a
    /// hundred candidates is not held up by the bound; it hands over the work and gets on with finding
    /// the next batch while the queue drains behind it.
    /// </remarks>
    private async Task RunAsync(ToolboxItem item, bool replace)
    {
        try
        {
            await _slots.WaitAsync(_abandon);

            try
            {
                CatalogueRecord? record = await _inspection.InspectAsync(
                    item,
                    replace ? InspectionPurpose.Repair : InspectionPurpose.Discovery,
                    _abandon);

                if (record is null) return;

                if (replace) _catalogue.Replace(record);
                else _catalogue.Append(record);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException) when (_abandon.IsCancellationRequested)
        {
            // The scanner is going away and the examination was abandoned with it.
        }
        catch (Exception exception)
        {
            // An examination that fails must not take the scan with it. One asset that the host refuses
            // in an unexpected way is not a reason to stop examining a hundred thousand others, and the
            // failure is recorded with the identifier so it can be looked at afterwards.
            _logger.LogError(exception, "Asset {ModelId} failed to be examined.", item.ModelId);
        }
        finally
        {
            _inFlight.TryRemove(item.ModelId, out _);
            _running.TryRemove(item.ModelId, out _);
        }
    }
}
