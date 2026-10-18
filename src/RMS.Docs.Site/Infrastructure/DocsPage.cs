using System;
using System.Collections.Generic;
using System.Net;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace RMS.Docs.Site.Infrastructure
{
    /// <summary>
    /// The base class for every page of the documentation site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It supplies the calls the pages make to the service and the small pieces of reading that every page needs:
    /// a bounded value out of the query string and the way a failed call is described. The calls themselves live
    /// in <see cref="DocsService"/>, so that a page and the handler refreshing one of its regions cannot disagree
    /// about a route.
    /// </para>
    /// <para>
    /// Every call returns a result rather than throwing. A service that is down, that has not been given a key, or
    /// that has nothing configured is a condition these pages exist to describe, and each page decides how loudly
    /// to say it through <see cref="RenderFailure"/>.
    /// </para>
    /// <para>
    /// The navigation is fixed and short, because this site's structure is a directory rather than a set of
    /// screens. A document is reached from the index, from a search, or from a link inside another document, and
    /// all three of those are document lists -- there is no page of the site that is about documents in general.
    /// </para>
    /// </remarks>
    public abstract class DocsPage : ServicePage
    {
        /// <summary>
        /// Gets the sections shown in the navigation bar.
        /// </summary>
        /// <returns>The sections.</returns>
        protected override IEnumerable<KeyValuePair<string, string>> Navigation()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Contents", "/"),
                new KeyValuePair<string, string>("Search", "/Search.aspx"),
            };
        }

        /// <summary>
        /// Reads the document named in the query string.
        /// </summary>
        /// <returns>The slug, or an empty string when the page was opened without one.</returns>
        /// <remarks>
        /// The rule for what a slug is lives in <see cref="DocsService.NormaliseSlug"/>, which the fragment
        /// handler uses too. The value only ever selects from the list the service built by walking its own
        /// directory, so nothing here is a defence against reading a file -- it is a defence against a page
        /// asking the service a question whose answer is certain and uninteresting.
        /// </remarks>
        protected string ReadSlug()
        {
            return DocsService.NormaliseSlug(ReadQuery("name", DocsService.MaximumSlugLength));
        }

        /// <summary>
        /// Renders a failed call.
        /// </summary>
        /// <typeparam name="T">The type the call would have returned.</typeparam>
        /// <param name="result">The failed result.</param>
        /// <returns>The markup.</returns>
        protected string RenderFailure<T>(ServiceResult<T> result)
            where T : class
        {
            return DocsViews.Failure(result.StatusCode, result.Error);
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// This forwards to <see cref="DocsViews.Live(string, string, string)"/> so that a page and the handler
        /// refreshing the region are kept in step by the compiler rather than by discipline.
        /// </remarks>
        protected string Live(string id, string view, string content)
        {
            return DocsViews.Live(id, view, content);
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes, with parameters of its own.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="extraQuery">Additional query parameters for the fragment, already encoded.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        protected string Live(string id, string view, string extraQuery, string content)
        {
            return DocsViews.Live(id, view, extraQuery, content);
        }

        /// <summary>
        /// Reads the catalogue.
        /// </summary>
        /// <returns>The catalogue, or the reason it could not be read.</returns>
        protected ServiceResult<DocumentListView> LoadDocuments()
        {
            return DocsService.Documents(Client);
        }

        /// <summary>
        /// Reads one document.
        /// </summary>
        /// <param name="slug">The document's slug.</param>
        /// <returns>The document, or the reason it could not be read.</returns>
        protected ServiceResult<DocumentView> LoadDocument(string slug)
        {
            return DocsService.Document(Client, slug);
        }

        /// <summary>
        /// Searches the documents.
        /// </summary>
        /// <param name="query">The phrase to look for.</param>
        /// <param name="limit">The largest number of results to ask for.</param>
        /// <returns>The matches, or the reason they could not be read.</returns>
        protected ServiceResult<SearchResponseView> LoadSearch(string query, int limit)
        {
            return DocsService.Search(Client, query, limit);
        }

        /// <summary>
        /// Determines whether a status describes a document that does not exist.
        /// </summary>
        /// <param name="status">The status the service answered with.</param>
        /// <returns><see langword="true"/> when the answer was a refusal about the name.</returns>
        /// <remarks>
        /// A page uses this to decide whether to offer the index. A document that has been renamed is the ordinary
        /// reason a reader arrives here, and the index is the answer to it.
        /// </remarks>
        protected static bool IsMissing(HttpStatusCode status)
        {
            return status == HttpStatusCode.NotFound;
        }
    }
}
