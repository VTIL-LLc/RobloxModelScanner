using RobloxModelScanner.Models;

namespace RobloxModelScanner.Interfaces;

/// <summary>
/// The scanner's view of the JSON catalogue it owns.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is a single JSON array on disk, which has one important consequence the interface is
/// shaped around: saving any part of it means rewriting all of it. The interface therefore separates
/// changing the catalogue from saving it. A caller appends or replaces a row as often as it likes and
/// the write happens later, in one pass, when the implementation decides it is due. An implementation
/// that saved on every mutation would be quadratic in the size of the document, which is precisely the
/// behaviour this design exists to avoid.
/// </para>
/// <para>
/// The whole document is held in memory. That is a deliberate trade: a forty-megabyte array is a
/// bounded, knowable working set, whereas streaming a rewrite and mutating rows in place at the same
/// time is not possible with a format that requires an enclosing bracket, and re-reading the file to
/// find one row is what made the previous revision spend its life parsing.
/// </para>
/// </remarks>
public interface IModelCatalogue
{
    /// <summary>
    /// Gets the number of rows currently known.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the number of changes that have not yet been written.
    /// </summary>
    int UnsavedCount { get; }

    /// <summary>
    /// Reads the catalogue from disk.
    /// </summary>
    /// <param name="cancellationToken">A token used to abandon the read.</param>
    /// <returns>A task that completes when the document has been read.</returns>
    /// <remarks>
    /// Loading is explicit and separate from construction so that a failure to read the document is
    /// reported at a point where the caller can decide what to do about it. A missing catalogue and an
    /// unreadable one are different problems with different answers -- a missing one is a fresh
    /// installation, an unreadable one means the scanner must refuse to start rather than sweep into a
    /// document it would then overwrite.
    /// </remarks>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether an identifier is already recorded.
    /// </summary>
    /// <param name="modelId">The asset identifier.</param>
    /// <returns><see langword="true"/> when the identifier is present.</returns>
    bool Contains(long modelId);

    /// <summary>
    /// Records a newly discovered row.
    /// </summary>
    /// <param name="record">The row to append.</param>
    /// <returns><see langword="true"/> when the row was added, <see langword="false"/> when the identifier was already present.</returns>
    /// <remarks>
    /// The append is refused for an identifier that is already known, which makes the two callers that
    /// can race -- a worker finishing an inspection and the same asset arriving again in a later batch --
    /// harmless rather than a source of duplicate rows.
    /// </remarks>
    bool Append(CatalogueRecord record);

    /// <summary>
    /// Replaces the row for an identifier.
    /// </summary>
    /// <param name="record">The corrected row.</param>
    /// <returns><see langword="true"/> when a row was replaced, <see langword="false"/> when the identifier was not present.</returns>
    /// <remarks>
    /// The identifier is the key, so a replacement never changes it and never adds a row. This is what
    /// the backfill uses, and it is why a repaired catalogue cannot gain a duplicate of a row that was
    /// already there.
    /// </remarks>
    bool Replace(CatalogueRecord record);

    /// <summary>
    /// Lists the rows that carry no descriptive metadata.
    /// </summary>
    /// <returns>A snapshot of the rows to repair, in catalogue order.</returns>
    /// <remarks>
    /// A snapshot rather than a live view, because the caller walks it while replacing the very rows it
    /// is walking. The set is bounded by the number of rows written by a revision that failed to capture
    /// metadata, and it shrinks as the walk proceeds, so it is only large once.
    /// </remarks>
    IReadOnlyList<CatalogueRecord> RowsWithoutMetadata();

    /// <summary>
    /// Writes the catalogue when the implementation decides a write is due.
    /// </summary>
    /// <param name="force">Whether to write regardless of how long the changes have been waiting.</param>
    /// <param name="cancellationToken">A token used to abandon the write.</param>
    /// <returns>A task that completes when the file matches the in-memory catalogue.</returns>
    Task FlushAsync(bool force, CancellationToken cancellationToken);
}
