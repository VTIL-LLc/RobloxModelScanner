using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Models;

namespace Scanner.Indexer.Implementation;

/// <summary>
/// Assembles the current view of the catalogue and the index.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is built from the catalogue's revision plus the writer's in-memory bookkeeping, so polling
/// it is cheap and cannot itself trigger a migration. The revision is read straight from the catalogue
/// rather than from a cached copy: reporting a stale number would defeat the purpose of having an endpoint
/// an operator checks when they suspect the index is behind.
/// </para>
/// <para>
/// Reporting the failure text alongside the staleness flag is deliberate. An operator needs to tell "the
/// catalogue has not changed" apart from "every migration attempt is being rejected", and the two look
/// identical if all they can see is a boolean.
/// </para>
/// <para>
/// A catalogue that cannot be read is reported as such rather than being allowed to throw out of here. This
/// endpoint is the one an operator reaches for when something is wrong, so it is the last place that should
/// answer with an exception instead of an answer.
/// </para>
/// </remarks>
public sealed class IndexStatusProvider : IIndexStatusProvider
{
    private readonly SqliteIndexWriter _writer;
    private readonly IndexerPaths _paths;
    private readonly ICatalogueReader _reader;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexStatusProvider"/> class.
    /// </summary>
    /// <param name="writer">The writer that owns the index state.</param>
    /// <param name="reader">The reader used to ask the catalogue which revision it is at.</param>
    /// <param name="paths">The resolved catalogue and index locations.</param>
    public IndexStatusProvider(SqliteIndexWriter writer, ICatalogueReader reader, IndexerPaths paths)
    {
        _writer = writer;
        _reader = reader;
        _paths = paths;
    }

    /// <inheritdoc />
    public IndexStatus GetStatus()
    {
        long? catalogueRevision = null;
        bool isStale = false;

        try
        {
            catalogueRevision = _reader.ReadRevision(_paths.CataloguePath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // A catalogue that is present but unreadable is not a reason to fail the status call: the
            // revision stays unknown, the caller can see that plainly, and the failure itself is what the
            // writer's last-failure text is already reporting pass after pass.
        }

        if (catalogueRevision is not null)
        {
            isStale = new IndexFingerprint(catalogueRevision.Value) != _writer.GetIndexedFingerprint();
        }

        (int models, int detections) = _writer.GetIndexedCounts();

        return new IndexStatus(
            CatalogueExists: catalogueRevision is not null,
            CatalogueRevision: catalogueRevision,
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
