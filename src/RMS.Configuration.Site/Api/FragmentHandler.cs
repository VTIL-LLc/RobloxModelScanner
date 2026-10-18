using System;
using System.Collections.Generic;
using System.Text;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Configuration.Site.Infrastructure;

namespace RMS.Configuration.Site.Api
{
    /// <summary>
    /// Rebuilds one region of a page as markup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The site's regions refresh by replacing their contents with an answer from here. The answer is markup and
    /// not data, and that is the point: the table that arrives is built by the same method that built the one
    /// already on the page, so a column cannot exist in one and be missing from the other, and a fix to a
    /// rendering cannot be applied in one place only.
    /// </para>
    /// <para>
    /// The view is a name from a fixed list, never a path. This handler reaches the service with the site's key,
    /// so a handler that could be pointed anywhere would be an open proxy standing inside the trust boundary --
    /// which is the worst place to put one. A manifest is likewise named and never pathed: the name is checked
    /// against the four this site knows before it becomes part of an address, and the service checks it again.
    /// </para>
    /// <para>
    /// Every response is one round trip to the service. A page with several regions makes several calls when it
    /// refreshes, which is what keeps one slow region from holding up the others.
    /// </para>
    /// </remarks>
    public class FragmentHandler : ServiceProxyHandler
    {
        /// <summary>
        /// The longest manifest name this handler will consider.
        /// </summary>
        /// <remarks>
        /// One of the four names is eleven characters long, so this is generous by a factor of three and still
        /// refuses to build an address out of something somebody pasted into a terminal.
        /// </remarks>
        private const int MaximumNameLength = 32;

        /// <summary>
        /// Builds the response.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        protected override void Handle(HttpContext context)
        {
            switch (ReadText(context, "view", 32))
            {
                case "overview": Overview(context); break;
                case "manifests": Manifests(context); break;
                case "manifest": Manifest(context); break;
                case "plan": Plan(context); break;
                case "vocabulary": Vocabulary(context); break;
                default: Refuse(context, 400, "That is not one of the views this site builds."); break;
            }
        }

        /// <summary>
        /// Answers with the addresses the service is reading and what it found there.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <remarks>
        /// This is the region that says a console is pointed somewhere else, so it is the one that has to be
        /// able to change without a reload: an operator repoints a deployment, reloads nothing, and watches this
        /// region start describing the right checkout.
        /// </remarks>
        private void Overview(HttpContext context)
        {
            ServiceResult<ManifestListView> result = ConfigurationService.Manifests(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? ConfigurationViews.AddressHeader(result.Value)
                    : ConfigurationViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with the table of manifests.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Manifests(HttpContext context)
        {
            ServiceResult<ManifestListView> result = ConfigurationService.Manifests(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? ConfigurationViews.ManifestTable(result.Value)
                    : ConfigurationViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with one manifest's facts, sections, settings and problems.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <remarks>
        /// The whole body of the manifest page, in one answer. Splitting it into three regions would mean three
        /// round trips that read the same file three times, and three answers about a file that could have
        /// changed in between -- so a section table and a settings table could describe two different states of
        /// one manifest.
        /// </remarks>
        private void Manifest(HttpContext context)
        {
            string name = ReadText(context, "name", MaximumNameLength);

            if (!IsKnown(name))
            {
                Refuse(context, 400, "That is not one of the manifests this site knows.");
                return;
            }

            ServiceResult<ManifestDocumentView> result = ConfigurationService.Manifest(Client, name);

            WriteHtml(
                context,
                result.IsSuccess
                    ? ConfigurationViews.ManifestBody(result.Value)
                    : ConfigurationViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with the plan the deployer generated.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Plan(HttpContext context)
        {
            ServiceResult<PlanView> result = ConfigurationService.Plan(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? ConfigurationViews.PlanBody(result.Value)
                    : ConfigurationViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with the keys one manifest kind may carry.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Vocabulary(HttpContext context)
        {
            string name = ReadText(context, "name", MaximumNameLength);

            if (!IsKnown(name))
            {
                Refuse(context, 400, "That is not one of the manifests this site knows.");
                return;
            }

            ServiceResult<VocabularyView> result = ConfigurationService.Vocabulary(Client);

            if (!result.IsSuccess)
            {
                WriteHtml(context, ConfigurationViews.Failure(result.StatusCode, result.Error));
                return;
            }

            VocabularySectionView section = ConfigurationService.SectionFor(result.Value, name);

            StringBuilder builder = new StringBuilder();

            builder.Append(ConfigurationViews.VocabularyTable(section));

            if (result.Value.Limits != null && result.Value.Limits.Count > 0)
            {
                builder.Append("<h4>The deployer's limits</h4>");
                builder.Append(ConfigurationViews.LimitTable(result.Value.Limits));
            }

            WriteHtml(context, builder.ToString());
        }

        /// <summary>
        /// Determines whether a name is one of the manifests this site has a page for.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns><see langword="true"/> when the name is known.</returns>
        private static bool IsKnown(string name)
        {
            return ConfigurationService.PageFor(name).Length > 0;
        }
    }
}
