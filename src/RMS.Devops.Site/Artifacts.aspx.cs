using System;
using System.Collections.Generic;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site
{
    /// <summary>
    /// Every program the deployment has published, and which service runs out of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything on this page is read off the artefacts themselves -- their file counts, their sizes, the
    /// version in the assembly's own metadata -- which is what makes it unable to go stale. Nothing recorded
    /// which deployment produced a directory, so this page does not claim to know; what it can say with
    /// certainty is what is on the disk right now, and that is what is being asked.
    /// </para>
    /// <para>
    /// The join from an artefact to a service is made through the plan rather than by comparing directory
    /// names. Two services can be published into directories whose names resemble each other, and a page that
    /// guessed from the name would eventually attribute one program's runs to another.
    /// </para>
    /// </remarks>
    public class Artifacts : DevopsPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Artefacts — " + Settings.Title; }
        }

        /// <summary>
        /// Gets the navigation entry to highlight.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Artefacts"; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ServiceResult<ArtifactList> artifacts = LoadArtifacts();

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Artefacts",
                "Each row is one published program. The version is the one the assembly was built with, and the "
                + "size is measured from the files that are actually present."));

            if (!artifacts.IsSuccess)
            {
                builder.Append(RenderFailure(artifacts));
            }
            else if (artifacts.Value.Artifacts == null || artifacts.Value.Artifacts.Count == 0)
            {
                builder.Append(Html.Alert(
                    "info",
                    "Nothing has been published.",
                    "The publish root is empty, which is what a machine looks like before its first deployment. "
                    + "The root being read is shown below."));
            }
            else
            {
                builder.Append(Html.Field("Publish root", Html.Value(artifacts.Value.Root)));

                builder.Append(Html.Panel(
                    "Published programs",
                    Live(
                        "artifact-table",
                        "artifacts",
                        DevopsViews.ArtifactTable(artifacts.Value.Artifacts))));
            }

            WriteDocument(PageTitle, Navigation(), builder.ToString());
        }
    }
}
