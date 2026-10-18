using System;
using System.Collections.Generic;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using Scanner.WebForms.Infrastructure;

namespace Scanner.WebForms
{
    /// <summary>
    /// The catalogue: what the scanner indexed, filtered and paged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the page the site exists for. An operator arrives with a model identifier, a creator's name or a
    /// rule label, and leaves with the rows that answer it. Everything about the view is in the address -- the
    /// filter, the page number and the ordering -- so a result can be sent to somebody else, bookmarked, or
    /// returned to with the back button.
    /// </para>
    /// <para>
    /// The table is a live region, so it refreshes itself while the page is open. The filter form is not, and
    /// that is deliberate: a control inside a region that replaces its contents is a control that eats what the
    /// reader was in the middle of typing.
    /// </para>
    /// </remarks>
    public class Default : ScannerPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return Settings.Title; }
        }

        /// <summary>
        /// Gets the navigation section this page belongs to.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Catalogue"; }
        }

        /// <summary>
        /// Gets a value indicating whether the catalogue is readable without signing in.
        /// </summary>
        /// <remarks>
        /// It is. This is the page the site is published for: a visitor arrives with an identifier, a name or a
        /// rule label, searches, opens a model, and downloads it. Everything the catalogue reads is the scanner's
        /// own record of public assets, and a visitor who signs in gains the two pages that describe the whole
        /// index rather than a row of it.
        /// </remarks>
        protected override bool IsPublic
        {
            get { return true; }
        }

        /// <summary>
        /// Builds the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            CatalogueFilter filter = Filter;

            ServiceResult<ModelPage> models = LoadModels(filter);

            // The rule reference is read for one reason: attributing the labels stored on rows written before
            // the current rule set. It is read alongside the catalogue rather than after it, because a page that
            // rendered without it would then have to be re-rendered once it arrived.
            ServiceResult<List<RuleStatisticView>> rules = LoadRules();

            WriteDocument(PageTitle, Navigation(), RenderBody(filter, models, rules));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="filter">The filter in force.</param>
        /// <param name="models">The catalogue page, which may have failed.</param>
        /// <param name="rules">The rule reference, which may have failed.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(
            CatalogueFilter filter,
            ServiceResult<ModelPage> models,
            ServiceResult<List<RuleStatisticView>> rules)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Catalogue",
                "Every model the scanner has indexed. Search by identifier, name or creator, narrow it to what "
                + "matched a rule, and follow a signal through to the models that carry it."));

            builder.Append(RenderDenial());

            if (!models.IsSuccess)
            {
                // One notice rather than two. If the rule reference failed as well it failed for the same
                // reason, and two sentences saying the same thing read as two separate faults.
                builder.Append(RenderFailure(models));

                return builder.ToString();
            }

            builder.Append(ScannerViews.FilterForm(filter));

            if (!rules.IsSuccess)
            {
                builder.Append(Html.Alert(
                    "warning",
                    "The rule reference could not be read.",
                    "The catalogue is shown, but a row recorded before the current rule set was written cannot be "
                    + "attributed to a rule by name. The scores and the counts on each row are the scanner's own "
                    + "and are unaffected."));
            }

            builder.Append(Live(
                "catalogue",
                "models",
                ScannerViews.Query(filter, filter.Page),
                RenderRegion(models.Value, filter, Labels(rules))));

            builder.Append("<p class=\"text-muted\">This table refreshes itself. Sorting a column rearranges the "
                           + "rows on this page only; to see a different set of rows, change the filter.</p>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the notice an operator leaves here after being refused a page.
        /// </summary>
        /// <returns>The markup, which is empty when there is nothing to say.</returns>
        /// <remarks>
        /// The administration console sends a signed-in operator who may not read it back to the catalogue with
        /// the sentence explaining the refusal in the address. Nothing else reads that sentence, so this is what
        /// turns it into something the operator can see: a viewer who follows a stale link to the admin page
        /// would otherwise land on the catalogue and be told nothing at all about why.
        /// </remarks>
        private string RenderDenial()
        {
            const int MaximumExplanationLength = 200;

            string denial = ReadQuery("denied", MaximumExplanationLength);

            return string.IsNullOrEmpty(denial)
                ? string.Empty
                : Html.Alert("warning", "That page is not yours to read.", denial);
        }

        /// <summary>
        /// Renders the refreshing part of the page.
        /// </summary>
        /// <param name="page">The catalogue page.</param>
        /// <param name="filter">The filter the page was read with.</param>
        /// <param name="labels">The labels the rule reference names.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Static and internal so that the fragment handler builds a region's contents through exactly this
        /// method. A handler that assembled the table itself would be the second renderer that this design exists
        /// to avoid, and the two would drift the first time a column changed.
        /// </remarks>
        internal static string RenderRegion(ModelPage page, CatalogueFilter filter, IReadOnlyList<string> labels)
        {
            return ScannerViews.ModelTable(page, labels, linked: true)
                   + ScannerViews.Pager(page, filter);
        }
    }
}
