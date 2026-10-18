using System;
using System.Collections.Generic;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace RMS.Configuration.Site.Infrastructure
{
    /// <summary>
    /// Every call this site makes to the Configuration service, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page and the fragment handler that refreshes one of its regions need the same data, and they need it
    /// from the same route. Writing those routes twice is how the two ends drift: a parameter named one way in
    /// the page and another in the handler produces a refresh that quietly returns something else, and the
    /// symptom is a table that changes shape when it updates.
    /// </para>
    /// <para>
    /// Every method takes the client rather than holding one, so nothing here decides which site is being
    /// served. The client carries the address and the key; this class only knows the shape of the surface.
    /// </para>
    /// <para>
    /// A manifest is named, never pathed. The name is one of four the service knows, and the service turns it
    /// into a configured directory plus a fixed file name. This class escapes the name into the address and does
    /// nothing else with it, because there is nothing else that could be done safely.
    /// </para>
    /// </remarks>
    public static class ConfigurationService
    {
        /// <summary>
        /// Reads every manifest's summary.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<ManifestListView> Manifests(ServiceClient client)
        {
            return client.GetJson<ManifestListView>("/api/manifests");
        }

        /// <summary>
        /// Reads one manifest, checked.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="name">The manifest's name.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<ManifestDocumentView> Manifest(ServiceClient client, string name)
        {
            return client.GetJson<ManifestDocumentView>("/api/manifests/" + Uri.EscapeDataString(name));
        }

        /// <summary>
        /// Checks a manifest's text without writing it.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="name">The manifest's name.</param>
        /// <param name="text">The text to check.</param>
        /// <returns>The result.</returns>
        /// <remarks>
        /// The hash is left empty because a check touches nothing: it is a statement about the text that was
        /// sent rather than about the file, which is what makes it usable before a decision to save.
        /// </remarks>
        public static ServiceResult<ManifestWriteResultView> Check(ServiceClient client, string name, string text)
        {
            return client.PostJson<ManifestWriteRequestView, ManifestWriteResultView>(
                "/api/manifests/" + Uri.EscapeDataString(name) + "/validate",
                new ManifestWriteRequestView { Text = text ?? string.Empty, ExpectedSha256 = string.Empty });
        }

        /// <summary>
        /// Writes a manifest.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="name">The manifest's name.</param>
        /// <param name="text">The text the file should have.</param>
        /// <param name="expectedSha256">The hash the page read, which is the precondition.</param>
        /// <returns>The result.</returns>
        /// <remarks>
        /// The hash is what makes two operators editing one file a refusal rather than a silent
        /// last-writer-wins. It is read from the document the page loaded, never rebuilt here, because a hash
        /// this side computed would be a statement about this side's reading rather than about the file's state.
        /// </remarks>
        public static ServiceResult<ManifestWriteResultView> Save(
            ServiceClient client,
            string name,
            string text,
            string expectedSha256)
        {
            return client.PostJson<ManifestWriteRequestView, ManifestWriteResultView>(
                "/api/manifests/" + Uri.EscapeDataString(name),
                new ManifestWriteRequestView
                {
                    Text = text ?? string.Empty,
                    ExpectedSha256 = expectedSha256 ?? string.Empty,
                });
        }

        /// <summary>
        /// Reads the plan the deployer generated.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<PlanView> Plan(ServiceClient client)
        {
            return client.GetJson<PlanView>("/api/plan");
        }

        /// <summary>
        /// Reads the format the deployer's manifests are written in.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<VocabularyView> Vocabulary(ServiceClient client)
        {
            return client.GetJson<VocabularyView>("/api/vocabulary");
        }

        /// <summary>
        /// Names one of the keys of a vocabulary view, for a page that wants to show what a key means.
        /// </summary>
        /// <param name="vocabulary">The vocabulary.</param>
        /// <param name="kind">The manifest kind to find.</param>
        /// <returns>The section, or <see langword="null"/> when the vocabulary did not describe that kind.</returns>
        /// <remarks>
        /// The comparison is on the service's own spelling of the kind name, so a page and the service always
        /// agree about which vocabulary is which. A missing section is answered with nothing rather than with an
        /// empty one: the editor shows no help at all rather than help that is wrong.
        /// </remarks>
        public static VocabularySectionView SectionFor(VocabularyView vocabulary, string kind)
        {
            if (vocabulary == null || vocabulary.Sections == null) return null;

            foreach (VocabularySectionView section in vocabulary.Sections)
            {
                if (string.Equals(section.Kind, kind, StringComparison.OrdinalIgnoreCase)) return section;
            }

            return null;
        }

        /// <summary>
        /// Pairs each manifest kind with the address of the page that shows it.
        /// </summary>
        /// <returns>The pairs, as the service names the kinds.</returns>
        /// <remarks>
        /// One table, because three places need it: the navigation, the overview's links, and the fragment
        /// handler's view names. A second table anywhere would be a second set of addresses to keep in step.
        /// </remarks>
        public static IList<KeyValuePair<string, string>> Kinds()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Requirements", "/Requirements.aspx"),
                new KeyValuePair<string, string>("Build", "/Build.aspx"),
                new KeyValuePair<string, string>("Services", "/Services.aspx"),
                new KeyValuePair<string, string>("Plan", "/Plan.aspx"),
            };
        }

        /// <summary>
        /// Finds the address of the page that shows a manifest kind.
        /// </summary>
        /// <param name="kind">The kind, as the service names it.</param>
        /// <returns>The address, or an empty string when there is no page for that kind.</returns>
        public static string PageFor(string kind)
        {
            foreach (KeyValuePair<string, string> pair in Kinds())
            {
                if (string.Equals(pair.Key, kind, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }

            return string.Empty;
        }
    }
}
