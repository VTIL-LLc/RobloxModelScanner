using System;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Configuration.Site.Infrastructure;

namespace RMS.Configuration.Site
{
    /// <summary>
    /// The overview: what this deployment is made of, in one screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the page an operator opens first, and it answers the question that has to be answered before any
    /// other: which checkout is this console reading. The addresses come first because everything below them is
    /// a statement about the files they name, and a console pointed at the wrong directory produces a complete
    /// and entirely wrong answer that looks exactly like a correct one.
    /// </para>
    /// <para>
    /// The plan is included because it is the one artefact that is generated rather than written. A manifest
    /// that was changed and never turned into a plan is the ordinary way a deployment comes to differ from its
    /// description, and this is the page where that shows up.
    /// </para>
    /// </remarks>
    public class Default : ConfigurationPage
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

            ServiceResult<ManifestListView> manifests = LoadManifests();
            ServiceResult<PlanView> plan = LoadPlan();

            WriteDocument(PageTitle, Navigation(), RenderBody(manifests, plan));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="manifests">The manifest list, which may have failed.</param>
        /// <param name="plan">The plan, which may have failed.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(ServiceResult<ManifestListView> manifests, ServiceResult<PlanView> plan)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Overview",
                "The files this deployment is described by, the addresses the service is reading them from, and "
                + "the plan the deployer last generated from them."));

            if (!manifests.IsSuccess)
            {
                // One notice, not two. Both regions come from the same service, so a call that could not
                // complete takes the other with it -- and repeating one sentence twice reads as two faults.
                builder.Append(RenderFailure(manifests));

                return builder.ToString();
            }

            builder.Append(Live("address-header", "overview", ConfigurationViews.AddressHeader(manifests.Value)));

            builder.Append(Html.Panel(
                "Manifests",
                Live(
                    "manifest-table",
                    "manifests",
                    ConfigurationViews.ManifestTable(manifests.Value))));

            builder.Append(Html.Panel(
                "The plan",
                plan.IsSuccess
                    ? Live("plan-panel", "plan", ConfigurationViews.PlanBody(plan.Value))
                    : RenderFailure(plan)));

            return builder.ToString();
        }
    }
}
