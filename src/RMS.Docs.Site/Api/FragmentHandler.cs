using System;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Docs.Site.Infrastructure;

namespace RMS.Docs.Site.Api
{
    /// <summary>
    /// Rebuilds one region of a page as markup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The site's regions refresh by replacing their contents with an answer from here. The answer is markup and
    /// not data, and that is the point: the table that arrives is built by the same method that built the one
    /// already on the page, so a column cannot exist in one and be missing from the other, and a fix to a
    /// rendering cannot be applied in one place only.
    /// </para>
    /// <para>
    /// The view is a name from a fixed list, never a path. This handler reaches the service with the site's key,
    /// so a handler that could be pointed anywhere would be an open proxy standing inside the trust boundary --
    /// which is the worst place to put one. A document is likewise named and never pathed: the name is normalised
    /// by the same rule the pages use, and the service resolves it against the directory it walked itself.
    /// </para>
    /// <para>
    /// Each region is one round trip to the service. A page with three regions makes three calls when it
    /// refreshes, which is what keeps one slow region from holding up the others.
    /// </para>
    /// </remarks>
    public class FragmentHandler : ServiceProxyHandler
    {
        /// <summary>
        /// The longest view name this handler will consider.
        /// </summary>
        /// <remarks>
        /// The longest view name is thirteen characters, so this is generous by a factor of two and still refuses
        /// to build an answer out of something somebody pasted into a terminal.
        /// </remarks>
        private const int MaximumViewLength = 32;

        /// <summary>
        /// Builds the response.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        protected override void Handle(HttpContext context)
        {
            switch (ReadText(context, "view", MaximumViewLength))
            {
                case "catalogue": Catalogue(context); break;
                case "document": Document(context); break;
                case "search": Search(context); break;
                default: Refuse(context, 400, "That is not one of the views this site builds."); break;
            }
        }

        /// <summary>
        /// Answers with the whole index, as one region.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <remarks>
        /// The address, the problems and the tables arrive together, because they answer about the same directory
        /// at the same moment. Sending them separately would let a table of eleven documents be rendered beside a
        /// line saying there are ten -- and the reader has no way to tell which of the two is the mistake.
        /// </remarks>
        private void Catalogue(HttpContext context)
        {
            ServiceResult<DocumentListView> result = DocsService.Documents(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DocsViews.Index(result.Value)
                    : DocsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with one document's facts.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <remarks>
        /// The facts only, never the body. This region is the one that refreshes while somebody is reading, and
        /// replacing the page of prose under them is the one thing this site must not do.
        /// </remarks>
        private void Document(HttpContext context)
        {
            string slug = DocsService.NormaliseSlug(ReadText(context, "name", DocsService.MaximumSlugLength));

            if (slug.Length == 0)
            {
                Refuse(context, 400, "That is not a document name.");
                return;
            }

            ServiceResult<DocumentView> result = DocsService.Document(Client, slug);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DocsViews.DocumentFacts(result.Value)
                    : DocsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with the results of a search.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <remarks>
        /// An empty phrase is not refused. It is the state the page is in when somebody has just opened it, and
        /// the service's own answer to it describes how the search works, which is more use than a fault.
        /// </remarks>
        private void Search(HttpContext context)
        {
            string query = ReadText(context, "q", DocsService.MaximumQueryLength);

            ServiceResult<SearchResponseView> result = DocsService.Search(Client, query, DocsService.SearchLimit);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DocsViews.SearchResults(result.Value)
                    : DocsViews.Failure(result.StatusCode, result.Error));
        }
    }
}
