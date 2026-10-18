using System;
using System.Text;

using RMS.Site.Common.Implementation;

namespace RMS.Configuration.Site
{
    /// <summary>
    /// What a visitor sees when something went wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This page says as little as it can. The framework's own error page names the assembly, the method and
    /// the line that failed, which is a description of the machine's internals handed to whoever asked; this
    /// one says that the fault is recorded and where it is recorded. An operator who needs the detail reads it
    /// off the disk, where it is written for them and not for the caller.
    /// </para>
    /// <para>
    /// The page is reachable without a session, because the fault being reported may well be the reason there
    /// is no session.
    /// </para>
    /// </remarks>
    public class Error : AnonymousPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Something went wrong — " + Settings.Title; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            StringBuilder builder = new StringBuilder();

            builder.Append("<p>This request could not be completed. The fault has been recorded beside the "
                           + "site's database, and an administrator can read it there.</p>");

            builder.Append("<p class=\"text-muted\">The file is <span class=\"code\">App_Data/")
                   .Append(Html.Encode("site-errors.log"))
                   .Append("</span> under the site's physical root. It holds the exception and the stack that "
                           + "produced it, which is why it is not shown here.</p>");

            WriteDocument(PageTitle, Navigation(), RenderCard("That did not work", builder.ToString()));
        }
    }
}
