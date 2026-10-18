using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using Scanner.WebForms.Infrastructure;

namespace Scanner.WebForms.Api
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
    /// The view is a name from a fixed list, never a path. This handler reaches the API with the site's key, so a
    /// handler that could be pointed anywhere would be an open proxy standing inside the trust boundary, which is
    /// the worst place to put one.
    /// </para>
    /// <para>
    /// The catalogue's filter arrives in this request's own query string, written there by the page that rendered
    /// the region. It is read back with <see cref="CatalogueFilter.FromQuery"/>, the same reader the page used,
    /// so a refresh cannot come back filtered differently from the page that asked for it.
    /// </para>
    /// </remarks>
    public class FragmentHandler : ServiceProxyHandler
    {
        /// <summary>
        /// The longest search term this handler will read out of an address.
        /// </summary>
        private const int MaximumSearchLength = 128;

        /// <summary>
        /// The longest rule label this handler will read out of an address.
        /// </summary>
        private const int MaximumTagLength = 160;

        /// <summary>
        /// Gets a value indicating whether the caller must have signed in.
        /// </summary>
        /// <remarks>
        /// It must not, because the catalogue and a model's page refresh themselves without a session and these
        /// regions are how they do it. The decision is therefore per view rather than per handler, and it is
        /// taken in <see cref="Handle"/> before a view is built: the two views those pages own are answered to
        /// anybody, and every other view is answered to nobody without a session.
        /// </remarks>
        protected override bool RequiresSignIn
        {
            get { return false; }
        }

        /// <summary>
        /// Builds the response.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        protected override void Handle(HttpContext context)
        {
            string view = ReadText(context, "view", 32);

            if (!IsPublicView(view) && CurrentUser == null)
            {
                // A status rather than a body. The caller is the script on a page that has been left open since
                // the session ended, and the only useful thing that can happen next is that the page notices and
                // sends the reader to sign in again.
                Refuse(context, 401, "Sign in to read this.");
                return;
            }

            switch (view)
            {
                case "models": SendCatalogue(context); break;
                case "analytics": SendAnalytics(context); break;
                case "rules": SendRules(context); break;
                case "model": SendModel(context); break;

                default:
                    Refuse(context, 400, "That is not one of the views this site builds.");
                    break;
            }
        }

        /// <summary>
        /// Answers whether a view belongs to a page that anybody may read.
        /// </summary>
        /// <param name="view">The view's name.</param>
        /// <returns><see langword="true"/> when the view may be built for a visitor without a session.</returns>
        /// <remarks>
        /// The two names are the regions of the catalogue and of a model's page. They are named here rather than
        /// derived from the pages, because this is the list that decides what an outsider can make the site do,
        /// and a list that grew by itself would be a list nobody read.
        /// </remarks>
        private static bool IsPublicView(string view)
        {
            return string.Equals(view, "models", StringComparison.Ordinal)
                   || string.Equals(view, "model", StringComparison.Ordinal);
        }

        /// <summary>
        /// Answers with a page of the catalogue and the pager beneath it.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void SendCatalogue(HttpContext context)
        {
            CatalogueFilter filter = ReadFilter(context);

            ServiceResult<ModelPage> models = ScannerService.Models(Client, filter);

            if (!models.IsSuccess)
            {
                WriteHtml(context, ScannerViews.Failure(models.StatusCode, models.Error));
                return;
            }

            // The reference is read again here rather than carried in the region's address. It is needed to
            // attribute the labels on rows written before the current rule set, and an address that carried it
            // would be an address carrying a list of every rule in the scanner.
            WriteHtml(context, Default.RenderRegion(models.Value, filter, LabelsOf(ScannerService.Rules(Client))));
        }

        /// <summary>
        /// Answers with the whole analytics page's content.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <remarks>
        /// One view and one call for the counters, the instant and every panel. A separate view for the counters
        /// would mean two calls per refresh and two answers that describe different instants, which is exactly the
        /// arithmetic a reader must not be invited to do.
        /// </remarks>
        private void SendAnalytics(HttpContext context)
        {
            ServiceResult<AnalyticsView> result = ScannerService.Analytics(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? Analytics.RenderRegion(result.Value)
                    : ScannerViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with the rule reference.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void SendRules(HttpContext context)
        {
            ServiceResult<List<RuleStatisticView>> result = ScannerService.Rules(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? Rules.RenderRegion(result.Value)
                    : ScannerViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with one model's detail.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void SendModel(HttpContext context)
        {
            long identifier;

            if (!long.TryParse(
                    ReadText(context, "id", 20),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out identifier)
                || identifier <= 0)
            {
                Refuse(context, 400, "That is not a model identifier.");
                return;
            }

            WriteHtml(
                context,
                Model.RenderRegion(
                    ScannerService.Model(Client, identifier),
                    identifier,
                    ScannerService.Rules(Client)));
        }

        /// <summary>
        /// Reads the catalogue filter out of this request's address.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <returns>The filter.</returns>
        /// <remarks>
        /// The shared reader already bounds the page number, the page size and the sort key. The search term and
        /// the rule label are bounded here as well, because they are the two values that end up inside the API's
        /// own query and the two whose length its index is sensitive to.
        /// </remarks>
        private static CatalogueFilter ReadFilter(HttpContext context)
        {
            CatalogueFilter filter = CatalogueFilter.FromQuery(context.Request.QueryString);

            if (filter.Search != null && filter.Search.Length > MaximumSearchLength)
            {
                filter.Search = filter.Search.Substring(0, MaximumSearchLength);
            }

            if (filter.RuleTag != null && filter.RuleTag.Length > MaximumTagLength)
            {
                filter.RuleTag = filter.RuleTag.Substring(0, MaximumTagLength);
            }

            return filter;
        }

        /// <summary>
        /// Reads the labels the rule reference names.
        /// </summary>
        /// <param name="rules">The reference, which may have failed.</param>
        /// <returns>The labels, or an empty list.</returns>
        /// <remarks>
        /// A failed reference yields an empty list rather than an exception. The catalogue is still worth
        /// refreshing when the rule reference cannot be read -- every row's own score and identifier are still
        /// true -- and the rows that cannot be attributed say so themselves.
        /// </remarks>
        private static IReadOnlyList<string> LabelsOf(ServiceResult<List<RuleStatisticView>> rules)
        {
            List<string> labels = new List<string>();

            if (rules == null || !rules.IsSuccess || rules.Value == null) return labels;

            foreach (RuleStatisticView rule in rules.Value)
            {
                if (!string.IsNullOrWhiteSpace(rule.Tag)) labels.Add(rule.Tag);
            }

            return labels;
        }
    }
}
