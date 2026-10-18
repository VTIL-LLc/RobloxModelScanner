using System;

using RMS.Configuration.Site.Infrastructure;

namespace RMS.Configuration.Site
{
    /// <summary>
    /// The services manifest: what runs once everything is built, and how each platform runs it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One section per service. This is the file an operator changes most often, because changing what runs is
    /// the ordinary way this stack is adjusted -- and it is also the file where a mistake is least visible from
    /// the file itself, since the value that matters on the machine is the one that survives a placeholder
    /// expansion and a platform qualification.
    /// </para>
    /// <para>
    /// The plan page shows what this file becomes. The two are meant to be read together: this one is what was
    /// declared, and the plan is what the deployer decided it meant.
    /// </para>
    /// </remarks>
    public class Services : ManifestPage
    {
        /// <summary>
        /// Gets the manifest this page shows, as the service names the kind.
        /// </summary>
        protected override string ManifestKind
        {
            get { return "Services"; }
        }

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Services — " + Settings.Title; }
        }

        /// <summary>
        /// Gets the heading shown at the top of the page.
        /// </summary>
        protected override string Heading
        {
            get { return "Services"; }
        }

        /// <summary>
        /// Gets the sentence shown under the heading.
        /// </summary>
        protected override string Description
        {
            get
            {
                return "Every service this deployment registers, and what each platform should do to run it. "
                       + "Nothing here is platform-specific syntax: the same section describes a Windows service, "
                       + "a systemd unit and a launchd property list.";
            }
        }
    }
}
