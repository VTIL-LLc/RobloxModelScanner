namespace RobloxModelScanner.Interfaces;

/// <summary>
/// The operator's pause and stop request, as the scanner sees it.
/// </summary>
/// <remarks>
/// <para>
/// The request lives in a file the administration API writes, so reading it is not free and its answer
/// can change between two reads. Both passes therefore ask this object at the points where it is safe to
/// act -- between batches -- rather than being handed a token that was decided some time ago.
/// </para>
/// <para>
/// A pause is not the same instruction as a stop, and the difference is what the pass does with work in
/// flight. A pause stops starting new work and lets the current batch finish; a stop additionally drains
/// what has already been submitted, because those assets have been downloaded and discarding them would
/// waste the only part of the work that cannot be recovered by asking again.
/// </para>
/// </remarks>
public interface IScanControl
{
    /// <summary>
    /// Gets a value indicating whether a stop has been requested.
    /// </summary>
    /// <remarks>
    /// This is a question about the file, not a latch, so it can stop being true if the request is
    /// withdrawn. That is deliberate: it means the scanner does not have to be restarted to be told that
    /// the stop was a mistake, because it only consults the answer when it is about to start work.
    /// </remarks>
    bool StopRequested { get; }

    /// <summary>
    /// Waits while a pause is in force.
    /// </summary>
    /// <param name="cancellationToken">A token used to abandon the wait.</param>
    /// <returns>A task that completes once the pause has been lifted.</returns>
    Task WaitWhilePausedAsync(CancellationToken cancellationToken);
}
