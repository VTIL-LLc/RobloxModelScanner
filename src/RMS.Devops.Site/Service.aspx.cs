using System;
using System.Collections.Generic;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site
{
    /// <summary>
    /// One service: what it is, what it runs, and what it has done lately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The facts are ordered the way somebody reads them when they have arrived with a problem: what the
    /// service is, whether it is running, what it last did, then the paths and the command line. The command
    /// line is last because it is the longest thing here and the least often looked at, and putting it first
    /// would push everything else below the fold.
    /// </para>
    /// <para>
    /// The run history is not on this page past the most recent handful. A service that runs every minute has
    /// thousands of runs, and a page that tried to list them would be a page that takes a second to open and
    /// is read by scrolling. The history page is the place for that, and it is one click away.
    /// </para>
    /// </remarks>
    public class Service : DevopsPage
    {
        /// <summary>
        /// How many recent runs are shown on this page.
        /// </summary>
        private const int RecentRuns = 20;

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return ServiceName.Length == 0 ? "Service — " + Settings.Title : ServiceName + " — " + Settings.Title; }
        }

        /// <summary>
        /// Gets the navigation entry to highlight.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Services"; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (ServiceName.Length == 0)
            {
                WriteDocument(
                    PageTitle,
                    Navigation(),
                    Html.Heading("Service", null)
                    + Html.Alert(
                        "warning",
                        "No service was named.",
                        "This page is opened from the service list, which supplies the name. Opening it directly "
                        + "leaves nothing to describe.")
                    + "<p>" + Html.Link("/Services.aspx", "Back to the service list") + "</p>");

                return;
            }

            ServiceResult<ServiceSnapshot> service = LoadService(ServiceName);

            if (!service.IsSuccess)
            {
                WriteDocument(
                    PageTitle,
                    Navigation(),
                    Html.Heading(ServiceName, null)
                    + RenderFailure(service)
                    + "<p>" + Html.Link("/Services.aspx", "Back to the service list") + "</p>");

                return;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                ServiceName,
                service.Value.Description.Length > 0
                    ? service.Value.Description
                    : "No description is recorded for this service."));

            builder.Append(SummaryPanel(service.Value));

            // The runs region is refreshed on its own schedule so that a long log or a wide fact list does not
            // have to be rebuilt to notice that a new run has started.
            builder.Append(Html.Panel(
                "Recent runs",
                Live(
                    "service-runs",
                    "service-runs",
                    "name=" + Uri.EscapeDataString(ServiceName),
                    DevopsViews.RunTable(service.Value.RecentRuns, showService: false))));

            builder.Append(Html.Panel(
                "History",
                "<p>" + Html.Link(
                    "/History.aspx?service=" + Uri.EscapeDataString(ServiceName),
                    "Every run this service has recorded") + "</p>"));

            WriteDocument(PageTitle, Navigation(), builder.ToString());
        }

        /// <summary>
        /// Builds the panel that describes the service.
        /// </summary>
        /// <param name="service">The service.</param>
        /// <returns>The markup.</returns>
        private string SummaryPanel(ServiceSnapshot service)
        {
            return Html.Panel(
                "What it is",
                Live(
                    "service-summary",
                    "service",
                    "name=" + Uri.EscapeDataString(ServiceName),
                    DevopsViews.ServiceFacts(service)));
        }
    }
}
