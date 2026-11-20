using System;
using System.Globalization;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using Scanner.WebForms.Infrastructure;

namespace Scanner.WebForms
{
    /// <summary>
    /// One model's instance tree, read in the visitor's browser.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree is built by the page's script from the payload itself: the browser fetches the model through the
    /// same handler the download link uses, and parses it there. The server does no part of that -- it writes an
    /// element with an identifier on it and nothing else -- which is what keeps a large model from becoming work
    /// on the site's own machine and what lets the tree be as detailed as the file is.
    /// </para>
    /// <para>
    /// Reading the payload in the browser also means the reader can search it, expand it and click into it without
    /// a request per click. The one request the page makes after it renders is the payload, and that is the same
    /// request the download link makes, so a model that cannot be downloaded is a model that cannot be explored,
    /// and the page says so in the words the model page would.
    /// </para>
    /// </remarks>
    public class Explorer : ScannerPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get
            {
                long identifier = ModelId;

                return identifier > 0
                    ? "Explorer " + identifier.ToString(CultureInfo.InvariantCulture)
                    : "Explorer";
            }
        }

        /// <summary>
        /// Gets the navigation section this page belongs to.
        /// </summary>
        /// <remarks>
        /// Empty, for the same reason a model's page highlights nothing: this page is reached from a model, and
        /// nothing about arriving here means the reader is browsing the catalogue.
        /// </remarks>
        protected override string ActiveSection
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// Gets a value indicating whether the explorer is readable without signing in.
        /// </summary>
        /// <remarks>
        /// It is. The payload it reads is the asset the download link on the same model page hands out, and the
        /// structure of a public model is not account data. Withholding the explorer while publishing the download
        /// would be a distinction without a difference: the file is the thing, and anybody may already have it.
        /// </remarks>
        protected override bool IsPublic
        {
            get { return true; }
        }

        /// <summary>
        /// Builds the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            long identifier = ModelId;

            ServiceResult<ModelPage> model = identifier > 0 ? LoadModel(identifier) : null;

            WriteDocument(PageTitle, Navigation(), RenderBody(identifier, model));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="identifier">The identifier the address named, or zero.</param>
        /// <param name="model">The result of asking the API for it.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(long identifier, ServiceResult<ModelPage> model)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                identifier > 0
                    ? "Explorer " + identifier.ToString(CultureInfo.InvariantCulture)
                    : "Explorer",
                "The instance tree inside a model, read by this browser from the payload the download link hands "
                + "out. Nothing about the structure is interpreted on the server."));

            if (identifier <= 0)
            {
                builder.Append(Html.Alert(
                    "warning",
                    "The address named no model.",
                    "This page is opened with a model's identifier in its address, such as /Explorer.aspx?id=12345. "
                    + "Nothing in this address is one."));

                builder.Append("<p><a class=\"btn btn-default\" href=\"")
                       .Append(Html.Attribute(ScannerViews.CatalogueAddress))
                       .Append("\">Back to the catalogue</a></p>");

                return builder.ToString();
            }

            if (model == null)
            {
                return builder.Append(Html.Alert(
                    "warning",
                    "There is nothing to show.",
                    "No model was asked for.")).ToString();
            }

            if (!model.IsSuccess)
            {
                builder.Append(RenderFailure(model));
                builder.Append(BackLink(identifier));

                return builder.ToString();
            }

            ModelRow row = ScannerViews.Find(model.Value, identifier);

            if (row == null)
            {
                builder.Append(Html.Alert(
                    "warning",
                    "The index does not hold model " + identifier.ToString(CultureInfo.InvariantCulture) + ".",
                    "Either the scanner has not seen this asset or it has not been indexed yet, so there is no "
                    + "payload to read."));

                builder.Append("<p><a class=\"btn btn-default\" href=\"")
                       .Append(Html.Attribute(ScannerViews.CatalogueAddress + "?q="
                                              + identifier.ToString(CultureInfo.InvariantCulture)))
                       .Append("\">Search the catalogue for it</a></p>");

                return builder.ToString();
            }

            builder.Append(Summary(row));
            builder.Append(Html.Panel("Structure", RenderRoot(identifier)));

            // The note is part of the page rather than the script so that a reader who arrives with script refused
            // is told what the page does and what it cannot do here.
            builder.Append("<noscript><p class=\"text-muted\">This page builds the tree with script, in this "
                           + "browser, from the model's own payload. Without script there is nothing to draw, but "
                           + "the model can still be downloaded.</p></noscript>");

            builder.Append(BackLink(identifier));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the one line naming what is being explored.
        /// </summary>
        /// <param name="row">The model.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A single sentence rather than the full facts panel. The identifier is already in the heading and the
        /// rest of the record is one link away on the model's own page; repeating it here would be a second place
        /// for the same three fields to be rendered differently.
        /// </remarks>
        private static string Summary(ModelRow row)
        {
            string name = string.IsNullOrWhiteSpace(row.Name)
                ? "<span class=\"muted\">no recorded name</span>"
                : "<strong>" + Html.Encode(row.Name) + "</strong>";

            return "<p class=\"explorer-summary\">" + name + "<span class=\"muted\"> &middot; "
                   + (string.IsNullOrWhiteSpace(row.CreatorName)
                       ? "creator not recorded"
                       : "by " + Html.Encode(row.CreatorName))
                   + "</span></p>";
        }

        /// <summary>
        /// Renders the element the script fills with the tree.
        /// </summary>
        /// <param name="identifier">The identifier of the model to read.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The identifier and the endpoint are attributes rather than an inline script, because the site is served
        /// under a policy that allows scripts from its own origin only and an inline call would be refused. The
        /// initial contents are what the reader sees before the payload arrives, and they say so.
        /// </remarks>
        private static string RenderRoot(long identifier)
        {
            return "<div class=\"explorer\" data-explorer=\"1\" data-model=\""
                   + identifier.ToString(CultureInfo.InvariantCulture)
                   + "\" data-endpoint=\"/Api/Download.ashx\">"
                   + "<p class=\"text-muted explorer-status\">Fetching the payload. The model is downloaded to this "
                   + "browser and read here; a large model takes a moment.</p>"
                   + "</div>";
        }

        /// <summary>
        /// Renders the link back to the model's own page.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <returns>The markup.</returns>
        private static string BackLink(long identifier)
        {
            return "<p><a href=\"/Model.aspx?id=" + identifier.ToString(CultureInfo.InvariantCulture)
                   + "\">Back to model " + identifier.ToString(CultureInfo.InvariantCulture) + "</a>, where its "
                   + "recorded facts and the rules it matched are.</p>";
        }
    }
}
