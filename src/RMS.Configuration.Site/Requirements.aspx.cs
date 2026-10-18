using System;

using RMS.Configuration.Site.Infrastructure;

namespace RMS.Configuration.Site
{
    /// <summary>
    /// The requirements manifest: what the deployer downloads and installs before it builds anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One section per prerequisite. Each names a tool, how to recognise it on a machine that already has it,
    /// and where to get it on a machine that does not -- which is the whole of what this stack needs in order
    /// to be deployable to a machine that has nothing but a compiler on it.
    /// </para>
    /// <para>
    /// A file the deployer cannot read stops the deployment before it starts, so this page reports the problems
    /// first and in the order they appear in the file.
    /// </para>
    /// </remarks>
    public class Requirements : ManifestPage
    {
        /// <summary>
        /// Gets the manifest this page shows, as the service names the kind.
        /// </summary>
        protected override string ManifestKind
        {
            get { return "Requirements"; }
        }

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Requirements — " + Settings.Title; }
        }

        /// <summary>
        /// Gets the heading shown at the top of the page.
        /// </summary>
        protected override string Heading
        {
            get { return "Requirements"; }
        }

        /// <summary>
        /// Gets the sentence shown under the heading.
        /// </summary>
        protected override string Description
        {
            get
            {
                return "Every prerequisite the deployer checks for and, where it is missing, installs. This is "
                       + "the first file the deployment reads, so an error in it is the difference between a "
                       + "machine that builds itself and a machine that does not.";
            }
        }
    }
}
