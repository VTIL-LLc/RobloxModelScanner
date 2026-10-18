namespace RMS.Docs.Service.Models;

/// <summary>
/// One document that contains the phrase being searched for.
/// </summary>
/// <remarks>
/// The snippet is cut around the first occurrence rather than taken from the start of the document. A search
/// result that shows the first line of every match is a result list assembled from the documents' preambles,
/// which is the one part of a document that is least likely to contain what was searched for.
/// </remarks>
public sealed class SearchResultView
{
    /// <summary>
    /// Gets or sets the document's slug.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document's title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document's section.
    /// </summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text around the first occurrence.
    /// </summary>
    /// <remarks>
    /// Newlines are collapsed to single spaces, because the snippet is rendered on one line and a result that
    /// arrives with the document's formatting still in it would leave a fragment of a table or a code block in
    /// the middle of a search result.
    /// </remarks>
    public string Snippet { get; set; } = string.Empty;
}
