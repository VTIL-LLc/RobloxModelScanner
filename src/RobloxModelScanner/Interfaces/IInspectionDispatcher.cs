using RobloxModelScanner.Models;

namespace RobloxModelScanner.Interfaces;

/// <summary>
/// Hands a discovered model to the inspection stage.
/// </summary>
/// <remarks>
/// <para>
/// The pass that finds candidates and the stage that inspects them run at different speeds and are
/// allowed to diverge. Finding is a handful of large requests; inspecting is a download and a scan of
/// it, and the two are bounded by different limits. This interface is the boundary between them: the
/// pass submits work and does not wait for it, and the implementation decides how much may run at once.
/// </para>
/// <para>
/// Submitting deliberately returns a task that completes when the inspection does. A caller that does
/// not care simply ignores it, and a caller that needs to apply back pressure -- because an unbounded
/// queue of downloads would exhaust sockets long before it exhausted memory -- can hold the task and
/// wait for the oldest when it has too many. Making the task optional rather than pushing the decision
/// into the implementation keeps the policy with the caller that knows its own rate.
/// </para>
/// <para>
/// Submitting the same identifier twice while the first inspection is still running is refused, so a
/// model that appears in two overlapping batches is inspected once.
/// </para>
/// </remarks>
public interface IInspectionDispatcher
{
    /// <summary>
    /// Submits a model for inspection.
    /// </summary>
    /// <param name="item">The model's catalogue metadata.</param>
    /// <param name="replace">Whether the resulting row should replace an existing one rather than be appended.</param>
    /// <returns>A task that completes when the inspection and its bookkeeping are finished.</returns>
    Task DispatchAsync(ToolboxItem item, bool replace);

    /// <summary>
    /// Waits for every submitted inspection to finish.
    /// </summary>
    /// <returns>A task that completes when nothing is in flight.</returns>
    /// <remarks>
    /// Used at shutdown, so that a stop request does not discard inspections that are already holding
    /// downloaded payloads.
    /// </remarks>
    Task DrainAsync();
}
