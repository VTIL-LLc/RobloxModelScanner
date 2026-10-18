using System;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using Scanner.WebForms.Infrastructure;

namespace Scanner.WebForms
{
    /// <summary>
    /// What the indexed catalogue adds up to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The catalogue shows one row at a time; this shows the whole thing at once. It answers the questions a row
    /// cannot: how much was indexed, how much of it carries script, which rules fire and how often, whether the
    /// scores cluster anywhere in particular, and which accounts publish the most.
    /// </para>
    /// <para>
    /// Every number here is computed by the API from indexed aggregates rather than from a walk over the
    /// catalogue, which is why this page is as quick against a hundred and twenty thousand models as against a
    /// hundred. The panels refresh themselves as one region so that they always describe the same instant: a
    /// counters strip that had moved on while the tables beneath it had not would invite arithmetic that does
    /// not add up.
    /// </para>
    /// </remarks>
    public class Analytics : ScannerPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Analytics"; }
        }

        /// <summary>
        /// Gets the navigation section this page belongs to.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Analytics"; }
        }

        /// <summary>
        /// Builds the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ServiceResult<AnalyticsView> analytics = LoadAnalytics();

            WriteDocument(PageTitle, Navigation(), RenderBody(analytics));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="analytics">The analytics view, which may have failed.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(ServiceResult<AnalyticsView> analytics)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Analytics",
                "The whole index at a glance: how much of the catalogue carries script, which rules fire, how the "
                + "scores distribute, and when the models were first seen."));

            if (!analytics.IsSuccess)
            {
                builder.Append(RenderFailure(analytics));

                return builder.ToString();
            }

            builder.Append(Live("analytics", "analytics", RenderRegion(analytics.Value)));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the refreshing part of the page.
        /// </summary>
        /// <param name="analytics">The analytics view.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Static and internal so that the fragment handler fills the region through exactly this method. One
        /// region covers the counters, the instant they were computed and every panel, so a refresh cannot leave
        /// the page describing two different moments.
        /// </remarks>
        internal static string RenderRegion(AnalyticsView analytics)
        {
            return ScannerViews.Counters(analytics)
                   + ScannerViews.Computed(analytics)
                   + ScannerViews.AnalyticsBody(analytics);
        }
    }
}
