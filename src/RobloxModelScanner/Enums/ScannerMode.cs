namespace RobloxModelScanner.Enums;

/// <summary>
/// The pass the scanner performs when it starts.
/// </summary>
/// <remarks>
/// <para>
/// The two passes answer different questions about the same catalogue. A sweep asks "what exists out
/// there that has not been looked at yet" and only ever appends. A backfill asks "what have I already
/// recorded but failed to describe" and only ever replaces. They share the inspection that produces a
/// result but not the decision about what to do with it, so they are separate passes rather than one
/// pass with a flag threaded through it.
/// </para>
/// <para>
/// A backfill is expected to be run deliberately, once, against a catalogue that was written before the
/// metadata was captured. It walks the whole catalogue and therefore competes with the sweep for the
/// same outbound connections, which is why it is not simply always on.
/// </para>
/// </remarks>
public enum ScannerMode
{
    /// <summary>
    /// Walk the identifier space upward, recording every model that is not already known.
    /// </summary>
    Sweep = 0,

    /// <summary>
    /// Revisit the recorded rows whose metadata is absent and rebuild them.
    /// </summary>
    Backfill = 1
}
