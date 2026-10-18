using System.Text.Json;

using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Models;

namespace Scanner.Indexer.Implementation;

/// <summary>
/// Streams the scanner's JSON catalogue into catalogue rows.
/// </summary>
/// <remarks>
/// <para>
/// The document is a JSON array of rows. It is read with the streaming deserializer and enumerated one
/// element at a time, so peak memory is a single row rather than the whole document. That matters because
/// the catalogue is tens of megabytes and is expected to keep growing.
/// </para>
/// <para>
/// The file is opened with <see cref="FileShare.Delete"/> so the scanner's atomic replace of the document
/// is never blocked by a concurrent read. An open handle keeps reading the revision it was opened against,
/// which is exactly the consistency a migration needs.
/// </para>
/// <para>
/// The read is written as an iterator so that the file handle is owned by the enumeration rather than by
/// the call that starts it. Returning an enumerable from a method that had already closed the handle would
/// hand the caller a sequence that fails on first use, which is a failure mode that only appears once the
/// caller actually reads the first row.
/// </para>
/// </remarks>
public sealed class CatalogueFileReader : ICatalogueReader
{
    /// <summary>
    /// The serializer policy used to read the document.
    /// </summary>
    /// <remarks>
    /// Case-insensitive matching lets one row type read both the current document and older rows, and
    /// trailing commas are tolerated so a hand-edited file still migrates.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    /// <inheritdoc />
    public IEnumerable<CatalogueRow> Read(string path, CancellationToken cancellationToken) =>
        ReadCore(path, cancellationToken);

    /// <summary>
    /// Opens the catalogue and yields its rows one at a time.
    /// </summary>
    /// <param name="path">The absolute path to the catalogue document.</param>
    /// <param name="cancellationToken">A token used to abandon the read.</param>
    /// <returns>The rows, with nulls removed.</returns>
    /// <remarks>
    /// The caller performs synchronous SQLite work per row, so there is nothing to gain from an
    /// asynchronous enumeration here. Adapting once keeps that fact in one place instead of forcing a task
    /// per row through the write path.
    /// </remarks>
    private static IEnumerable<CatalogueRow> ReadCore(string path, CancellationToken cancellationToken)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 1 << 16,
            FileOptions.SequentialScan);

        IAsyncEnumerable<CatalogueRow?> rows = JsonSerializer
            .DeserializeAsyncEnumerable<CatalogueRow>(stream, SerializerOptions, cancellationToken);

        foreach (CatalogueRow? row in rows.ToBlockingEnumerable(cancellationToken))
        {
            if (row is not null) yield return row;
        }
    }
}
