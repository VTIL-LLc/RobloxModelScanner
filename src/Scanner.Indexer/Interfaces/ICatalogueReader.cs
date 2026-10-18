using Scanner.Indexer.Models;

namespace Scanner.Indexer.Interfaces;

/// <summary>
/// Reads the scanner's catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is the scanner's file and no implementation of this interface may write it. A reader
/// opens it read-only and asks it two questions: which revision it is at, and what rows it holds.
/// </para>
/// <para>
/// An implementation is expected to yield rows as the statement walks the table rather than materialising
/// them. The migration writes each row into the index as it arrives, so there is never a reason for a
/// second copy of a hundred and twenty thousand rows to exist in memory, and the reader is the component
/// that would otherwise create one.
/// </para>
/// </remarks>
public interface ICatalogueReader
{
    /// <summary>
    /// Reads the revision the catalogue is at.
    /// </summary>
    /// <param name="path">The absolute path to the catalogue database.</param>
    /// <returns>The catalogue's revision, or <see langword="null"/> when no catalogue exists at the path.</returns>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">
    /// Thrown when a catalogue exists there but cannot be read as one.
    /// </exception>
    /// <remarks>
    /// The revision is a number the scanner writes inside the same transaction that changes the rows, which
    /// is what makes it trustworthy as a staleness test. It is read on every inspect pass, so it is one
    /// keyed lookup and nothing else.
    /// </remarks>
    long? ReadRevision(string path);

    /// <summary>
    /// Reads the catalogue at the supplied path.
    /// </summary>
    /// <param name="path">The absolute path to the catalogue database.</param>
    /// <param name="cancellationToken">A token used to abandon the read.</param>
    /// <returns>The catalogue rows, in the order the catalogue found them.</returns>
    /// <exception cref="IOException">Thrown when the catalogue cannot be opened or read.</exception>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">
    /// Thrown when a catalogue exists there but cannot be read as one.
    /// </exception>
    IEnumerable<CatalogueRow> Read(string path, CancellationToken cancellationToken);
}
