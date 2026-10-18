using System;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site
{
    /// <summary>
    /// What the scanner's index says about the catalogue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a summary and not an analytics console. The totals, the most frequent detections and the shape
    /// of the catalogue over time are what a machine-wide overview needs; the questions that go further --
    /// which model matched which rule, and when -- are answered by the scanner's own API, which owns the
    /// index. A second implementation of them here would be a second answer that could disagree with the
    /// first.
    /// </para>
    /// <para>
    /// The panel is the same one the overview shows, on a page of its own with room to read it.
    /// </para>
    /// </remarks>
    public class Catalogue : DevopsPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Catalogue — " + Settings.Title; }
        }

        /// <summary>
        /// Gets the navigation entry to highlight.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Catalogue"; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ServiceResult<CatalogSummary> catalogue = LoadCatalogue();

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Catalogue",
                "The state of the index the scanner builds. It is a derived file: it can be deleted, and the "
                + "next scan rebuilds it from the catalogue."));

            if (!catalogue.IsSuccess)
            {
                builder.Append(RenderFailure(catalogue));
            }
            else
            {
                builder.Append(Live(
                    "catalogue-panel",
                    "catalogue",
                    DevopsViews.CatalogPanel(catalogue.Value)));
            }

            WriteDocument(PageTitle, Navigation(), builder.ToString());
        }
    }
}
