using System;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Docs.Site.Infrastructure;

namespace RMS.Docs.Site
{
    /// <summary>
    /// Finds the pages that mention a phrase.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The search runs over the files themselves, in the service, rather than over an index of them. A
    /// documentation directory holds tens of files of a few kilobytes, so a scan costs less than keeping a second
    /// copy of the documentation would, and it cannot go stale: a page that was corrected a second ago is
    /// searchable now, without anything having to notice that it changed.
    /// </para>
    /// <para>
    /// The results are a live region, which is unusual for a page whose content came from a question the reader
    /// typed. It is safe here and useful: the query travels in the address, so a refresh re-asks the same
    /// question, and a document that is added while the reader is looking at the results appears in them.
    /// </para>
    /// </remarks>
    public class Search : DocsPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Search — " + Settings.Title; }
        }

        /// <summary>
        /// Gets the navigation entry this page belongs to.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Search"; }
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
            string query = ReadQuery("q", DocsService.MaximumQueryLength);

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Search",
                "Every document's title and text, matched as a phrase without regard to case. Results show the "
                + "text around the first occurrence, so a match in the middle of a page is visible without "
                + "opening it."));

            builder.Append(DocsViews.SearchForm(query));

            ServiceResult<SearchResponseView> results = LoadSearch(query, DocsService.SearchLimit);

            if (!results.IsSuccess)
            {
                builder.Append(RenderFailure(results));

                return builder.ToString();
            }

            // The phrase travels in the region's address so that a refresh asks the same question. It is escaped
            // here and then once more as part of the attribute, which is what makes a phrase containing an
            // ampersand arrive at the handler whole rather than as the beginning of a parameter the reader never
            // typed.
            builder.Append(Live(
                "search-results",
                "search",
                query.Length == 0 ? string.Empty : "q=" + Uri.EscapeDataString(query),
                DocsViews.SearchResults(results.Value)));

            return builder.ToString();
        }
    }
}
