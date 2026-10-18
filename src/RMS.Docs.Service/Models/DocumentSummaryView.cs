using RMS.Docs.Service.Enums;

namespace RMS.Docs.Service.Models;

/// <summary>
/// One document as the navigation sees it: everything needed to list it and link to it, without the body.
/// </summary>
/// <remarks>
/// <para>
/// The body is left out on purpose. A navigation render happens on every page, and the catalogue it asks for
/// would otherwise carry the whole documentation across the wire each time -- the largest part of which is text
/// the page is not going to show.
/// </para>
/// <para>
/// <see cref="Order"/> and <see cref="Section"/> exist so that the reading order is a property of the
/// documentation rather than of the reader. A file system sorts alphabetically, which is almost never the order
/// a reader wants, and every consumer that invented its own order would invent a different one.
/// </para>
/// </remarks>
public sealed class DocumentSummaryView
{
    /// <summary>
    /// Gets or sets the document's slug: its path below the document directory, without the extension, in lower
    /// case, with forward slashes.
    /// </summary>
    /// <remarks>
    /// The slug is the document's identity everywhere. It is what a page requests and what a link points at, so
    /// it is stable as long as the file is not moved or renamed -- which is the same promise a file makes.
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document's title.
    /// </summary>
    /// <remarks>
    /// Taken from the document's front matter, then from its first heading, then from its file name. It is
    /// resolved here rather than by each consumer, so that the navigation and the page heading cannot disagree
    /// about what a document is called.
    /// </remarks>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the section the document belongs to, derived from the first directory below the document
    /// directory.
    /// </summary>
    /// <remarks>
    /// Empty for a document at the top level. A section is not a separate thing to configure: it comes into
    /// being when a document is filed in a subdirectory, which is the only place a folder structure is declared.
    /// </remarks>
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how the body is meant to be read.
    /// </summary>
    public DocumentFormat Format { get; set; }

    /// <summary>
    /// Gets or sets the document's position within its section.
    /// </summary>
    /// <remarks>
    /// Declared in the document's front matter, defaulting to a value that puts an undeclared document after
    /// every declared one. The default is deliberately late rather than early: a document that has just been
    /// added should not silently push the announcement at the top of the section down.
    /// </remarks>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the document's size in bytes.
    /// </summary>
    /// <remarks>
    /// Reported so that an operator can see the difference between a document and a placeholder without opening
    /// it. It is the file's size on disk, not the length of the body after its front matter has been removed.
    /// </remarks>
    public long LengthBytes { get; set; }

    /// <summary>
    /// Gets or sets when the document was last written, in UTC.
    /// </summary>
    /// <remarks>
    /// This is what makes it possible to tell a documentation site that is serving the files from one that is
    /// serving a copy: the reader can compare the timestamp with the file in the repository.
    /// </remarks>
    public DateTime LastWriteUtc { get; set; }
}
