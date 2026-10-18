using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Web;

using RMS.Site.Common.Implementation;

namespace RMS.Configuration.Site.Infrastructure
{
    /// <summary>
    /// Renders the Configuration console's pieces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every table, badge and panel this console draws is built here, and both the page that renders first and
    /// the handler that refreshes it call the same methods. That is what keeps a region from changing shape
    /// when it updates, and it is why nothing in this class reads the request: what a view shows is a parameter,
    /// never something it went and looked up for itself.
    /// </para>
    /// <para>
    /// Text that came from a file is encoded everywhere it is shown, including inside a <c>title</c> attribute.
    /// These pages display a hundred and twenty lines of a file somebody else wrote, which is the largest
    /// quantity of untrusted text anywhere in this stack.
    /// </para>
    /// <para>
    /// Nothing here emits an inline style attribute or an inline event handler. The pages are served under a
    /// content-security policy that permits scripts and styles from this origin only, so an inline attribute
    /// would be a rule the browser refuses to apply and a feature that silently does not work.
    /// </para>
    /// </remarks>
    public static class ConfigurationViews
    {
        /// <summary>
        /// How many characters of a SHA-256 are shown before the ellipsis.
        /// </summary>
        /// <remarks>
        /// Twelve characters, which is forty-eight bits. Short enough to compare two of them by eye down a
        /// column, long enough that the chance of two files in one deployment colliding in the displayed part is
        /// not a real risk. The whole digest is in the tooltip for the case where somebody has to compare it
        /// against a value from somewhere else.
        /// </remarks>
        private const int ShownHashLength = 12;

        /// <summary>
        /// Wraps a region in the element the site's script refreshes.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
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
        /// ordinary request: a region that carried its parameters in an attribute would have to have them read
        /// back out and re-sent by the script, and the two would eventually disagree.
        /// </remarks>
        public static string Live(string id, string view, string extraQuery, string content)
        {
            string address = "/Api/Fragment.ashx?view=" + Uri.EscapeDataString(view);

            // The separator is a plain ampersand here and is escaped once, by Html.Attribute. Writing the entity
            // in this string would have it escaped a second time, and an attribute that reaches the browser as
            // "&amp;amp;" parses back to "&amp;", which is a parameter literally named "amp;view" -- so the
            // region refreshes with whichever answer the handler gives for a request that named nothing.
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
        /// <para>
        /// A refusal and an outage are shown differently, because they have different remedies. A refusal means
        /// the two ends disagree about a secret; an outage means the service is not there. A page that showed
        /// both as "could not load" would send the operator to the wrong machine roughly half the time.
        /// </para>
        /// <para>
        /// This is here rather than on the page base so that the fragment handler can render a failure the same
        /// way. A region that failed to refresh and showed a different sentence from the page that failed to
        /// load is a difference somebody would eventually spend an afternoon on.
        /// </para>
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
                    "Nothing is listening where this site expects the Configuration service. Check that the "
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
                    "warning",
                    "The service has nothing at that address.",
                    "This is what a service with the surface switched off answers, and what a manifest it does "
                    + "not know answers. Check the service's own log for the reason it recorded.");
            }

            if (status == HttpStatusCode.Conflict)
            {
                return Html.Alert(
                    "warning",
                    "The file has changed since this page read it.",
                    "Something wrote the manifest after this page loaded it, so the change was not applied and "
                    + "nothing was overwritten. Reload the page and make the change again.");
            }

            if (status == HttpStatusCode.BadRequest)
            {
                // The service's own sentence, because a refusal for a reason of its own is the only kind that
                // produces this status: the surface has already decided the request cannot be satisfied, and it
                // is the only end that knows why.
                return Html.Alert("warning", "The service will not do that.", error);
            }

            return Html.Alert("danger", "The service could not be read.", error);
        }

        /// <summary>
        /// Gets the label class a problem severity is drawn with.
        /// </summary>
        /// <param name="severity">The severity, as the service names it.</param>
        /// <returns>The class, which is <c>default</c> for a word this class does not know.</returns>
        /// <remarks>
        /// An unknown word is grey rather than red. A console that coloured an unrecognised severity as an error
        /// would make a service that had learned a new word look like it had found a new fault.
        /// </remarks>
        public static string SeverityClass(string severity)
        {
            if (string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase)) return "danger";
            if (string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase)) return "warning";
            if (string.Equals(severity, "Note", StringComparison.OrdinalIgnoreCase)) return "info";

            return "default";
        }

        /// <summary>
        /// Renders a problem severity as a label.
        /// </summary>
        /// <param name="severity">The severity, as the service names it.</param>
        /// <returns>The markup.</returns>
        public static string SeverityBadge(string severity)
        {
            string word = string.IsNullOrWhiteSpace(severity) ? "unknown" : severity.Trim();

            return Html.Badge(word, SeverityClass(word), null);
        }

        /// <summary>
        /// Renders a count of problems.
        /// </summary>
        /// <param name="errors">The number of errors, or a negative number for "not asked".</param>
        /// <param name="warnings">The number of warnings, or a negative number for "not asked".</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A file the service did not parse is shown as "not checked" and not as "no problems". The difference
        /// matters: one of them tells an operator to stop looking, and it is the wrong one.
        /// </remarks>
        public static string Counts(int errors, int warnings)
        {
            if (errors < 0 || warnings < 0)
            {
                return "<span class=\"muted\" title=\"The service did not parse this file, so it cannot say "
                       + "whether there is anything wrong with it.\">not checked</span>";
            }

            if (errors == 0 && warnings == 0) return "<span class=\"label label-success\">nothing wrong</span>";

            StringBuilder builder = new StringBuilder();

            if (errors > 0)
            {
                builder.Append(Html.Badge(
                    errors.ToString(CultureInfo.InvariantCulture) + (errors == 1 ? " error" : " errors"),
                    "danger",
                    "The deployer refuses a file with an error in it."));
            }

            if (warnings > 0)
            {
                if (builder.Length > 0) builder.Append(' ');

                builder.Append(Html.Badge(
                    warnings.ToString(CultureInfo.InvariantCulture) + (warnings == 1 ? " warning" : " warnings"),
                    "warning",
                    "The deployer reads the file anyway, and the file does not mean what the line says."));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders a SHA-256 as something short enough to compare by eye.
        /// </summary>
        /// <param name="hash">The digest.</param>
        /// <returns>The markup.</returns>
        public static string Hash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return "<span class=\"muted\">&mdash;</span>";

            string trimmed = hash.Trim();

            string shown = trimmed.Length > ShownHashLength
                ? trimmed.Substring(0, ShownHashLength) + "&hellip;"
                : trimmed;

            return "<span class=\"code hash\" title=\"" + Html.Attribute(trimmed) + "\">" + shown + "</span>";
        }

        /// <summary>
        /// Renders the addresses the service is reading, and what it found there.
        /// </summary>
        /// <param name="list">The manifest list.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The addresses are the first thing on the overview because they are the first thing that is wrong when
        /// an operator sees a console describing the wrong deployment. Everything below this is a statement about
        /// the files; this is the statement about where the files came from.
        /// </remarks>
        public static string AddressHeader(ManifestListView list)
        {
            if (list == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"row manifest-counters\">");
            builder.Append(Counter(
                "Manifests present",
                list.PresentCount.ToString(CultureInfo.InvariantCulture) + " of 4",
                list.PresentCount == 4 ? "success" : list.PresentCount == 0 ? "danger" : "warning",
                list.PresentCount == 4
                    ? "Every file the deployer reads is here."
                    : "The deployer reports the parts of the plan it cannot read as absent."));
            builder.Append(Counter(
                "Errors",
                list.ErrorCount.ToString(CultureInfo.InvariantCulture),
                list.ErrorCount == 0 ? "success" : "danger",
                "Errors stop the deployer. Warnings do not."));
            builder.Append("</div>");

            builder.Append("<p class=\"text-muted\">").Append(Html.Encode(list.Summary)).Append("</p>");

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Repository root", Html.Value(list.RepositoryRoot)),
                new KeyValuePair<string, string>("Manifest directory", Html.Value(list.ManifestDirectory)),
                new KeyValuePair<string, string>("Plan file", Html.Value(list.PlanFile)),
            };

            builder.Append(Facts(pairs));

            if (list.Problems != null && list.Problems.Count > 0)
            {
                builder.Append(ProblemList(list.Problems, null));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders one counter.
        /// </summary>
        /// <param name="label">What is being counted.</param>
        /// <param name="value">The value, already rendered.</param>
        /// <param name="tone">The label class the value is drawn with.</param>
        /// <param name="title">The sentence explaining what the number means.</param>
        /// <returns>The markup.</returns>
        private static string Counter(string label, string value, string tone, string title)
        {
            return "<div class=\"col-sm-3\"><div class=\"manifest-counter\" title=\"" + Html.Attribute(title) + "\">"
                   + "<div class=\"counter-label\">" + Html.Encode(label) + "</div>"
                   + "<div class=\"counter-value\"><span class=\"label label-" + Html.Attribute(tone) + "\">"
                   + Html.Encode(value) + "</span></div>"
                   + "</div></div>";
        }

        /// <summary>
        /// Renders the four manifests as a table.
        /// </summary>
        /// <param name="list">The manifest list.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The file name is the link, because the file name is what the deployer and the compiler report when
        /// something is wrong with one. An operator arriving from a build failure has
        /// <c>rmsdeploy.build</c> in front of them and nothing else.
        /// </remarks>
        public static string ManifestTable(ManifestListView list)
        {
            if (list == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("File"))
                   .Append(Html.Head("Present"))
                   .Append(Html.Head("Size", "col-number"))
                   .Append(Html.Head("Sections", "col-number"))
                   .Append(Html.Head("Problems"))
                   .Append(Html.Head("Changed", "col-time"))
                   .Append(Html.Head("SHA-256"))
                   .Append(Html.Head(string.Empty))
                   .Append("</tr></thead><tbody>");

            if (list.Files == null || list.Files.Count == 0)
            {
                builder.Append("<tr><td colspan=\"8\" class=\"muted\">The service described no files at all.</td></tr>");
            }
            else
            {
                foreach (ManifestFileView file in list.Files)
                {
                    builder.Append(ManifestRow(file));
                }
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders one manifest as a table row.
        /// </summary>
        /// <param name="file">The manifest.</param>
        /// <returns>The markup.</returns>
        private static string ManifestRow(ManifestFileView file)
        {
            string page = ConfigurationService.PageFor(file.Kind);

            StringBuilder builder = new StringBuilder();

            builder.Append("<tr>");
            builder.Append(Html.Column(
                "<span class=\"code\">" + Html.Link(page, file.Name, file.Path) + "</span>"
                + "<br /><span class=\"text-muted\">" + Html.Encode(file.Kind) + "</span>"));

            builder.Append(Html.Column(file.Exists
                ? "<span class=\"label label-success\">present</span>"
                : "<span class=\"label label-warning\" title=\""
                  + Html.Attribute(string.IsNullOrWhiteSpace(file.Unreadable)
                      ? "The deployer reports this part of the plan as absent."
                      : file.Unreadable)
                  + "\">absent</span>"));

            builder.Append(Html.Column(
                file.Exists ? Html.Encode(Html.Bytes(file.Length)) : "<span class=\"muted\">&mdash;</span>",
                "col-number"));

            builder.Append(Html.Column(
                file.SectionCount > 0
                    ? Html.Encode(file.SectionCount.ToString(CultureInfo.InvariantCulture))
                    : "<span class=\"muted\">&mdash;</span>",
                "col-number"));

            builder.Append(Html.Column(Counts(file.ErrorCount, file.WarningCount)));
            builder.Append(Html.Column(Html.Instant(ToOffset(file.ModifiedUtc)), "col-time"));
            builder.Append(Html.Column(Hash(file.Sha256)));

            builder.Append(Html.Column(
                page.Length == 0
                    ? string.Empty
                    : Html.Link(page, "Open")
                      + (file.Editable
                          ? " <span class=\"text-muted\">&middot;</span> "
                            + Html.Link("/Editor.aspx?name=" + HttpUtility.UrlEncode(KindName(file.Kind)), "Edit")
                          : " <span class=\"text-muted\">&middot;</span> generated")));

            builder.Append("</tr>");

            return builder.ToString();
        }

        /// <summary>
        /// Names a manifest the way the editor's address spells it.
        /// </summary>
        /// <param name="kind">The kind, as the service names it.</param>
        /// <returns>The lower-case name.</returns>
        /// <remarks>
        /// The service accepts both spellings and compares them without regard to case, so this is a
        /// convenience rather than a requirement: it makes the address an operator copies into a bug report a
        /// word they can read.
        /// </remarks>
        public static string KindName(string kind)
        {
            return string.IsNullOrWhiteSpace(kind) ? string.Empty : kind.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Renders what the service says is wrong with a manifest.
        /// </summary>
        /// <param name="problems">The problems.</param>
        /// <param name="document">The document they are about, which is quoted where it is available.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Each problem quotes the line it is about, when the document was read alongside it. An operator
        /// reading "the deployer does not read 'knid' in this file" wants to see the line, and fetching it
        /// themselves means opening the file in another window and counting.
        /// </remarks>
        public static string ProblemList(IEnumerable<ManifestProblemView> problems, ManifestDocumentView document)
        {
            List<ManifestProblemView> list = new List<ManifestProblemView>(problems ?? new List<ManifestProblemView>());

            if (list.Count == 0) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen("problem-table"));
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Severity"))
                   .Append(Html.Head("Line", "col-number"))
                   .Append(Html.Head("Section"))
                   .Append(Html.Head("Key"))
                   .Append(Html.Head("What is wrong"))
                   .Append("</tr></thead><tbody>");

            foreach (ManifestProblemView problem in list)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column(SeverityBadge(problem.Severity)));

                builder.Append(Html.Column(
                    problem.Line > 0
                        ? "<span class=\"code\">" + problem.Line.ToString(CultureInfo.InvariantCulture) + "</span>"
                        : "<span class=\"muted\" title=\"This is about the file rather than about one of its "
                          + "lines.\">the file</span>",
                    "col-number"));

                builder.Append(Html.Column(Html.Cell(problem.Section)));
                builder.Append(Html.Column(
                    string.IsNullOrWhiteSpace(problem.Key)
                        ? Html.Cell(string.Empty)
                        : "<span class=\"code\">" + Html.Encode(problem.Key) + "</span>"));

                builder.Append(Html.Column(
                    "<span class=\"wrap-anywhere\">" + Html.Encode(problem.Message) + "</span>"
                    + Quote(document, problem.Line)));

                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the line a problem is about, when the document is available.
        /// </summary>
        /// <param name="document">The document, which may be absent.</param>
        /// <param name="line">The line number.</param>
        /// <returns>The markup, which is empty when there is no line to quote.</returns>
        private static string Quote(ManifestDocumentView document, int line)
        {
            if (document == null || document.Lines == null) return string.Empty;
            if (line <= 0 || line > document.Lines.Count) return string.Empty;

            ManifestLineView source = document.Lines[line - 1];

            if (source == null) return string.Empty;

            return "<br /><span class=\"code wrap-anywhere problem-line\">"
                   + Html.Encode(source.Text ?? string.Empty)
                   + "</span>";
        }

        /// <summary>
        /// Renders what is known about one manifest file.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The markup.</returns>
        public static string ManifestFacts(ManifestDocumentView document)
        {
            if (document == null) return string.Empty;

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Path", Html.Value(document.Path)),
                new KeyValuePair<string, string>("State", Counts(document.ErrorCount, document.WarningCount)),
                new KeyValuePair<string, string>(
                    "Size",
                    document.Exists
                        ? Html.Encode(Html.Bytes(document.Length))
                        : "<span class=\"muted\">&mdash;</span>"),
                new KeyValuePair<string, string>("Changed", Html.Instant(ToOffset(document.ModifiedUtc))),
                new KeyValuePair<string, string>(
                    "Line endings",
                    Html.Encode(DescribeLineEnding(document.LineEnding))),
                new KeyValuePair<string, string>("SHA-256", Hash(document.Sha256)),
                new KeyValuePair<string, string>(
                    "Editable",
                    document.Editable
                        ? "<span class=\"label label-success\">yes</span>"
                        : "<span class=\"label label-default\" title=\"The deployer generates this file. A change "
                          + "made here would be lost on the next deployment.\">generated</span>"),
            };

            return Facts(pairs);
        }

        /// <summary>
        /// Describes a file's line ending in words.
        /// </summary>
        /// <param name="lineEnding">The ending, as the service reported it.</param>
        /// <returns>The description.</returns>
        /// <remarks>
        /// The writer preserves whatever the file had, so the value is reported rather than changed. An operator
        /// who has just watched a diff show every line as changed is looking for this field.
        /// </remarks>
        private static string DescribeLineEnding(string lineEnding)
        {
            if (lineEnding == "\r\n") return "CRLF (Windows)";
            if (lineEnding == "\r") return "CR (a bare carriage return)";
            if (lineEnding == "\n") return "LF (Unix and macOS)";

            // The service answers with one of the three above, or with nothing at all for a document it did
            // not read. A fourth spelling is one this renderer does not know, and saying so is better than
            // naming the wrong ending on the strength of its being the commonest.
            return string.IsNullOrEmpty(lineEnding) ? "not known" : "not one of the three";
        }

        /// <summary>
        /// Renders a manifest's sections.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The section table is the structure of the file: what it declares and what each declaration says about
        /// itself. The settings themselves are the next table down, so that neither has to carry the other's
        /// columns.
        /// </remarks>
        public static string SectionTable(ManifestDocumentView document)
        {
            if (document == null || document.Sections == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Section"))
                   .Append(Html.Head("Line", "col-number"))
                   .Append(Html.Head("Kind"))
                   .Append(Html.Head("Platforms"))
                   .Append(Html.Head("Settings", "col-number"))
                   .Append(Html.Head("What it is"))
                   .Append(Html.Head("Problems"))
                   .Append("</tr></thead><tbody>");

            if (document.Sections.Count == 0)
            {
                builder.Append("<tr><td colspan=\"7\" class=\"muted\">The file declares no sections.</td></tr>");
            }
            else
            {
                foreach (ManifestSectionView section in document.Sections)
                {
                    builder.Append("<tr>");
                    builder.Append(Html.Column("<span class=\"code\">" + Html.Encode(section.Id) + "</span>"));
                    builder.Append(Html.Column(
                        section.Line.ToString(CultureInfo.InvariantCulture),
                        "col-number"));

                    builder.Append(Html.Column(string.IsNullOrWhiteSpace(section.Kind)
                        ? "<span class=\"muted\">none</span>"
                        : Html.Encode(section.Kind)));

                    builder.Append(Html.Column(string.IsNullOrWhiteSpace(section.Platforms)
                        ? "<span class=\"muted\" title=\"This section applies to every platform.\">all</span>"
                        : Html.Encode(section.Platforms)));

                    builder.Append(Html.Column(
                        (section.Entries == null ? 0 : section.Entries.Count).ToString(CultureInfo.InvariantCulture),
                        "col-number"));

                    builder.Append(Html.Column(string.IsNullOrWhiteSpace(section.Summary)
                        ? Html.Cell(string.Empty)
                        : "<span class=\"wrap-anywhere\">" + Html.Encode(section.Summary) + "</span>"));

                    builder.Append(Html.Column(ProblemsOf(section)));
                    builder.Append("</tr>");
                }
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Summarises the problems one section carries.
        /// </summary>
        /// <param name="section">The section.</param>
        /// <returns>The markup.</returns>
        private static string ProblemsOf(ManifestSectionView section)
        {
            if (section.Problems == null || section.Problems.Count == 0)
            {
                return "<span class=\"muted\">&mdash;</span>";
            }

            int errors = 0;
            int warnings = 0;

            foreach (ManifestProblemView problem in section.Problems)
            {
                if (string.Equals(problem.Severity, "Error", StringComparison.OrdinalIgnoreCase)) errors++;
                else warnings++;
            }

            return Counts(errors, warnings);
        }

        /// <summary>
        /// Renders every setting of a manifest, in the order the deployer reads them.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The platform qualification is its own column rather than part of the key, because that is the one
        /// piece of this format that is not obvious from a single line: <c>windows.executable</c> and
        /// <c>executable</c> are one setting with two values, and a table that merged them into one column
        /// would show a setting twice with no explanation.
        /// </remarks>
        public static string SettingTable(ManifestDocumentView document)
        {
            if (document == null || document.Sections == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Line", "col-number"))
                   .Append(Html.Head("Section"))
                   .Append(Html.Head("Setting"))
                   .Append(Html.Head("Platform"))
                   .Append(Html.Head("Value"))
                   .Append(Html.Head("Read"))
                   .Append("</tr></thead><tbody>");

            int shown = 0;

            foreach (ManifestSectionView section in document.Sections)
            {
                if (section.Entries == null) continue;

                foreach (ManifestEntryView entry in section.Entries)
                {
                    builder.Append("<tr>");
                    builder.Append(Html.Column(entry.Line.ToString(CultureInfo.InvariantCulture), "col-number"));
                    builder.Append(Html.Column("<span class=\"code\">" + Html.Encode(section.Id) + "</span>"));
                    builder.Append(Html.Column("<span class=\"code\">" + Html.Encode(entry.Key) + "</span>"));
                    builder.Append(Html.Column(string.IsNullOrWhiteSpace(entry.Platform)
                        ? "<span class=\"muted\">all</span>"
                        : Html.Encode(entry.Platform)));
                    builder.Append(Html.Column(string.IsNullOrWhiteSpace(entry.Value)
                        ? "<span class=\"muted\" title=\"The deployer supplies its own default.\">&mdash;</span>"
                        : "<span class=\"code wrap-anywhere\">" + Html.Encode(entry.Value) + "</span>"));

                    builder.Append(Html.Column(entry.Recognised
                        ? "<span class=\"label label-success\">yes</span>"
                        : "<span class=\"label label-danger\" title=\"The deployer ignores this key without a "
                          + "word. It is almost always a misspelling.\">ignored</span>"));

                    builder.Append("</tr>");

                    shown++;
                }
            }

            if (shown == 0)
            {
                builder.Append("<tr><td colspan=\"6\" class=\"muted\">The file sets nothing at all.</td></tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the keys one manifest kind may carry.
        /// </summary>
        /// <param name="vocabulary">The vocabulary for the kind, or <see langword="null"/>.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Published by the service rather than written into the page, so that the list an operator reads here
        /// and the list the validator accepts cannot be two lists. When the vocabulary is unavailable this
        /// renders nothing at all rather than an empty table, because a table with no rows says "this file has
        /// no keys", which is false.
        /// </remarks>
        public static string VocabularyTable(VocabularySectionView vocabulary)
        {
            if (vocabulary == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(vocabulary.Summary))
            {
                builder.Append("<p class=\"text-muted\">").Append(Html.Encode(vocabulary.Summary)).Append("</p>");
            }

            if (vocabulary.Kinds != null && vocabulary.Kinds.Count > 0)
            {
                builder.Append("<p class=\"text-muted\">A section's <span class=\"code\">kind</span> is one of: ");
                builder.Append(KindList(vocabulary.Kinds));
                builder.Append(".</p>");
            }

            builder.Append(Html.TableOpen("vocabulary-table"));
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Setting"))
                   .Append(Html.Head("Required"))
                   .Append(Html.Head("Per platform"))
                   .Append(Html.Head("Default"))
                   .Append(Html.Head("Accepts"))
                   .Append(Html.Head("What it is for"))
                   .Append("</tr></thead><tbody>");

            if (vocabulary.Keys == null || vocabulary.Keys.Count == 0)
            {
                builder.Append("<tr><td colspan=\"6\" class=\"muted\">The service published no keys for this "
                               + "file, which means this build does not know what goes in it.</td></tr>");
            }
            else
            {
                foreach (VocabularyEntryView entry in vocabulary.Keys)
                {
                    builder.Append("<tr>");
                    builder.Append(Html.Column("<span class=\"code\">" + Html.Encode(entry.Key) + "</span>"));

                    builder.Append(Html.Column(entry.Required
                        ? "<span class=\"label label-warning\" title=\"The deployer refuses a section of this "
                          + "file that does not set it.\">required</span>"
                        : "<span class=\"muted\">&mdash;</span>"));

                    builder.Append(Html.Column(entry.PerPlatform
                        ? "<span class=\"label label-info\" title=\"A platform prefix overrides this key for one "
                          + "platform, the way windows.executable overrides executable.\">yes</span>"
                        : "<span class=\"muted\" title=\"The deployer reads this key directly, so a platform prefix "
                          + "in front of it would hide it.\">&mdash;</span>"));

                    builder.Append(Html.Column(string.IsNullOrWhiteSpace(entry.Default)
                        ? "<span class=\"muted\">&mdash;</span>"
                        : "<span class=\"code\">" + Html.Encode(entry.Default) + "</span>"));

                    StringBuilder accepts = new StringBuilder();

                    if (entry.Values != null && entry.Values.Count > 0)
                    {
                        accepts.Append(KindList(entry.Values));
                    }
                    else if (entry.MaximumLength > 0)
                    {
                        accepts.Append("<span class=\"text-muted\">up to ")
                               .Append(Html.Encode(entry.MaximumLength.ToString(CultureInfo.InvariantCulture)))
                               .Append(" characters</span>");
                    }
                    else
                    {
                        accepts.Append("<span class=\"muted\">any</span>");
                    }

                    builder.Append(Html.Column(accepts.ToString()));

                    builder.Append(Html.Column(
                        "<span class=\"wrap-anywhere\">" + Html.Encode(entry.Summary) + "</span>"));

                    builder.Append("</tr>");
                }
            }

            builder.Append("</tbody></table>");

            if (vocabulary.SectionSummary != null && vocabulary.SectionSummary.Length > 0)
            {
                builder.Append("<p class=\"text-muted\">").Append(Html.Encode(vocabulary.SectionSummary))
                       .Append("</p>");
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders a list of words as code spans.
        /// </summary>
        /// <param name="values">The words.</param>
        /// <returns>The markup.</returns>
        private static string KindList(IEnumerable<string> values)
        {
            StringBuilder builder = new StringBuilder();

            foreach (string value in values ?? new List<string>())
            {
                if (builder.Length > 0) builder.Append(", ");

                builder.Append("<span class=\"code\">").Append(Html.Encode(value)).Append("</span>");
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders the deployer's parser limits.
        /// </summary>
        /// <param name="limits">The limits.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The C constant's name travels with the number so that a message an operator has just been refused by
        /// has somewhere to be looked up.
        /// </remarks>
        public static string LimitTable(IEnumerable<DeployerLimitView> limits)
        {
            List<DeployerLimitView> list = new List<DeployerLimitView>(limits ?? new List<DeployerLimitView>());

            if (list.Count == 0) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Constant"))
                   .Append(Html.Head("Limit", "col-number"))
                   .Append(Html.Head("What it applies to"))
                   .Append("</tr></thead><tbody>");

            foreach (DeployerLimitView limit in list)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column("<span class=\"code\">" + Html.Encode(limit.Name) + "</span>"));
                builder.Append(Html.Column(
                    limit.Value.ToString(CultureInfo.InvariantCulture),
                    "col-number"));
                builder.Append(Html.Column(
                    "<span class=\"wrap-anywhere\">" + Html.Encode(limit.Summary) + "</span>"));
                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders what the plan is, before its services are listed.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The markup.</returns>
        public static string PlanFacts(PlanView plan)
        {
            if (plan == null) return string.Empty;

            if (!plan.Available)
            {
                return Html.Alert("info", "There is no plan to read.", plan.Detail);
            }

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Path", Html.Value(plan.Path)),
                new KeyValuePair<string, string>("Schema version", Html.Encode(plan.Version.ToString(CultureInfo.InvariantCulture))),
                new KeyValuePair<string, string>("Platform", Html.Value(plan.Platform)),
                new KeyValuePair<string, string>("Repository root", Html.Value(plan.Root)),
                new KeyValuePair<string, string>("Publish output", Html.Value(plan.OutputRoot)),
                new KeyValuePair<string, string>("Log root", Html.Value(plan.LogDirectory)),
                new KeyValuePair<string, string>("Supervisor", Html.Value(plan.HostExecutable)),
            };

            return Facts(pairs)
                   + "<p class=\"text-muted\">" + Html.Encode(plan.Summary) + "</p>";
        }

        /// <summary>
        /// Renders the services the plan declares.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Every value here has been resolved: the placeholders are expanded, the platform-qualified values have
        /// been chosen, the defaults have been filled in. That is the point of showing it beside the manifest it
        /// came from -- it is where an operator sees what their edit actually became.
        /// </remarks>
        public static string PlanServiceTable(IEnumerable<PlanServiceView> services)
        {
            List<PlanServiceView> list = new List<PlanServiceView>(services ?? new List<PlanServiceView>());

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Entry"))
                   .Append(Html.Head("Name"))
                   .Append(Html.Head("Kind"))
                   .Append(Html.Head("Port", "col-number"))
                   .Append(Html.Head("Platforms"))
                   .Append(Html.Head("Restart"))
                   .Append(Html.Head("Applies"))
                   .Append(Html.Head(string.Empty))
                   .Append("</tr></thead><tbody>");

            if (list.Count == 0)
            {
                builder.Append("<tr><td colspan=\"8\" class=\"muted\">The plan declares no services.</td></tr>");
            }
            else
            {
                foreach (PlanServiceView service in list)
                {
                    builder.Append("<tr>");
                    builder.Append(Html.Column("<span class=\"code\">" + Html.Encode(service.Id) + "</span>"));
                    builder.Append(Html.Column(Html.Encode(service.Name)));
                    builder.Append(Html.Column(DescribeMechanism(service)));
                    builder.Append(Html.Column(string.IsNullOrWhiteSpace(service.Port)
                        ? "<span class=\"muted\">&mdash;</span>"
                        : Html.Encode(service.Port), "col-number"));
                    builder.Append(Html.Column(string.IsNullOrWhiteSpace(service.Platforms)
                        ? "<span class=\"muted\">all</span>"
                        : Html.Encode(service.Platforms)));
                    builder.Append(Html.Column(Html.Encode(service.Restart)));
                    builder.Append(Html.Column(service.AppliesToThisPlan
                        ? "<span class=\"label label-success\">yes</span>"
                        : "<span class=\"label label-default\" title=\"The plan was written for another platform, "
                          + "so the installer will not register this entry.\">no</span>"));
                    builder.Append(Html.Column(
                        "<a href=\"/Plan.aspx?entry="
                        + Html.Attribute(HttpUtility.UrlEncode(service.Id)) + "\">Details</a>"));
                    builder.Append("</tr>");
                }
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Describes how a plan entry will be started.
        /// </summary>
        /// <param name="service">The plan entry.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A web site the platform serves and a program the supervisor runs are two different mechanisms with
        /// two different failure modes, so the distinction is a column rather than something to be inferred from
        /// whether one field is empty.
        /// </remarks>
        private static string DescribeMechanism(PlanServiceView service)
        {
            if (!string.IsNullOrWhiteSpace(service.Website) && string.IsNullOrWhiteSpace(service.Executable))
            {
                return "<span class=\"label label-info\" title=\"The platform's own web server serves this "
                       + "directory, so there is no process for the supervisor to own.\">web site</span>";
            }

            if (!string.IsNullOrWhiteSpace(service.Executable))
            {
                return "<span class=\"label label-default\" title=\"The supervisor owns this process.\">process</span>";
            }

            return "<span class=\"label label-danger\">nothing to run</span>";
        }

        /// <summary>
        /// Renders everything the plan says about one entry.
        /// </summary>
        /// <param name="service">The plan entry.</param>
        /// <returns>The markup.</returns>
        public static string PlanServiceDetail(PlanServiceView service)
        {
            if (service == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Entry", "<span class=\"code\">" + Html.Encode(service.Id) + "</span>"),
                new KeyValuePair<string, string>("Name", Html.Value(service.Name)),
                new KeyValuePair<string, string>("Description", Html.Value(service.Description)),
                new KeyValuePair<string, string>("Kind", DescribeMechanism(service)),
                new KeyValuePair<string, string>("Executable", Html.Value(service.Executable)),
                new KeyValuePair<string, string>("Working directory", Html.Value(service.WorkingDirectory)),
                new KeyValuePair<string, string>("Log directory", Html.Value(service.LogDirectory)),
                new KeyValuePair<string, string>(
                    "Restart",
                    Html.Encode(service.Restart)
                    + " <span class=\"text-muted\">first delay "
                    + Html.Encode(service.RestartDelaySeconds.ToString(CultureInfo.InvariantCulture))
                    + "s, ceiling "
                    + Html.Encode(service.MaximumRestartDelaySeconds.ToString(CultureInfo.InvariantCulture))
                    + "s, grace "
                    + Html.Encode(service.StopTimeoutSeconds.ToString(CultureInfo.InvariantCulture))
                    + "s</span>"),
                new KeyValuePair<string, string>("Shutdown line", Html.Value(service.ShutdownLine)),
                new KeyValuePair<string, string>("Platforms", Html.Value(service.Platforms)),
                new KeyValuePair<string, string>("Run as", Html.Value(service.User)),
            };

            if (!string.IsNullOrWhiteSpace(service.Website))
            {
                pairs.Add(new KeyValuePair<string, string>(
                    "Website",
                    Html.Value(service.Website)
                    + (string.IsNullOrWhiteSpace(service.Port)
                        ? string.Empty
                        : " <span class=\"text-muted\">on port</span> " + Html.Encode(service.Port))));
            }

            pairs.Add(new KeyValuePair<string, string>(
                "Command",
                string.IsNullOrWhiteSpace(service.Command)
                    ? "<span class=\"muted\">&mdash;</span>"
                    : "<span class=\"code wrap-anywhere\">" + Html.Encode(service.Command) + "</span>"));

            if (service.Arguments != null && service.Arguments.Count > 0)
            {
                pairs.Add(new KeyValuePair<string, string>("Arguments", ArgumentList(service.Arguments)));
            }

            builder.Append(Facts(pairs));

            if (service.Environment != null && service.Environment.Count > 0)
            {
                builder.Append("<h4>Environment</h4>");
                builder.Append(EnvironmentTable(service.Environment));
            }

            if (service.Problems != null && service.Problems.Count > 0)
            {
                builder.Append(ProblemList(service.Problems, null));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders a plan entry's arguments, which the plan carries one per line.
        /// </summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// One per line rather than as a joined command line, because the point of the plan carrying them
        /// separately is that no consumer ever splits a command line -- and a page that joined them to display
        /// them would be the first consumer to do exactly that.
        /// </remarks>
        private static string ArgumentList(IEnumerable<string> arguments)
        {
            StringBuilder builder = new StringBuilder();

            foreach (string argument in arguments)
            {
                if (builder.Length > 0) builder.Append("<br />");

                builder.Append("<span class=\"code wrap-anywhere\">")
                       .Append(Html.Encode(argument ?? string.Empty))
                       .Append("</span>");
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders a plan entry's environment.
        /// </summary>
        /// <param name="variables">The variables.</param>
        /// <returns>The markup.</returns>
        public static string EnvironmentTable(IEnumerable<PlanVariableView> variables)
        {
            List<PlanVariableView> list = new List<PlanVariableView>(variables ?? new List<PlanVariableView>());

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Variable"))
                   .Append(Html.Head("Value"))
                   .Append("</tr></thead><tbody>");

            if (list.Count == 0)
            {
                builder.Append("<tr><td colspan=\"2\" class=\"muted\">Nothing is set for this entry.</td></tr>");
            }
            else
            {
                foreach (PlanVariableView variable in list)
                {
                    builder.Append("<tr>");
                    builder.Append(Html.Column("<span class=\"code\">" + Html.Encode(variable.Key) + "</span>"));
                    builder.Append(Html.Column("<span class=\"code wrap-anywhere\">"
                                               + Html.Encode(variable.Value) + "</span>"));
                    builder.Append("</tr>");
                }
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the editor's form.
        /// </summary>
        /// <param name="document">The document as the service last read it.</param>
        /// <param name="result">What the service said about the text that was sent, when one was.</param>
        /// <param name="vocabulary">The keys this file may carry, or <see langword="null"/>.</param>
        /// <param name="notice">
        /// The site's own sentence about what just happened, for the one status a redirect carries. Empty when
        /// the answer came from the service instead, which is every other case.
        /// </param>
        /// <param name="token">
        /// The anti-forgery token, already rendered as a hidden field. It arrives as markup rather than as a
        /// value because the renderer has no session and must not acquire one: the thing that knows who is signed
        /// in is the page.
        /// </param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// <para>
        /// The whole file is posted back, not a change to it. These files are between six and eleven kilobytes,
        /// the operator is looking at all of them, and a difference would be a second syntax to get wrong on top
        /// of the one being edited -- and one that could not express a comment.
        /// </para>
        /// <para>
        /// The hash of the file as it was read travels with the form. It is the precondition the service checks,
        /// which is what makes two operators editing one file a refusal rather than a silent last-writer-wins.
        /// </para>
        /// <para>
        /// The form has two buttons and posts to this page. A check touches nothing, so an operator can find out
        /// whether their edit is readable before deciding to keep it, and the page has no handler to keep in step
        /// with it. The form posts to a page rather than to a script because there is nothing here that a script
        /// would make better, and a page works with the script switched off.
        /// </para>
        /// <para>
        /// Nothing on this page is a live region. A region that refreshed itself would replace the textarea
        /// while it was being typed into, which would be worse than showing a stale file.
        /// </para>
        /// </remarks>
        public static string Editor(
            ManifestDocumentView document,
            ManifestWriteResultView result,
            VocabularySectionView vocabulary,
            string notice,
            string token)
        {
            if (document == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(notice))
            {
                builder.Append(Html.Alert("info", notice, string.Empty));
            }

            if (result != null && !string.IsNullOrWhiteSpace(result.Message))
            {
                // The tone is the answer rather than the action. A check that found nothing and a write that
                // was accepted are both good news; a text the deployer could not read is a fault however it was
                // arrived at; and a text that was read fine but was not written for some other reason is the one
                // case in between.
                bool refused = result.Document != null && result.Document.ErrorCount > 0;

                string tone = refused ? "danger" : result.CheckedOnly ? "info" : "warning";

                string detail = string.IsNullOrEmpty(result.BackupPath)
                    ? string.Empty
                    : "A copy of what was there is at " + result.BackupPath + ".";

                builder.Append(Html.Alert(tone, result.Message, detail));
            }

            if (result != null && result.Problems != null && result.Problems.Count > 0)
            {
                builder.Append(Html.Panel(
                    "What is wrong with that text",
                    ProblemList(result.Problems, result.Document)));
            }

            if (!document.Exists && !string.IsNullOrWhiteSpace(document.Unreadable))
            {
                builder.Append(Html.Alert("warning", "That file could not be read.", document.Unreadable));

                return builder.ToString();
            }

            builder.Append("<form method=\"post\" action=\"/Editor.aspx\" class=\"editor-form\">");

            // The token proves the post came from a page this site served to this session. Without it, a page
            // on another site could make a signed-in operator's browser write a manifest -- the browser sends
            // the session cookie with a cross-site form post, and the write would be indistinguishable from one
            // the operator made. It is rendered by the page and passed in as markup because the session belongs
            // to the page.
            builder.Append(token ?? string.Empty);

            builder.Append("<input type=\"hidden\" name=\"name\" value=\"")
                   .Append(Html.Attribute(KindName(document.Kind)))
                   .Append("\" />");

            // The hash of the file as it was read, which is the whole of the concurrency control: the service
            // refuses the write when the file no longer matches it, so a second operator's work is never
            // silently replaced. It is the file's hash and not the sent text's, because what the precondition is
            // about is what is on disk.
            builder.Append("<input type=\"hidden\" name=\"hash\" value=\"")
                   .Append(Html.Attribute(document.Sha256))
                   .Append("\" />");

            builder.Append("<div class=\"form-group\">");
            builder.Append("<label for=\"manifest-text\" class=\"control-label\">")
                   .Append(Html.Encode(document.Name))
                   .Append(" &mdash; ")
                   .Append(document.Exists
                       ? Html.Encode(Html.Bytes(document.Length)) + " on disk, changed "
                         + Html.Encode(Html.Relative(ToOffset(document.ModifiedUtc) ?? DateTimeOffset.UtcNow))
                         + ", lines ended with " + Html.Encode(DescribeLineEnding(document.LineEnding))
                       : "the file does not exist yet, so saving this would create it")
                   .Append("</label>");

            builder.Append("<textarea id=\"manifest-text\" name=\"text\" class=\"form-control code\" rows=\"")
                   .Append(TextAreaRows(document))
                   .Append("\" spellcheck=\"false\" wrap=\"off\">")
                   .Append(Html.Encode(CurrentText(document, result)))
                   .Append("</textarea>");
            builder.Append("</div>");

            builder.Append("<div class=\"form-group\">");
            builder.Append("<button type=\"submit\" name=\"action\" value=\"check\" class=\"btn btn-default\">"
                           + "Check without saving</button> ");
            builder.Append("<button type=\"submit\" name=\"action\" value=\"save\" class=\"btn btn-primary\">"
                           + "Save</button> ");
            builder.Append(Html.Link(PageNameOf(document.Kind), "Cancel"));
            builder.Append("</div>");

            builder.Append("</form>");

            builder.Append("<h4>What this file may contain</h4>");
            builder.Append(VocabularyTable(vocabulary));

            return builder.ToString();
        }

        /// <summary>
        /// Renders a manifest's body as the panels a page is made of.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The page that renders first and the handler that refreshes it both call this, so a region cannot
        /// arrive in a different shape from the one it replaced. The problems are first when there are any,
        /// because a file whose sections are interesting and whose fourth line the deployer cannot read is a file
        /// whose fourth line is the thing to look at.
        /// </remarks>
        public static string ManifestBody(ManifestDocumentView document)
        {
            if (document == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            if (document.Problems != null && document.Problems.Count > 0)
            {
                builder.Append(Html.Panel("Problems", ProblemList(document.Problems, document)));
            }
            else if (document.Exists)
            {
                builder.Append(Html.Alert(
                    "info",
                    "Nothing here would stop the deployer reading this file.",
                    string.Empty));
            }

            builder.Append(Html.Panel("The file", ManifestFacts(document)));
            builder.Append(Html.Panel("Sections", SectionTable(document)));
            builder.Append(Html.Panel("Settings", SettingTable(document)));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the plan's body as the panels a page is made of.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A plan that could not be read is a sentence and nothing else. There is no table to render and no
        /// count to show, and a page that rendered empty tables under a failure would look like a machine with
        /// nothing in it rather than like a plan that is not there.
        /// </remarks>
        public static string PlanBody(PlanView plan)
        {
            if (plan == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append(PlanFacts(plan));

            if (!plan.Available) return builder.ToString();

            if (plan.Problems != null && plan.Problems.Count > 0)
            {
                builder.Append(Html.Panel("Problems", ProblemList(plan.Problems, null)));
            }

            builder.Append(Html.Panel("Services", PlanServiceTable(plan.Services)));

            return builder.ToString();
        }

        /// <summary>
        /// Chooses the text the editor starts with.
        /// </summary>
        /// <param name="document">The document as the file stands.</param>
        /// <param name="result">What the service said about the text that was sent, when one was.</param>
        /// <returns>The text.</returns>
        /// <remarks>
        /// A text that was refused is shown back to the operator rather than being replaced by the file's
        /// contents. Losing an edit because a validator disagreed with one line of it is the thing that makes an
        /// operator stop using an editor and start using a text editor plus a deployment.
        /// </remarks>
        private static string CurrentText(ManifestDocumentView document, ManifestWriteResultView result)
        {
            if (result != null && result.Document != null && result.Document.Lines != null)
            {
                StringBuilder builder = new StringBuilder();

                foreach (ManifestLineView line in result.Document.Lines)
                {
                    builder.Append(line.Text ?? string.Empty).Append('\n');
                }

                return builder.ToString();
            }

            return Text(document);
        }

        /// <summary>
        /// Rebuilds a document's text from its lines.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The text.</returns>
        /// <remarks>
        /// The lines are joined with a line feed whatever the file uses, because a form field carries line
        /// feeds and the service normalises them back to the file's own ending before it writes. Rendering the
        /// file's own ending here would put a carriage return in the form field for a Windows file, and a
        /// browser submits those as two line breaks.
        /// </remarks>
        public static string Text(ManifestDocumentView document)
        {
            if (document == null || document.Lines == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            foreach (ManifestLineView line in document.Lines)
            {
                builder.Append(line.Text ?? string.Empty).Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Chooses how tall the editor is.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The number of rows.</returns>
        /// <remarks>
        /// Tall enough for the whole file, with a floor and a ceiling. A scrollbar inside a form field that
        /// holds a hundred and twenty lines makes the second half of the file something an operator has to go
        /// looking for, which is how a mistake in it survives.
        /// </remarks>
        private static string TextAreaRows(ManifestDocumentView document)
        {
            int lines = document == null || document.Lines == null ? 0 : document.Lines.Count;

            if (lines < 24) lines = 24;
            if (lines > 200) lines = 200;

            return lines.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Finds the page that shows a manifest kind.
        /// </summary>
        /// <param name="kind">The kind, as the service names it.</param>
        /// <returns>The address, or the overview when the kind has no page.</returns>
        private static string PageNameOf(string kind)
        {
            string page = ConfigurationService.PageFor(kind);

            return page.Length == 0 ? "/" : page;
        }

        /// <summary>
        /// Renders the key and value block that heads a page.
        /// </summary>
        /// <param name="pairs">The pairs, in the order they are shown.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Pair order is the caller's. It is not sorted, because the order a detail page presents its facts in
        /// is the order somebody reads them in, and sorting alphabetically would be arranging them for a machine.
        /// </remarks>
        public static string Facts(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"row\"><div class=\"col-sm-12 detail-facts\">");

            foreach (KeyValuePair<string, string> pair in pairs ?? new List<KeyValuePair<string, string>>())
            {
                builder.Append(Html.Field(pair.Key, pair.Value));
            }

            builder.Append("</div></div>");

            return builder.ToString();
        }

        /// <summary>
        /// Converts a timestamp read from the service into one this site renders consistently.
        /// </summary>
        /// <param name="value">The timestamp.</param>
        /// <returns>The instant in UTC, or <see langword="null"/>.</returns>
        /// <remarks>
        /// A value with no zone is taken as UTC rather than as local time. The service always writes UTC, so a
        /// value that lost its offset is a value whose zone is already known, and treating it as local would
        /// shift every timestamp on the page by the server's offset from Greenwich.
        /// </remarks>
        public static DateTimeOffset? ToOffset(DateTime? value)
        {
            if (!value.HasValue) return null;

            DateTime moment = value.Value;

            if (moment.Kind == DateTimeKind.Unspecified) moment = DateTime.SpecifyKind(moment, DateTimeKind.Utc);

            return new DateTimeOffset(moment.ToUniversalTime());
        }
    }
}
