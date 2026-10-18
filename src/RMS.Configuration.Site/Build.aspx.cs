using System;

using RMS.Configuration.Site.Infrastructure;

namespace RMS.Configuration.Site
{
    /// <summary>
    /// The build manifest: what the deployer builds, in what order, and what it publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One section per project in the solution that is built and published. The order is the file's own order,
    /// which makes this manifest the description of the deployment as much as the projects are: a project that
    /// is absent from it is a project that is not deployed, and that is a thing this page is read to confirm.
    /// </para>
    /// <para>
    /// A step whose project cannot be found is reported as a warning rather than as an error, because a
    /// solution that has been rewritten may legitimately have a step pointing at a path that no longer exists,
    /// and refusing to describe the rest of the file because of it would be worse than saying so.
    /// </para>
    /// </remarks>
    public class Build : ManifestPage
    {
        /// <summary>
        /// Gets the manifest this page shows, as the service names the kind.
        /// </summary>
        protected override string ManifestKind
        {
            get { return "Build"; }
        }

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Build — " + Settings.Title; }
        }

        /// <summary>
        /// Gets the heading shown at the top of the page.
        /// </summary>
        protected override string Heading
        {
            get { return "Build"; }
        }

        /// <summary>
        /// Gets the sentence shown under the heading.
        /// </summary>
        protected override string Description
        {
            get
            {
                return "Every project the deployer builds and where its output is published. The order here is "
                       + "the order the deployment runs in, and a project that is not in this file is a project "
                       + "that is not deployed at all.";
            }
        }
    }
}
