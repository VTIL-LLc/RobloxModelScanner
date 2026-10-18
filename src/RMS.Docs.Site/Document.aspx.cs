using System;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Docs.Site.Infrastructure;

namespace RMS.Docs.Site
{
    /// <summary>
    /// One document, read in full.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document is named in the query string and resolved by the service against the directory it walked
    /// itself. There is no path in this page's request and no path in its own code: a name that is not a document
    /// the service can see is answered as absent, and the page says so and offers the index.
    /// </para>
    /// <para>
    /// The body is not a live region. Everything else on this site can be refreshed under the reader safely,
    /// because a refreshed list is the same list; a refreshed page of prose is not, because the reader may be
    /// halfway through it, or holding a selection, and a document that redraws itself every minute is a document
    /// nobody can read. What refreshes instead is the strip of facts above it, which carries the one value that
    /// tells a reader the file has changed.
    /// </para>
    /// </remarks>
    public class Document : DocsPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        /// <remarks>
        /// The slug rather than the document's title, because the title is only known once the service has
        /// answered and this property is read before that. It is the honest answer either way: the address is
        /// what the reader asked for.
        /// </remarks>
        protected override string PageTitle
        {
            get
            {
                string slug = ReadSlug();

                return slug.Length == 0 ? "Document — " + Settings.Title : slug + " — " + Settings.Title;
            }
        }

        /// <summary>
        /// Gets the navigation entry this page belongs to.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Contents"; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            WriteDocument(PageTitle, Navigation(), RenderBody());
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <returns>The markup.</returns>
        private string RenderBody()
        {
            string slug = ReadSlug();

            StringBuilder builder = new StringBuilder();

            if (slug.Length == 0)
            {
                builder.Append(Html.Heading("Document", "No document was named."));
                builder.Append(Html.Alert(
                    "info",
                    "This address names no document.",
                    "Open the index and follow a link from there."));
                builder.Append("<p>").Append(Html.Link("/", "The index")).Append("</p>");

                return builder.ToString();
            }

            ServiceResult<DocumentView> document = LoadDocument(slug);

            if (!document.IsSuccess)
            {
                builder.Append(Html.Heading("Document", slug));
                builder.Append(RenderFailure(document));

                // A name that has been renamed is the ordinary reason a reader arrives at an answer like this, so
                // the way out is offered here rather than only on the index.
                if (IsMissing(document.StatusCode)) builder.Append(RenderIndex());

                return builder.ToString();
            }

            builder.Append(DocsViews.DocumentHeading(document.Value));

            builder.Append(Live(
                "document-facts",
                "document",
                "name=" + Uri.EscapeDataString(slug),
                DocsViews.DocumentFacts(document.Value)));

            builder.Append(RenderIndex());

            builder.Append(DocsViews.DocumentBody(document.Value));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the list of other documents.
        /// </summary>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Read separately from the document, and a failure to read it does not cost the reader the document. The
        /// catalogue is navigation; the document is the thing they came for, and hiding it because a second call
        /// failed would be answering the wrong question.
        /// </remarks>
        private string RenderIndex()
        {
            ServiceResult<DocumentListView> list = LoadDocuments();

            return list.IsSuccess
                ? DocsViews.Contents(list.Value, ReadSlug())
                : RenderFailure(list);
        }
    }
}
