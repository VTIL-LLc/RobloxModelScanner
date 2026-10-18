using System;
using System.Globalization;
using System.Text;
using System.Web;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// Renders the fragments every RMS page is made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These helpers exist so that the pages stay readable as markup and the escaping happens in one place.
    /// A page that built its own strings would eventually forget to encode one of them, and the value that
    /// gets forgotten is always the one an outsider controls -- a model name, a service description, a log
    /// line. Encoding is therefore not optional anywhere: every method here that takes text encodes it, and
    /// there is no overload that does not.
    /// </para>
    /// <para>
    /// The markup emitted is Bootstrap 3 with the Slate theme, which is what the interfaces this stack
    /// replaces were built from. Matching it is not nostalgia: an operator moving between the old console and
    /// this one should not have to learn where anything is, and the class names carry the layout rules that
    /// make a dense table readable at a glance.
    /// </para>
    /// <para>
    /// Nothing here emits an inline style attribute or an inline event handler. The pages are served under a
    /// content-security policy that permits scripts and styles from this origin only, so an inline attribute
    /// would be a rule the browser refuses to apply and a feature that silently does not work.
    /// </para>
    /// </remarks>
    public static class Html
    {
        /// <summary>
        /// The Bootstrap alert class each notice severity is rendered with.
        /// </summary>
        private const string AlertPrefix = "alert alert-";

        /// <summary>
        /// Encodes text for insertion into markup.
        /// </summary>
        /// <param name="value">The text.</param>
        /// <returns>The encoded text.</returns>
        public static string Encode(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : HttpUtility.HtmlEncode(value);
        }

        /// <summary>
        /// Encodes text for insertion into a quoted attribute.
        /// </summary>
        /// <param name="value">The text.</param>
        /// <returns>The encoded text.</returns>
        /// <remarks>
        /// Attribute encoding differs from markup encoding in that a single quote and a double quote both have
        /// to be handled, because a page that used one for an attribute and encoded for the other would let a
        /// value escape it. Both are encoded here, so the answer does not depend on which the caller used.
        /// </remarks>
        public static string Attribute(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return HttpUtility.HtmlAttributeEncode(value).Replace("'", "&#39;");
        }

        /// <summary>
        /// Renders an anchor.
        /// </summary>
        /// <param name="href">The destination, relative to this site.</param>
        /// <param name="text">The link text.</param>
        /// <param name="title">The tooltip, when one is wanted.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Only a relative destination is accepted. An absolute URL -- or worse, a scheme such as
        /// <c>javascript:</c> -- would make this a way of turning any value that reaches a page into a
        /// navigation, and the values that reach these pages come from files and services rather than from
        /// this application.
        /// </remarks>
        public static string Link(string href, string text, string title = null)
        {
            string safe = SafeHref(href);

            if (safe.Length == 0) return "<span>" + Encode(text) + "</span>";

            return string.Format(
                CultureInfo.InvariantCulture,
                "<a href=\"{0}\"{1}>{2}</a>",
                Attribute(safe),
                string.IsNullOrEmpty(title) ? string.Empty : " title=\"" + Attribute(title) + "\"",
                Encode(text));
        }

        /// <summary>
        /// Constrains a destination to somewhere inside this application.
        /// </summary>
        /// <param name="href">The requested destination.</param>
        /// <returns>The destination, or an empty string when it is not local.</returns>
        public static string SafeHref(string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return string.Empty;

            string value = href.Trim();

            if (value.StartsWith("//", StringComparison.Ordinal)) return string.Empty;
            if (value.IndexOf(':') >= 0) return string.Empty;
            if (value.StartsWith("/", StringComparison.Ordinal)) return value;

            return value;
        }

        /// <summary>
        /// Renders a value as table cell text, showing a dash when there is nothing.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The markup.</returns>
        public static string Cell(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<span class=\"muted\">&mdash;</span>"
                : Encode(value);
        }

        /// <summary>
        /// Renders a value as table cell text, with a dash that can be hovered for a reason.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="reason">What the dash means.</param>
        /// <returns>The markup.</returns>
        public static string Cell(string value, string reason)
        {
            if (!string.IsNullOrWhiteSpace(value)) return Encode(value);

            return string.IsNullOrWhiteSpace(reason)
                ? "<span class=\"muted\">&mdash;</span>"
                : "<span class=\"muted\" title=\"" + Attribute(reason) + "\">&mdash;</span>";
        }

        /// <summary>
        /// Renders an instant as an absolute moment with the elapsed time beside it, as a tooltip.
        /// </summary>
        /// <param name="value">The instant.</param>
        /// <returns>The markup.</returns>
        public static string Instant(DateTimeOffset? value)
        {
            if (!value.HasValue) return "<span class=\"muted\">&mdash;</span>";

            DateTimeOffset moment = value.Value;

            return string.Format(
                CultureInfo.InvariantCulture,
                "<span title=\"{0}\">{1}</span>",
                Attribute(moment.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)),
                Encode(Relative(moment)));
        }

        /// <summary>
        /// Renders an instant in UTC.
        /// </summary>
        /// <param name="value">The instant.</param>
        /// <returns>The markup.</returns>
        public static string Timestamp(DateTimeOffset? value)
        {
            return value.HasValue
                ? Encode(value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                : "<span class=\"muted\">&mdash;</span>";
        }

        /// <summary>
        /// Describes how long ago an instant was.
        /// </summary>
        /// <param name="value">The instant.</param>
        /// <returns>The description.</returns>
        /// <remarks>
        /// The description is a phrase rather than a count because it is read at a glance: an operator looking
        /// for what changed recently is comparing rows, and "4 minutes ago" sorts itself while "4m 12s" has to
        /// be decoded.
        /// </remarks>
        public static string Relative(DateTimeOffset value)
        {
            TimeSpan age = DateTimeOffset.UtcNow - value.ToUniversalTime();

            if (age < TimeSpan.Zero) return "just now";

            if (age.TotalSeconds < 45) return "just now";
            if (age.TotalMinutes < 2) return string.Format(CultureInfo.InvariantCulture, "{0} seconds ago", (int)age.TotalSeconds);
            if (age.TotalMinutes < 60) return string.Format(CultureInfo.InvariantCulture, "{0} minutes ago", (int)age.TotalMinutes);
            if (age.TotalHours < 2) return "an hour ago";
            if (age.TotalHours < 24) return string.Format(CultureInfo.InvariantCulture, "{0} hours ago", (int)age.TotalHours);
            if (age.TotalDays < 2) return "yesterday";
            if (age.TotalDays < 30) return string.Format(CultureInfo.InvariantCulture, "{0} days ago", (int)age.TotalDays);
            if (age.TotalDays < 365) return string.Format(CultureInfo.InvariantCulture, "{0} months ago", (int)(age.TotalDays / 30));

            return string.Format(CultureInfo.InvariantCulture, "{0} years ago", (int)(age.TotalDays / 365));
        }

        /// <summary>
        /// Describes a duration in the largest unit that keeps it comprehensible.
        /// </summary>
        /// <param name="value">The duration.</param>
        /// <returns>The description.</returns>
        public static string Duration(TimeSpan? value)
        {
            if (!value.HasValue) return string.Empty;

            TimeSpan span = value.Value;

            if (span.TotalSeconds < 1) return string.Format(CultureInfo.InvariantCulture, "{0} ms", (int)span.TotalMilliseconds);
            if (span.TotalSeconds < 60) return string.Format(CultureInfo.InvariantCulture, "{0} sec", (int)span.TotalSeconds);
            if (span.TotalMinutes < 60) return string.Format(CultureInfo.InvariantCulture, "{0} min {1} sec", (int)span.TotalMinutes, span.Seconds);
            if (span.TotalHours < 24) return string.Format(CultureInfo.InvariantCulture, "{0} hr {1} min", (int)span.TotalHours, span.Minutes);

            return string.Format(CultureInfo.InvariantCulture, "{0} d {1} hr", (int)span.TotalDays, span.Hours);
        }

        /// <summary>
        /// Renders a byte count in the unit a person reads.
        /// </summary>
        /// <param name="bytes">The count.</param>
        /// <returns>The description.</returns>
        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
            if (bytes < 1024L * 1024) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB", bytes / 1024.0);
            if (bytes < 1024L * 1024 * 1024) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / (1024.0 * 1024));
            if (bytes < 1024L * 1024 * 1024 * 1024) return string.Format(CultureInfo.InvariantCulture, "{0:0.00} GB", bytes / (1024.0 * 1024 * 1024));

            return string.Format(CultureInfo.InvariantCulture, "{0:0.00} TB", bytes / (1024.0 * 1024 * 1024 * 1024));
        }

        /// <summary>
        /// Renders a whole number with thousands separators.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <returns>The text.</returns>
        public static string Number(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Renders a number with a decimal point where the value needs one.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <returns>The text.</returns>
        public static string Score(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 0.0001
                ? Math.Round(value).ToString("N0", CultureInfo.InvariantCulture)
                : value.ToString("N1", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Renders a label whose class carries the state's own colour.
        /// </summary>
        /// <param name="text">The state's name.</param>
        /// <param name="cssClass">The class suffix, such as <c>success</c> or <c>danger</c>.</param>
        /// <param name="title">The sentence explaining the state.</param>
        /// <returns>The markup.</returns>
        public static string Badge(string text, string cssClass, string title)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "<span class=\"label label-{0}\"{1}>{2}</span>",
                Attribute(string.IsNullOrWhiteSpace(cssClass) ? "default" : cssClass),
                string.IsNullOrWhiteSpace(title) ? string.Empty : " title=\"" + Attribute(title) + "\"",
                Encode(text));
        }

        /// <summary>
        /// Renders a Bootstrap alert.
        /// </summary>
        /// <param name="severity">The severity: <c>info</c>, <c>warning</c> or <c>danger</c>.</param>
        /// <param name="title">The heading, rendered in bold.</param>
        /// <param name="body">The body, rendered as text.</param>
        /// <param name="dismissible">Whether the reader may close it.</param>
        /// <returns>The markup.</returns>
        public static string Alert(string severity, string title, string body, bool dismissible = false)
        {
            string suffix = severity == "warning" || severity == "danger" ? severity : "info";

            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"").Append(AlertPrefix).Append(Attribute(suffix))
                   .Append(dismissible ? " alert-dismissible\" role=\"alert\">" : "\" role=\"alert\">");

            if (dismissible)
            {
                // The close control is a button rather than a link with a script in its href, because the
                // policy the site is served under permits scripts from this origin only. The behaviour is
                // attached by the site's own script through a data attribute.
                builder.Append("<button type=\"button\" class=\"close\" data-dismiss=\"alert\" aria-label=\"Close\">" +
                               "<span aria-hidden=\"true\">&times;</span></button>");
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                builder.Append("<strong>").Append(Encode(title)).Append("</strong>");
                if (!string.IsNullOrWhiteSpace(body)) builder.Append(' ');
            }

            if (!string.IsNullOrWhiteSpace(body)) builder.Append(Encode(body));

            builder.Append("</div>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders a panel with a title and a body.
        /// </summary>
        /// <param name="title">The panel's title.</param>
        /// <param name="body">The panel's body, which is markup and is not encoded.</param>
        /// <param name="cssClass">An additional class for the panel.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The body is the one parameter in this class that is inserted as markup. It is not encoded, and it
        /// is documented as such, because the body of a panel is a table or a list that the caller has already
        /// built through the helpers that do encode. A caller that passed a raw value here would be a caller
        /// that bypassed this class deliberately, and the alternative -- encoding a table -- would be absurd.
        /// </remarks>
        public static string Panel(string title, string body, string cssClass = null)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<div class=\"panel panel-primary")
                   .Append(string.IsNullOrWhiteSpace(cssClass) ? string.Empty : " " + Attribute(cssClass))
                   .Append("\">");

            if (!string.IsNullOrWhiteSpace(title))
            {
                builder.Append("<div class=\"panel-heading\"><h3 class=\"panel-title\">")
                       .Append(Encode(title))
                       .Append("</h3></div>");
            }

            builder.Append("<div class=\"panel-body\">").Append(body).Append("</div>");
            builder.Append("</div>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the opening tag of a table the site's script can sort.
        /// </summary>
        /// <param name="cssClass">Additional classes.</param>
        /// <param name="sortable">Whether the header cells should be clickable.</param>
        /// <returns>The markup.</returns>
        public static string TableOpen(string cssClass = null, bool sortable = false)
        {
            string classes = "table table-condensed table-striped not-too-small"
                             + (string.IsNullOrWhiteSpace(cssClass) ? string.Empty : " " + cssClass);

            return "<table class=\"" + Attribute(classes) + "\""
                   + (sortable ? " data-sortable=\"true\"" : string.Empty) + ">";
        }

        /// <summary>
        /// Renders a header cell.
        /// </summary>
        /// <param name="text">The column's heading.</param>
        /// <param name="cssClass">An optional class, such as one that right-aligns a numeric column.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The heading is rendered as a button-like span rather than as a link. Sorting is done in the browser,
        /// so an anchor would be a navigation that never happens and a history entry for every column the
        /// reader tried.
        /// </remarks>
        public static string Head(string text, string cssClass = null)
        {
            return "<th" + (string.IsNullOrWhiteSpace(cssClass) ? string.Empty : " class=\"" + Attribute(cssClass) + "\"")
                   + "><span class=\"sortable-head\">" + Encode(text) + "</span></th>";
        }

        /// <summary>
        /// Renders a table cell.
        /// </summary>
        /// <param name="markup">The cell's content, which is markup and is not encoded.</param>
        /// <param name="cssClass">An optional class.</param>
        /// <returns>The markup.</returns>
        public static string Column(string markup, string cssClass = null)
        {
            return "<td" + (string.IsNullOrWhiteSpace(cssClass) ? string.Empty : " class=\"" + Attribute(cssClass) + "\"")
                   + ">" + markup + "</td>";
        }

        /// <summary>
        /// Renders a definition list entry.
        /// </summary>
        /// <param name="label">The term.</param>
        /// <param name="value">The description, which is markup and is not encoded.</param>
        /// <param name="cssClass">The column width for the label.</param>
        /// <returns>The markup.</returns>
        public static string Field(string label, string value, string cssClass = "col-sm-3")
        {
            return "<div class=\"form-group form-group-sm\">"
                   + "<label class=\"" + Attribute(cssClass) + " control-label\">" + Encode(label) + "</label>"
                   + "<div class=\"col-sm-9\"><p class=\"form-control-static\">" + value + "</p></div>"
                   + "</div>";
        }

        /// <summary>
        /// Renders a bare text value inside a definition list entry.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The markup.</returns>
        public static string Value(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<span class=\"muted\">&mdash;</span>"
                : "<span class=\"code\">" + Encode(value) + "</span>";
        }

        /// <summary>
        /// Renders a page's heading with an optional explanation beneath it.
        /// </summary>
        /// <param name="title">The heading.</param>
        /// <param name="description">The explanation.</param>
        /// <returns>The markup.</returns>
        public static string Heading(string title, string description = null)
        {
            return "<h3>" + Encode(title) + "</h3>"
                   + (string.IsNullOrWhiteSpace(description)
                       ? string.Empty
                       : "<p class=\"text-muted\">" + Encode(description) + "</p>");
        }
    }
}
