namespace Scanner.Indexer.Models;

/// <summary>
/// The identity of one revision of the catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The scanner bumps a revision number inside the same transaction that changes a row, and a migration pass
/// is required whenever that number moves. Detecting the change costs one keyed lookup, which matters
/// because the check runs on every inspect pass over a catalogue of a hundred thousand rows.
/// </para>
/// <para>
/// The revision replaced a pair of file statistics -- the document's length and last-write time -- when the
/// catalogue stopped being a JSON document. Those two were sufficient for a file that was replaced
/// atomically and whose length grew with its contents. They are not sufficient for a database that is
/// written in place: its length moves for reasons that are not content, a checkpoint or a reused page among
/// them, and a commit can land inside the resolution of the file system clock and leave the timestamp where
/// it was. A number the writer maintains cannot do either of those things.
/// </para>
/// </remarks>
/// <param name="Revision">The catalogue's revision number.</param>
public sealed record IndexFingerprint(long Revision)
{
    /// <summary>
    /// The fingerprint that never matches a real revision.
    /// </summary>
    /// <remarks>
    /// Used as the initial value so that the first inspection of a catalogue always schedules a migration,
    /// including for a catalogue that has never been written to and therefore reports revision zero.
    /// </remarks>
    public static IndexFingerprint Unknown { get; } = new(-1);
}
