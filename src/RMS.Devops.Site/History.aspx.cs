using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site
{
    /// <summary>
    /// Every run this machine has recorded, newest first, one page at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list is read for one of two reasons: to find a particular run, or to see how often something is
    /// happening. The second is answered by the pager's own summary line -- how many runs exist -- and the
    /// first by the fact that a run can be filtered down to one service and reached by clicking.
    /// </para>
    /// <para>
    /// The page is the same page with or without a service filter. Two pages would have been two places to
    /// fix, and the filter is a parameter rather than a destination.
    /// </para>
    /// </remarks>
    public class History : DevopsPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get
            {
                string filter = ReadQuery("service", 128);

                return (filter.Length == 0 ? "History" : filter + " history") + " — " + Settings.Title;
            }
        }

        /// <summary>
        /// Gets the navigation entry to highlight.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "History"; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            string filter = ReadQuery("service", 128);

            ServiceResult<RunPage> runs = LoadRuns(filter, Skip, Take);

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                filter.Length == 0 ? "History" : "History for " + filter,
                "One row per run, newest first. A run is a single execution of a service, and it is recorded "
                + "whether it finished well or badly."));

            if (!runs.IsSuccess)
            {
                builder.Append(RenderFailure(runs));
            }
            else
            {
                builder.Append(Html.Panel(
                    "Runs",
                    Live(
                        "run-list",
                        "runs",
                        "service=" + Uri.EscapeDataString(filter)
                        + "&skip=" + Skip.ToString(CultureInfo.InvariantCulture)
                        + "&take=" + Take.ToString(CultureInfo.InvariantCulture),
                        DevopsViews.RunList(runs.Value, showService: true, basePath: "/History.aspx"))));
            }

            builder.Append(RenderFilter(filter));

            WriteDocument(PageTitle, Navigation(), builder.ToString());
        }

        /// <summary>
        /// Renders the links that switch between the whole list and one service.
        /// </summary>
        /// <param name="filter">The service currently being shown, or an empty string.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The filter is a small group of links rather than a dropdown, because the number of services a
        /// machine runs is small and a dropdown would be a form to submit for what a click can do. A machine
        /// that ran enough services to need a dropdown would need a search box, not a select element.
        /// </remarks>
        private string RenderFilter(string filter)
        {
            ServiceResult<List<ServiceSnapshot>> services = LoadServices(recent: 0);

            if (!services.IsSuccess || services.Value == null || services.Value.Count == 0) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append("<ul class=\"nav nav-pills filter-strip\">");

            builder.Append(filter.Length == 0
                ? "<li class=\"active\"><span>All services</span></li>"
                : "<li>" + Html.Link("/History.aspx", "All services") + "</li>");

            foreach (ServiceSnapshot service in services.Value)
            {
                bool active = string.Equals(service.Name, filter, StringComparison.OrdinalIgnoreCase);

                builder.Append(active
                    ? "<li class=\"active\"><span>" + Html.Encode(service.Name) + "</span></li>"
                    : "<li>" + Html.Link(
                        "/History.aspx?service=" + Uri.EscapeDataString(service.Name),
                        service.Name) + "</li>");
            }

            builder.Append("</ul>");

            return builder.ToString();
        }
    }
}
