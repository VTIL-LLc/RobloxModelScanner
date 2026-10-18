using System.Text.Json;

using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// The scanner's JSON catalogue, held in memory and rewritten in one pass when a change is due.
/// </summary>
/// <remarks>
/// <para>
/// The document is a single JSON array, so there is no way to save one row: saving anything rewrites
/// everything. That single fact shapes this class. Rows are changed in memory immediately and the file
/// is written later, on a timer or when enough rows have accumulated, so the cost of a write is paid at
/// a rate the operator chose rather than once per discovered model. The previous revision saved after
/// every asset, which meant that scanning the hundred-thousandth model re-serialised the whole
/// ninety-nine thousand before it -- quadratic work that grew with the catalogue and eventually spent
/// more time serialising than scanning.
/// </para>
/// <para>
/// The whole catalogue is resident. That is a deliberate trade against a streaming implementation: an
/// array cannot be extended in place, and re-reading the file to find one row is exactly the behaviour
/// being removed. A hundred thousand rows is roughly twenty megabytes of objects, which is a bounded
/// and knowable cost, and it is what makes the backfill's in-place repair possible at all.
/// </para>
/// <para>
/// Writes are atomic. The document is written to a sibling file that is then moved over the catalogue,
/// so a reader -- the indexing service reads this file while the scanner runs -- sees either the whole
/// previous document or the whole new one. A reader that arrived mid-write would otherwise see a
/// truncated array and could only conclude that the catalogue had been corrupted.
/// </para>
/// </remarks>
/// <seealso cref="IModelCatalogue"/>
public sealed class JsonModelCatalogue : IModelCatalogue
{
    /// <summary>
    /// The options used for both reading and writing.
    /// </summary>
    /// <remarks>
    /// Reading is case-insensitive so that a document written by an earlier revision, whose member
    /// spelling may differ, still loads rather than being reported as corrupt. Writing is indented to
    /// match the document already on disk, which keeps an operator's diff of a single row readable.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// The writer settings, which govern the whitespace the serializer above cannot.
    /// </summary>
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    /// <summary>
    /// Guards the rows, the index and the pending-change bookkeeping.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The path of the document, which may not exist yet.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// How long a change may wait before it forces a write.
    /// </summary>
    private readonly TimeSpan _flushInterval;

    /// <summary>
    /// How many changes force a write before the interval elapses.
    /// </summary>
    private readonly int _flushRows;

    /// <summary>
    /// The rows, in document order.
    /// </summary>
    private readonly List<CatalogueRecord> _records = [];

    /// <summary>
    /// Each identifier's position in <see cref="_records"/>.
    /// </summary>
    private readonly Dictionary<long, int> _index = [];

    /// <summary>
    /// The number of changes made since the last write began.
    /// </summary>
    private int _unsaved;

    /// <summary>
    /// Whether the document on disk differs from the rows in memory.
    /// </summary>
    private bool _dirty;

    /// <summary>
    /// When the last write completed.
    /// </summary>
    private DateTime _lastWriteUtc = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonModelCatalogue"/> class.
    /// </summary>
    /// <param name="path">The path of the document, which need not exist yet.</param>
    /// <param name="flushInterval">How long a change may wait before it forces a write.</param>
    /// <param name="flushRows">How many changes force a write before the interval elapses.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    public JsonModelCatalogue(string path, TimeSpan flushInterval, int flushRows)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        _path = System.IO.Path.GetFullPath(path);
        _flushInterval = flushInterval;
        _flushRows = Math.Max(1, flushRows);
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_gate) return _records.Count;
        }
    }

    /// <inheritdoc />
    public int UnsavedCount
    {
        get
        {
            lock (_gate) return _unsaved;
        }
    }

    /// <summary>
    /// Gets the path of the document.
    /// </summary>
    public string Path => _path;

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the document exists but is not a valid catalogue.</exception>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return;

        // Opened for shared reading, and for deletion, because the indexing service reads the same file
        // while the scanner runs and the atomic replace below needs the destination not to be held
        // exclusively. Both readers allow the other, so both keep working.
        await using FileStream stream = new(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 1 << 16,
            useAsync: true);

        lock (_gate)
        {
            _records.Clear();
            _index.Clear();
        }

        List<CatalogueRecord> loaded = [];

        await foreach (CatalogueRecord? record in JsonSerializer.DeserializeAsyncEnumerable<CatalogueRecord>(
            stream,
            SerializerOptions,
            cancellationToken))
        {
            if (record is not null) loaded.Add(record);
        }

        lock (_gate)
        {
            foreach (CatalogueRecord record in loaded)
            {
                int position = _records.Count;

                // A document written by a revision that appended without checking its own in-flight work
                // can hold the same identifier twice. Keeping the last occurrence in the first one's
                // position preserves the document's order and makes the catalogue a set, which every
                // caller already assumes it is.
                if (_index.TryGetValue(record.Model, out int existing)) _records[existing] = record;
                else
                {
                    _index[record.Model] = position;
                    _records.Add(record);
                }
            }

            _unsaved = 0;
            _dirty = false;
        }
    }

    /// <inheritdoc />
    public bool Contains(long modelId)
    {
        lock (_gate) return _index.ContainsKey(modelId);
    }

    /// <inheritdoc />
    public bool Append(CatalogueRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            if (_index.ContainsKey(record.Model)) return false;

            _index[record.Model] = _records.Count;
            _records.Add(record);
            MarkChanged();
            return true;
        }
    }

    /// <inheritdoc />
    public bool Replace(CatalogueRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            if (!_index.TryGetValue(record.Model, out int position)) return false;

            _records[position] = record;
            MarkChanged();
            return true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogueRecord> RowsWithoutMetadata()
    {
        lock (_gate)
        {
            List<CatalogueRecord> rows = [];
            foreach (CatalogueRecord record in _records)
            {
                if (record.HasNoMetadata()) rows.Add(record);
            }

            return rows;
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync(bool force, CancellationToken cancellationToken)
    {
        CatalogueRecord[] snapshot;

        lock (_gate)
        {
            // Both halves matter. Without the first, an idle scanner would rewrite the whole catalogue
            // every time the timer fired. Without the second, every change would write immediately and the
            // throttle would be decorative.
            bool due = force || _unsaved >= _flushRows || DateTime.UtcNow - _lastWriteUtc >= _flushInterval;
            if (!_dirty || !due) return;

            snapshot = _records.ToArray();
            _unsaved = 0;
        }

        // Serialised outside the lock, from a snapshot. Holding the lock across a forty-megabyte write
        // would stall every worker for the duration of the write, which is the opposite of the point.
        await WriteAsync(snapshot, cancellationToken);

        lock (_gate)
        {
            _lastWriteUtc = DateTime.UtcNow;

            // Cleared only when nothing arrived while the write was running. A change made during the write
            // is not in the snapshot, and leaving the flag set is what guarantees the next flush writes it.
            if (_unsaved == 0) _dirty = false;
        }
    }

    /// <summary>
    /// Records that the in-memory rows no longer match the document.
    /// </summary>
    /// <remarks>
    /// Called with the lock held.
    /// </remarks>
    private void MarkChanged()
    {
        _unsaved++;
        _dirty = true;
    }

    /// <summary>
    /// Writes a snapshot of the catalogue over the document.
    /// </summary>
    /// <param name="snapshot">The rows to write, in order.</param>
    /// <param name="cancellationToken">A token used to abandon the write.</param>
    /// <exception cref="IOException">Thrown when the write fails.</exception>
    /// <remarks>
    /// The document is streamed a row at a time rather than built as a string first. A single string for
    /// the whole catalogue is another forty megabytes of transient allocation per write, and the garbage
    /// collector's work is paid for by the scanning threads that are trying to do something useful.
    /// </remarks>
    private async Task WriteAsync(CatalogueRecord[] snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);

        string temporary = string.Concat(_path, ".writing");

        await using (FileStream stream = new(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 16,
            useAsync: true))
        {
            await using Utf8JsonWriter writer = new(stream, WriterOptions);

            writer.WriteStartArray();
            foreach (CatalogueRecord record in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonSerializer.Serialize(writer, record, SerializerOptions);
            }

            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);
        }

        File.Move(temporary, _path, overwrite: true);
    }
}
