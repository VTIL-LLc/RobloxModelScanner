using System;
using System.Collections.Generic;

namespace RMS.Docs.Site.Infrastructure
{
    /// <summary>
    /// The documents the service can serve, as the catalogue describes them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These types mirror the service's models field for field. The site keeps its own copy because the two ends
    /// run on different frameworks and share no assembly, and the copy is read with the JavaScript serializer,
    /// which needs public settable properties and a parameterless constructor.
    /// </para>
    /// <para>
    /// The names are written in this site's own convention rather than the service's, because the serializer
    /// matches member names without regard to case. That is what keeps the wire format -- which is the service's
    /// -- from dictating how this side spells things.
    /// </para>
    /// </remarks>
    public sealed class DocumentListView
    {
        /// <summary>
        /// Gets or sets the directory the documents were read from.
        /// </summary>
        public string DocumentDirectory { get; set; }

        /// <summary>
        /// Gets or sets the documents, in the order the service put them in.
        /// </summary>
        public List<DocumentSummaryView> Documents { get; set; }

        /// <summary>
        /// Gets or sets the files that could not be served.
        /// </summary>
        public List<DocumentProblemView> Problems { get; set; }

        /// <summary>
        /// Gets or sets the service's one-line description of the catalogue.
        /// </summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// One document as the navigation sees it.
    /// </summary>
    public sealed class DocumentSummaryView
    {
        /// <summary>
        /// Gets or sets the document's slug: its path below the document directory, without the extension.
        /// </summary>
        public string Slug { get; set; }

        /// <summary>
        /// Gets or sets the document's title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the section the document belongs to, which is its first directory.
        /// </summary>
        public string Section { get; set; }

        /// <summary>
        /// Gets or sets how the body is meant to be read, as the service names it.
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Gets or sets the document's position within its section.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Gets or sets the document's size in bytes.
        /// </summary>
        public long LengthBytes { get; set; }

        /// <summary>
        /// Gets or sets when the document was last written, in UTC.
        /// </summary>
        /// <remarks>
        /// Nullable, and deliberately so. A service that omitted the value for any reason would deserialise to
        /// the default instant, which is 0001-01-01: a date shown as "2001 years ago" is a worse failure than a
        /// date shown as absent, because the first looks like data and the second looks like a gap.
        /// </remarks>
        public DateTime? LastWriteUtc { get; set; }
    }

    /// <summary>
    /// One document, read in full.
    /// </summary>
    public sealed class DocumentView
    {
        /// <summary>
        /// Gets or sets the document's metadata.
        /// </summary>
        public DocumentSummaryView Summary { get; set; }

        /// <summary>
        /// Gets or sets the document's body, with its front matter already removed.
        /// </summary>
        public string Body { get; set; }
    }

    /// <summary>
    /// One file in the document directory that is not being served.
    /// </summary>
    public sealed class DocumentProblemView
    {
        /// <summary>
        /// Gets or sets the slug the problem is about, which is empty for a problem with the directory itself.
        /// </summary>
        public string Slug { get; set; }

        /// <summary>
        /// Gets or sets how badly the problem affects the catalogue, as the service names it.
        /// </summary>
        public string Severity { get; set; }

        /// <summary>
        /// Gets or sets the sentence describing the problem.
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// The answer to a search.
    /// </summary>
    public sealed class SearchResponseView
    {
        /// <summary>
        /// Gets or sets the phrase that was searched for.
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Gets or sets the number of results returned.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets the largest number of results that would have been returned.
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// Gets or sets the matching documents.
        /// </summary>
        public List<SearchResultView> Results { get; set; }
    }

    /// <summary>
    /// One document that contains the phrase being searched for.
    /// </summary>
    public sealed class SearchResultView
    {
        /// <summary>
        /// Gets or sets the document's slug.
        /// </summary>
        public string Slug { get; set; }

        /// <summary>
        /// Gets or sets the document's title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the document's section.
        /// </summary>
        public string Section { get; set; }

        /// <summary>
        /// Gets or sets the text around the first occurrence.
        /// </summary>
        public string Snippet { get; set; }
    }
}
