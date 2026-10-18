using System;
using System.Collections.Generic;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using Scanner.WebForms.Infrastructure;

namespace Scanner.WebForms
{
    /// <summary>
    /// What the detection rules are and what each one is for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A catalogue row carries labels, and a label is a name rather than an explanation. This page is the
    /// explanation: every rule the scanner has, the category it belongs to, how severe a match is, what it
    /// contributes to a model's score, and the sentence its author wrote for an operator.
    /// </para>
    /// <para>
    /// The reference is read from the API rather than written here, and that is the point of the page. The rules
    /// are compiled into the scanner, so the only honest description of them is the one the scanner itself
    /// publishes; a table maintained on this side would be wrong the first time a rule changed, and wrong in the
    /// way that is hardest to notice.
    /// </para>
    /// </remarks>
    public class Rules : ScannerPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Rules"; }
        }

        /// <summary>
        /// Gets the navigation section this page belongs to.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Rules"; }
        }

        /// <summary>
        /// Builds the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ServiceResult<List<RuleStatisticView>> rules = LoadRules();

            WriteDocument(PageTitle, Navigation(), RenderBody(rules));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="rules">The reference, which may have failed.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(ServiceResult<List<RuleStatisticView>> rules)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Detection rules",
                "Every rule the scanner applies, what it looks for, and how much a match counts toward a model's "
                + "score."));

            builder.Append("<p class=\"text-muted\">A rule's label is what a catalogue row stores. Clicking one "
                           + "here shows every model that matched it. The counts are over the whole index, so a "
                           + "rule with no matches is a rule the scanner carries and the catalogue has not "
                           + "triggered.</p>");

            if (!rules.IsSuccess)
            {
                builder.Append(RenderFailure(rules));

                return builder.ToString();
            }

            builder.Append(Live("rules", "rules", RenderRegion(rules.Value)));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the refreshing part of the page.
        /// </summary>
        /// <param name="rules">The rules.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Static and internal so that the fragment handler fills the region through exactly this method. The
        /// counts are shown here as well as on the analytics page, because a reader asking what a rule means is
        /// also asking whether it ever fires.
        /// </remarks>
        internal static string RenderRegion(List<RuleStatisticView> rules)
        {
            return ScannerViews.RuleTable(rules, showCounts: true);
        }
    }
}
