using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Models;

namespace Scanner.Indexer.Implementation;

/// <summary>
/// Assembles the current view of the catalogue and the index.
/// </summary>
/// <remarks>
/// The snapshot is built from a single stat of the catalogue plus the writer's in-memory bookkeeping, so
/// polling it is cheap and cannot itself trigger a migration. Reporting the failure text alongside the
/// staleness flag is deliberate: an operator needs to tell "the catalogue has not changed" apart from
/// "every migration attempt is being rejected".
/// </remarks>
public sealed class IndexStatusProvider : IIndexStatusProvider
{
    private readonly SqliteIndexWriter _writer;
    private readonly IndexerPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexStatusProvider"/> class.
    /// </summary>
    /// <param name="writer">The writer that owns the index state.</param>
    /// <param name="paths">The resolved catalogue and index locations.</param>
    public IndexStatusProvider(SqliteIndexWriter writer, IndexerPaths paths)
    {
        _writer = writer;
        _paths = paths;
    }

    /// <inheritdoc />
    public IndexStatus GetStatus()
    {
        FileInfo source = new(_paths.ModelsPath);

        bool catalogueExists = source.Exists;
        long catalogueLength = 0;
        DateTimeOffset? catalogueWriteUtc = null;
        bool isStale = false;

        if (catalogueExists)
        {
            // Read the timestamps once: each access on a FileInfo that has not been refreshed can hit the
            // file system, and the two values must describe the same revision of the file.
            long length = source.Length;
            DateTime writeUtc = source.LastWriteTimeUtc;

            catalogueLength = length;
            catalogueWriteUtc = writeUtc;
            isStale = new IndexFingerprint(length, writeUtc.Ticks) != _writer.GetIndexedFingerprint();
        }

        (int models, int detections) = _writer.GetIndexedCounts();

        return new IndexStatus(
            CatalogueExists: catalogueExists,
            CatalogueLength: catalogueLength,
            CatalogueWriteUtc: catalogueWriteUtc,
            IndexExists: File.Exists(_paths.IndexPath),
            IndexedModels: models,
            IndexedDetections: detections,
            IndexedFingerprint: _writer.GetIndexedFingerprint(),
            IndexedUtc: _writer.GetIndexedUtc(),
            IsStale: isStale,
            LastAttemptUtc: _writer.GetLastAttemptUtc(),
            LastFailure: _writer.GetLastFailure(),
            GeneratedUtc: DateTimeOffset.UtcNow);
    }
}
