namespace Scanner.Indexer.Models;

/// <summary>
/// The cheap identity of one revision of the catalogue document.
/// </summary>
/// <remarks>
/// <para>
/// The scanner rewrites its catalogue in full rather than appending to it, so a migration pass is
/// required whenever the document is replaced. Detecting that cheaply matters: the document is tens of
/// megabytes, and hashing it would cost more than many migrations do.
/// </para>
/// <para>
/// Length plus last-write time is sufficient here because the scanner publishes with an atomic replace.
/// A replacement always changes the file's identity, and no in-place writer exists to produce a
/// same-length, same-timestamp edit.
/// </para>
/// </remarks>
/// <param name="Length">The length of the document in bytes.</param>
/// <param name="WriteTicks">The document's last-write time in UTC ticks.</param>
public sealed record IndexFingerprint(long Length, long WriteTicks)
{
    /// <summary>
    /// The fingerprint that never matches a real document.
    /// </summary>
    /// <remarks>
    /// Used as the initial value so that the first inspection of a catalogue always schedules a
    /// migration, even when the document happens to be zero bytes long.
    /// </remarks>
    public static IndexFingerprint Unknown { get; } = new(-1, -1);
}
