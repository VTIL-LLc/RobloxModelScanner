using System;
using System.Collections.Generic;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site
{
    /// <summary>
    /// Every service the plan describes, with the columns the overview has no room for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overview and this page show the same list, and that is deliberate rather than duplication: the
    /// overview is read while something is happening and this one is read while something is being
    /// investigated, and the second use wants the executable, the restart policy and the log size in the same
    /// row as the state.
    /// </para>
    /// <para>
    /// A service that the current plan no longer describes is listed rather than hidden. Its history is still
    /// on disk, and a console that quietly dropped it would leave somebody unable to find the log of the
    /// service they removed an hour ago.
    /// </para>
    /// </remarks>
    public class Services : DevopsPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Services — " + Settings.Title; }
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

            ServiceResult<List<ServiceSnapshot>> services = LoadServices(recent: 0);

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Services",
                "Each service is one program the deployment asked this machine to keep running, and one "
                + "directory of run history under the log root."));

            if (!services.IsSuccess)
            {
                builder.Append(RenderFailure(services));
            }
            else if (services.Value == null || services.Value.Count == 0)
            {
                builder.Append(Html.Alert(
                    "info",
                    "No services are known.",
                    "The service is running but its plan describes nothing. Check that RMS_DEVOPS_PLAN_FILE "
                    + "points at the plan the deployment wrote, and that the plan has been generated."));
            }
            else
            {
                builder.Append(Html.Panel(
                    "All services",
                    Live(
                        "service-table",
                        "services",
                        "detailed=1",
                        DevopsViews.ServiceTable(services.Value, detailed: true))));
            }

            WriteDocument(PageTitle, Navigation(), builder.ToString());
        }
    }
}
