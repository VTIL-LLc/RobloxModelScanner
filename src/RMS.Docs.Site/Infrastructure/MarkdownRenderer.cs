using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using RMS.Site.Common.Implementation;

namespace RMS.Docs.Site.Infrastructure
{
    /// <summary>
    /// Turns a document's Markdown into markup this site can serve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The renderer exists because the alternative is worse in both directions. Sending a browser a Markdown
    /// parser would mean bundling one, allowing it under the content security policy, and trusting it to sanitise
    /// -- and a documentation site that renders untrusted markup in the browser is a documentation site that can
    /// be made to run script by editing a file. Sending the raw text would mean a page that shows its own
    /// punctuation instead of the documentation.
    /// </para>
    /// <para>
    /// <strong>Safety is structural, not a filter.</strong> Every line is encoded before any rule is applied, so
    /// by the time this class looks for a link or an emphasis there are no angle brackets left in the text to
    /// form a tag with. A document that contains <c>&lt;script&gt;</c> renders as the four visible words of that
    /// tag, not as a script element, and there is no list of forbidden constructs to keep up to date.
    /// </para>
    /// <para>
    /// It implements the part of Markdown this documentation is written in, and no more: headings, paragraphs,
    /// lists, tables, fenced code, rules, block quotes, and links, code spans, bold and italic inline. What it
    /// does not implement it does not guess at -- see <see cref="Render"/> for the two deliberate omissions.
    /// </para>
    /// </remarks>
    public static class MarkdownRenderer
    {
        /// <summary>
        /// How far a heading is demoted on its way into the page.
        /// </summary>
        /// <remarks>
        /// A document's <c>#</c> heading becomes an <c>h3</c>, because the page has already printed the
        /// document's title as an <c>h3</c> above it. Leaving the document's own levels alone would put its
        /// sections at a larger size than the page they are part of, and the reader would see a document
        /// competing with its own heading.
        /// </remarks>
        private const int HeadingDemotion = 2;

        /// <summary>
        /// The deepest heading level the site's stylesheet has a size for.
        /// </summary>
        private const int DeepestHeading = 6;

        /// <summary>
        /// The longest link target that will be considered.
        /// </summary>
        private const int MaximumTargetLength = 512;

        /// <summary>
        /// Matches an inline code span.
        /// </summary>
        private static readonly Regex CodeSpan = new Regex(@"`([^`]+)`", RegexOptions.Compiled);

        /// <summary>
        /// Matches a link, whose text may not contain a closing bracket and whose target may not contain a space.
        /// </summary>
        private static readonly Regex LinkSpan = new Regex(@"\[([^\]]+)\]\(([^)\s]*)\)", RegexOptions.Compiled);

        /// <summary>
        /// Matches bold text.
        /// </summary>
        private static readonly Regex BoldSpan = new Regex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.Compiled);

        /// <summary>
        /// Matches italic text.
        /// </summary>
        /// <remarks>
        /// The opening mark must not be preceded by a letter and the closing one must not be followed by one, so
        /// that a mark inside a word is left alone. Only the asterisk is an emphasis mark here, never the
        /// underscore: this documentation is full of identifiers such as <c>SCAN_MODE</c> and <c>ModelTags</c>, and
        /// a renderer that treated an underscore as emphasis would eat their middles.
        /// </remarks>
        private static readonly Regex ItalicSpan = new Regex(@"(?<![\w*])\*(?=\S)([^*\s](?:[^*]*[^*\s])?)\*(?![\w*])", RegexOptions.Compiled);

        /// <summary>
        /// Matches a document link target: a slug, which is a path of letters, digits, dots, dashes and slashes.
        /// </summary>
        private static readonly Regex DocumentSlug = new Regex(@"^[A-Za-z0-9._/-]+$", RegexOptions.Compiled);

        /// <summary>
        /// Renders a document.
        /// </summary>
        /// <param name="markdown">The document's body.</param>
        /// <param name="suppressedHeading">
        /// The heading to leave out when the document opens with it, or <see langword="null"/> to render
        /// everything.
        /// </param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// <para>
        /// The suppressed heading is how a page avoids printing a document's title twice. Every document opens
        /// with its own <c># Title</c>, and every page prints the title as part of its heading; only the first
        /// heading is dropped, and only when it says the same thing, so a document that repeats its title in the
        /// middle of its text still renders it there.
        /// </para>
        /// <para>
        /// Two things are deliberately not implemented. Nested lists are rendered flat, because this
        /// documentation does not use them and a renderer that guessed at indentation would eventually guess
        /// wrong on a code sample. And a list is ended by a blank line, so an item cannot contain a paragraph of
        /// its own, for the same reason.
        /// </para>
        /// </remarks>
        public static string Render(string markdown, string suppressedHeading)
        {
            if (string.IsNullOrEmpty(markdown)) return string.Empty;

            List<string> lines = Split(markdown);
            StringBuilder builder = new StringBuilder(markdown.Length + 256);

            bool opening = true;
            int index = 0;

            while (index < lines.Count)
            {
                string trimmed = lines[index].Trim();

                if (trimmed.Length == 0)
                {
                    index++;
                    continue;
                }

                string fence = FenceOf(trimmed);

                if (fence != null)
                {
                    index = RenderCode(builder, lines, index, fence);
                    opening = false;
                    continue;
                }

                if (StartsTable(lines, index))
                {
                    index = RenderTable(builder, lines, index);
                    opening = false;
                    continue;
                }

                if (IsRule(trimmed))
                {
                    builder.Append("<hr />");
                    index++;
                    opening = false;
                    continue;
                }

                int level = HeadingLevel(trimmed);

                if (level > 0)
                {
                    string text = trimmed.Substring(level).Trim();

                    bool suppressed = opening
                                      && level == 1
                                      && suppressedHeading != null
                                      && string.Equals(text, suppressedHeading, StringComparison.OrdinalIgnoreCase);

                    if (!suppressed)
                    {
                        int depth = Math.Min(level + HeadingDemotion, DeepestHeading);

                        builder.Append("<h").Append(depth).Append('>')
                               .Append(Inline(text))
                               .Append("</h").Append(depth).Append('>');
                    }

                    index++;
                    opening = false;
                    continue;
                }

                if (IsBullet(trimmed) || IsOrdered(trimmed))
                {
                    index = RenderList(builder, lines, index);
                    opening = false;
                    continue;
                }

                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    index = RenderQuote(builder, lines, index);
                    opening = false;
                    continue;
                }

                index = RenderParagraph(builder, lines, index);
                opening = false;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Splits a document into lines.
        /// </summary>
        /// <param name="text">The document.</param>
        /// <returns>The lines, without their terminators.</returns>
        /// <remarks>
        /// Handled here rather than with a split on the newline alone, because a file written on Windows and read
        /// on Linux would otherwise carry a carriage return at the end of every line and every table cell would
        /// end in a stray character.
        /// </remarks>
        private static List<string> Split(string text)
        {
            List<string> lines = new List<string>();
            StringBuilder current = new StringBuilder();

            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];

                if (character == '\n')
                {
                    lines.Add(current.ToString());
                    current.Length = 0;
                    continue;
                }

                if (character == '\r')
                {
                    // A carriage return ends the line only when no line feed follows it; the pair is one break.
                    if (index + 1 < text.Length && text[index + 1] == '\n') continue;

                    lines.Add(current.ToString());
                    current.Length = 0;
                    continue;
                }

                current.Append(character);
            }

            lines.Add(current.ToString());

            return lines;
        }

        /// <summary>
        /// Renders a fenced code block.
        /// </summary>
        /// <param name="builder">The output.</param>
        /// <param name="lines">The document's lines.</param>
        /// <param name="index">The index of the opening fence.</param>
        /// <param name="fence">The fence marking used, which the block must close with.</param>
        /// <returns>The index of the first line after the block.</returns>
        /// <remarks>
        /// An unclosed fence renders the rest of the document as code, which is what a reader looking at the
        /// page needs to see: the fault is a missing fence, and showing the remaining text verbatim is a clearer
        /// statement of that than hiding it would be.
        /// </remarks>
        private static int RenderCode(StringBuilder builder, List<string> lines, int index, string fence)
        {
            builder.Append("<pre class=\"code-block\"><code>");

            int position = index + 1;

            while (position < lines.Count && !lines[position].Trim().StartsWith(fence, StringComparison.Ordinal))
            {
                if (position > index + 1) builder.Append('\n');

                builder.Append(Html.Encode(lines[position]));
                position++;
            }

            builder.Append("</code></pre>");

            return position < lines.Count ? position + 1 : position;
        }

        /// <summary>
        /// Determines whether a table starts at a given line.
        /// </summary>
        /// <param name="lines">The document's lines.</param>
        /// <param name="index">The line to test.</param>
        /// <returns><see langword="true"/> when a header row and its separator are both present.</returns>
        /// <remarks>
        /// Both rows are required. A lone line beginning with a pipe is the middle of a wrapped line of prose
        /// containing one, and treating it as a table header would turn a sentence into a column.
        /// </remarks>
        private static bool StartsTable(List<string> lines, int index)
        {
            if (!lines[index].Trim().StartsWith("|", StringComparison.Ordinal)) return false;
            if (index + 1 >= lines.Count) return false;

            return IsTableSeparator(lines[index + 1]);
        }

        /// <summary>
        /// Determines whether a line is a table's separator row.
        /// </summary>
        /// <param name="line">The line to test.</param>
        /// <returns><see langword="true"/> when every cell is made of dashes and colons.</returns>
        private static bool IsTableSeparator(string line)
        {
            string trimmed = line.Trim();

            if (!trimmed.StartsWith("|", StringComparison.Ordinal)) return false;

            string[] cells = SplitRow(trimmed);

            if (cells.Length == 0) return false;

            foreach (string cell in cells)
            {
                string content = cell.Trim();

                if (content.Length == 0) return false;

                bool dash = false;

                foreach (char character in content)
                {
                    if (character == '-') { dash = true; continue; }
                    if (character == ':') continue;

                    return false;
                }

                if (!dash) return false;
            }

            return true;
        }

        /// <summary>
        /// Renders a table.
        /// </summary>
        /// <param name="builder">The output.</param>
        /// <param name="lines">The document's lines.</param>
        /// <param name="index">The index of the header row.</param>
        /// <returns>The index of the first line after the table.</returns>
        /// <remarks>
        /// Short rows are padded and long ones are cut, so that a table with a missing cell renders as a table
        /// with a gap rather than as a row that overflows its own columns. The header is sortable, which the
        /// site's script wires up from the marker rather than from a column of its own.
        /// </remarks>
        private static int RenderTable(StringBuilder builder, List<string> lines, int index)
        {
            string[] header = SplitRow(lines[index].Trim());

            builder.Append(Html.TableOpen(sortable: true));
            builder.Append("<thead><tr>");

            foreach (string cell in header)
            {
                builder.Append(Html.Head(Plain(cell)));
            }

            builder.Append("</tr></thead><tbody>");

            int position = index + 2;
            bool any = false;

            while (position < lines.Count && lines[position].Trim().StartsWith("|", StringComparison.Ordinal))
            {
                string[] cells = SplitRow(lines[position].Trim());

                builder.Append("<tr>");

                for (int column = 0; column < header.Length; column++)
                {
                    builder.Append(Html.Column(column < cells.Length ? Inline(cells[column].Trim()) : string.Empty));
                }

                builder.Append("</tr>");

                any = true;
                position++;
            }

            if (!any)
            {
                builder.Append("<tr><td colspan=\"").Append(header.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
                       .Append("\" class=\"muted\">This table has no rows.</td></tr>");
            }

            builder.Append("</tbody></table>");

            return position;
        }

        /// <summary>
        /// Splits a table row into its cells.
        /// </summary>
        /// <param name="line">The row, starting and ending with a pipe.</param>
        /// <returns>The cells, without their surrounding whitespace.</returns>
        private static string[] SplitRow(string line)
        {
            string content = line;

            if (content.StartsWith("|", StringComparison.Ordinal)) content = content.Substring(1);
            if (content.EndsWith("|", StringComparison.Ordinal)) content = content.Substring(0, content.Length - 1);

            return content.Split('|');
        }

        /// <summary>
        /// Renders a list, ordered or not.
        /// </summary>
        /// <param name="builder">The output.</param>
        /// <param name="lines">The document's lines.</param>
        /// <param name="index">The index of the first item.</param>
        /// <returns>The index of the first line after the list.</returns>
        /// <remarks>
        /// A line that is not a new item and is not blank continues the item before it, and is joined to it with a
        /// space. That is what a hard-wrapped bullet needs: without it, a bullet that runs to two lines in the
        /// file would become two bullets on the page.
        /// </remarks>
        private static int RenderList(StringBuilder builder, List<string> lines, int index)
        {
            bool ordered = IsOrdered(lines[index].Trim());

            builder.Append(ordered ? "<ol>" : "<ul>");

            StringBuilder item = new StringBuilder();
            int position = index;

            while (position < lines.Count)
            {
                string trimmed = lines[position].Trim();

                if (trimmed.Length == 0) break;

                bool isItem = ordered ? IsOrdered(trimmed) : IsBullet(trimmed);

                if (isItem)
                {
                    if (item.Length > 0) builder.Append("<li>").Append(Inline(item.ToString())).Append("</li>");

                    item.Length = 0;
                    item.Append(ItemText(trimmed, ordered));
                }
                else if (item.Length > 0 && !StartsBlock(lines, position))
                {
                    item.Append(' ');
                    item.Append(trimmed);
                }
                else
                {
                    // Another kind of list, a heading or a table ended this one without a blank line.
                    break;
                }

                position++;
            }

            if (item.Length > 0) builder.Append("<li>").Append(Inline(item.ToString())).Append("</li>");

            builder.Append(ordered ? "</ol>" : "</ul>");

            // A list that was interrupted by a different block leaves that block for the caller to render.
            return position;
        }

        /// <summary>
        /// Renders a block quote.
        /// </summary>
        /// <param name="builder">The output.</param>
        /// <param name="lines">The document's lines.</param>
        /// <param name="index">The index of the first quoted line.</param>
        /// <returns>The index of the first line after the quote.</returns>
        private static int RenderQuote(StringBuilder builder, List<string> lines, int index)
        {
            StringBuilder quote = new StringBuilder();
            int position = index;

            while (position < lines.Count)
            {
                string trimmed = lines[position].Trim();

                if (!trimmed.StartsWith(">", StringComparison.Ordinal)) break;

                string content = trimmed.Substring(1).Trim();

                if (content.Length == 0) break;

                if (quote.Length > 0) quote.Append(' ');

                quote.Append(content);
                position++;
            }

            builder.Append("<blockquote><p>").Append(Inline(quote.ToString())).Append("</p></blockquote>");

            return position;
        }

        /// <summary>
        /// Renders a paragraph.
        /// </summary>
        /// <param name="builder">The output.</param>
        /// <param name="lines">The document's lines.</param>
        /// <param name="index">The index of the paragraph's first line.</param>
        /// <returns>The index of the first line after the paragraph.</returns>
        /// <remarks>
        /// Consecutive lines are joined with a space, which is required rather than tidy: these documents are
        /// hard-wrapped, so treating each line as its own paragraph would render every sentence break as a
        /// paragraph break.
        /// </remarks>
        private static int RenderParagraph(StringBuilder builder, List<string> lines, int index)
        {
            StringBuilder paragraph = new StringBuilder();
            int position = index;

            while (position < lines.Count)
            {
                string trimmed = lines[position].Trim();

                if (trimmed.Length == 0) break;
                if (position > index && StartsBlock(lines, position)) break;

                if (paragraph.Length > 0) paragraph.Append(' ');

                paragraph.Append(trimmed);
                position++;
            }

            builder.Append("<p>").Append(Inline(paragraph.ToString())).Append("</p>");

            return position;
        }

        /// <summary>
        /// Determines whether a line begins a block other than a paragraph.
        /// </summary>
        /// <param name="lines">The document's lines.</param>
        /// <param name="index">The line to test.</param>
        /// <returns><see langword="true"/> when the line starts a block.</returns>
        private static bool StartsBlock(List<string> lines, int index)
        {
            string trimmed = lines[index].Trim();

            if (trimmed.Length == 0) return true;
            if (FenceOf(trimmed) != null) return true;
            if (HeadingLevel(trimmed) > 0) return true;
            if (IsRule(trimmed)) return true;
            if (IsBullet(trimmed) || IsOrdered(trimmed)) return true;
            if (trimmed.StartsWith(">", StringComparison.Ordinal)) return true;

            return StartsTable(lines, index);
        }

        /// <summary>
        /// Renders the markup inside one line or cell.
        /// </summary>
        /// <param name="text">The text, which is encoded here.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The order is what makes this safe and correct. Code spans are taken first, so that a link written
        /// inside one stays literal; links are taken next, because a link's text may contain emphasis; and the
        /// emphasis rules run last, over text that is by then already encoded.
        /// </remarks>
        private static string Inline(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string encoded = Html.Encode(text);

            encoded = CodeSpan.Replace(encoded, Code);
            encoded = LinkSpan.Replace(encoded, Link);
            encoded = BoldSpan.Replace(encoded, Bold);
            encoded = ItalicSpan.Replace(encoded, Italic);

            return encoded;
        }

        /// <summary>
        /// Renders a text value with no markup of any kind.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The text, with any markdown punctuation left as it was written.</returns>
        /// <remarks>
        /// Used for a table's headings, where a stray asterisk or backtick is far more likely to be part of a
        /// setting's name than to be emphasis somebody meant.
        /// </remarks>
        private static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string value = text.Trim();

            // A heading that was written in code marks is shown in code, minus the marks.
            if (value.Length > 1 && value.StartsWith("`", StringComparison.Ordinal) && value.EndsWith("`", StringComparison.Ordinal))
            {
                value = value.Substring(1, value.Length - 2);
            }

            return value;
        }

        /// <summary>
        /// Renders a code span.
        /// </summary>
        /// <param name="match">The matched span.</param>
        /// <returns>The markup.</returns>
        private static string Code(Match match)
        {
            return "<code>" + match.Groups[1].Value + "</code>";
        }

        /// <summary>
        /// Renders an emphasis span.
        /// </summary>
        /// <param name="match">The matched span.</param>
        /// <returns>The markup.</returns>
        private static string Bold(Match match)
        {
            return "<strong>" + match.Groups[1].Value + "</strong>";
        }

        /// <summary>
        /// Renders an italic span.
        /// </summary>
        /// <param name="match">The matched span.</param>
        /// <returns>The markup.</returns>
        private static string Italic(Match match)
        {
            return "<em>" + match.Groups[1].Value + "</em>";
        }

        /// <summary>
        /// Renders a link.
        /// </summary>
        /// <param name="match">The matched link.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// Both the text and the target are already encoded, which is why the anchor is built here rather than
        /// with <see cref="Html.Link"/>: that helper encodes its text, and encoding a second time would show the
        /// reader the entities rather than the words.
        /// </remarks>
        private static string Link(Match match)
        {
            string text = match.Groups[1].Value;
            string target = match.Groups[2].Value.Trim();

            if (target.Length == 0 || target.Length > MaximumTargetLength) return text;

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Only these two schemes, and only by name. A target is written by whoever wrote the document, and
                // honouring whatever scheme it named is how a document acquires the ability to run script.
                return "<a href=\"" + Html.Attribute(target) + "\" rel=\"noopener noreferrer\" target=\"_blank\">"
                       + text + "</a>";
            }

            if (target.StartsWith("#", StringComparison.Ordinal))
            {
                return "<a href=\"" + Html.Attribute(SafeFragment(target)) + "\">" + text + "</a>";
            }

            string slug = ToSlug(target);

            if (slug.Length == 0) return text;

            return "<a href=\"" + Html.Attribute(DocsService.PageFor(slug)) + "\">" + text + "</a>";
        }

        /// <summary>
        /// Turns a link target into a document slug.
        /// </summary>
        /// <param name="target">The target as it was written.</param>
        /// <returns>The slug, or an empty string when the target is not a document.</returns>
        /// <remarks>
        /// A target that is not a slug is not a link. The alternative -- passing an unrecognised target through
        /// to the browser -- would make the documentation a way of pointing a reader at something this site never
        /// chose, and the rule that keeps that from happening is decided here, once, rather than by every page.
        /// </remarks>
        private static string ToSlug(string target)
        {
            string value = target.Trim();

            if (value.Length == 0) return string.Empty;

            // A leading slash is how a document refers to a page of this site rather than to another document.
            if (value.StartsWith("/", StringComparison.Ordinal)) return string.Empty;

            if (value.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 3);
            }
            else if (value.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 9);
            }

            if (value.Length == 0) return string.Empty;
            if (!DocumentSlug.IsMatch(value)) return string.Empty;

            return value;
        }

        /// <summary>
        /// Constrains a fragment destination to a name an identifier can carry.
        /// </summary>
        /// <param name="target">The fragment, including its leading hash.</param>
        /// <returns>The fragment, or a bare hash when the name is not one.</returns>
        private static string SafeFragment(string target)
        {
            string name = target.Substring(1);

            foreach (char character in name)
            {
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.') continue;

                return "#";
            }

            return name.Length == 0 ? "#" : target;
        }

        /// <summary>
        /// Returns the fence a line opens a code block with, if it opens one.
        /// </summary>
        /// <param name="trimmed">The trimmed line.</param>
        /// <returns>The fence, or <see langword="null"/>.</returns>
        /// <remarks>
        /// The language written after the fence is not read. Nothing in the site colours a code block by
        /// language, and a value taken from a file and put into a class name is a value that has to be sanitised
        /// for no benefit.
        /// </remarks>
        private static string FenceOf(string trimmed)
        {
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) return "```";
            if (trimmed.StartsWith("~~~", StringComparison.Ordinal)) return "~~~";

            return null;
        }

        /// <summary>
        /// Returns a heading's level, if the line is a heading.
        /// </summary>
        /// <param name="trimmed">The trimmed line.</param>
        /// <returns>The number of marks, or zero.</returns>
        private static int HeadingLevel(string trimmed)
        {
            int marks = 0;

            while (marks < trimmed.Length && trimmed[marks] == '#') marks++;

            if (marks == 0 || marks > 6) return 0;

            // A mark run with no space after it is not a heading. That is what keeps a line beginning with a
            // hash -- a fragment, a shell comment inside a paragraph -- from becoming one.
            return marks < trimmed.Length && trimmed[marks] == ' ' ? marks : 0;
        }

        /// <summary>
        /// Determines whether a line is a horizontal rule.
        /// </summary>
        /// <param name="trimmed">The trimmed line.</param>
        /// <returns><see langword="true"/> when the line is a rule.</returns>
        private static bool IsRule(string trimmed)
        {
            if (trimmed.Length < 3) return false;

            char mark = trimmed[0];

            if (mark != '-' && mark != '*' && mark != '_') return false;

            int count = 0;

            foreach (char character in trimmed)
            {
                if (character == mark) { count++; continue; }
                if (character == ' ') continue;

                return false;
            }

            return count >= 3;
        }

        /// <summary>
        /// Determines whether a line begins an unordered list item.
        /// </summary>
        /// <param name="trimmed">The trimmed line.</param>
        /// <returns><see langword="true"/> when the line is an item.</returns>
        private static bool IsBullet(string trimmed)
        {
            if (trimmed.Length == 0) return false;
            if (trimmed[0] != '-' && trimmed[0] != '*') return false;

            return trimmed.Length == 1 || trimmed[1] == ' ';
        }

        /// <summary>
        /// Determines whether a line begins an ordered list item.
        /// </summary>
        /// <param name="trimmed">The trimmed line.</param>
        /// <returns><see langword="true"/> when the line is an item.</returns>
        private static bool IsOrdered(string trimmed)
        {
            int digits = 0;

            while (digits < trimmed.Length && char.IsDigit(trimmed[digits])) digits++;

            if (digits == 0 || digits > 9) return false;
            if (digits + 1 >= trimmed.Length) return false;

            return trimmed[digits] == '.' && trimmed[digits + 1] == ' ';
        }

        /// <summary>
        /// Removes a list item's marker.
        /// </summary>
        /// <param name="trimmed">The trimmed line.</param>
        /// <param name="ordered">Whether the item is part of an ordered list.</param>
        /// <returns>The item's text.</returns>
        private static string ItemText(string trimmed, bool ordered)
        {
            if (!ordered) return trimmed.Length > 1 ? trimmed.Substring(2).Trim() : string.Empty;

            int digits = 0;

            while (digits < trimmed.Length && char.IsDigit(trimmed[digits])) digits++;

            return digits + 1 < trimmed.Length ? trimmed.Substring(digits + 2).Trim() : string.Empty;
        }
    }
}
