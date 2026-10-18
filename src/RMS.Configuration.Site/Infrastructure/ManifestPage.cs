using System;
using System.Text;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace RMS.Configuration.Site.Infrastructure
{
    /// <summary>
    /// The base class for the pages that show one manifest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three pages show one manifest each and differ only in which one. Everything else -- the heading, the
    /// call, the marker that refreshes the body, the link to the editor -- is the same, and it is the same
    /// because it is written here once. Three copies of this body would be three places for the manifest page
    /// to drift, and the drift would be invisible: two pages would look right and one would be answering about
    /// a different file.
    /// </para>
    /// <para>
    /// The manifest is named by <see cref="ConfigurationPage.ManifestKind"/>, which is one of the four names
    /// the service resolves. A name that is not one of them is reported as a fault in this site rather than
    /// turned into an address, because there is no address for it.
    /// </para>
    /// </remarks>
    public abstract class ManifestPage : ConfigurationPage
    {
        /// <summary>
        /// Gets the heading shown at the top of the page.
        /// </summary>
        protected abstract string Heading { get; }

        /// <summary>
        /// Gets the sentence shown under the heading.
        /// </summary>
        protected abstract string Description { get; }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            WriteDocument(PageTitle, Navigation(), RenderBody());
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <returns>The markup.</returns>
        private string RenderBody()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(Heading, Description));

            ServiceResult<ManifestDocumentView> document = LoadPageManifest();

            if (!document.IsSuccess)
            {
                builder.Append(RenderFailure(document));

                return builder.ToString();
            }

            builder.Append(Live(
                "manifest-body",
                "manifest",
                "name=" + Uri.EscapeDataString(ManifestKind),
                ConfigurationViews.ManifestBody(document.Value)));

            builder.Append(RenderEditorLink(document.Value));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the link to the editor, when the file may be written.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The markup, which is empty for a generated file.</returns>
        /// <remarks>
        /// Offered when the file has errors as well as when it does not, which is the case it matters for: a
        /// manifest the deployer refuses is a manifest an operator is on their way to fix, and hiding the way in
        /// until it is readable would be backwards.
        /// </remarks>
        private static string RenderEditorLink(ManifestDocumentView document)
        {
            if (document == null || !document.Editable) return string.Empty;

            string name = ConfigurationViews.KindName(document.Kind);

            if (name.Length == 0) return string.Empty;

            return "<p>"
                   + Html.Link("/Editor.aspx?name=" + HttpUtility.UrlEncode(name), "Edit this file")
                   + "</p>";
        }
    }
}
