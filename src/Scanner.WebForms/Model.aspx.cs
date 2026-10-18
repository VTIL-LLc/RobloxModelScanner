using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using Scanner.WebForms.Infrastructure;

namespace Scanner.WebForms
{
    /// <summary>
    /// One model: what the scanner recorded about it, and what the rules found in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is where an operator lands when a row in the catalogue interests them. It answers the two questions
    /// the catalogue cannot: what each signal on the row actually means, and where the asset can be had. The
    /// second is answered by a link that resolves a download address when it is followed, because Roblox issues
    /// those addresses signed and time-limited and one fetched at page-render time would be dead by the time
    /// anybody clicked it.
    /// </para>
    /// <para>
    /// The detail is found by searching the catalogue for the identifier rather than by a route of its own,
    /// because the API publishes a search. That is why the page has something to say when the search comes back
    /// with models that are near the one asked for but not it: the identifier is matched exactly, and the near
    /// ones are offered rather than silently shown as though one of them were the answer.
    /// </para>
    /// </remarks>
    public class Model : ScannerPage
    {
        /// <summary>
        /// The longest status word this page will read back out of the address.
        /// </summary>
        private const int MaximumStatusLength = 32;

        /// <summary>
        /// Gets the asset identifier the address names, or zero when it names none.
        /// </summary>
        private long PageId
        {
            get { return ModelId; }
        }

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get
            {
                long identifier = PageId;

                return identifier > 0
                    ? "Model " + identifier.ToString(CultureInfo.InvariantCulture)
                    : "Model";
            }
        }

        /// <summary>
        /// Gets the navigation section this page belongs to.
        /// </summary>
        /// <remarks>
        /// Empty, so nothing in the navigation is highlighted. This page is reached from the catalogue, from a
        /// creator's row in the analytics, and from a link somebody was sent -- and highlighting the catalogue
        /// for a reader who arrived from somewhere else would be a claim about how they got here.
        /// </remarks>
        protected override string ActiveSection
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// Gets a value indicating whether a model's page is readable without signing in.
        /// </summary>
        /// <remarks>
        /// It is, because the catalogue links to it and a link that leads to a refusal is worse than no link. The
        /// page answers the two questions a catalogue row raises -- what the rules found in this asset, and where
        /// it can be had -- and neither of those is about this deployment. The download link is deliberately part
        /// of that: resolving an address is a read of Roblox's own delivery service through this site's key, and
        /// the key itself never leaves this side of the boundary.
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

            long identifier = PageId;

            ServiceResult<List<RuleStatisticView>> rules = LoadRules();
            ServiceResult<ModelPage> model = identifier > 0 ? LoadModel(identifier) : null;

            WriteDocument(PageTitle, Navigation(), RenderBody(identifier, model, rules));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="identifier">The identifier the address named, or zero.</param>
        /// <param name="model">The result of asking the API for it.</param>
        /// <param name="rules">The rule reference.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(
            long identifier,
            ServiceResult<ModelPage> model,
            ServiceResult<List<RuleStatisticView>> rules)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                identifier > 0
                    ? "Model " + identifier.ToString(CultureInfo.InvariantCulture)
                    : "Model",
                "What the scanner recorded for this asset, the rules it matched, and where it can be had."));

            builder.Append(RenderStatus());

            if (identifier <= 0)
            {
                builder.Append(Html.Alert(
                    "warning",
                    "The address named no model.",
                    "This page is opened with a model's identifier in its address, such as /Model.aspx?id=12345. "
                    + "Nothing in this address is one."));

                builder.Append("<p><a class=\"btn btn-default\" href=\"")
                       .Append(Html.Attribute(ScannerViews.CatalogueAddress))
                       .Append("\">Back to the catalogue</a></p>");

                return builder.ToString();
            }

            builder.Append(Live("model-detail", "model", "id=" + identifier.ToString(CultureInfo.InvariantCulture),
                RenderRegion(model, identifier, rules)));

            builder.Append("<p><a href=\"")
                   .Append(Html.Attribute(ScannerViews.CatalogueAddress + "?q=" + identifier.ToString(CultureInfo.InvariantCulture)))
                   .Append("\">Look at this model in the catalogue</a>, where the rows around it can be read "
                           + "with it.</p>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the notice a download attempt left behind.
        /// </summary>
        /// <returns>The markup, which is empty when nothing was left.</returns>
        /// <remarks>
        /// <para>
        /// The download link is a handler rather than a direct address, because the API's signed address has to be
        /// resolved with the site's key and cannot be handed to the browser to fetch. When that resolution fails
        /// the handler cannot render a page, so it redirects here with a short status word, and this turns the
        /// word into a sentence.
        /// </para>
        /// <para>
        /// A word this page does not know is shown as a generic failure rather than ignored. An unreadable code
        /// means the two ends of this site have drifted, and saying nothing would hide that.
        /// </para>
        /// </remarks>
        private string RenderStatus()
        {
            string status = ReadQuery("status", MaximumStatusLength);

            switch (status)
            {
                case "":
                    return string.Empty;

                case "unusable":
                    return Html.Alert(
                        "warning",
                        "The download address was not usable.",
                        "Roblox answered with an address that is not a Roblox address over HTTPS, so this site "
                        + "would not send a browser to it. This is either a change on Roblox's side or something "
                        + "between this machine and it.");

                case "unavailable":
                    return Html.Alert(
                        "warning",
                        "That asset has no downloadable address.",
                        "Roblox answered that it holds no content address for this asset. Archived and moderated "
                        + "assets commonly answer this way.");

                case "refused":
                    return Html.Alert(
                        "warning",
                        "The API refused the download request.",
                        "The key this site presents for downloads is not the one the API was started with.");

                case "offline":
                    return Html.Alert(
                        "danger",
                        "The API is not answering.",
                        "Nothing is listening where this site expects the scanner's API, so the address could not "
                        + "be resolved.");

                // The API holds no session at all. Roblox has required one for asset delivery since April
                // 2025, so an API started without a cookie can resolve nothing and never could.
                case "nocookie":
                    return Html.Alert(
                        "warning",
                        "The API has no Roblox session.",
                        "The API was started without a Roblox session cookie, and Roblox has required one for "
                        + "asset delivery since April 2025. Paste a cookie into the scanner's settings file and "
                        + "restart the API.");

                // The API holds a session and Roblox would not accept it. This is the case that used to be
                // reported as a missing cookie, which sent whoever was reading it looking for a setting that
                // was already correct.
                case "stale":
                    return Html.Alert(
                        "warning",
                        "The API's Roblox session is out of date.",
                        "The API holds a session cookie and Roblox did not accept it, so no address can be "
                        + "issued. A cookie stops being accepted when the account signs out or its password "
                        + "changes. Paste a fresh one into the scanner's settings file and restart the API.");

                case "unresolved":
                    return Html.Alert(
                        "warning",
                        "The API could not resolve an address for this asset.",
                        "The API reached Roblox's delivery service and no usable address came back. Assets that "
                        + "Roblox has removed or moderated answer this way, as does a delivery service having a "
                        + "bad day. Trying again in a moment is reasonable.");

                case "slow":
                    return Html.Alert(
                        "warning",
                        "The API did not answer in time.",
                        "Resolving an address makes Roblox's own delivery service do the work, and it took longer "
                        + "than this site was prepared to wait. Trying again usually works.");

                default:
                    return Html.Alert(
                        "danger",
                        "The download link could not be resolved.",
                        "The handler that resolves one answered with a reason this page does not know. The site's "
                        + "own error log has the detail.");
            }
        }

        /// <summary>
        /// Renders the model's detail, which is also what the refreshing region is filled with.
        /// </summary>
        /// <param name="model">The result of asking the API for the model.</param>
        /// <param name="identifier">The identifier that was asked for.</param>
        /// <param name="rules">The rule reference.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Static and internal so that the fragment handler builds the region through exactly this method. The
        /// handler and the page reach it with the same three values, so a refresh cannot produce a different
        /// panel set from the one the page was rendered with.
        /// </remarks>
        internal static string RenderRegion(
            ServiceResult<ModelPage> model,
            long identifier,
            ServiceResult<List<RuleStatisticView>> rules)
        {
            if (model == null)
            {
                return Html.Alert("warning", "There is nothing to show.", "No model was asked for.");
            }

            if (!model.IsSuccess)
            {
                return ScannerViews.Failure(model.StatusCode, model.Error);
            }

            ModelRow row = ScannerViews.Find(model.Value, identifier);

            if (row == null) return RenderMissing(model.Value, identifier, rules);

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Panel("Facts", ScannerViews.ModelFacts(row)));
            builder.Append(Html.Panel("Description", ScannerViews.ModelDescription(row)));
            builder.Append(Html.Panel("Detections", ScannerViews.ModelRules(row, rules == null ? null : rules.Value)));

            return builder.ToString();
        }

        /// <summary>
        /// Renders what is shown when the catalogue does not hold the identifier that was asked for.
        /// </summary>
        /// <param name="page">The page the search answered with.</param>
        /// <param name="identifier">The identifier that was asked for.</param>
        /// <param name="rules">The rule reference.</param>
        /// <returns>The markup.</returns>
        private static string RenderMissing(
            ModelPage page,
            long identifier,
            ServiceResult<List<RuleStatisticView>> rules)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Alert(
                "warning",
                "The index does not hold model " + identifier.ToString(CultureInfo.InvariantCulture) + ".",
                "Either the scanner has not seen this asset or it has not been indexed yet. The rows below matched "
                + "the search that was used to find it, and one of them may be what was meant."));

            List<string> labels = new List<string>();

            if (rules != null && rules.IsSuccess && rules.Value != null)
            {
                foreach (RuleStatisticView rule in rules.Value)
                {
                    if (!string.IsNullOrWhiteSpace(rule.Tag)) labels.Add(rule.Tag);
                }
            }

            builder.Append(ScannerViews.ModelTable(page, labels, linked: true));

            return builder.ToString();
        }
    }
}
