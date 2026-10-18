using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web;

using RMS.Site.Common.Implementation;

namespace RMS.Devops.Site.Infrastructure
{
    /// <summary>
    /// Renders every table, badge and panel this site shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One renderer, not two. Every region that refreshes itself is built by the same method that built it the
    /// first time, so a table cannot come out differently depending on whether it was rendered into the page or
    /// fetched afterwards, and a column added in one place cannot be missing in the other. That is the reason
    /// the fragment handlers answer with markup rather than with data: the alternative is a JavaScript template
    /// that has to be kept in step with a C# one by hand, and the two always drift.
    /// </para>
    /// <para>
    /// Every value that came from a file, a service or an operator passes through <see cref="Html"/>, which
    /// encodes. The only strings inserted unencoded are the ones this class built, and they are built from
    /// encoded pieces.
    /// </para>
    /// </remarks>
    public static class DevopsViews
    {
        /// <summary>
        /// The class a state's badge is drawn with.
        /// </summary>
        /// <param name="state">The state, as the service named it.</param>
        /// <returns>The Bootstrap label suffix.</returns>
        /// <remarks>
        /// The mapping is by name rather than by position so that a state the service adds later is drawn in
        /// grey rather than as the wrong colour. Grey is the honest answer to a word this site does not know;
        /// green would be a claim about a machine nobody made.
        /// </remarks>
        public static string StateClass(string state)
        {
            switch (Normalise(state))
            {
                case "running": return "info";
                case "healthy": return "success";
                case "failed": return "danger";
                case "abandoned": return "warning";
                case "stopped": return "default";
                case "idle": return "default";
                default: return "default";
            }
        }

        /// <summary>
        /// The class a run's status badge is drawn with.
        /// </summary>
        /// <param name="status">The status, as the supervisor recorded it.</param>
        /// <returns>The Bootstrap label suffix.</returns>
        public static string StatusClass(string status)
        {
            switch (Normalise(status))
            {
                case "running": return "info";
                case "succeeded": return "success";
                case "failed": return "danger";
                case "abandoned": return "warning";
                case "stopped": return "default";
                default: return "default";
            }
        }

        /// <summary>
        /// Renders a state as a label.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="description">The sentence explaining it.</param>
        /// <returns>The markup.</returns>
        public static string StateBadge(string state, string description)
        {
            if (string.IsNullOrWhiteSpace(state)) return "<span class=\"muted\">&mdash;</span>";

            return Html.Badge(state, StateClass(state), description);
        }

        /// <summary>
        /// Renders a run's status as a label.
        /// </summary>
        /// <param name="status">The status.</param>
        /// <returns>The markup.</returns>
        public static string StatusBadge(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return "<span class=\"muted\">&mdash;</span>";

            return Html.Badge(status, StatusClass(status), null);
        }

        /// <summary>
        /// Renders the header of the overview: the counts, the plan and the machine.
        /// </summary>
        /// <param name="fleet">The fleet summary.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Each count is a panel rather than a sentence so that a reader can find the number they came for
        /// without reading the others. The one that is not zero and should be -- failures, abandoned runs -- is
        /// given the colour that says so without anybody having to decide what "a lot" means.
        /// </remarks>
        public static string FleetHeader(FleetSummary fleet)
        {
            if (fleet == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"row\">");

            builder.Append(Counter("Services", fleet.Services, fleet.Services == 0 ? "warning" : "default",
                fleet.Services == 0 ? "No service plan has been read." : "Services this machine knows about."));
            builder.Append(Counter("Running", fleet.Running, fleet.Running > 0 ? "info" : "default",
                "Services with a running process right now."));
            builder.Append(Counter("Healthy", fleet.Healthy, "success",
                "Services whose most recent run finished well."));
            builder.Append(Counter("Failed", fleet.Failed, fleet.Failed > 0 ? "danger" : "default",
                "Services whose most recent run finished badly."));
            builder.Append(Counter("Idle", fleet.Idle, "default", "Services that have never run."));
            builder.Append(Counter("Artefacts", fleet.Artifacts, "default", "Programs the deployment has published."));

            builder.Append("</div>");

            builder.Append("<div class=\"row\"><div class=\"col-sm-12\">");
            builder.Append(Html.Field("Machine", Html.Value(fleet.Machine)));
            builder.Append(Html.Field("Plan", fleet.PlanFile.Length > 0
                ? Html.Value(fleet.PlanFile)
                : "<span class=\"muted\">&mdash;</span>"));

            if (!fleet.PlanLoaded)
            {
                builder.Append(Html.Field(
                    "Plan state",
                    Html.Alert("warning", "The plan has not been read.", fleet.PlanDetail)));
            }

            builder.Append(Html.Field("Log root", fleet.LogRoot.Length > 0
                ? Html.Value(fleet.LogRoot)
                : "<span class=\"muted\">&mdash;</span>"));
            builder.Append(Html.Field("Answer as at", Html.Timestamp(fleet.ReportedUtc)));
            builder.Append("</div></div>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders one counter panel.
        /// </summary>
        /// <param name="label">The counter's label.</param>
        /// <param name="value">The number.</param>
        /// <param name="tone">The tone: <c>default</c>, <c>info</c>, <c>success</c>, <c>warning</c> or <c>danger</c>.</param>
        /// <param name="title">The sentence explaining what is counted.</param>
        /// <returns>The markup.</returns>
        private static string Counter(string label, int value, string tone, string title)
        {
            return "<div class=\"col-sm-2\">"
                   + "<div class=\"panel panel-primary counter-panel counter-" + Html.Attribute(tone) + "\" title=\""
                   + Html.Attribute(title) + "\">"
                   + "<div class=\"panel-body\">"
                   + "<div class=\"counter-value\">" + value.ToString("N0", CultureInfo.InvariantCulture) + "</div>"
                   + "<div class=\"counter-label\">" + Html.Encode(label) + "</div>"
                   + "</div></div></div>";
        }

        /// <summary>
        /// Renders the service table.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <param name="detailed">Whether to include the columns only the services page has room for.</param>
        /// <returns>The markup.</returns>
        public static string ServiceTable(IEnumerable<ServiceSnapshot> services, bool detailed)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));

            builder.Append("<thead><tr>");
            builder.Append(Html.Head("Service"));
            builder.Append(Html.Head("State"));
            builder.Append(Html.Head("Last run"));
            builder.Append(Html.Head("Status"));
            builder.Append(Html.Head("Finished", "col-time"));
            builder.Append(Html.Head("Runs", "col-number"));

            if (detailed)
            {
                builder.Append(Html.Head("Restart"));
                builder.Append(Html.Head("Executable"));
                builder.Append(Html.Head("Port"));
                builder.Append(Html.Head("Log", "col-number"));
            }

            builder.Append("</tr></thead><tbody>");

            foreach (ServiceSnapshot service in services ?? new List<ServiceSnapshot>())
            {
                builder.Append(ServiceRow(service, detailed));
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders one service row.
        /// </summary>
        /// <param name="service">The service.</param>
        /// <param name="detailed">Whether the wide columns are included.</param>
        /// <returns>The markup.</returns>
        public static string ServiceRow(ServiceSnapshot service, bool detailed)
        {
            if (service == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append("<tr>");

            // The name is the link to everything else about the service, so it is the one column that is never
            // abbreviated. A service that has been removed from the plan is marked here rather than filtered
            // out: its history is still on disk, and this is the page that has to say so.
            builder.Append(Html.Column(
                Html.Link("/Service.aspx?name=" + HttpUtility.UrlEncode(service.Name), service.Name, service.Description)
                + (service.IsInPlan
                    ? string.Empty
                    : " <span class=\"label label-warning\" title=\"The current plan no longer describes this service, but its history is still on disk.\">out of plan</span>")));

            builder.Append(Html.Column(StateBadge(service.State, service.StateDescription)));
            builder.Append(Html.Column(service.LatestRun == null
                ? "<span class=\"muted\">&mdash;</span>"
                : RunLink(service.Name, service.LatestRun, service.LatestRun)));
            builder.Append(Html.Column(StatusBadge(service.LatestStatus)));
            builder.Append(Html.Column(Html.Instant(ToOffset(service.LatestCompleted)), "col-time"));
            builder.Append(Html.Column(Html.Number(service.RunCount), "col-number"));

            if (detailed)
            {
                builder.Append(Html.Column(Html.Cell(service.Restart)));
                builder.Append(Html.Column(Html.Value(service.Executable)));
                builder.Append(Html.Column(Html.Cell(service.Port)));
                builder.Append(Html.Column(Html.Bytes(service.LatestLogBytes), "col-number"));
            }

            builder.Append("</tr>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders a page of runs together with the pager beneath it.
        /// </summary>
        /// <param name="page">The page of runs.</param>
        /// <param name="showService">Whether the service column is included.</param>
        /// <param name="basePath">The page the pager links back to.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The table and its pager are rendered together because they have to agree: a pager built from one
        /// answer and a table built from another is a pager whose links land on pages that do not hold what it
        /// said they would. Keeping them in one method is what makes that impossible rather than unlikely.
        /// </remarks>
        public static string RunList(RunPage page, bool showService, string basePath)
        {
            if (page == null) return "<p class=\"text-muted empty-note\">No runs were reported.</p>";

            string extra = string.IsNullOrWhiteSpace(page.Service)
                ? string.Empty
                : "service=" + Uri.EscapeDataString(page.Service);

            return RunTable(page.Runs, showService) + Pager(basePath, extra, page.Skip, page.Take, page.Total);
        }

        /// <summary>
        /// Renders a page of runs.
        /// </summary>
        /// <param name="runs">The runs.</param>
        /// <param name="showService">Whether the service column is included.</param>
        /// <returns>The markup.</returns>
        public static string RunTable(IEnumerable<RunSnapshot> runs, bool showService)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));

            builder.Append("<thead><tr>");
            builder.Append(Html.Head("Run"));
            if (showService) builder.Append(Html.Head("Service"));
            builder.Append(Html.Head("Status"));
            builder.Append(Html.Head("Created", "col-time"));
            builder.Append(Html.Head("Took", "col-time"));
            builder.Append(Html.Head("Attempts", "col-number"));
            builder.Append(Html.Head("Exit", "col-number"));
            builder.Append(Html.Head("Log", "col-number"));
            builder.Append("</tr></thead><tbody>");

            foreach (RunSnapshot run in runs ?? new List<RunSnapshot>())
            {
                builder.Append(RunRow(run, showService));
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders one run row.
        /// </summary>
        /// <param name="run">The run.</param>
        /// <param name="showService">Whether the service column is included.</param>
        /// <returns>The markup.</returns>
        public static string RunRow(RunSnapshot run, bool showService)
        {
            if (run == null) return string.Empty;

            StringBuilder builder = new StringBuilder();

            builder.Append("<tr>");
            builder.Append(Html.Column(RunLink(run.Service, run.Run, run.Run)));

            if (showService)
            {
                builder.Append(Html.Column(
                    Html.Link("/Service.aspx?name=" + HttpUtility.UrlEncode(run.Service), run.Service)));
            }

            builder.Append(Html.Column(StatusBadge(run.Status)));
            builder.Append(Html.Column(Html.Instant(ToOffset(run.Created)), "col-time"));
            builder.Append(Html.Column(Html.Cell(run.RuntimeText), "col-time"));

            // An attempt count above one is the interesting case, so it is marked rather than merely shown.
            // A run that had to be started twice is a fact about the machine that the exit code does not carry.
            builder.Append(Html.Column(
                run.Attempts > 1
                    ? "<span class=\"label label-warning\" title=\"The process had to be started more than once.\">"
                      + Html.Encode(run.Attempts.ToString(CultureInfo.InvariantCulture)) + "</span>"
                    : Html.Encode(run.Attempts.ToString(CultureInfo.InvariantCulture)),
                "col-number"));

            builder.Append(Html.Column(
                run.ExitCode.HasValue
                    ? Html.Encode(run.ExitCode.Value.ToString(CultureInfo.InvariantCulture))
                    : "<span class=\"muted\">&mdash;</span>",
                "col-number"));

            builder.Append(Html.Column(Html.Bytes(run.LogBytes), "col-number"));
            builder.Append("</tr>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the pager under a list of runs.
        /// </summary>
        /// <param name="basePath">The page the pager links to.</param>
        /// <param name="extraQuery">Extra query parameters to preserve, already encoded.</param>
        /// <param name="skip">How many rows were skipped.</param>
        /// <param name="take">How many rows the page holds.</param>
        /// <param name="total">How many rows exist.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A pager and not an infinite scroll. The lists here are read by somebody looking for a particular run
        /// -- the one that failed, the one that changed something -- and a reader who cannot get to the end
        /// cannot answer the question they arrived with.
        /// </remarks>
        public static string Pager(string basePath, string extraQuery, int skip, int take, int total)
        {
            if (total <= take) return string.Empty;

            int pages = (total + take - 1) / take;
            int current = skip / take + 1;

            StringBuilder builder = new StringBuilder();

            builder.Append("<ul class=\"pagination pagination-sm pager-strip\">");

            builder.Append(PageLink(basePath, extraQuery, "&laquo; Previous", Math.Max(0, skip - take), take, current <= 1));

            for (int page = 1; page <= pages; page++)
            {
                // The window is bounded so that a catalogue with thousands of runs does not turn its pager into
                // a second list. The first and last pages are always offered, because they are the two an
                // operator actually asks for.
                bool edge = page == 1 || page == pages;
                bool near = Math.Abs(page - current) <= 2;

                if (!edge && !near)
                {
                    if (Math.Abs(page - current) == 3) builder.Append("<li class=\"disabled\"><span>&hellip;</span></li>");
                    continue;
                }

                builder.Append(PageLink(
                    basePath,
                    extraQuery,
                    page.ToString(CultureInfo.InvariantCulture),
                    (page - 1) * take,
                    take,
                    false,
                    page == current));
            }

            builder.Append(PageLink(
                basePath,
                extraQuery,
                "Next &raquo;",
                skip + take,
                take,
                current >= pages));

            builder.Append("</ul>");

            builder.Append("<p class=\"text-muted pager-summary\">Showing ")
                   .Append(Html.Number(Math.Min(skip + 1, total)))
                   .Append(" to ").Append(Html.Number(Math.Min(skip + take, total)))
                   .Append(" of ").Append(Html.Number(total)).Append(".</p>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders one pager entry.
        /// </summary>
        /// <param name="basePath">The page the pager links to.</param>
        /// <param name="extraQuery">Extra query parameters to preserve.</param>
        /// <param name="label">The entry's text.</param>
        /// <param name="skip">The offset the entry links to.</param>
        /// <param name="take">The page size to preserve.</param>
        /// <param name="disabled">Whether the entry is inert.</param>
        /// <param name="active">Whether the entry is the current page.</param>
        /// <returns>The markup.</returns>
        private static string PageLink(
            string basePath,
            string extraQuery,
            string label,
            int skip,
            int take,
            bool disabled,
            bool active = false)
        {
            if (disabled)
            {
                return "<li class=\"disabled\"><span>" + label + "</span></li>";
            }

            if (active)
            {
                return "<li class=\"active\"><span>" + label + "</span></li>";
            }

            string address = basePath + "?" + extraQuery
                             + (extraQuery.Length > 0 ? "&" : string.Empty)
                             + "skip=" + skip.ToString(CultureInfo.InvariantCulture)
                             + "&take=" + take.ToString(CultureInfo.InvariantCulture);

            return "<li>" + Html.Link(address, HttpUtility.HtmlDecode(label)) + "</li>";
        }

        /// <summary>
        /// Renders the catalogue summary.
        /// </summary>
        /// <param name="catalog">The summary.</param>
        /// <returns>The markup.</returns>
        public static string CatalogPanel(CatalogSummary catalog)
        {
            if (catalog == null) return string.Empty;

            if (!catalog.Available)
            {
                return Html.Alert(
                    "info",
                    "The catalogue index has not been read.",
                    string.IsNullOrWhiteSpace(catalog.Detail)
                        ? "No scanner index is configured on this machine."
                        : catalog.Detail);
            }

            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"row\">");
            builder.Append(Counter("Models", (int)Math.Min(catalog.Models, int.MaxValue), "default", "Rows the catalogue holds."));
            builder.Append(Counter("With scripts", (int)Math.Min(catalog.ModelsWithScripts, int.MaxValue), "info", "Rows whose scripts were found."));
            builder.Append(Counter("With detections", (int)Math.Min(catalog.ModelsWithDetections, int.MaxValue), "warning", "Rows that matched at least one rule."));
            builder.Append(Counter("Matches", (int)Math.Min(catalog.Detections, int.MaxValue), "default", "Detection rows in total."));
            builder.Append(Counter("Highest score", (int)Math.Min(catalog.MaximumScore, int.MaxValue), "default", "The highest score any single row reached."));
            builder.Append(Counter("Total score", (int)Math.Min(catalog.TotalScore, int.MaxValue), "default", "Every score added together."));
            builder.Append("</div>");

            builder.Append("<div class=\"row\"><div class=\"col-sm-12\">");
            builder.Append(Html.Field("Index", Html.Value(catalog.Path)));
            builder.Append(Html.Field("Built", Html.Timestamp(catalog.IndexedUtc)));
            builder.Append(Html.Field("Catalogue revision", Html.Encode(catalog.SourceRevision.ToString(CultureInfo.InvariantCulture))));
            builder.Append(Html.Field("Schema", Html.Encode(catalog.SchemaVersion.ToString(CultureInfo.InvariantCulture))));
            builder.Append("</div></div>");

            builder.Append(DetectionTable(catalog.TopDetections));
            builder.Append(PeriodTable(catalog.Periods));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the most frequent detections.
        /// </summary>
        /// <param name="detections">The detections.</param>
        /// <returns>The markup.</returns>
        public static string DetectionTable(IEnumerable<TagStatistic> detections)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<h4>Most frequent detections</h4>");
            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Detection"))
                   .Append(Html.Head("Matches", "col-number"))
                   .Append(Html.Head("Category", "col-number"))
                   .Append("</tr></thead><tbody>");

            bool any = false;

            foreach (TagStatistic detection in detections ?? new List<TagStatistic>())
            {
                any = true;

                builder.Append("<tr>")
                       .Append(Html.Column(Html.Encode(detection.Tag)))
                       .Append(Html.Column(Html.Number(detection.Matches), "col-number"))
                       .Append(Html.Column(Html.Encode(detection.Category.ToString(CultureInfo.InvariantCulture)), "col-number"))
                       .Append("</tr>");
            }

            if (!any)
            {
                builder.Append("<tr><td colspan=\"3\" class=\"muted\">Nothing has matched yet.</td></tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders how the catalogue is distributed over time.
        /// </summary>
        /// <param name="periods">The periods.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Each row carries a bar whose width is the share of the largest period, which is what makes the shape
        /// of the distribution readable without a charting dependency. The width is expressed as one of eleven
        /// classes rather than as an inline style, because these pages are served under a policy that refuses
        /// inline styles -- and a bar that is refused is a bar that silently does not appear.
        /// </remarks>
        public static string PeriodTable(IEnumerable<PeriodStatistic> periods)
        {
            List<PeriodStatistic> list = new List<PeriodStatistic>(periods ?? new List<PeriodStatistic>());

            if (list.Count == 0) return string.Empty;

            long largest = 0;

            foreach (PeriodStatistic period in list)
            {
                if (period.Models > largest) largest = period.Models;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append("<h4>When the catalogue was published</h4>");
            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Period"))
                   .Append(Html.Head("Models", "col-number"))
                   .Append(Html.Head("Share"))
                   .Append("</tr></thead><tbody>");

            foreach (PeriodStatistic period in list)
            {
                builder.Append("<tr>")
                       .Append(Html.Column(Html.Encode(period.Period)))
                       .Append(Html.Column(Html.Number(period.Models), "col-number"))
                       .Append(Html.Column(
                           "<span class=\"bar share-" + Share(period.Models, largest).ToString(CultureInfo.InvariantCulture) + "\"></span>"))
                       .Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Expresses a count as one of eleven steps of the largest count.
        /// </summary>
        /// <param name="value">The count.</param>
        /// <param name="largest">The largest count in the same set.</param>
        /// <returns>The step, from zero to ten.</returns>
        /// <remarks>
        /// Eleven steps rather than a hundred, because a bar whose length is finer than the eye can compare is
        /// a bar that claims a precision the reader cannot use -- and because eleven classes are a length
        /// somebody can count, where a hundred are a generated block of rules nobody reads.
        /// </remarks>
        private static int Share(long value, long largest)
        {
            if (largest <= 0 || value <= 0) return 0;

            int step = (int)Math.Round(value * 10.0 / largest, MidpointRounding.AwayFromZero);

            return step < 0 ? 0 : step > 10 ? 10 : step;
        }

        /// <summary>
        /// Renders the artefact table.
        /// </summary>
        /// <param name="artifacts">The artefacts.</param>
        /// <returns>The markup.</returns>
        public static string ArtifactTable(IEnumerable<ArtifactSnapshot> artifacts)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Artefact"))
                   .Append(Html.Head("Version"))
                   .Append(Html.Head("Service"))
                   .Append(Html.Head("Files", "col-number"))
                   .Append(Html.Head("Size", "col-number"))
                   .Append(Html.Head("Runs", "col-number"))
                   .Append(Html.Head("Changed", "col-time"))
                   .Append("</tr></thead><tbody>");

            bool any = false;

            foreach (ArtifactSnapshot artifact in artifacts ?? new List<ArtifactSnapshot>())
            {
                any = true;

                builder.Append("<tr>");
                builder.Append(Html.Column(
                    "<span class=\"code\">" + Html.Encode(artifact.Name) + "</span>"
                    + (string.IsNullOrWhiteSpace(artifact.Assembly)
                        ? string.Empty
                        : " <span class=\"text-muted\">" + Html.Encode(artifact.Assembly) + "</span>"),
                    null));

                builder.Append(Html.Column(Html.Cell(
                    string.IsNullOrWhiteSpace(artifact.InformationalVersion)
                        ? artifact.Version
                        : artifact.InformationalVersion)));

                builder.Append(Html.Column(string.IsNullOrWhiteSpace(artifact.ServiceName)
                    ? "<span class=\"muted\" title=\"Nothing in the plan runs out of this directory.\">&mdash;</span>"
                    : Html.Link("/Service.aspx?name=" + HttpUtility.UrlEncode(artifact.ServiceName), artifact.ServiceName)));

                builder.Append(Html.Column(Html.Number(artifact.Files), "col-number"));
                builder.Append(Html.Column(Html.Encode(Html.Bytes(artifact.Bytes)), "col-number"));
                builder.Append(Html.Column(Html.Number(artifact.Runs), "col-number"));
                builder.Append(Html.Column(Html.Instant(ToOffset(artifact.Updated)), "col-time"));
                builder.Append("</tr>");
            }

            if (!any)
            {
                builder.Append("<tr><td colspan=\"7\" class=\"muted\">Nothing has been published under this root.</td></tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// <para>
        /// The region is rendered now and refreshed afterwards, rather than being left empty for the script to
        /// fill. A page whose content arrives only once script has run is a page that shows nothing at all when
        /// script is refused, and the reader is then unable to tell a broken page from a working one that has
        /// nothing to report.
        /// </para>
        /// <para>
        /// The address is this site's own, and the view is named rather than passed through. A region that
        /// carried an arbitrary address would be a way to make the browser fetch something this site did not
        /// choose.
        /// </para>
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
        /// This is here rather than on the page base so that the fragment handlers can render a failure the
        /// same way. A region that failed to refresh and showed a different sentence from the page that failed
        /// to load is a difference somebody would eventually spend an afternoon on.
        /// </para>
        /// </remarks>
        public static string Failure(System.Net.HttpStatusCode status, string error)
        {
            if (status == System.Net.HttpStatusCode.Forbidden || status == System.Net.HttpStatusCode.Unauthorized)
            {
                return Html.Alert(
                    "warning",
                    "The service refused this site.",
                    "The shared key on this site does not match the one on the service, or this site's address is "
                    + "not on the service's allow-list. This is a configuration fault on the machine, not a fault "
                    + "in the page.");
            }

            if (status == System.Net.HttpStatusCode.ServiceUnavailable
                || status == System.Net.HttpStatusCode.BadGateway)
            {
                return Html.Alert(
                    "danger",
                    "The service is not answering.",
                    "Nothing is listening where this site expects the DevOps service. Check that the service is "
                    + "running and that the address in this site's SiteServiceBaseUrl is the one it was given.")
                    /*
                     * The site's own account of the failure follows the general one, because on a deployment that
                     * has just been installed the general sentence points at the wrong machine: the usual cause is a
                     * key file this site was named and cannot read, and this site is the only one that knows which
                     * file that is. The text is encoded rather than trusted, since it can carry a transport's own
                     * message.
                     */
                    + (string.IsNullOrWhiteSpace(error)
                        ? string.Empty
                        : Html.Alert("warning", "What this site found.", Html.Encode(error)));
            }

            return Html.Alert("danger", "The service could not be read.", error);
        }

        /// <summary>
        /// Renders the facts that describe one service.
        /// </summary>
        /// <param name="service">The service.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The order is the order somebody reads them when they have arrived with a problem: what state it is
        /// in, what it last did, what it runs, and then where its files are. The command line is not here at
        /// all -- it is long enough that it belongs on the run page, beside the run it produced.
        /// </remarks>
        public static string ServiceFacts(ServiceSnapshot service)
        {
            if (service == null) return string.Empty;

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("State", StateBadge(service.State, service.StateDescription)),
                new KeyValuePair<string, string>(
                    "In plan",
                    service.IsInPlan
                        ? "<span class=\"label label-success\">yes</span>"
                        : "<span class=\"label label-warning\" title=\"The current plan no longer describes this "
                          + "service. Its history is still on disk, and it will not be started again.\">out of plan</span>"),
                new KeyValuePair<string, string>(
                    "Last run",
                    service.LatestRun == null
                        ? "<span class=\"muted\">never run</span>"
                        : RunLink(service.Name, service.LatestRun, service.LatestRun)
                          + " " + StatusBadge(service.LatestStatus)
                          + " <span class=\"text-muted\">finished "
                          + Html.Encode(Html.Relative(ToOffset(service.LatestCompleted) ?? DateTimeOffset.UtcNow))
                          + "</span>"),
                new KeyValuePair<string, string>("Executable", Html.Value(service.Executable)),
            };

            if (service.IsWebsite)
            {
                pairs.Add(new KeyValuePair<string, string>(
                    "Website",
                    Html.Value(service.Website)
                    + (string.IsNullOrWhiteSpace(service.Port)
                        ? string.Empty
                        : " <span class=\"text-muted\">on port</span> " + Html.Encode(service.Port))));
            }

            pairs.Add(new KeyValuePair<string, string>("Working directory", Html.Value(service.WorkingDirectory)));
            pairs.Add(new KeyValuePair<string, string>("Log directory", Html.Value(service.LogDirectory)));

            // A service that has no executable is one the host does not supervise -- a site IIS owns -- and
            // saying so is more useful than showing an empty restart policy that reads like a mistake.
            pairs.Add(new KeyValuePair<string, string>(
                "Restart",
                string.IsNullOrWhiteSpace(service.Restart)
                    ? "<span class=\"muted\" title=\"This service is not supervised by the host, so it has no "
                      + "restart policy. Whatever owns it decides when it runs.\">supervised elsewhere</span>"
                    : Html.Cell(service.Restart)));

            pairs.Add(new KeyValuePair<string, string>(
                "Retained runs",
                Html.Encode(service.RetainedRuns.ToString(CultureInfo.InvariantCulture))));

            pairs.Add(new KeyValuePair<string, string>(
                "Runs recorded",
                Html.Encode(service.RunCount.ToString("N0", CultureInfo.InvariantCulture))));

            pairs.Add(new KeyValuePair<string, string>("Plan identifier", Html.Value(service.PlanId)));

            return Facts(pairs);
        }

        /// <summary>
        /// Renders the facts that describe one run.
        /// </summary>
        /// <param name="run">The run, with the details only its own page has.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The command line is included here rather than on the service page because it is per run: the plan is
        /// reloaded when it changes, so the command a run was started with is a fact about that run and not
        /// about the service that is described today.
        /// </remarks>
        public static string RunFacts(RunDetail run)
        {
            if (run == null || run.Summary == null) return string.Empty;

            RunSnapshot summary = run.Summary;

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    "Service",
                    Html.Link("/Service.aspx?name=" + HttpUtility.UrlEncode(summary.Service), summary.Service)),
                new KeyValuePair<string, string>("Run", Html.Encode(summary.Run)),
                new KeyValuePair<string, string>(
                    "Sequence",
                    Html.Encode(summary.Sequence.ToString("N0", CultureInfo.InvariantCulture))),
                new KeyValuePair<string, string>("Status", StatusBadge(summary.Status)),
                new KeyValuePair<string, string>("Took", Html.Cell(summary.RuntimeText)),
                new KeyValuePair<string, string>(
                    "Attempts",
                    summary.Attempts > 1
                        ? "<span class=\"label label-warning\" title=\"The process had to be started more than "
                          + "once, which means it exited during start-up and the supervisor retried.\">"
                          + Html.Encode(summary.Attempts.ToString(CultureInfo.InvariantCulture)) + "</span>"
                        : Html.Encode(summary.Attempts.ToString(CultureInfo.InvariantCulture))),
                new KeyValuePair<string, string>(
                    "Exit code",
                    summary.ExitCode.HasValue
                        ? "<span class=\"code\">"
                          + Html.Encode(summary.ExitCode.Value.ToString(CultureInfo.InvariantCulture))
                          + "</span>"
                        : "<span class=\"muted\" title=\"The run has not exited, or it was stopped rather than "
                          + "allowed to finish.\">&mdash;</span>"),
                new KeyValuePair<string, string>("Created", Html.Timestamp(ToOffset(summary.Created))),
                new KeyValuePair<string, string>("Started", Html.Timestamp(ToOffset(summary.Started))),
                new KeyValuePair<string, string>("Completed", Html.Timestamp(ToOffset(summary.Completed))),
                new KeyValuePair<string, string>("Last written", Html.Timestamp(ToOffset(summary.Updated))),
                new KeyValuePair<string, string>(
                    "Process",
                    run.ProcessId.HasValue
                        ? "<span class=\"code\">"
                          + Html.Encode(run.ProcessId.Value.ToString(CultureInfo.InvariantCulture))
                          + "</span> <span class=\"text-muted\" title=\"The process identifier is the one the "
                          + "supervisor recorded when it started the process. It is reused by the operating "
                          + "system, so it identifies this run only for as long as the run lasted.\">as recorded</span>"
                        : "<span class=\"muted\">&mdash;</span>"),
                new KeyValuePair<string, string>("Log file", Html.Value(run.LogFile)),
                new KeyValuePair<string, string>("Log size", Html.Encode(Html.Bytes(summary.LogBytes))),
                new KeyValuePair<string, string>("Command", Command(run.Command)),
            };

            return Facts(pairs);
        }

        /// <summary>
        /// Renders a command line, allowing it to wrap.
        /// </summary>
        /// <param name="command">The command line.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Wrapped rather than shown on one line. A command line for a published build is longer than any
        /// window, and a value that runs off the side is a value the reader has to select and paste elsewhere
        /// to read -- which is exactly the sort of small friction that turns into a habit of not reading it.
        /// </remarks>
        private static string Command(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return "<span class=\"muted\">&mdash;</span>";

            return "<span class=\"code wrap-anywhere\">" + Html.Encode(command) + "</span>";
        }

        /// <summary>
        /// Renders the key and value block that heads a detail page.
        /// </summary>
        /// <param name="pairs">The pairs, in the order they are shown.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Pair order is the caller's. It is not sorted, because the order a detail page presents its facts in
        /// is the order somebody reads them in -- what it is, then what it runs, then where its files are -- and
        /// sorting alphabetically would be arranging them for a machine.
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
        /// Renders an address for a run.
        /// </summary>
        /// <param name="service">The service's name.</param>
        /// <param name="run">The run's identifier.</param>
        /// <param name="text">The link text.</param>
        /// <returns>The markup.</returns>
        public static string RunLink(string service, string run, string text)
        {
            if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(run))
            {
                return Html.Encode(text);
            }

            return Html.Link(
                "/Run.aspx?name=" + HttpUtility.UrlEncode(service) + "&run=" + HttpUtility.UrlEncode(run),
                text);
        }

        /// <summary>
        /// Converts a timestamp read from the service into one this site renders consistently.
        /// </summary>
        /// <param name="value">The timestamp.</param>
        /// <returns>The instant in UTC, or <see langword="null"/>.</returns>
        public static DateTimeOffset? ToOffset(DateTime? value)
        {
            if (!value.HasValue) return null;

            DateTime moment = value.Value;

            // A value with no zone is taken as UTC rather than as local time. The service always writes UTC,
            // so a value that lost its offset is a value whose zone is already known, and treating it as local
            // would shift every timestamp on the page by the server's offset from Greenwich.
            if (moment.Kind == DateTimeKind.Unspecified) moment = DateTime.SpecifyKind(moment, DateTimeKind.Utc);

            return new DateTimeOffset(moment.ToUniversalTime());
        }

        /// <summary>
        /// Normalises a word the service used into the form this class switches on.
        /// </summary>
        /// <param name="value">The word.</param>
        /// <returns>The normalised word.</returns>
        private static string Normalise(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }
    }
}
