using System;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Docs.Site.Infrastructure;

namespace RMS.Docs.Site
{
    /// <summary>
    /// The index: every document, grouped by section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is where a reader arrives, and it answers the two questions that have to be answered before anything
    /// else: where the documentation is being read from, and what is in it. The address comes first because
    /// everything below it is a statement about the files it names -- a site pointed at the wrong checkout
    /// produces a complete and entirely wrong answer that looks exactly like a correct one.
    /// </para>
    /// <para>
    /// The whole index is one live region. A document that is added or corrected while somebody is reading
    /// appears here without a reload, which is what makes this page usable as the thing an operator leaves open
    /// while they are editing files.
    /// </para>
    /// </remarks>
    public class Default : DocsPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Contents — " + Settings.Title; }
        }

        /// <summary>
        /// Gets the navigation entry this page belongs to.
        /// </summary>
        protected override string ActiveSection
        {
            get { return "Contents"; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            WriteDocument(PageTitle, Navigation(), RenderBody(LoadDocuments()));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="list">The catalogue, which may have failed.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(ServiceResult<DocumentListView> list)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Documentation",
                "Every page this deployment's documentation directory holds. Each one is the file itself rather "
                + "than a copy of it, so a page that has been corrected reads correctly here immediately."));

            builder.Append(DocsViews.SearchForm(string.Empty));

            if (!list.IsSuccess)
            {
                builder.Append(RenderFailure(list));

                return builder.ToString();
            }

            builder.Append(Live("catalogue", "catalogue", DocsViews.Index(list.Value)));

            return builder.ToString();
        }
    }
}
