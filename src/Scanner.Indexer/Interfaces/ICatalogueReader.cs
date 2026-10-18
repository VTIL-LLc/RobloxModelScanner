using Scanner.Indexer.Models;

namespace Scanner.Indexer.Interfaces;

/// <summary>
/// Streams the scanner's JSON catalogue row by row.
/// </summary>
/// <remarks>
/// The catalogue is tens of megabytes and grows with every scan, so it is never materialized as a whole.
/// A reader implementation is expected to yield rows as they are parsed, which keeps peak memory at one
/// row regardless of how large the catalogue becomes.
/// </remarks>
public interface ICatalogueReader
{
    /// <summary>
    /// Reads the catalogue at the supplied path.
    /// </summary>
    /// <param name="path">The absolute path to the catalogue document.</param>
    /// <param name="cancellationToken">A token used to abandon the read.</param>
    /// <returns>The catalogue rows in document order.</returns>
    /// <exception cref="IOException">Thrown when the document cannot be opened or read.</exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when the document is not a valid JSON array of catalogue rows.
    /// </exception>
    IEnumerable<CatalogueRow> Read(string path, CancellationToken cancellationToken);
}
