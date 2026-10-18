using RMS.Docs.Service.Models;

namespace RMS.Docs.Service.Interfaces;

/// <summary>
/// Reads the documents in the configured directory.
/// </summary>
/// <remarks>
/// <para>
/// There is no write side. The documentation is edited as files by whoever is writing it, and a rendering
/// service that could also change the documents would be an editing surface that has to solve conflicts,
/// history and authorisation -- none of which is what a reader needs.
/// </para>
/// <para>
/// Every method re-reads the directory rather than caching it. A documentation directory holds tens of files of
/// a few kilobytes, so the cost of being current is a handful of directory-entry reads, and the alternative is
/// a service that shows yesterday's text after somebody has corrected it.
/// </para>
/// </remarks>
public interface IDocumentCatalog
{
    /// <summary>
    /// Lists every document in the directory.
    /// </summary>
    /// <returns>The catalogue, including whatever problems were found while building it.</returns>
    DocumentListView List();

    /// <summary>
    /// Reads one document.
    /// </summary>
    /// <param name="slug">The document's slug, as reported by <see cref="List"/>.</param>
    /// <returns>The document, or <see langword="null"/> when no document has that slug.</returns>
    DocumentView? Read(string slug);

    /// <summary>
    /// Searches the documents for a phrase.
    /// </summary>
    /// <param name="query">The phrase to look for. Whitespace and case are ignored.</param>
    /// <param name="limit">The largest number of results to return.</param>
    /// <returns>The matching documents, with a snippet around the first occurrence in each.</returns>
    SearchResponseView Search(string query, int limit);
}
