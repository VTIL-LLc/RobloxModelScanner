using System;
using System.Collections.Generic;
using System.Net;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace RMS.Configuration.Site.Infrastructure
{
    /// <summary>
    /// The base class for every page of the Configuration console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It supplies the calls the pages make to the service and the small pieces of reading that every page
    /// needs: a bounded value out of the query string, a bounded number, and the way a failed call is described.
    /// The calls themselves live in <see cref="ConfigurationService"/>, so that a page and the handler
    /// refreshing one of its regions cannot disagree about a route.
    /// </para>
    /// <para>
    /// Every call returns a result rather than throwing. A service that is down, that has not been given a key,
    /// or that has nothing configured is a condition these pages exist to describe, and each page decides how
    /// loudly to say it through <see cref="RenderFailure{T}"/>.
    /// </para>
    /// </remarks>
    public abstract class ConfigurationPage : ServicePage
    {
        /// <summary>
        /// The longest manifest text a page will read out of a posted form.
        /// </summary>
        /// <remarks>
        /// The service refuses anything larger than this, so the bound here is the service's own limit restated:
        /// a form post is read into a string of a size this page chose rather than one the caller chose, and a
        /// text at the boundary is sent rather than refused here, which leaves the sentence an operator reads to
        /// the end that knows why it refused.
        /// </remarks>
        protected const int MaximumDocumentLength = 512 * 1024;

        /// <summary>
        /// Gets the manifest this page shows, as the service names the kind.
        /// </summary>
        /// <remarks>
        /// Empty for a page that is not about one manifest, such as the overview or the administration page. A
        /// page that is about one sets it, and everything else follows from that: the heading, the call, the
        /// fragment's view name.
        /// </remarks>
        protected virtual string ManifestKind
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// Gets the sections shown in the navigation bar.
        /// </summary>
        /// <returns>The sections.</returns>
        /// <remarks>
        /// Built from the one table that names the manifests, so a manifest that gains a page gains a
        /// navigation entry in the same edit -- and so that the entry an operator clicks and the page that
        /// answers it cannot name the same manifest differently. The overview is first because it is the page
        /// an operator opens first; everything after it is in the table's own order, which puts the plan last
        /// because the plan is what the three files above it produce.
        /// </remarks>
        protected override IEnumerable<KeyValuePair<string, string>> Navigation()
        {
            List<KeyValuePair<string, string>> sections = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Overview", "/"),
            };

            sections.AddRange(ConfigurationService.Kinds());

            return sections;
        }

        /// <summary>
        /// Reads a bounded text value from a posted form.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="maximumLength">The longest value accepted.</param>
        /// <returns>The value, or an empty string.</returns>
        /// <remarks>
        /// Trimmed, unlike the manifest text. Every field this reads is a name, a hash or a verb, and a value
        /// that arrived with a space around it arrived from something that was not this form.
        /// </remarks>
        protected string ReadField(string name, int maximumLength)
        {
            string raw = Request.Form[name];

            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string value = raw.Trim();

            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        /// <summary>
        /// Reads the manifest text out of a posted form.
        /// </summary>
        /// <param name="text">The text, when it was read.</param>
        /// <returns><see langword="true"/> when the text is usable.</returns>
        /// <remarks>
        /// <para>
        /// Not trimmed: the text is a file, and a file whose first line is a comment begins with a character a
        /// trim would remove.
        /// </para>
        /// <para>
        /// A text that is too long is refused rather than shortened. Everything else in this console clamps a
        /// value it cannot use, because a page number past the end of a list is an ordinary mistake; a file is
        /// not, and writing the first half of what somebody sent is the one outcome nobody would choose.
        /// </para>
        /// </remarks>
        protected bool TryReadManifestText(out string text)
        {
            string raw = Request.Form["text"];

            text = raw ?? string.Empty;

            return text.Length <= MaximumDocumentLength;
        }

        /// <summary>
        /// Determines whether a name is one of the four manifests the service knows.
        /// </summary>
        /// <param name="kind">The name to check.</param>
        /// <returns><see langword="true"/> when the service would resolve it.</returns>
        /// <remarks>
        /// The check is against this side's table rather than against the service, because the check has to
        /// happen before the address is built. The service refuses an unknown name itself, and that refusal is
        /// the one that matters; this one keeps a page from asking a question it knows the shape of the answer to.
        /// </remarks>
        protected static bool IsKnownKind(string kind)
        {
            return ConfigurationService.PageFor(kind).Length > 0;
        }

        /// <summary>
        /// Renders a failed call as something an operator can act on.
        /// </summary>
        /// <param name="status">The status the service answered with, or the one this site synthesised.</param>
        /// <param name="error">The sentence the client recorded.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A refusal and an outage are shown differently, because they have different remedies. A refusal means
        /// the two ends disagree about a secret; an outage means the service is not there. A page that showed
        /// both as "could not load" would send the operator to the wrong machine roughly half the time.
        /// </remarks>
        protected string RenderFailure(HttpStatusCode status, string error)
        {
            return ConfigurationViews.Failure(status, error);
        }

        /// <summary>
        /// Renders a failed call as something an operator can act on.
        /// </summary>
        /// <typeparam name="T">The shape the call was read into.</typeparam>
        /// <param name="result">The failed result.</param>
        /// <returns>The markup.</returns>
        protected string RenderFailure<T>(ServiceResult<T> result)
            where T : class
        {
            if (result == null) return string.Empty;

            return RenderFailure(result.StatusCode, result.Error);
        }

        /// <summary>
        /// Loads every manifest's summary.
        /// </summary>
        /// <returns>The result.</returns>
        protected ServiceResult<ManifestListView> LoadManifests()
        {
            return ConfigurationService.Manifests(Client);
        }

        /// <summary>
        /// Loads one manifest, checked.
        /// </summary>
        /// <param name="name">The manifest's name.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<ManifestDocumentView> LoadManifest(string name)
        {
            return ConfigurationService.Manifest(Client, name);
        }

        /// <summary>
        /// Loads the manifest this page is about.
        /// </summary>
        /// <returns>The result, or a failure describing the page's own misconfiguration.</returns>
        protected ServiceResult<ManifestDocumentView> LoadPageManifest()
        {
            if (!IsKnownKind(ManifestKind))
            {
                return ServiceResult<ManifestDocumentView>.Failure(
                    HttpStatusCode.BadRequest,
                    "This page is not about one of the manifests, which is a fault in the site rather than in "
                    + "the deployment.");
            }

            return LoadManifest(ManifestKind);
        }

        /// <summary>
        /// Loads the plan the deployer generated.
        /// </summary>
        /// <returns>The result.</returns>
        protected ServiceResult<PlanView> LoadPlan()
        {
            return ConfigurationService.Plan(Client);
        }

        /// <summary>
        /// Loads the format the manifests are written in.
        /// </summary>
        /// <returns>The result.</returns>
        protected ServiceResult<VocabularyView> LoadVocabulary()
        {
            return ConfigurationService.Vocabulary(Client);
        }

        /// <summary>
        /// Checks a manifest's text without writing it.
        /// </summary>
        /// <param name="name">The manifest's name.</param>
        /// <param name="text">The text to check.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<ManifestWriteResultView> CheckManifest(string name, string text)
        {
            return ConfigurationService.Check(Client, name, text);
        }

        /// <summary>
        /// Writes a manifest.
        /// </summary>
        /// <param name="name">The manifest's name.</param>
        /// <param name="text">The text the file should have.</param>
        /// <param name="expectedSha256">The hash the page read.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<ManifestWriteResultView> SaveManifest(
            string name,
            string text,
            string expectedSha256)
        {
            return ConfigurationService.Save(Client, name, text, expectedSha256);
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// This forwards to <c>ConfigurationViews.Live</c> so that a page and the fragment handler refreshing
        /// the same region build the same element. A page that wrapped its regions its own way would produce a
        /// region the script then refreshed into a slightly different shape than it started in.
        /// </remarks>
        protected static string Live(string id, string view, string content)
        {
            return ConfigurationViews.Live(id, view, content);
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes, with parameters of its own.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="extraQuery">Additional query parameters for the fragment, already encoded.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        protected static string Live(string id, string view, string extraQuery, string content)
        {
            return ConfigurationViews.Live(id, view, extraQuery, content);
        }
    }
}
