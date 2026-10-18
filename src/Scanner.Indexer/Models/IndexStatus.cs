namespace Scanner.Indexer.Models;

/// <summary>
/// A snapshot of the indexing service's view of the world.
/// </summary>
/// <remarks>
/// This is what the internal status endpoint answers with, and what the scanner API relays so an
/// operator can tell "the catalogue is quiet" apart from "the indexer has stopped".
/// </remarks>
/// <param name="CatalogueExists">Whether the scanner's catalogue database is present on disk.</param>
/// <param name="CatalogueRevision">
/// The revision the catalogue is currently at, or <see langword="null"/> when it could not be read.
/// </param>
/// <param name="IndexExists">Whether the SQLite index database exists.</param>
/// <param name="IndexedModels">The number of models in the current index.</param>
/// <param name="IndexedDetections">The number of detection rows in the current index.</param>
/// <param name="IndexedFingerprint">
/// The catalogue revision the index currently reflects, or <see cref="IndexFingerprint.Unknown"/> when
/// no migration has ever completed.
/// </param>
/// <param name="IndexedUtc">The instant the last migration committed, or <see langword="null"/>.</param>
/// <param name="IsStale">
/// Whether the catalogue differs from the revision the index reflects. False when the catalogue cannot be
/// read at all, because there is nothing to compare and the failure to read is already stated by
/// <see cref="CatalogueRevision"/> being <see langword="null"/>.
/// </param>
/// <param name="LastAttemptUtc">The instant the last migration attempt started, or <see langword="null"/>.</param>
/// <param name="LastFailure">The last migration failure, or <see langword="null"/> when healthy.</param>
/// <param name="GeneratedUtc">The instant this snapshot was produced.</param>
public sealed record IndexStatus(
    bool CatalogueExists,
    long? CatalogueRevision,
    bool IndexExists,
    int IndexedModels,
    int IndexedDetections,
    IndexFingerprint IndexedFingerprint,
    DateTimeOffset? IndexedUtc,
    bool IsStale,
    DateTimeOffset? LastAttemptUtc,
    string? LastFailure,
    DateTimeOffset GeneratedUtc);
