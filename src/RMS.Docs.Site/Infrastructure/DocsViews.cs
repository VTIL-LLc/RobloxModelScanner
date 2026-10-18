using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

using RMS.Site.Common.Implementation;

namespace RMS.Docs.Site.Infrastructure
{
    /// <summary>
    /// Renders the documentation site's pieces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every method here returns markup and takes a model, which is what lets a page and the handler that
    /// refreshes one of its regions produce the same thing. A page that built its own table and a handler that
    /// built a different one would look correct until the first refresh, and then the two would disagree in a way
    /// nobody would think to look for.
    /// </para>
    /// <para>
    /// Nothing here writes a value into a response without encoding it, and every address is this site's own --
    /// built from a slug that has been through <see cref="DocsService.PageFor"/> and never from a URL a document
    /// or a caller supplied.
    /// </para>
    /// </remarks>
    public static class DocsViews
    {
        /// <summary>
        /// The label given to documents that sit at the top level of the documentation directory.
        /// </summary>
        private const string RootSectionLabel = "Overview";

        /// <summary>
        /// Wraps a region in the element the site's script refreshes.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The region is rendered now and refreshed afterwards, rather than left empty for the script to fill. A
        /// page whose content arrives only once script has run is a page that shows nothing at all when script is
        /// refused, and the reader is then unable to tell a broken page from a working one that has nothing to
        /// report.
        /// </remarks>
        public static string Live(string id, string view, string content)
        {
            return Live(id, view, string.Empty, content);
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes, with parameters of its own.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="extraQuery">Additional query parameters for the fragment, already encoded.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The parameters travel in the address rather than in the element, because the fragment handler is an
        /// ordinary request. The separator is a plain ampersand here and is escaped once, by
        /// <see cref="Html.Attribute"/>: writing the entity in this string would have it escaped a second time,
        /// and a parameter that reaches the handler as <c>amp;q</c> would make the region refresh with an answer
        /// to a question nobody asked.
        /// </remarks>
        public static string Live(string id, string view, string extraQuery, string content)
        {
            string address = "/Api/Fragment.ashx?view=" + Uri.EscapeDataString(view);

            if (!string.IsNullOrEmpty(extraQuery)) address += "&" + extraQuery;

            return "<div id=\"" + Html.Attribute(id) + "\" class=\"rms-live\" data-url=\""
                   + Html.Attribute(address) + "\">" + content + "</div>";
        }

        /// <summary>
        /// Renders a failed call as something an operator can act on.
        /// </summary>
        /// <param name="status">The status the service answered with, or the one this site synthesised.</param>
        /// <param name="error">The sentence the client recorded.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A refusal, an outage and a missing document are shown differently, because they have different
        /// remedies: a key mismatch is fixed on the machine, a stopped service is fixed by starting it, and a
        /// document that does not exist is fixed by writing it or by following a different link. A page that
        /// showed all three as "could not load" would send the reader to the wrong one of those roughly two times
        /// in three.
        /// </remarks>
        public static string Failure(HttpStatusCode status, string error)
        {
            if (status == HttpStatusCode.Forbidden || status == HttpStatusCode.Unauthorized)
            {
                return Html.Alert(
                    "warning",
                    "The service refused this site.",
                    "The shared key on this site does not match the one on the service, or this site's address is "
                    + "not on the service's allow-list. This is a configuration fault on the machine, not a fault "
                    + "in the page.");
            }

            if (status == HttpStatusCode.ServiceUnavailable || status == HttpStatusCode.BadGateway)
            {
                return Html.Alert(
                    "danger",
                    "The service is not answering.",
                    "Nothing is listening where this site expects the documentation service. Check that the "
                    + "service is running and that the address in this site's SiteServiceBaseUrl is the one it "
                    + "was given.")
                    /*
                     * The site's own account of the failure follows the general one, because on a deployment that
                     * has just been installed the general sentence points at the wrong machine: the usual cause is
                     * a key file this site was named and cannot read, and this site is the only one that knows
                     * which file that is. The text is encoded rather than trusted, since it can carry a transport's
                     * own message.
                     */
                    + (string.IsNullOrWhiteSpace(error)
                        ? string.Empty
                        : Html.Alert("warning", "What this site found.", Html.Encode(error)));
            }

            if (status == HttpStatusCode.NotFound)
            {
                return Html.Alert(
                    "info",
                    "There is no document by that name.",
                    "The documentation directory is the list of what exists. A document that was renamed answers "
                    + "here until every link to it has been corrected, which is why the index names each "
                    + "document rather than its address.");
            }

            return Html.Alert("danger", "The service could not be read.", error);
        }

        /// <summary>
        /// Converts a timestamp the service reported into an instant this site can render.
        /// </summary>
        /// <param name="value">The timestamp.</param>
        /// <returns>The instant, or <see langword="null"/> when there is none.</returns>
        /// <remarks>
        /// A value with no zone is read as UTC rather than as the server's local time. The service writes UTC and
        /// says so, so the only value that arrives without a zone is one that lost it on the way, and reading
        /// that as local time would shift it by the offset of whatever machine happened to render the page.
        /// </remarks>
        public static DateTimeOffset? ToOffset(DateTime? value)
        {
            if (!value.HasValue) return null;

            DateTime moment = value.Value;

            if (moment.Kind == DateTimeKind.Utc) return new DateTimeOffset(moment);
            if (moment.Kind == DateTimeKind.Local) return new DateTimeOffset(moment.ToUniversalTime());

            return new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc));
        }

        /// <summary>
        /// Renders the address the service is reading and what it found there.
        /// </summary>
        /// <param name="list">The catalogue.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The directory comes first because everything below it is a statement about the files it names. A site
        /// pointed at the wrong checkout produces a complete and entirely wrong answer that looks exactly like a
        /// correct one, and the only thing that makes that visible is showing the address the service resolved.
        /// </remarks>
        public static string AddressHeader(DocumentListView list)
        {
            if (list == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Panel("Where this documentation comes from", string.Concat(
                Html.Field("Directory", Html.Value(list.DocumentDirectory)),
                Html.Field("Documents", Html.Value(Describe(list.Documents))),
                Html.Field("Summary", Html.Value(list.Summary)))));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the files that could not be served.
        /// </summary>
        /// <param name="list">The catalogue.</param>
        /// <returns>The markup, which is empty when there is nothing wrong.</returns>
        /// <remarks>
        /// An unreadable document is reported here rather than being hidden, because the person who can fix it is
        /// the person reading this page. It is reported as a warning when the catalogue is complete without it and
        /// as a danger when part of the documentation is missing, which is the distinction the service draws and
        /// the one that decides whether anything is actually wrong.
        /// </remarks>
        public static string Problems(DocumentListView list)
        {
            if (list == null || list.Problems == null || list.Problems.Count == 0) return string.Empty;

            StringBuilder builder = new StringBuilder();

            foreach (DocumentProblemView problem in list.Problems)
            {
                if (problem == null) continue;

                bool error = string.Equals(problem.Severity, "Error", StringComparison.OrdinalIgnoreCase);

                string title = problem.Slug == null || problem.Slug.Length == 0
                    ? "The document directory could not be read"
                    : "docs/" + problem.Slug;

                builder.Append(Html.Alert(error ? "danger" : "warning", title, problem.Message));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders the whole index, grouped by section.
        /// </summary>
        /// <param name="list">The catalogue.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// One panel per section rather than one table for everything, because a section is how the documentation
        /// is organised and a flat list of every page is the shape a reader has to reconstruct in their head. The
        /// order inside a section is the order the service sorted it into, which is the documentation's own.
        /// </remarks>
        public static string Catalogue(DocumentListView list)
        {
            if (list == null) return string.Empty;

            List<DocumentSummaryView> documents = list.Documents ?? new List<DocumentSummaryView>();

            if (documents.Count == 0)
            {
                return Html.Alert(
                    "info",
                    "Nothing is being served.",
                    "The documentation directory holds no documents this site can read. If the deployment has just "
                    + "been created, the directory is empty until it is checked out.");
            }

            StringBuilder builder = new StringBuilder();

            foreach (string section in Sections(documents))
            {
                List<DocumentSummaryView> group = InSection(documents, section);

                builder.Append(Html.Panel(SectionName(section), Table(group)));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders the index as a strip of links, with the current document marked.
        /// </summary>
        /// <param name="list">The catalogue.</param>
        /// <param name="currentSlug">The document being read, if any.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// This is what a reader uses to move between documents, so it is compact rather than complete: the
        /// titles only, one list per section. It is a strip rather than a column because these pages are one
        /// column wide, and pushing the document down the page so that a reader who has already arrived can see
        /// where else to go would be backwards.
        /// </remarks>
        public static string Contents(DocumentListView list, string currentSlug)
        {
            if (list == null || list.Documents == null || list.Documents.Count == 0) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append("<h4>Contents</h4>");

            foreach (string section in Sections(list.Documents))
            {
                builder.Append("<p class=\"text-muted section-label\">")
                       .Append(Html.Encode(SectionName(section)))
                       .Append("</p>");

                builder.Append("<ul class=\"nav nav-pills\">");

                foreach (DocumentSummaryView document in InSection(list.Documents, section))
                {
                    if (document == null || document.Slug == null) continue;

                    bool current = string.Equals(document.Slug, currentSlug, StringComparison.OrdinalIgnoreCase);

                    builder.Append("<li").Append(current ? " class=\"active\"" : string.Empty).Append('>')
                           .Append(Html.Link(DocsService.PageFor(document.Slug), document.Title))
                           .Append("</li>");
                }

                builder.Append("</ul>");
            }

            return Html.Panel("All documents", builder.ToString());
        }

        /// <summary>
        /// Renders one document's facts.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The last-write time is here rather than only on the index, because it is the one fact that lets a
        /// reader tell a site that is serving the repository's files from one that is serving a copy: the value
        /// can be compared with the file itself. It is also the region this page refreshes, which is why the page
        /// notices a document being corrected without reloading.
        /// </remarks>
        public static string DocumentFacts(DocumentView document)
        {
            if (document == null || document.Summary == null) return string.Empty;

            DocumentSummaryView summary = document.Summary;

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Slug", Html.Value(summary.Slug)),
                new KeyValuePair<string, string>("Section", Html.Cell(SectionName(summary.Section))),
                new KeyValuePair<string, string>("Written as", FormatBadge(summary.Format)),
                new KeyValuePair<string, string>("Size", Html.Encode(Html.Bytes(summary.LengthBytes))),
                new KeyValuePair<string, string>("Last written", Html.Instant(ToOffset(summary.LastWriteUtc))),
            };

            return Facts(pairs);
        }

        /// <summary>
        /// Renders the whole index as one region's worth of markup.
        /// </summary>
        /// <param name="list">The catalogue.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The address, the problems and the tables are one region rather than three, because a document that
        /// appeared while the page was open appears in one of them and changes the count in another. Refreshing
        /// them separately would show a table of eleven documents beside a line saying there are ten.
        /// </remarks>
        public static string Index(DocumentListView list)
        {
            if (list == null) return string.Empty;

            return AddressHeader(list) + Problems(list) + Catalogue(list);
        }

        /// <summary>
        /// Renders the heading of a document's page.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The description is the file name, its size and when it was last written -- the three facts by which a
        /// reader confirms that the page in front of them is the page they were sent to, and that it is current.
        /// </remarks>
        public static string DocumentHeading(DocumentView document)
        {
            if (document == null || document.Summary == null) return string.Empty;

            return Html.Heading(document.Summary.Title, Describe(document.Summary));
        }

        /// <summary>
        /// Renders one document's body.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// <para>
        /// The document's own opening heading is dropped when it says the same thing as the page's, which it
        /// always does: every one of these files begins with its title, because that is what makes it readable as
        /// a file. Printing it again under the page's copy of it would make every page look pasted in twice.
        /// </para>
        /// <para>
        /// A plain text document is shown as it was written, in a code block. Reading it as Markdown would eat
        /// any line that happened to begin with a character Markdown gives a meaning to, and a transcript or a
        /// list of ports is exactly the kind of document that begins such a line without meaning to.
        /// </para>
        /// </remarks>
        public static string DocumentBody(DocumentView document)
        {
            if (document == null || document.Summary == null) return string.Empty;

            string body = document.Body ?? string.Empty;

            if (string.IsNullOrEmpty(body))
            {
                return Html.Alert(
                    "warning",
                    "This document is empty.",
                    "The file is present and holds no text after its front matter. Nothing is wrong with the site; "
                    + "the page has not been written yet.");
            }

            if (string.Equals(document.Summary.Format, "Text", StringComparison.OrdinalIgnoreCase))
            {
                return "<pre class=\"code-block\"><code>"
                       + Html.Encode(body.TrimEnd('\r', '\n'))
                       + "</code></pre>";
            }

            return "<article class=\"document\">"
                   + MarkdownRenderer.Render(body, document.Summary.Title)
                   + "</article>";
        }

        /// <summary>
        /// Renders the search form.
        /// </summary>
        /// <param name="query">The phrase to start with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A plain HTML form that submits on its own, and it always submits to the search page rather than to the
        /// page it is on. That is deliberate: a search that answered on the sign-in page, on a document and on
        /// the index would be three answers to one question, and the reader would have three places to look for
        /// the results of the same click.
        /// </remarks>
        public static string SearchForm(string query)
        {
            return "<form class=\"form-inline\" role=\"search\" method=\"get\" action=\"/Search.aspx\">"
                   + "<div class=\"form-group\">"
                   + "<label class=\"sr-only\" for=\"q\">Search the documentation</label>"
                   + "<input type=\"search\" class=\"form-control\" id=\"q\" name=\"q\" maxlength=\"120\" "
                   + "placeholder=\"Search the documentation\" value=\"" + Html.Attribute(query ?? string.Empty) + "\" />"
                   + "</div> "
                   + "<button type=\"submit\" class=\"btn btn-primary btn-sm\">Search</button>"
                   + "</form>";
        }

        /// <summary>
        /// Renders the results of a search.
        /// </summary>
        /// <param name="response">The service's answer.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The snippet is shown rather than the document's opening line, because a result that shows every match
        /// from the beginning of the page shows the one part of it that is least likely to contain what was
        /// searched for. When the phrase matched the title and not the body, the snippet is the opening line,
        /// which is the next most useful thing to show.
        /// </remarks>
        public static string SearchResults(SearchResponseView response)
        {
            if (response == null) return string.Empty;

            if (string.IsNullOrWhiteSpace(response.Query))
            {
                return Html.Alert(
                    "info",
                    "Search the documentation.",
                    "Every document is searched, its title and its text. A phrase is matched as it is written, "
                    + "without regard to case.");
            }

            List<SearchResultView> results = response.Results ?? new List<SearchResultView>();

            StringBuilder builder = new StringBuilder();

            builder.Append("<p class=\"text-muted\">")
                   .Append(Html.Encode(Describe(response.Count, response.Limit, response.Query)))
                   .Append("</p>");

            if (results.Count == 0) return builder.ToString();

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Document"))
                   .Append(Html.Head("Match"))
                   .Append("</tr></thead><tbody>");

            foreach (SearchResultView result in results)
            {
                if (result == null) continue;

                builder.Append("<tr>");

                builder.Append(Html.Column(
                    Html.Link(DocsService.PageFor(result.Slug), result.Title)
                    + "<br /><span class=\"text-muted\">" + Html.Encode(SectionName(result.Section)) + "</span>",
                    "col-doc"));

                builder.Append(Html.Column(Html.Encode(result.Snippet)));

                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders a definition list of label and value pairs.
        /// </summary>
        /// <param name="pairs">The pairs, in the order they are read.</param>
        /// <returns>The markup.</returns>
        public static string Facts(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            if (pairs == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            foreach (KeyValuePair<string, string> pair in pairs)
            {
                builder.Append(Html.Field(pair.Key, pair.Value));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Turns a section's directory name into something that reads like a heading.
        /// </summary>
        /// <param name="section">The section name, which is empty for a top level document.</param>
        /// <returns>The label.</returns>
        public static string SectionName(string section)
        {
            if (string.IsNullOrWhiteSpace(section)) return RootSectionLabel;

            string name = section.Trim().Replace('-', ' ').Replace('_', ' ');

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Renders a document's format as a badge.
        /// </summary>
        /// <param name="format">The format, as the service names it.</param>
        /// <returns>The markup.</returns>
        public static string FormatBadge(string format)
        {
            if (string.Equals(format, "Text", StringComparison.OrdinalIgnoreCase))
            {
                return Html.Badge(
                    "plain text",
                    "default",
                    "The body is shown exactly as it was written, without being read as Markdown.");
            }

            return Html.Badge(
                "markdown",
                "info",
                "The body is read as Markdown and rendered as markup.");
        }

        /// <summary>
        /// Builds the table of documents in one section.
        /// </summary>
        /// <param name="documents">The documents.</param>
        /// <returns>The markup.</returns>
        private static string Table(List<DocumentSummaryView> documents)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));

            builder.Append("<thead><tr>")
                   .Append(Html.Head("Document"))
                   .Append(Html.Head("As written", "col-time"))
                   .Append(Html.Head("Size", "col-number"))
                   .Append(Html.Head("Last written", "col-time"))
                   .Append("</tr></thead><tbody>");

            foreach (DocumentSummaryView document in documents)
            {
                if (document == null) continue;

                builder.Append("<tr>");

                builder.Append(Html.Column(
                    Html.Link(DocsService.PageFor(document.Slug), document.Title)
                    + "<br /><span class=\"text-muted\">" + Html.Encode(document.Slug) + "</span>"));

                builder.Append(Html.Column(FormatBadge(document.Format)));
                builder.Append(Html.Column(Html.Encode(Html.Bytes(document.LengthBytes)), "col-number"));
                builder.Append(Html.Column(Html.Instant(ToOffset(document.LastWriteUtc)), "col-time"));

                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Lists the sections the documents are filed under, in the order they first appear.
        /// </summary>
        /// <param name="documents">The documents, in the service's order.</param>
        /// <returns>The section names, with an empty string for the top level.</returns>
        /// <remarks>
        /// Taken from the documents rather than from a second call to the service, and taken in the order the
        /// documents arrived in, because that order is the documentation's own. Sorting the sections again here
        /// would put them in an order the service never chose.
        /// </remarks>
        private static List<string> Sections(List<DocumentSummaryView> documents)
        {
            List<string> sections = new List<string>();

            foreach (DocumentSummaryView document in documents)
            {
                if (document == null) continue;

                string section = document.Section ?? string.Empty;

                if (sections.Contains(section)) continue;

                sections.Add(section);
            }

            return sections;
        }

        /// <summary>
        /// Lists the documents in one section, keeping the service's order.
        /// </summary>
        /// <param name="documents">Every document.</param>
        /// <param name="section">The section to select.</param>
        /// <returns>The documents in that section.</returns>
        private static List<DocumentSummaryView> InSection(List<DocumentSummaryView> documents, string section)
        {
            List<DocumentSummaryView> selected = new List<DocumentSummaryView>();

            foreach (DocumentSummaryView document in documents)
            {
                if (document == null) continue;

                if (!string.Equals(document.Section ?? string.Empty, section ?? string.Empty, StringComparison.Ordinal))
                {
                    continue;
                }

                selected.Add(document);
            }

            return selected;
        }

        /// <summary>
        /// Describes a catalogue's documents.
        /// </summary>
        /// <param name="documents">The documents.</param>
        /// <returns>A sentence.</returns>
        private static string Describe(List<DocumentSummaryView> documents)
        {
            int count = documents == null ? 0 : documents.Count;

            return count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " document" : " documents");
        }

        /// <summary>
        /// Describes one document for the line under its heading.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>A sentence.</returns>
        private static string Describe(DocumentSummaryView document)
        {
            string size = Html.Bytes(document.LengthBytes);

            DateTimeOffset? written = ToOffset(document.LastWriteUtc);

            string when = written.HasValue
                ? "last written " + written.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                : "with no recorded write time";

            return document.Slug + " — " + size + ", " + when;
        }

        /// <summary>
        /// Describes a search's results.
        /// </summary>
        /// <param name="count">The number of results.</param>
        /// <param name="limit">The largest number of results the service would have returned.</param>
        /// <param name="query">The phrase that was searched for.</param>
        /// <returns>A sentence.</returns>
        private static string Describe(int count, int limit, string query)
        {
            if (count == 0)
            {
                return "Nothing in the documentation contains \"" + query + "\".";
            }

            string sentence = count.ToString(CultureInfo.InvariantCulture)
                              + (count == 1 ? " document contains " : " documents contain ")
                              + "\"" + query + "\".";

            if (count >= limit)
            {
                sentence += " Only the first " + limit.ToString(CultureInfo.InvariantCulture)
                            + " are shown, so a more specific phrase may find a later one.";
            }

            return sentence;
        }
    }
}
