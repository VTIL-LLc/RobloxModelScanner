using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

using RMS.Site.Common.Implementation;

namespace Scanner.WebForms.Infrastructure
{
    /// <summary>
    /// Renders every piece of markup this site shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page and the fragment handler that refreshes one of its regions call the same methods here, which is
    /// what stops a table from being built twice -- once in C# for the page and once in script for the refresh --
    /// and drifting apart. There is no markup in the page files and none in the script: both show what this
    /// class built.
    /// </para>
    /// <para>
    /// Three rules run through all of it.
    /// </para>
    /// <para>
    /// The first is that a value from the catalogue is encoded, and the encoding happens in <see cref="Html"/>.
    /// A model's name, its creator and its description are all written by somebody else, and this site is read
    /// by an operator who has enough authority already without also being able to be handed a script by a model
    /// author.
    /// </para>
    /// <para>
    /// The second is that nothing here decides anything about the catalogue. What a rule means, which severity
    /// it carries and which category it belongs to are the API's answers, carried through. This class
    /// translates a category number into the name of a colour and does nothing else with it.
    /// </para>
    /// <para>
    /// The third is that an absent value is shown as absent. Most of the catalogue has no creator recorded and
    /// a great deal of it has no name, and a table that rendered those rows as blanks would be a table an
    /// operator cannot tell apart from one that failed to load.
    /// </para>
    /// </remarks>
    public static class ScannerViews
    {
        /// <summary>
        /// The address the catalogue's own page lives at.
        /// </summary>
        /// <remarks>
        /// Named once because the filter form, the pager, the rule links and the creator links all have to agree
        /// about it. A link that navigated to a different page than the form submitted to would reset the filter
        /// whenever the reader turned a page.
        /// </remarks>
        public const string CatalogueAddress = "/Default.aspx";

        /// <summary>
        /// Renders a failed call as something an operator can act on.
        /// </summary>
        /// <param name="status">The status the API answered with, or the one this site synthesised.</param>
        /// <param name="error">The sentence the client recorded.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A refusal and an outage are shown differently because they have different remedies. A refusal means
        /// the two ends disagree about a secret; an outage means nothing is listening. A page that showed both as
        /// "could not load" would send the operator to the wrong machine roughly half the time.
        /// </remarks>
        public static string Failure(HttpStatusCode status, string error)
        {
            if (status == HttpStatusCode.Forbidden || status == HttpStatusCode.Unauthorized)
            {
                return Html.Alert(
                    "warning",
                    "The API refused this site.",
                    "The key this site presents is not the one the scanner's API was started with, or the query "
                    + "parameter this site puts it in is not the name the API reads. Both are settings on this "
                    + "machine rather than faults in the page.");
            }

            if (status == HttpStatusCode.ServiceUnavailable || status == HttpStatusCode.BadGateway)
            {
                return Html.Alert(
                    "danger",
                    "The API is not answering.",
                    "Nothing is listening where this site expects the scanner's API. Check that the API is running "
                    + "and that the address in this site's SiteServiceBaseUrl is the one it was given.")
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
                return Html.Alert("warning", "There is nothing there.", error);
            }

            if (status == HttpStatusCode.RequestTimeout)
            {
                return Html.Alert(
                    "warning",
                    "The API did not answer in time.",
                    "The API accepted the request and has not finished with it. The index it reads may be being "
                    + "rebuilt; this page will ask again by itself.");
            }

            return Html.Alert("danger", "The API could not be read.", error);
        }

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
        /// <param name="extraQuery">Additional query parameters, not yet encoded.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// <para>
        /// The region is rendered now and refreshed afterwards rather than left empty for the script to fill. A
        /// page whose content arrives only once script has run is a page that shows nothing when script is
        /// refused, and a reader then cannot tell a broken page from a working one that has nothing to report.
        /// </para>
        /// <para>
        /// The separator is a plain ampersand here, escaped once by <see cref="Html.Attribute"/>. Writing the
        /// entity into this string would have it escaped a second time, and an attribute that reaches the
        /// browser as <c>&amp;amp;</c> parses back to a lone <c>&amp;</c> -- so the second parameter would be
        /// named <c>amp;view</c> and the region would refresh with whatever the handler answers for a request
        /// that named nothing.
        /// </para>
        /// </remarks>
        public static string Live(string id, string view, string extraQuery, string content)
        {
            string address = "/Api/Fragment.ashx?view=" + Uri.EscapeDataString(view);

            if (!string.IsNullOrEmpty(extraQuery)) address += "&" + extraQuery;

            return "<div id=\"" + Html.Attribute(id) + "\" class=\"rms-live\" data-url=\""
                   + Html.Attribute(address) + "\">" + content + "</div>";
        }

        /// <summary>
        /// Renders an empty state.
        /// </summary>
        /// <param name="message">The sentence explaining what is missing.</param>
        /// <returns>The markup.</returns>
        public static string Empty(string message)
        {
            return "<p class=\"text-muted empty-note\">" + Html.Encode(message) + "</p>";
        }

        /// <summary>
        /// Renders the headline counters.
        /// </summary>
        /// <param name="analytics">The analytics view.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The counters answer "how many", in the order the question is asked: how much was indexed, how much of
        /// it carries script, how much of it matched a rule, and how much score all of that accounts for. There
        /// are six of them because six fit across a wide screen and they are the six an operator reads before
        /// deciding whether to look at anything else.
        /// </remarks>
        public static string Counters(AnalyticsView analytics)
        {
            if (analytics == null) return string.Empty;

            string note = "Average " + Html.Score(analytics.AverageScore) + " per model, highest "
                          + Html.Score(analytics.MaximumScore) + ".";

            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"row\">");
            builder.Append(Counter("Models", Html.Number(analytics.TotalModels), "info",
                "Every model the scanner has indexed."));
            builder.Append(Counter("With script", Html.Number(analytics.ModelsWithScripts), "info",
                "Models whose stored data contains script."));
            builder.Append(Counter("With detections", Html.Number(analytics.ModelsWithDetections), "danger",
                "Models that matched at least one detection rule."));
            builder.Append(Counter("Rules matched", Html.Number(analytics.DistinctRulesTriggered), "warning",
                "Distinct rules that matched at least one model."));
            builder.Append(Counter("Rule matches", Html.Number(analytics.TotalRuleMatches), "warning",
                "Individual matches, counting each rule once per model."));
            builder.Append(Counter("Total score", Html.Score(analytics.TotalScore), "info", note));
            builder.Append("</div>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders one counter.
        /// </summary>
        /// <param name="label">The counter's label.</param>
        /// <param name="value">The value, already rendered as text.</param>
        /// <param name="tint">The tint: <c>info</c>, <c>warning</c> or <c>danger</c>.</param>
        /// <param name="note">The sentence shown beneath the value.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The tint is a class name rather than a style, because the policy these pages are served under permits
        /// styles from this origin only and an inline colour would be refused without saying so.
        /// </remarks>
        private static string Counter(string label, string value, string tint, string note)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"col-sm-6 col-md-2\">");
            builder.Append("<div class=\"panel panel-primary counter-panel counter-").Append(Html.Attribute(tint))
                   .Append("\">");
            builder.Append("<div class=\"panel-body\">");
            builder.Append("<p class=\"counter-value\">").Append(Html.Encode(value)).Append("</p>");
            builder.Append("<p class=\"counter-label\">").Append(Html.Encode(label)).Append("</p>");
            builder.Append("<p class=\"counter-note text-muted\">").Append(Html.Encode(note)).Append("</p>");
            builder.Append("</div></div></div>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the sentence saying when the analytics were computed.
        /// </summary>
        /// <param name="analytics">The analytics view.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Worth saying, because these numbers are computed against a snapshot of the index rather than against
        /// the live catalogue. A reader comparing them with a table below has to know which instant the
        /// comparison is against.
        /// </remarks>
        public static string Computed(AnalyticsView analytics)
        {
            DateTimeOffset? moment = ToOffset(analytics == null ? null : analytics.GeneratedUtc);

            return moment.HasValue
                ? "<p class=\"text-muted\">Computed " + Html.Instant(moment) + ".</p>"
                : string.Empty;
        }

        /// <summary>
        /// Renders the catalogue filter form.
        /// </summary>
        /// <param name="filter">The filter currently in force.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// <para>
        /// A plain form with a method and an action, so it works with script refused and so the address it
        /// produces is the address of the result. That is what makes a filtered catalogue something an operator
        /// can bookmark, send to somebody else, or come back to with the back button -- none of which is true of
        /// a filter that lives in a script's variable.
        /// </para>
        /// <para>
        /// There is no anti-forgery token, because this form changes nothing. It is a search, and a search is a
        /// read; a token on it would be a token the site had to keep in step with a session for no gain.
        /// </para>
        /// </remarks>
        public static string FilterForm(CatalogueFilter filter)
        {
            CatalogueFilter current = filter ?? new CatalogueFilter();

            StringBuilder builder = new StringBuilder();

            builder.Append("<form method=\"get\" action=\"").Append(Html.Attribute(CatalogueAddress))
                   .Append("\" class=\"form-horizontal filter-form\">");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label for=\"filter-q\" class=\"col-sm-2 control-label\">Search</label>");
            builder.Append("<div class=\"col-sm-6\">");
            builder.Append("<input class=\"form-control\" type=\"search\" id=\"filter-q\" name=\"q\" maxlength=\"128\" ")
                   .Append("placeholder=\"a model identifier, a name, a creator\" value=\"")
                   .Append(Html.Attribute(current.Search)).Append("\" />");
            builder.Append("</div>");
            builder.Append("<div class=\"col-sm-4\">");
            builder.Append("<button type=\"submit\" class=\"btn btn-primary btn-sm\">Search</button>");

            if (current.IsFiltered)
            {
                builder.Append(" <a class=\"btn btn-default btn-sm\" href=\"")
                       .Append(Html.Attribute(CatalogueAddress))
                       .Append("\">Clear</a>");
            }

            builder.Append("</div>");
            builder.Append("</div>");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label for=\"filter-scripts\" class=\"col-sm-2 control-label\">Script</label>");
            builder.Append("<div class=\"col-sm-3\">");
            builder.Append("<select class=\"form-control\" id=\"filter-scripts\" name=\"scripts\">");
            builder.Append(Option(string.Empty, "Either", current.HasScripts, null));
            builder.Append(Option("1", "Contains script", current.HasScripts, true));
            builder.Append(Option("0", "No script", current.HasScripts, false));
            builder.Append("</select>");
            builder.Append("</div>");

            builder.Append("<label for=\"filter-detections\" class=\"col-sm-2 control-label\">Detections</label>");
            builder.Append("<div class=\"col-sm-3\">");
            builder.Append("<select class=\"form-control\" id=\"filter-detections\" name=\"detections\">");
            builder.Append(Option(string.Empty, "Either", current.HasDetections, null));
            builder.Append(Option("1", "Matched a rule", current.HasDetections, true));
            builder.Append(Option("0", "Matched nothing", current.HasDetections, false));
            builder.Append("</select>");
            builder.Append("</div>");

            builder.Append("<div class=\"col-sm-2\">");
            builder.Append("<button type=\"submit\" class=\"btn btn-default btn-sm\">Apply</button>");
            builder.Append("</div>");
            builder.Append("</div>");

            if (!string.IsNullOrWhiteSpace(current.RuleTag))
            {
                // The rule filter is carried in a hidden field rather than a visible one. It is set by clicking a
                // rule, and the control for it is the sentence below, which takes it off again.
                builder.Append("<input type=\"hidden\" name=\"tag\" value=\"")
                       .Append(Html.Attribute(current.RuleTag)).Append("\" />");

                builder.Append("<div class=\"form-group form-group-sm\">");
                builder.Append("<div class=\"col-sm-offset-2 col-sm-10\">");
                builder.Append("<p class=\"form-control-static\">Only models that matched <span class=\"label ")
                       .Append(Html.Attribute(TagClass(current.RuleTag)))
                       .Append("\">").Append(Html.Encode(current.RuleTag))
                       .Append("</span> are shown. <a href=\"")
                       .Append(Html.Attribute(AddressWithoutRule(current)))
                       .Append("\">Show every model again.</a></p>");
                builder.Append("</div></div>");
            }

            builder.Append("</form>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders one option of a filter's select.
        /// </summary>
        /// <param name="value">The value the option submits.</param>
        /// <param name="label">The option's text.</param>
        /// <param name="current">The filter's value, when it has one.</param>
        /// <param name="option">The value this option stands for, or <see langword="null"/> for "either".</param>
        /// <returns>The markup.</returns>
        private static string Option(string value, string label, bool? current, bool? option)
        {
            bool selected = current.HasValue
                ? option.HasValue && current.Value == option.Value
                : !option.HasValue;

            return "<option value=\"" + Html.Attribute(value) + "\""
                   + (selected ? " selected=\"selected\"" : string.Empty) + ">"
                   + Html.Encode(label) + "</option>";
        }

        /// <summary>
        /// Renders the catalogue table.
        /// </summary>
        /// <param name="page">The page of models.</param>
        /// <param name="knownLabels">The labels the rule reference names, for reading historical rows.</param>
        /// <param name="linked">Whether each row links to a page of its own.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The table can be sorted in the browser, and that sorting is over the rows that are present. It is not
        /// a second order the API was asked for: a reader who sorts this table is rearranging the page in front
        /// of them, and a reader who wants a different fifty rows asks through the filter. Keeping the two apart
        /// is what stops them from disagreeing about what is on the page.
        /// </remarks>
        public static string ModelTable(ModelPage page, IReadOnlyList<string> knownLabels, bool linked)
        {
            List<ModelRow> rows = page == null || page.Items == null
                ? new List<ModelRow>()
                : page.Items;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));

            builder.Append("<thead><tr>");
            builder.Append(Html.Head("Model", "col-number"));
            builder.Append(Html.Head("Recorded", "col-time"));
            builder.Append(Html.Head("Name"));
            builder.Append(Html.Head("Creator"));
            builder.Append(Html.Head("Script"));
            builder.Append(Html.Head("Signals"));
            builder.Append(Html.Head("Score", "col-number"));
            builder.Append("</tr></thead><tbody>");

            if (rows.Count == 0)
            {
                builder.Append("<tr><td colspan=\"7\" class=\"text-muted\">")
                       .Append(page != null && page.Total > 0
                           ? "No models on this page. The page number is past the end of the results."
                           : "Nothing in the catalogue matches that.")
                       .Append("</td></tr>");
            }

            foreach (ModelRow row in rows)
            {
                string identifier = row.Model.ToString(CultureInfo.InvariantCulture);

                builder.Append("<tr>");

                // The identifier is written onto the cell as well as into it. The script's sort reads that
                // attribute first and falls back to the cell's text, which is what makes a column of numbers sort
                // as numbers rather than as the strings they are rendered as -- "100" before "99" is the kind of
                // ordering that makes an operator distrust the whole page.
                builder.Append("<td class=\"col-number\" data-sort=\"").Append(Html.Attribute(identifier))
                       .Append("\">");

                builder.Append(linked
                    ? Html.Link("/Model.aspx?id=" + identifier, identifier)
                    : "<span class=\"code\">" + Html.Encode(identifier) + "</span>");

                builder.Append("</td>");

                builder.Append(Html.Column(Html.Instant(ToOffset(row.Date)), "col-time"));
                builder.Append(Html.Column(ModelName(row, linked)));
                builder.Append(Html.Column(Creator(row)));
                builder.Append(Html.Column(row.HasScripts
                    ? "<span class=\"label label-info\">script</span>"
                    : "<span class=\"muted\">none</span>"));
                builder.Append(Html.Column(TagBadges(row.Tags, knownLabels)));
                builder.Append(Html.Column(Score(row.Score), "col-number"));

                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the pager that belongs beneath the catalogue.
        /// </summary>
        /// <param name="page">The page of models.</param>
        /// <param name="filter">The filter in force.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The pager is links rather than script, so every page of the catalogue is an address. It shows the ends
        /// and a window around the current page rather than every page number: a catalogue of a hundred thousand
        /// rows at fifty a page is two thousand pages, and a strip of two thousand numbers is not a control
        /// anybody uses.
        /// </remarks>
        public static string Pager(ModelPage page, CatalogueFilter filter)
        {
            if (page == null || filter == null) return string.Empty;

            // The size the API answered with, not the one that was asked for. The API clamps the page size to its
            // own limit, and a pager that divided by the requested size would offer a page count that does not
            // exist and describe a range that is not the one on screen.
            int size = page.PageSize > 0 ? page.PageSize : filter.PageSize;

            if (size <= 0) size = CatalogueFilter.DefaultPageSize;

            int last = page.Total <= 0 ? 1 : (page.Total + size - 1) / size;
            int current = page.Page < 1 ? 1 : page.Page > last ? last : page.Page;

            StringBuilder builder = new StringBuilder();

            builder.Append("<nav class=\"pager-strip\">");
            builder.Append("<ul class=\"pagination pagination-sm\">");

            builder.Append(PageItem("&laquo;", current > 1 ? Address(filter, 1) : null, false));
            builder.Append(PageItem("&lsaquo;", current > 1 ? Address(filter, current - 1) : null, false));

            int first = current - 2;
            int final = current + 2;

            if (first < 1) first = 1;
            if (final > last) final = last;

            if (first > 1)
            {
                builder.Append(PageItem("1", Address(filter, 1), false));

                if (first > 2) builder.Append("<li class=\"disabled\"><span>&hellip;</span></li>");
            }

            for (int number = first; number <= final; number++)
            {
                builder.Append(PageItem(
                    number.ToString(CultureInfo.InvariantCulture),
                    Address(filter, number),
                    number == current));
            }

            if (final < last)
            {
                if (final < last - 1) builder.Append("<li class=\"disabled\"><span>&hellip;</span></li>");

                builder.Append(PageItem(
                    last.ToString(CultureInfo.InvariantCulture),
                    Address(filter, last),
                    false));
            }

            builder.Append(PageItem("&rsaquo;", current < last ? Address(filter, current + 1) : null, false));
            builder.Append(PageItem("&raquo;", current < last ? Address(filter, last) : null, false));

            builder.Append("</ul>");
            builder.Append("</nav>");

            builder.Append("<p class=\"text-muted pager-summary\">");
            builder.Append(DescribeRange(page, size, last));
            builder.Append("</p>");

            return builder.ToString();
        }

        /// <summary>
        /// Describes which models the page is showing.
        /// </summary>
        /// <param name="page">The page of models.</param>
        /// <param name="size">The number of rows a page holds.</param>
        /// <param name="last">The last page number.</param>
        /// <returns>The sentence.</returns>
        private static string DescribeRange(ModelPage page, int size, int last)
        {
            if (page.Total <= 0) return "No models matched.";

            long first = ((long)(page.Page - 1) * size) + 1;
            long final = Math.Min((long)page.Page * size, page.Total);

            if (first < 1) first = 1;
            if (final < first) final = first;

            return string.Format(
                CultureInfo.InvariantCulture,
                "Showing {0} to {1} of {2} models. Page {3} of {4}.",
                Html.Number(first),
                Html.Number(final),
                Html.Number(page.Total),
                Html.Number(page.Page),
                Html.Number(last));
        }

        /// <summary>
        /// Renders one page of the pager.
        /// </summary>
        /// <param name="text">The link's text, which is markup and is not encoded.</param>
        /// <param name="address">The address, or <see langword="null"/> when there is nowhere to go.</param>
        /// <param name="active">Whether this is the page being shown.</param>
        /// <returns>The markup.</returns>
        private static string PageItem(string text, string address, bool active)
        {
            if (address == null) return "<li class=\"disabled\"><span>" + text + "</span></li>";

            return "<li" + (active ? " class=\"active\"" : string.Empty) + "><a href=\""
                   + Html.Attribute(address) + "\">" + text + "</a></li>";
        }

        /// <summary>
        /// Builds the address of the catalogue showing one page of the current filter.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="page">The page number to name.</param>
        /// <returns>The address.</returns>
        public static string Address(CatalogueFilter filter, int page)
        {
            return CatalogueAddress + "?" + Query(filter, page);
        }

        /// <summary>
        /// Renders a filter as a query string.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="page">The page number to name.</param>
        /// <returns>The query string, without the leading question mark.</returns>
        /// <remarks>
        /// Public because the live region needs the same string to put on the element its script refreshes, and
        /// a region whose address was built separately would refresh into a differently filtered table.
        /// </remarks>
        public static string Query(CatalogueFilter filter, int page)
        {
            CatalogueFilter source = filter ?? new CatalogueFilter();

            StringBuilder builder = new StringBuilder();

            KeyValuePair<string, string>[] pairs = source.ToAddress(page);

            for (int index = 0; index < pairs.Length; index++)
            {
                if (index > 0) builder.Append('&');

                builder.Append(Uri.EscapeDataString(pairs[index].Key))
                       .Append('=')
                       .Append(Uri.EscapeDataString(pairs[index].Value));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Builds the address of the catalogue with the rule filter taken off.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <returns>The address.</returns>
        private static string AddressWithoutRule(CatalogueFilter filter)
        {
            CatalogueFilter without = new CatalogueFilter
            {
                Search = filter.Search,
                HasScripts = filter.HasScripts,
                HasDetections = filter.HasDetections,
                RuleTag = string.Empty,
                Page = 1,
                PageSize = filter.PageSize,
                SortBy = filter.SortBy,
                Descending = filter.Descending,
            };

            return Address(without, 1);
        }

        /// <summary>
        /// Builds the address of the catalogue filtered to one rule.
        /// </summary>
        /// <param name="tag">The rule's label.</param>
        /// <returns>The address.</returns>
        public static string RuleAddress(string tag)
        {
            return CatalogueAddress + "?tag=" + Uri.EscapeDataString(tag ?? string.Empty);
        }

        /// <summary>
        /// Renders a model's name, or its identifier when it has none.
        /// </summary>
        /// <param name="row">The model.</param>
        /// <param name="linked">Whether the name links to the model's own page.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A row with no name is the ordinary state of the oldest part of the catalogue, so the identifier stands
        /// in rather than a dash, and it says so. The identifier is also the one part of a row that is always
        /// there, which makes it the only candidate for a link that always works.
        /// </remarks>
        private static string ModelName(ModelRow row, bool linked)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                return "<span class=\"muted\" title=\"The scanner had not started reading names when it captured "
                       + "this model, or Roblox no longer serves one.\">no name recorded</span>";
            }

            if (!linked) return Html.Encode(row.Name);

            string identifier = row.Model.ToString(CultureInfo.InvariantCulture);

            return Html.Link("/Model.aspx?id=" + identifier, row.Name);
        }

        /// <summary>
        /// Renders a model's creator.
        /// </summary>
        /// <param name="row">The model.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The name and the identifier are shown together when both are known, because a creator's name is
        /// neither unique nor stable: the identifier is the part that still refers to the same account after
        /// somebody renames themselves.
        /// </remarks>
        private static string Creator(ModelRow row)
        {
            if (string.IsNullOrWhiteSpace(row.CreatorName) && string.IsNullOrWhiteSpace(row.CreatorId))
            {
                return "<span class=\"muted\" title=\"The scanner recorded no creator for this model. The "
                       + "catalogue is being read again to fill these in.\">&mdash;</span>";
            }

            if (string.IsNullOrWhiteSpace(row.CreatorId)) return Html.Encode(row.CreatorName);
            if (string.IsNullOrWhiteSpace(row.CreatorName)) return Html.Encode(row.CreatorId);

            return Html.Encode(row.CreatorName) + " <span class=\"muted code\">" + Html.Encode(row.CreatorId)
                   + "</span>";
        }

        /// <summary>
        /// Renders a model's score.
        /// </summary>
        /// <param name="score">The score.</param>
        /// <returns>The markup.</returns>
        private static string Score(double score)
        {
            return score <= 0
                ? "<span class=\"muted\">0</span>"
                : "<span class=\"code\">" + Html.Encode(Html.Score(score)) + "</span>";
        }

        /// <summary>
        /// Renders the rules a model matched, as labels.
        /// </summary>
        /// <param name="stored">The stored tag column.</param>
        /// <param name="knownLabels">The labels the rule reference names.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Each label is a link to the catalogue filtered to that rule, which is the question an operator asks
        /// next: what else matched this. The label shown is the one stored, mapped through the rename table, so
        /// a rule that was named differently two revisions ago still reads as the rule it means.
        /// </remarks>
        public static string TagBadges(string stored, IReadOnlyList<string> knownLabels)
        {
            IReadOnlyList<string> labels = TagList.Parse(stored, knownLabels);

            if (labels.Count == 0) return "<span class=\"muted\">&mdash;</span>";

            StringBuilder builder = new StringBuilder();

            foreach (string label in labels)
            {
                if (builder.Length > 0) builder.Append(' ');

                builder.Append(BadgeLink(label, "Show every model that matched this rule"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders a rule label that links to the models that matched it.
        /// </summary>
        /// <param name="label">The label.</param>
        /// <param name="title">The tooltip.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The address is put through the shared destination check even though this method built it, because the
        /// label it was built from came from a stored column. A rule label containing a colon would otherwise be
        /// the one way a value from the catalogue could reach an anchor's destination on this site.
        /// </remarks>
        public static string BadgeLink(string label, string title)
        {
            string safe = Html.SafeHref(RuleAddress(label));

            if (safe.Length == 0) return Html.Badge(label, TagClass(label), title);

            return "<a class=\"label label-" + Html.Attribute(TagClass(label)) + "\" href=\""
                   + Html.Attribute(safe) + "\" title=\"" + Html.Attribute(title) + "\">"
                   + Html.Encode(label) + "</a>";
        }

        /// <summary>
        /// Translates a rule label into the colour its badge is drawn in.
        /// </summary>
        /// <param name="label">The label.</param>
        /// <returns>The class suffix.</returns>
        /// <remarks>
        /// The translation is on the words a label uses rather than on a table of every label, because the labels
        /// belong to the rule set and the rule set belongs to the scanner. A rule added tomorrow shows as a plain
        /// badge rather than as a missing one, which is the failure that is survivable.
        /// </remarks>
        private static string TagClass(string label)
        {
            if (string.IsNullOrEmpty(label)) return "default";

            if (label.IndexOf("Good people", StringComparison.OrdinalIgnoreCase) >= 0) return "success";
            if (label.IndexOf("whitelist", StringComparison.OrdinalIgnoreCase) >= 0) return "success";
            if (label.IndexOf("Possible", StringComparison.OrdinalIgnoreCase) >= 0) return "warning";
            if (label.IndexOf("Heuristic", StringComparison.OrdinalIgnoreCase) >= 0) return "warning";

            return "danger";
        }

        /// <summary>
        /// Renders the analytics body: what was found, by category, by rule, by score and over time.
        /// </summary>
        /// <param name="analytics">The analytics view.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The order is the order the questions are asked in. "What kind of signal" comes before "which rule",
        /// because the category is how an operator decides whether to care at all; "which rule" comes before the
        /// distribution, because the rule table is the one that names something to go and look at; and the
        /// creator list is last because it is the only panel here that is about a person rather than a thing.
        /// </remarks>
        public static string AnalyticsBody(AnalyticsView analytics)
        {
            if (analytics == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Panel("By category", CategoryTable(analytics.Categories)));
            builder.Append(Html.Panel("Rules that matched", RuleTable(analytics.Rules, showCounts: true)));
            builder.Append(Html.Panel("Score distribution", ScoreTable(analytics.ScoreDistribution)));
            builder.Append(Html.Panel("Discovery", TimelineTable(analytics.DiscoveryTimeline)));
            builder.Append(Html.Panel("Most prolific creators", CreatorTable(analytics.TopCreators)));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the per-category roll-up.
        /// </summary>
        /// <param name="categories">The categories.</param>
        /// <returns>The markup.</returns>
        public static string CategoryTable(List<CategoryView> categories)
        {
            if (categories == null || categories.Count == 0)
            {
                return Empty("No rule has matched anything in the index.");
            }

            int largest = Largest(categories);

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>");
            builder.Append(Html.Head("Category"));
            builder.Append(Html.Head("Matches", "col-number"));
            builder.Append(Html.Head("Rules", "col-number"));
            builder.Append(Html.Head("Share", "col-share"));
            builder.Append("</tr></thead><tbody>");

            foreach (CategoryView category in categories)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column(Html.Badge(
                    Display(category.Category),
                    CategoryClass(category.Category),
                    CategoryTitle(category.Category))));
                builder.Append(Html.Column(Html.Number(category.Matches), "col-number"));
                builder.Append(Html.Column(Html.Number(category.DistinctRules), "col-number"));
                builder.Append(Html.Column(Bar(category.Matches, largest)));
                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Finds the largest match count in a category roll-up.
        /// </summary>
        /// <param name="categories">The categories.</param>
        /// <returns>The largest count.</returns>
        private static int Largest(List<CategoryView> categories)
        {
            int largest = 0;

            foreach (CategoryView category in categories)
            {
                if (category.Matches > largest) largest = category.Matches;
            }

            return largest;
        }

        /// <summary>
        /// Renders a table of rules.
        /// </summary>
        /// <param name="rules">The rules.</param>
        /// <param name="showCounts">Whether the match counts belong in the table.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The same table serves two pages. On the analytics page the counts are the point; on the rule reference
        /// they are noise beside the explanation of what each rule is for, so they are left out and the table is
        /// one column narrower.
        /// </remarks>
        public static string RuleTable(List<RuleStatisticView> rules, bool showCounts)
        {
            if (rules == null || rules.Count == 0)
            {
                return Empty("The API published no rules.");
            }

            int largest = 0;

            foreach (RuleStatisticView rule in rules)
            {
                if (rule.Matches > largest) largest = rule.Matches;
            }

            int columns = showCounts ? 7 : 5;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>");
            builder.Append(Html.Head("Rule"));
            builder.Append(Html.Head("Category"));
            builder.Append(Html.Head("Severity"));
            builder.Append(Html.Head("Score", "col-number"));
            builder.Append(Html.Head("What it is for"));

            if (showCounts)
            {
                builder.Append(Html.Head("Matches", "col-number"));
                builder.Append(Html.Head("Share", "col-share"));
            }

            builder.Append("</tr></thead><tbody>");

            foreach (RuleStatisticView rule in rules)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column(BadgeLink(
                    rule.Tag,
                    "Show every model that matched this rule")));
                builder.Append(Html.Column(Html.Badge(
                    CategoryName(rule.Category),
                    CategoryClass(rule.Category),
                    CategoryTitle(CategoryName(rule.Category)))));
                builder.Append(Html.Column(SeverityBadge(rule.Severity)));
                builder.Append(Html.Column(Html.Encode(Html.Score(rule.Score)), "col-number"));
                builder.Append(Html.Column(Purpose(rule)));

                if (showCounts)
                {
                    builder.Append(Html.Column(Html.Number(rule.Matches), "col-number"));
                    builder.Append(Html.Column(Bar(rule.Matches, largest)));
                }

                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            // A rule that matched nothing is still a rule, and the table above lists every one of them. The note
            // is for the reader who wonders why the counts column is all zeroes.
            if (showCounts && largest == 0)
            {
                builder.Append(Empty("No rule in this list has matched anything in the index yet."));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders a rule's explanation.
        /// </summary>
        /// <param name="rule">The rule.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The purpose is the sentence the rule's author wrote for an operator, and the technical description is
        /// longer and is offered as a tooltip. Both are there; it is the purpose that is on the row, because a
        /// table of thirty signature descriptions is a table nobody reads to the end.
        /// </remarks>
        private static string Purpose(RuleStatisticView rule)
        {
            string purpose = string.IsNullOrWhiteSpace(rule.Purpose)
                ? "<span class=\"muted\">The API published no purpose for this rule.</span>"
                : Html.Encode(rule.Purpose);

            if (string.IsNullOrWhiteSpace(rule.Description)) return purpose;

            return "<span title=\"" + Html.Attribute(rule.Description) + "\">" + purpose + "</span>";
        }

        /// <summary>
        /// Renders the score histogram.
        /// </summary>
        /// <param name="buckets">The buckets.</param>
        /// <returns>The markup.</returns>
        public static string ScoreTable(List<ScoreBucketView> buckets)
        {
            if (buckets == null || buckets.Count == 0)
            {
                return Empty("The API published no score distribution.");
            }

            int largest = 0;

            foreach (ScoreBucketView bucket in buckets)
            {
                if (bucket.Count > largest) largest = bucket.Count;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>");
            builder.Append(Html.Head("Score"));
            builder.Append(Html.Head("Models", "col-number"));
            builder.Append(Html.Head("Share", "col-share"));
            builder.Append("</tr></thead><tbody>");

            foreach (ScoreBucketView bucket in buckets)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column(Html.Encode(BucketLabel(bucket))));
                builder.Append(Html.Column(Html.Number(bucket.Count), "col-number"));
                builder.Append(Html.Column(Bar(bucket.Count, largest)));
                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders a score bucket's range.
        /// </summary>
        /// <param name="bucket">The bucket.</param>
        /// <returns>The text.</returns>
        /// <remarks>
        /// The last bucket has no upper bound, and it is described as having none rather than given a very large
        /// number. A range that ended somewhere would be a claim about the catalogue that the catalogue does not
        /// make.
        /// </remarks>
        private static string BucketLabel(ScoreBucketView bucket)
        {
            if (!string.IsNullOrWhiteSpace(bucket.Label)) return bucket.Label;

            return bucket.UpperBound.HasValue
                ? Html.Score(bucket.LowerBound) + " to " + Html.Score(bucket.UpperBound.Value)
                : Html.Score(bucket.LowerBound) + " or more";
        }

        /// <summary>
        /// Renders the discovery timeline.
        /// </summary>
        /// <param name="timeline">The months.</param>
        /// <returns>The markup.</returns>
        public static string TimelineTable(List<TimelineView> timeline)
        {
            if (timeline == null || timeline.Count == 0)
            {
                return Empty("The index records no dates.");
            }

            int largest = 0;

            foreach (TimelineView point in timeline)
            {
                if (point.Count > largest) largest = point.Count;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>");
            builder.Append(Html.Head("Month"));
            builder.Append(Html.Head("Models", "col-number"));
            builder.Append(Html.Head("Share", "col-share"));
            builder.Append("</tr></thead><tbody>");

            foreach (TimelineView point in timeline)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column(Html.Cell(point.Period)));
                builder.Append(Html.Column(Html.Number(point.Count), "col-number"));
                builder.Append(Html.Column(Bar(point.Count, largest)));
                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the creator leaderboard.
        /// </summary>
        /// <param name="creators">The creators.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Each count links to the catalogue searched for that creator's identifier. That is the whole use of
        /// this table: an account that publishes scripted content in bulk is found here and then read through
        /// the catalogue, one page at a time, with the filter already applied.
        /// </remarks>
        public static string CreatorTable(List<CreatorView> creators)
        {
            if (creators == null || creators.Count == 0)
            {
                return Empty("No creator is recorded against anything in the index.");
            }

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>");
            builder.Append(Html.Head("Creator"));
            builder.Append(Html.Head("Identifier"));
            builder.Append(Html.Head("Models", "col-number"));
            builder.Append(Html.Head("With script", "col-number"));
            builder.Append(Html.Head("Detections", "col-number"));
            builder.Append("</tr></thead><tbody>");

            foreach (CreatorView creator in creators)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column(Html.Cell(creator.CreatorName)));
                builder.Append(Html.Column(Html.Value(creator.CreatorId)));
                builder.Append(Html.Column(CreatorLink(creator), "col-number"));
                builder.Append(Html.Column(Html.Number(creator.ModelsWithScripts), "col-number"));
                builder.Append(Html.Column(Html.Number(creator.Detections), "col-number"));
                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders a creator's model count as a link into the catalogue.
        /// </summary>
        /// <param name="creator">The creator.</param>
        /// <returns>The markup.</returns>
        private static string CreatorLink(CreatorView creator)
        {
            if (string.IsNullOrWhiteSpace(creator.CreatorId)) return Html.Number(creator.Models);

            return Html.Link(
                CatalogueAddress + "?q=" + Uri.EscapeDataString(creator.CreatorId),
                Html.Number(creator.Models),
                "Show every model recorded against this creator");
        }

        /// <summary>
        /// Renders a share bar.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="largest">The largest value in the same table.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The width is one of eleven classes rather than a style attribute, because the policy these pages are
        /// served under permits styles from this origin only and an inline width would be refused without saying
        /// so. A zero still draws an empty track, which is what makes the shape of a table readable at a glance
        /// and what makes an all-zero column obviously all-zero.
        /// </remarks>
        private static string Bar(int value, int largest)
        {
            int tenths = 0;

            if (largest > 0 && value > 0)
            {
                tenths = (int)Math.Round((value * 10.0) / largest, MidpointRounding.AwayFromZero);

                if (tenths < 1) tenths = 1;
                if (tenths > 10) tenths = 10;
            }

            return "<span class=\"bar share-" + tenths.ToString(CultureInfo.InvariantCulture) + "\" title=\""
                   + Html.Attribute(Html.Number(value) + " against the largest entry, " + Html.Number(largest))
                   + "\"></span>";
        }

        /// <summary>
        /// Renders one model's facts.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The markup.</returns>
        public static string ModelFacts(ModelRow model)
        {
            if (model == null) return string.Empty;

            string identifier = model.Model.ToString(CultureInfo.InvariantCulture);

            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"detail-facts\">");

            builder.Append(Html.Field("Identifier", Html.Value(identifier)));
            builder.Append(Html.Field("Recorded", Html.Instant(ToOffset(model.Date))));

            builder.Append(Html.Field(
                "Name",
                string.IsNullOrWhiteSpace(model.Name)
                    ? "<span class=\"muted\" title=\"The scanner had not started reading names when it captured "
                      + "this model, or Roblox no longer serves one.\">&mdash;</span>"
                    : Html.Encode(model.Name)));

            builder.Append(Html.Field("Creator", Creator(model)));

            builder.Append(Html.Field(
                "Script",
                model.HasScripts
                    ? "<span class=\"label label-info\">contains script</span>"
                    : "<span class=\"label label-default\">no script</span>"));

            builder.Append(Html.Field("Score", Score(model.Score)));

            builder.Append(Html.Field(
                "Detections",
                "<a href=\"" + Html.Attribute(CatalogueAddress + "?q=" + Uri.EscapeDataString(identifier))
                + "\">Show this model in the catalogue</a>"));

            builder.Append(Html.Field("Store page", StoreLink(model)));
            builder.Append(Html.Field("Preview", PreviewLink(model)));

            builder.Append(Html.Field(
                "Download",
                Html.Link(
                    "/Api/Download.ashx?id=" + identifier,
                    "download the model",
                    "The file is fetched through the API and saved under the asset's own identifier with an "
                    + "extension for the kind of content it is. Roblox's signed address is used on the way and "
                    + "expires shortly afterwards, so it is never stored and never shown here.")));

            builder.Append("</div>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders a model's description.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The markup.</returns>
        public static string ModelDescription(ModelRow model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Description))
            {
                return Empty("The scanner recorded no description for this model.");
            }

            return "<p class=\"wrap-anywhere\">" + Html.Encode(model.Description) + "</p>";
        }

        /// <summary>
        /// Renders the link to a model's store page.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The markup.</returns>
        private static string StoreLink(ModelRow model)
        {
            return ExternalLink(
                model == null ? null : model.AssetUrl,
                "the model on Roblox",
                "The asset's own page on Roblox.");
        }

        /// <summary>
        /// Renders the link to Roblox's rendered image of a model.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A link and not an embedded image. An <c>img</c> pointing at Roblox's image host would make every
        /// reader of every model page announce to that host which asset they are looking at, and it would widen
        /// the policy these pages are served under for a picture that says nothing an operator needs. Following
        /// the link is a deliberate act and is the reader's to make.
        /// </remarks>
        private static string PreviewLink(ModelRow model)
        {
            return ExternalLink(
                model == null ? null : model.Thumbnail,
                "the asset image",
                "Roblox's rendered image of the asset, on Roblox's own image host. It is not embedded in this "
                + "page.");
        }

        /// <summary>
        /// Renders a destination written by somebody else, as a link only when it may be one.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <param name="text">The link's text.</param>
        /// <param name="title">The tooltip.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// An address that is not a Roblox address over HTTPS is shown as text instead. The column it comes from
        /// is filled from an upstream answer, and rendering it as a link would make a page a way of turning
        /// whatever that answer contained into a navigation.
        /// </remarks>
        private static string ExternalLink(string address, string text, string title)
        {
            if (string.IsNullOrWhiteSpace(address)) return "<span class=\"muted\">&mdash;</span>";

            string trimmed = address.Trim();

            if (!IsRobloxAddress(trimmed))
            {
                return "<span class=\"code wrap-anywhere\">" + Html.Encode(trimmed) + "</span>";
            }

            return "<a href=\"" + Html.Attribute(trimmed) + "\" rel=\"noreferrer noopener\" target=\"_blank\" "
                   + "title=\"" + Html.Attribute(title) + "\">" + Html.Encode(text) + "</a>";
        }

        /// <summary>
        /// The hosts a value from the catalogue may point at.
        /// </summary>
        /// <remarks>
        /// Both are Roblox's own names. They are listed as roots and matched against the whole host, so the
        /// subdomains Roblox actually uses are accepted and a name that merely ends with one of these strings
        /// is not.
        /// </remarks>
        private static readonly string[] PermittedHosts = { "roblox.com", "rbxcdn.com" };

        /// <summary>
        /// Decides whether an address may be used as a destination.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns><see langword="true"/> when it may.</returns>
        /// <remarks>
        /// <para>
        /// One implementation, called by the page that renders a link. It used to be called by the download
        /// handler as well, back when the handler sent a browser to the resolved address; it no longer does,
        /// because the bytes are read through the API instead. The check is kept here because a column of
        /// upstream addresses is still rendered as links, and the day a second caller needs it, it is one
        /// implementation rather than two that agree until they do not.
        /// </para>
        /// <para>
        /// Only HTTPS, and only without credentials. A URL that carries a user name would send it to the host
        /// named after the <c>@</c>, which need not be the host that was checked.
        /// </para>
        /// </remarks>
        public static bool IsRobloxAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;

            Uri parsed;

            if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out parsed)) return false;
            if (parsed.Scheme != Uri.UriSchemeHttps) return false;
            if (!string.IsNullOrEmpty(parsed.UserInfo)) return false;

            string host = parsed.Host;

            if (host.Length == 0) return false;

            foreach (string permitted in PermittedHosts)
            {
                if (string.Equals(host, permitted, StringComparison.OrdinalIgnoreCase)) return true;

                if (host.Length > permitted.Length + 1
                    && host.EndsWith("." + permitted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Renders the rules a model matched, with what each one means.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="rules">The rule reference.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The stored labels are attributed against the reference, so a row written before a rule was renamed
        /// still reads as the rule it means. A label the reference does not name is shown as such rather than
        /// dropped: a model that matched something the API no longer publishes is a fact about the model, and
        /// hiding it would leave an operator looking at a score with nothing to explain it.
        /// </remarks>
        public static string ModelRules(ModelRow model, List<RuleStatisticView> rules)
        {
            List<string> known = new List<string>();

            if (rules != null)
            {
                foreach (RuleStatisticView rule in rules)
                {
                    if (!string.IsNullOrWhiteSpace(rule.Tag)) known.Add(rule.Tag);
                }
            }

            IReadOnlyList<string> labels = TagList.Parse(model == null ? null : model.Tags, known);

            if (labels.Count == 0)
            {
                return Empty("No detection rule matched this model.");
            }

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>");
            builder.Append("<th>Rule</th><th>Category</th><th>Severity</th>");
            builder.Append("<th class=\"col-number\">Score</th><th>What it is for</th>");
            builder.Append("</tr></thead><tbody>");

            foreach (string label in labels)
            {
                RuleStatisticView rule = Find(rules, label);

                builder.Append("<tr>");
                builder.Append(Html.Column(Html.Badge(label, TagClass(label), null)));

                if (rule == null)
                {
                    builder.Append(Html.Column("<span class=\"muted\" title=\"The API publishes no rule with this "
                                               + "label. It was written by a revision of the scanner whose rule "
                                               + "set has since changed.\">&mdash;</span>"));
                    builder.Append(Html.Column("<span class=\"muted\">&mdash;</span>"));
                    builder.Append(Html.Column("<span class=\"muted\">&mdash;</span>", "col-number"));
                    builder.Append(Html.Column("<span class=\"muted\">This label is no longer in the scanner's "
                                               + "registry, so nothing describes what it meant.</span>"));
                }
                else
                {
                    builder.Append(Html.Column(Html.Badge(
                        CategoryName(rule.Category),
                        CategoryClass(rule.Category),
                        CategoryTitle(CategoryName(rule.Category)))));
                    builder.Append(Html.Column(SeverityBadge(rule.Severity)));
                    builder.Append(Html.Column(Html.Encode(Html.Score(rule.Score)), "col-number"));
                    builder.Append(Html.Column(Purpose(rule)));
                }

                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Finds one model on a page of the catalogue.
        /// </summary>
        /// <param name="page">The page of models.</param>
        /// <param name="model">The asset identifier.</param>
        /// <returns>The row, or <see langword="null"/> when the page does not hold it.</returns>
        /// <remarks>
        /// The API publishes a search and not a lookup by identifier, so a page that wants one model asks for the
        /// identifier and picks the row out. The identifier is matched exactly rather than taken as the first
        /// result: the search is a substring match across several columns, so asking for 12345 can also answer
        /// with 912345 and with a model whose description mentions it, and presenting the first of those as the
        /// model somebody asked for would be wrong about which asset they are looking at.
        /// </remarks>
        public static ModelRow Find(ModelPage page, long model)
        {
            if (page == null || page.Items == null) return null;

            foreach (ModelRow row in page.Items)
            {
                if (row.Model == model) return row;
            }

            return null;
        }

        /// <summary>
        /// Finds the rule reference entry for a label.
        /// </summary>
        /// <param name="rules">The reference.</param>
        /// <param name="label">The label.</param>
        /// <returns>The entry, or <see langword="null"/> when the reference does not name it.</returns>
        private static RuleStatisticView Find(List<RuleStatisticView> rules, string label)
        {
            if (rules == null) return null;

            foreach (RuleStatisticView rule in rules)
            {
                if (string.Equals(rule.Tag, label, StringComparison.Ordinal)) return rule;
            }

            return null;
        }

        /// <summary>
        /// Renders a severity as a label whose colour matches it.
        /// </summary>
        /// <param name="severity">The severity's name, as the API sent it.</param>
        /// <returns>The markup.</returns>
        public static string SeverityBadge(string severity)
        {
            return Html.Badge(SeverityName(severity), SeverityClass(severity), SeverityTitle(severity));
        }

        /// <summary>
        /// Names a severity.
        /// </summary>
        /// <param name="severity">The severity's name, as the API sent it.</param>
        /// <returns>The name, spaced for reading.</returns>
        /// <remarks>
        /// The API publishes its enumerations by name -- it registers the string converter -- so the name is
        /// what arrives and there is no numbering to keep in step on this side. What is left here is the
        /// spacing: an enumeration member cannot contain a space, so <c>VirtualMachine</c> is how the value
        /// travels and "Virtual Machine" is how it is shown.
        /// </remarks>
        public static string SeverityName(string severity)
        {
            return string.IsNullOrWhiteSpace(severity) ? "Unclassified" : Display(severity);
        }

        /// <summary>
        /// Names the colour a severity is drawn in.
        /// </summary>
        /// <param name="severity">The severity's name, as the API sent it.</param>
        /// <returns>The class suffix.</returns>
        /// <remarks>
        /// An unrecognised name is drawn plainly rather than guessed at. A rule added to the scanner with a
        /// severity this site has never seen should appear as an unremarkable label, not as a critical one,
        /// and the alternative -- folding anything unknown into the most alarming colour -- would make the
        /// page cry wolf the first time the scanner grew.
        /// </remarks>
        private static string SeverityClass(string severity)
        {
            switch (Normalise(severity))
            {
                case "informational": return "default";
                case "low": return "info";
                case "medium": return "warning";
                case "high": return "danger";
                case "critical": return "danger";
                default: return "default";
            }
        }

        /// <summary>
        /// Describes what a severity means.
        /// </summary>
        /// <param name="severity">The severity's name, as the API sent it.</param>
        /// <returns>The sentence.</returns>
        private static string SeverityTitle(string severity)
        {
            switch (Normalise(severity))
            {
                case "informational": return "A watermark or a signature from a trusted author. It says who made the model, not that the model is dangerous.";
                case "low": return "A weak signal, useful only in aggregate.";
                case "medium": return "A meaningful signal that should be read in context.";
                case "high": return "A strong signal that usually indicates hostile intent.";
                case "critical": return "A definitive match for a known, actively abused tool.";
                default: return "This rule has not been classified.";
            }
        }

        /// <summary>
        /// Names a rule category.
        /// </summary>
        /// <param name="category">The category's name, as the API sent it.</param>
        /// <returns>The name, spaced for reading.</returns>
        public static string CategoryName(string category)
        {
            return string.IsNullOrWhiteSpace(category) ? "Unknown" : Display(category);
        }

        /// <summary>
        /// Names the colour a category is drawn in.
        /// </summary>
        /// <param name="category">The category's name, as the API sent it.</param>
        /// <returns>The class suffix.</returns>
        /// <remarks>
        /// One mapping for both shapes the category arrives in. The roll-up and the per-rule breakdown carry
        /// the same name, so the same rule cannot end up coloured two ways depending on which panel it is
        /// being read in.
        /// </remarks>
        private static string CategoryClass(string category)
        {
            switch (Normalise(category))
            {
                case "httpservice": return "info";
                case "webhook": return "danger";
                case "concurrency": return "warning";
                case "virtualmachine": return "danger";
                case "compiler": return "warning";
                case "obfuscator": return "danger";
                case "heuristic": return "warning";
                case "whitelist": return "success";
                default: return "default";
            }
        }

        /// <summary>
        /// Describes what a category covers.
        /// </summary>
        /// <param name="category">The category's name, as the API sent it.</param>
        /// <returns>The sentence.</returns>
        private static string CategoryTitle(string category)
        {
            switch (Normalise(category))
            {
                case "httpservice": return "Outbound requests made through Roblox's own HTTP service.";
                case "webhook": return "An endpoint used to ship data off the platform, such as a Discord webhook.";
                case "concurrency": return "Thread scheduling: parallel Luau, Actors, and explicit task synchronisation.";
                case "virtualmachine": return "A custom virtual machine that executes bytecode hidden inside the model.";
                case "compiler": return "A compiler or loader for a scripting language other than Luau.";
                case "obfuscator": return "A known obfuscator's signature.";
                case "heuristic": return "A structural hint rather than a signature: the shape of the code itself.";
                case "whitelist": return "A marker left by a trusted author or signer.";
                default: return "This category is not one this site knows about.";
            }
        }

        /// <summary>
        /// Folds a value from the wire into the form the comparisons above are written in.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The value, lower-cased and trimmed, with no spaces or punctuation.</returns>
        /// <remarks>
        /// Case is not folded with <c>ToLowerInvariant</c> alone: the value comes from the API's enumeration
        /// names, and the point of folding here is that a single spelling of "HttpService" is matched by every
        /// comparison above regardless of how it was capitalised on the way. Digits and underscores are kept,
        /// because a future member could legitimately contain them.
        /// </remarks>
        private static string Normalise(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string trimmed = value.Trim();
            StringBuilder builder = new StringBuilder(trimmed.Length);

            foreach (char character in trimmed)
            {
                if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Puts the spaces into an enumeration name.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The name, spaced.</returns>
        private static string Display(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";

            StringBuilder builder = new StringBuilder(name.Length + 4);

            for (int index = 0; index < name.Length; index++)
            {
                char character = name[index];

                if (index > 0 && char.IsUpper(character)) builder.Append(' ');

                builder.Append(character);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Reads the timestamp text the API sent as an instant.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The instant, or <see langword="null"/> when there is not one.</returns>
        /// <remarks>
        /// The API sends an offset and it is honoured. A value that carries no offset at all is read as UTC
        /// rather than as local time, because every timestamp in this catalogue was written by a scanner working
        /// in UTC, and reading one as local time would shift every row by the machine's offset.
        /// </remarks>
        public static DateTimeOffset? ToOffset(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            DateTimeOffset moment;

            if (DateTimeOffset.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out moment))
            {
                return moment;
            }

            return null;
        }
    }
}
