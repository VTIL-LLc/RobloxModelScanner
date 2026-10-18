using Microsoft.Data.Sqlite;

using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Interfaces;

/// <summary>
/// Reads the SQLite index the indexing service publishes.
/// </summary>
/// <remarks>
/// <para>
/// The API is a consumer of the index, not a participant in it. Connections are opened read-only, no
/// statement here creates a table, and there is nothing to call before querying: a request that arrives
/// against an index that has just been replaced opens the new one, and a request that arrives against no
/// index at all is refused with a reason rather than repaired. That division is what allows the two
/// processes to run side by side without coordinating.
/// </para>
/// <para>
/// The cost of a read is one stat plus, at most once per revision of the file, one keyed read of the
/// schema version. A stat is cheap enough to run on every request, which means the reader notices a
/// deleted or replaced index immediately rather than after a cache interval.
/// </para>
/// </remarks>
public interface IScannerIndexReader
{
    /// <summary>
    /// Gets the resolved file locations the reader was configured with.
    /// </summary>
    ScannerPaths Paths { get; }

    /// <summary>
    /// Gets a value indicating whether the source catalogue document exists on disk.
    /// </summary>
    /// <remarks>
    /// This is the scanner's own <c>models.json</c>, not the index. It answers a different question --
    /// "has the scanner produced anything yet" -- and is reported alongside the index's state so that an
    /// operator can tell a scanner that has never run from an indexer that has never run.
    /// </remarks>
    bool CatalogueExists { get; }

    /// <summary>
    /// Gets a value indicating whether an index database exists on disk.
    /// </summary>
    bool IndexExists { get; }

    /// <summary>
    /// Opens a read-only connection to the index.
    /// </summary>
    /// <returns>An open connection that the caller is responsible for disposing.</returns>
    /// <exception cref="Exceptions.IndexUnavailableException">
    /// No index exists, or the one that does cannot be read or was built with an unfamiliar schema.
    /// </exception>
    /// <remarks>
    /// The check happens here rather than in a separate call so that it cannot be skipped and cannot go
    /// stale between the check and the read.
    /// </remarks>
    SqliteConnection OpenConnection();
}
