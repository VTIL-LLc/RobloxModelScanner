using System;
using System.Collections.Generic;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site
{
    /// <summary>
    /// The overview: what is on this machine, in one screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the page an operator opens first, so it answers three questions in the order they are asked:
    /// is everything running, is anything broken, and what has been built. The catalogue is included because
    /// the scanner's index is the other thing on this machine that goes stale without saying so, and a
    /// console that reported the services but not the index would be reporting half of what an operator is
    /// responsible for.
    /// </para>
    /// <para>
    /// Every region here refreshes itself. Nothing on this page is worth reading slowly: it exists to be
    /// glanced at while something is happening, and a page that had to be reloaded by hand would be showing
    /// whatever was true when it was opened.
    /// </para>
    /// </remarks>
    public class Default : DevopsPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Overview — " + Settings.Title; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ServiceResult<FleetSummary> fleet = LoadFleet();
            ServiceResult<List<ServiceSnapshot>> services = LoadServices(recent: 0);
            ServiceResult<ArtifactList> artifacts = LoadArtifacts();

            WriteDocument(PageTitle, Navigation(), RenderBody(fleet, services, artifacts));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="fleet">The fleet summary, which may have failed.</param>
        /// <param name="services">The service list, which may have failed.</param>
        /// <param name="artifacts">The artefact list, which may have failed.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(
            ServiceResult<FleetSummary> fleet,
            ServiceResult<List<ServiceSnapshot>> services,
            ServiceResult<ArtifactList> artifacts)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Overview",
                "Everything this machine has been asked to run, and the state of the catalogue the scanner "
                + "indexes."));

            builder.Append(RenderDenied());

            if (!fleet.IsSuccess)
            {
                // One notice, not four. Every panel on this page comes from the same service, so if the call
                // that reports the machine could not complete, the three that follow it will fail identically
                // -- and repeating one sentence four times reads as four separate faults.
                builder.Append(RenderFailure(fleet));

                return builder.ToString();
            }

            builder.Append(Live("fleet-header", "fleet", DevopsViews.FleetHeader(fleet.Value)));

            builder.Append(Html.Panel(
                "Services",
                Live(
                    "service-table",
                    "services",
                    DevopsViews.ServiceTable(
                        services.IsSuccess ? services.Value : new List<ServiceSnapshot>(),
                        detailed: false))));

            builder.Append(Html.Panel(
                "Catalogue",
                Live(
                    "catalogue-panel",
                    "catalogue",
                    DevopsViews.CatalogPanel(fleet.Value.Catalog ?? new CatalogSummary { Available = false }))));

            builder.Append(Html.Panel(
                "Published artefacts",
                Live(
                    "artifact-table",
                    "artifacts",
                    DevopsViews.ArtifactTable(
                        artifacts.IsSuccess ? artifacts.Value.Artifacts : new List<ArtifactSnapshot>()))));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the notice shown when an operator was refused something.
        /// </summary>
        /// <returns>The markup, which is empty when nothing was refused.</returns>
        /// <remarks>
        /// The refusal is carried in the query rather than held in the session, so it survives the redirect
        /// that produced it without leaving something behind that would be shown again on the next visit.
        /// </remarks>
        private string RenderDenied()
        {
            string denied = ReadQuery("denied", 512);

            return denied.Length == 0 ? string.Empty : Html.Alert("warning", "That was refused.", denied);
        }
    }
}
