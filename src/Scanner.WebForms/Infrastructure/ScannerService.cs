using System;
using System.Collections.Generic;
using System.Globalization;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace Scanner.WebForms.Infrastructure
{
    /// <summary>
    /// Every call this site makes to the scanner's API, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page and the fragment handler that refreshes one of its regions need the same data, and they need it
    /// from the same route. Writing those routes twice is how the two ends drift: a parameter spelled one way
    /// in the page and another in the handler produces a refresh that quietly returns a different set from the
    /// one the page rendered, and the symptom is a table that changes shape while somebody is looking at it.
    /// </para>
    /// <para>
    /// Every method takes the client rather than holding one, so nothing here decides which site is being
    /// served. The client carries the address and the key and presents the key the way this API takes it --
    /// which is as a query parameter, named in the site's own configuration -- so nothing here has to know
    /// where a secret travels.
    /// </para>
    /// </remarks>
    public static class ScannerService
    {
        /// <summary>
        /// The route every model document is read from.
        /// </summary>
        private const string ModelsRoute = "/api/models";

        /// <summary>
        /// Reads the rule reference: every rule, its score, its category and what it is for.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>The result.</returns>
        /// <remarks>
        /// This is the answer to "what rules are there and what are they for". It is read on the rules page and
        /// again wherever a stored label has to be attributed to a rule, which is why it is worth reading
        /// rather than inferring from the labels a page happens to contain.
        /// </remarks>
        public static ServiceResult<List<RuleStatisticView>> Rules(ServiceClient client)
        {
            return client.GetJson<List<RuleStatisticView>>("/api/rules");
        }

        /// <summary>
        /// Reads the whole analytics view of the indexed catalogue.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<AnalyticsView> Analytics(ServiceClient client)
        {
            return client.GetJson<AnalyticsView>("/api/analytics/summary");
        }

        /// <summary>
        /// Reads a page of the model catalogue.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="filter">The filter the page is showing.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<ModelPage> Models(ServiceClient client, CatalogueFilter filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            return client.GetJson<ModelPage>(ModelsRoute, filter.ToQuery());
        }

        /// <summary>
        /// Reads one model.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="model">The asset identifier.</param>
        /// <returns>The result.</returns>
        /// <remarks>
        /// Read as a one-row page rather than through a route of its own, because the API publishes a search
        /// and not a lookup by identifier. Searching for the identifier and taking the first row costs the
        /// index the same as any other search and removes a route that would exist only for this page. The
        /// assistant checks that the row that came back is the one that was asked for, so a search that matched
        /// nothing by another column cannot be presented as the model somebody asked for.
        /// </remarks>
        public static ServiceResult<ModelPage> Model(ServiceClient client, long model)
        {
            CatalogueFilter filter = new CatalogueFilter
            {
                Search = model.ToString(CultureInfo.InvariantCulture),
                Page = 1,
                PageSize = 10,
                SortBy = "Model",
                Descending = false,
            };

            return client.GetJson<ModelPage>(ModelsRoute, filter.ToQuery());
        }

        /// <summary>
        /// Fetches an asset's content, with the name it is to be saved under.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="model">The asset identifier.</param>
        /// <returns>The result, carrying the contents, the name and the type.</returns>
        /// <remarks>
        /// <para>
        /// The API answers this route three ways and the site asks for the third. A redirect is what a
        /// browser link wants, and following it on this side would hand the framework a cross-origin response
        /// to render. The signed location as a document is what the site used to ask for and then pass on,
        /// and a browser following that saved the file under the last segment of the address, which is a hash
        /// Roblox generated. The bytes come back through here instead, which is what allows a file to be
        /// named after the asset rather than after its address: only the API knows both the identifier and
        /// the kind of content, so the name arrives with the bytes instead of being guessed from a link.
        /// </para>
        /// <para>
        /// The name and the media type are read here through the same checks every other answer goes
        /// through, so what the handler writes has already been reduced to something a response can carry.
        /// </para>
        /// </remarks>
        public static ServiceResult<ServiceFile> Download(ServiceClient client, long model)
        {
            return client.GetFile(
                ModelsRoute + "/"
                + model.ToString(CultureInfo.InvariantCulture)
                + "/download",
                new KeyValuePair<string, string>("stream", "true"));
        }
    }
}
