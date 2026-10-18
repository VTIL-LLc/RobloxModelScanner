using Scanner.Indexer.Enums;

namespace Scanner.Indexer.Interfaces;

/// <summary>
/// Owns the SQLite index: it is the only component permitted to write it.
/// </summary>
/// <remarks>
/// Single-writer ownership is the reason every other process can open the database read-only. It removes
/// lock contention between the migration and the API's queries, and it means a request can never corrupt
/// the index or trigger an unbounded migration.
/// </remarks>
public interface IIndexWriter
{
    /// <summary>
    /// Migrates the catalogue into the index when the index does not already reflect it.
    /// </summary>
    /// <param name="ignoreThrottle">
    /// When <see langword="true"/>, the inspect interval is bypassed and the catalogue is inspected
    /// immediately. Used by the internal rebuild request so that an operator who knows the index is behind
    /// does not have to wait for the loop's next tick. The migration itself still only runs when the
    /// catalogue revision differs from the indexed one, so this can never be used to force needless work.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel the migration.</param>
    /// <returns>How the pass ended.</returns>
    /// <remarks>
    /// A failure never destroys the previous index: the migration is a single transaction that is either
    /// committed in full or rolled back. Concurrent callers are serialized; only one migration runs at a
    /// time and the others observe its result.
    /// </remarks>
    Task<IndexBuildOutcome> EnsureCurrentAsync(bool ignoreThrottle, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the catalogue revision the index currently reflects.
    /// </summary>
    /// <returns>The indexed revision, or <see cref="Models.IndexFingerprint.Unknown"/> when none has completed.</returns>
    Models.IndexFingerprint GetIndexedFingerprint();

    /// <summary>
    /// Gets the number of models and detections recorded by the last completed migration.
    /// </summary>
    /// <returns>The indexed model and detection counts.</returns>
    (int Models, int Detections) GetIndexedCounts();

    /// <summary>
    /// Gets the instant the last migration committed.
    /// </summary>
    /// <returns>The completion instant, or <see langword="null"/> when no migration has completed.</returns>
    DateTimeOffset? GetIndexedUtc();

    /// <summary>
    /// Gets the arity of the index schema currently on disk.
    /// </summary>
    /// <returns>The schema version, or zero when the database has never been written.</returns>
    /// <remarks>
    /// Consumers compare this against the version they expect and refuse to read an index they do not
    /// understand, rather than misinterpreting columns that have moved.
    /// </remarks>
    int GetSchemaVersion();
}
