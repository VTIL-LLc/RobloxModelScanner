using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site
{
    /// <summary>
    /// One run: what it was asked to do, how it ended, and what it said while it was doing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the page an operator ends up on when something has gone wrong, and the log is the reason they
    /// came. The log is therefore rendered as the page loads rather than fetched by script, so that a reader
    /// with script disabled -- or a reader whose script has been refused by the policy -- still sees it.
    /// </para>
    /// <para>
    /// While the run is still going the log follows it. The offset the page starts from is the end of what it
    /// rendered, so the script asks for what has been written since rather than for the whole file again, and
    /// the reader's scroll position is left alone.
    /// </para>
    /// <para>
    /// A run that has finished is not followed, because there is nothing to follow and a request every fifteen
    /// seconds that can only ever return nothing is a request nobody needs to pay for.
    /// </para>
    /// </remarks>
    public class Run : DevopsPage
    {
        /// <summary>
        /// How much of a finished run's log is rendered into the page.
        /// </summary>
        /// <remarks>
        /// A build log can be megabytes. The tail is what matters -- the failure is at the end -- and the rest
        /// is one click away on the log viewer's own control.
        /// </remarks>
        private const int TailBytes = 128 * 1024;

        /// <summary>
        /// How much of a running run's log is rendered into the page.
        /// </summary>
        private const int LiveTailBytes = 64 * 1024;

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return RunId.Length == 0 ? "Run — " + Settings.Title : RunId + " — " + Settings.Title; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (ServiceName.Length == 0 || RunId.Length == 0)
            {
                WriteDocument(
                    PageTitle,
                    Navigation(),
                    Html.Heading("Run", null)
                    + Html.Alert(
                        "warning",
                        "No run was named.",
                        "A run is identified by the service it belongs to and its own identifier, and this address "
                        + "carried neither. Open it from the history page.")
                    + "<p>" + Html.Link("/History.aspx", "Back to history") + "</p>");

                return;
            }

            ServiceResult<RunDetail> run = LoadRun(ServiceName, RunId);

            if (!run.IsSuccess)
            {
                WriteDocument(
                    PageTitle,
                    Navigation(),
                    Html.Heading(ServiceName + " / " + RunId, null)
                    + RenderFailure(run)
                    + "<p>" + Html.Link(
                        "/History.aspx?service=" + Uri.EscapeDataString(ServiceName),
                        "Back to this service's history") + "</p>");

                return;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                ServiceName + " / " + RunId,
                "One execution of the service. Everything below is recorded in the run store under the log root."));

            builder.Append(Html.Panel(
                "The run",
                Live(
                    "run-summary",
                    "run",
                    "name=" + Uri.EscapeDataString(ServiceName) + "&run=" + Uri.EscapeDataString(RunId),
                    DevopsViews.RunFacts(run.Value))));

            builder.Append(RenderLog(run.Value));

            WriteDocument(PageTitle, Navigation(), builder.ToString());
        }

        /// <summary>
        /// Renders the log viewer.
        /// </summary>
        /// <param name="run">The run.</param>
        /// <returns>The markup.</returns>
        private string RenderLog(RunDetail run)
        {
            bool running = run.Summary != null && !run.Summary.IsComplete;

            ServiceResult<LogSlice> slice = LoadLog(
                ServiceName,
                RunId,
                offset: 0,
                count: running ? LiveTailBytes : TailBytes,
                tail: true);

            if (!slice.IsSuccess)
            {
                return Html.Panel("Log", RenderFailure(slice));
            }

            LogSlice value = slice.Value;

            long read = value.Offset + value.Length;

            StringBuilder builder = new StringBuilder();

            if (value.TotalBytes == 0)
            {
                builder.Append(Empty("Nothing has been written to this run's log."));
            }
            else
            {
                if (value.Offset > 0)
                {
                    // The reader is told that what they are looking at does not start at the beginning.
                    // Rendering a tail without saying so is how a log gets read as if the first line were the
                    // first thing the program said.
                    builder.Append("<p class=\"text-muted log-note\">")
                           .Append(Html.Encode("Showing the last " + Html.Bytes(value.Length) + " of " + Html.Bytes(value.TotalBytes) + "."))
                           .Append("</p>");
                }

                builder.Append("<pre id=\"run-log\" class=\"log-view\" data-log-url=\"")
                       .Append(Html.Attribute(
                           "/Api/Log.ashx?name=" + Uri.EscapeDataString(ServiceName)
                           + "&run=" + Uri.EscapeDataString(RunId)))
                       .Append("\" data-offset=\"").Append(read.ToString(CultureInfo.InvariantCulture)).Append('"')
                       .Append(running ? " data-live=\"true\"" : string.Empty)
                       .Append('>')
                       .Append(Html.Encode(value.Text))
                       .Append("</pre>");
            }

            return Html.Panel(
                running ? "Log (following)" : "Log",
                builder.ToString()
                + "<p class=\"text-muted log-note\">"
                + Html.Link("/History.aspx?service=" + Uri.EscapeDataString(ServiceName), "This service's runs")
                + "</p>");
        }
    }
}
