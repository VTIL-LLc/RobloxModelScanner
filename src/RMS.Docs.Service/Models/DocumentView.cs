namespace RMS.Docs.Service.Models;

/// <summary>
/// One document, read in full.
/// </summary>
/// <remarks>
/// <para>
/// The metadata is a nested object rather than a second set of properties repeated beside the body. Listing and
/// reading are two views of the same thing, and duplicating the property list between them would let the two
/// disagree about a document the moment one of them gained a field -- which is exactly the class of bug a
/// catalogue is supposed to make impossible.
/// </para>
/// <para>
/// The body is the document's text with its front matter removed. The front matter is a note to the reader of
/// the service, not part of what is being said, and a renderer that had to strip it would be a second place the
/// format is understood.
/// </para>
/// </remarks>
public sealed class DocumentView
{
    /// <summary>
    /// Gets or sets the document's metadata.
    /// </summary>
    public DocumentSummaryView Summary { get; set; } = new();

    /// <summary>
    /// Gets or sets the document's body.
    /// </summary>
    /// <remarks>
    /// Returned exactly as it is written, with its own line endings and its own Unicode. Nothing here normalises
    /// it: a Markdown renderer and a plain text renderer both care about the original text, and a service that
    /// reformatted it would be making a rendering decision on the reader's behalf.
    /// </remarks>
    public string Body { get; set; } = string.Empty;
}
