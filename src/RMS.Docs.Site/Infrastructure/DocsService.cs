using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace RMS.Docs.Site.Infrastructure
{
    /// <summary>
    /// Every call this site makes to the documentation service, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page and the handler that refreshes one of its regions both go through here, so the two cannot disagree
    /// about a route: there is one spelling of each address in the application, and a route that moved would move
    /// for both of them or for neither.
    /// </para>
    /// <para>
    /// Every call returns a result rather than throwing. A service that is down, that has not been given a key, or
    /// that has nothing configured is a condition these pages exist to describe, and each page decides how loudly
    /// to say it through <see cref="DocsViews.Failure"/>.
    /// </para>
    /// </remarks>
    public static class DocsService
    {
        /// <summary>
        /// The longest document slug the site will accept in a request.
        /// </summary>
        /// <remarks>
        /// A directory several levels deep can produce a long slug, so this is generous. It is bounded at all
        /// because the value is used to build an address for the service, and an address built from an unbounded
        /// value is a request this site can be made to send on somebody else's behalf.
        /// </remarks>
        public const int MaximumSlugLength = 256;

        /// <summary>
        /// The longest search phrase the site will accept in a request.
        /// </summary>
        public const int MaximumQueryLength = 120;

        /// <summary>
        /// The largest number of search results the site asks the service for.
        /// </summary>
        /// <remarks>
        /// A search is meant to narrow a reader's choice, not to reproduce the documentation. A page that showed
        /// every match for a common word would be a worse index than the index, and the service's own answer says
        /// when the ceiling was reached so the reader is told why the list stops.
        /// </remarks>
        public const int SearchLimit = 40;

        /// <summary>
        /// The route that lists every document.
        /// </summary>
        private const string DocumentsRoute = "/api/documents";

        /// <summary>
        /// The route that searches the documents.
        /// </summary>
        private const string SearchRoute = "/api/search";

        /// <summary>
        /// Reduces a value from a request to a slug that names one of the service's documents, if it names any.
        /// </summary>
        /// <param name="value">The value as it arrived.</param>
        /// <returns>The slug, or an empty string when the value is not one.</returns>
        /// <remarks>
        /// <para>
        /// Shared by the pages and the fragment handler, so that a name means the same thing wherever it arrives
        /// from. Two implementations of this would be two ideas of what a valid slug is, and the difference would
        /// show up as a link that works on a page and not in a refreshed region.
        /// </para>
        /// <para>
        /// Backslashes become separators and doubled or surrounding separators are dropped, because a slug is
        /// pasted by people as often as it is clicked. A value naming a parent is rejected outright rather than
        /// cleaned: a slug never contains one, so a value that does is either a mistake or an attempt, and both
        /// are answered better by asking the service nothing at all.
        /// </para>
        /// </remarks>
        public static string NormaliseSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string slug = value.Trim().Replace('\\', '/').Trim('/');

            while (slug.Contains("//")) slug = slug.Replace("//", "/");

            if (slug.Length == 0 || slug.Length > MaximumSlugLength) return string.Empty;

            return slug.Contains("..") ? string.Empty : slug;
        }

        /// <summary>
        /// Reads the catalogue.
        /// </summary>
        /// <param name="client">The client that carries the key the browser never sees.</param>
        /// <returns>The catalogue, or the reason it could not be read.</returns>
        public static ServiceResult<DocumentListView> Documents(ServiceClient client)
        {
            return client.GetJson<DocumentListView>(DocumentsRoute);
        }

        /// <summary>
        /// Reads one document.
        /// </summary>
        /// <param name="client">The client that carries the key the browser never sees.</param>
        /// <param name="slug">The document's slug.</param>
        /// <returns>The document, or the reason it could not be read.</returns>
        /// <remarks>
        /// A document that does not exist is refused by the service with <c>404</c>, which arrives here as a failed
        /// result rather than as a missing value. That is the right shape for it: from this side, "there is no such
        /// document" and "the service is not answering" are both answers that the page has to render.
        /// </remarks>
        public static ServiceResult<DocumentView> Document(ServiceClient client, string slug)
        {
            return client.GetJson<DocumentView>(DocumentsRoute + "/" + EscapeSlug(slug));
        }

        /// <summary>
        /// Searches the documents.
        /// </summary>
        /// <param name="client">The client that carries the key the browser never sees.</param>
        /// <param name="query">The phrase to look for.</param>
        /// <param name="limit">The largest number of results to ask for.</param>
        /// <returns>The matches, or the reason they could not be read.</returns>
        public static ServiceResult<SearchResponseView> Search(ServiceClient client, string query, int limit)
        {
            return client.GetJson<SearchResponseView>(
                SearchRoute,
                new KeyValuePair<string, string>("q", query ?? string.Empty),
                new KeyValuePair<string, string>("limit", limit.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Builds the address of one document's page.
        /// </summary>
        /// <param name="slug">The document's slug.</param>
        /// <returns>The address.</returns>
        /// <remarks>
        /// The slug travels as one encoded query value rather than as path segments, because a slug is a path
        /// below the document directory and a page in the middle of a path would have to reason about which of its
        /// segments were structure and which were content. An encoded value has no structure to confuse.
        /// </remarks>
        public static string PageFor(string slug)
        {
            return "/Document.aspx?name=" + Uri.EscapeDataString(slug ?? string.Empty);
        }

        /// <summary>
        /// Encodes a slug for use as a path, one segment at a time.
        /// </summary>
        /// <param name="slug">The document's slug.</param>
        /// <returns>The encoded path.</returns>
        /// <remarks>
        /// The separators are left alone and everything else is escaped. Escaping the whole string would turn
        /// <c>guides/deployer</c> into a single segment named <c>guides/deployer</c>, which the service's
        /// catch-all route would not match -- so a document filed in a section would answer as absent while the
        /// catalogue listed it.
        /// </remarks>
        private static string EscapeSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return string.Empty;

            string[] segments = slug.Split('/');
            StringBuilder builder = new StringBuilder(slug.Length + 8);

            for (int index = 0; index < segments.Length; index++)
            {
                if (index > 0) builder.Append('/');

                // An empty segment is a doubled separator or a leading one. It is dropped rather than encoded,
                // because it names nothing and the service would answer an absent document for it.
                builder.Append(Uri.EscapeDataString(segments[index]));
            }

            return builder.ToString();
        }
    }
}
