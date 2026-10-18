using System;
using System.Collections.Generic;
using System.Text;

namespace Scanner.WebForms.Infrastructure
{
    /// <summary>
    /// Recovers the individual rule labels stored in a catalogue row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Current rows join their labels with a pipe-delimited separator, so those rows are split directly. Rows
    /// written by earlier revisions of the scanner joined the same labels with a single space, which cannot be
    /// split in isolation because several labels themselves contain spaces -- one of them is a phrase of two
    /// words. A legacy row is therefore decoded by looking for the longest known label first and blanking each
    /// match before the next pass, which recovers the exact set.
    /// </para>
    /// <para>
    /// The known labels are passed in rather than written here. The rule set lives in the scanner's analysis
    /// assembly, and the site reads it from the API's rule reference; a second copy of the labels on this side
    /// would be a copy that goes stale the first time a rule is renamed, and the symptom would be a rule that
    /// silently stopped appearing against the models it fired on.
    /// </para>
    /// <para>
    /// Parsing is conservative in the same way the service's is: a label that cannot be attributed to one the
    /// rule reference names is dropped rather than invented, so the site never shows a rule that does not
    /// exist. When the rule reference is unavailable a legacy row yields nothing, which is the honest answer
    /// -- the site cannot say which labels a space-joined row holds, and guessing would be worse than the gap.
    /// </para>
    /// </remarks>
    public static class TagList
    {
        /// <summary>
        /// The delimiter current catalogue rows join their labels with.
        /// </summary>
        /// <remarks>
        /// Duplicated from the analysis assembly deliberately: this site does not reference it, and a
        /// separator that is a single character in one place and a phrase in another is a parsing bug waiting
        /// for a rule whose label happens to contain the character.
        /// </remarks>
        public const string Separator = " | ";

        /// <summary>
        /// Labels emitted by earlier revisions of the scanner, mapped onto the label for the same signal.
        /// </summary>
        /// <remarks>
        /// This table is the site's copy of the one the service holds, and it has to be a copy: the site shows
        /// historical rows, the service counts them, and both have to attribute a row written two revisions
        /// ago to the rule the row means today. Keeping it here rather than deriving it keeps this readable as
        /// a list of renames, which is what it is.
        /// </remarks>
        private static readonly Dictionary<string, string> LegacyAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The original HttpService rule matched the bare word "async" and was tagged "Possible";
            // it now requires HttpService itself, so its label was corrected.
            { "HttpService functions(Possible)", "HttpService functions(Verified)" },

            // Two labels used to embed a Discord mention. Mentions are presentation, not identity, so
            // they were removed from the stored label.
            { "mau3 ??! <@1112708936332759170>", "mau3 Obfuscation(Possible)" },
            { "Jack_Hase Obfuscation(Verified) <@1112708936332759170>", "Jack_Hase Obfuscation(Verified)" },

            // The trusted-creator watermark used to name two of the handles it looks for. It now carries a
            // label that describes what the rule means rather than which usernames happened to trigger it.
            { "Player_57 or NopeUsername(Verified)", "Good people(Verified)" },
        };

        /// <summary>
        /// Splits the labels stored in a catalogue row.
        /// </summary>
        /// <param name="tags">The raw stored column, which may be absent or empty.</param>
        /// <param name="knownLabels">
        /// The labels the rule reference names. Used only for legacy rows, and only to attribute text that is
        /// already there.
        /// </param>
        /// <returns>The labels, in the order they were recovered.</returns>
        public static IReadOnlyList<string> Parse(string tags, IReadOnlyList<string> knownLabels)
        {
            if (string.IsNullOrWhiteSpace(tags)) return new List<string>();

            // Current rows carry an unambiguous delimiter, so trust it. This is the common case: only the
            // oldest part of the catalogue was written before the delimiter existed.
            if (tags.IndexOf(Separator, StringComparison.Ordinal) >= 0)
            {
                string[] parts = tags.Split(
                    new[] { Separator },
                    StringSplitOptions.RemoveEmptyEntries);

                List<string> canonical = new List<string>(parts.Length);

                foreach (string part in parts)
                {
                    string label = part.Trim();

                    if (label.Length == 0) continue;

                    canonical.Add(Canonical(label));
                }

                return canonical;
            }

            if (knownLabels == null || knownLabels.Count == 0)
            {
                // Without the rule reference there is nothing to match a space-joined row against. The one
                // thing that is certain is that the row matched something, so it is shown as a single
                // unattributed label and the page says the rule reference is what would name it.
                return new List<string> { tags.Trim() };
            }

            return Recover(tags, knownLabels);
        }

        /// <summary>
        /// Maps a stored label onto the one in use today.
        /// </summary>
        /// <param name="label">The stored label.</param>
        /// <returns>The label to show.</returns>
        public static string Canonical(string label)
        {
            if (label == null) return string.Empty;

            string mapped;

            return LegacyAliases.TryGetValue(label, out mapped) ? mapped : label;
        }

        /// <summary>
        /// Recovers the labels from a row that was joined with spaces.
        /// </summary>
        /// <param name="tags">The stored column.</param>
        /// <param name="knownLabels">The labels the rule reference names.</param>
        /// <returns>The recovered labels.</returns>
        /// <remarks>
        /// The search order is what makes this work. Every real label is a contiguous run of characters in the
        /// stored text, so matching the longest one first -- and blanking what was matched before looking for
        /// the next -- prevents a short label from being found inside a longer one that contains it.
        /// </remarks>
        private static IReadOnlyList<string> Recover(string tags, IReadOnlyList<string> knownLabels)
        {
            List<string> candidates = new List<string>(knownLabels.Count + LegacyAliases.Count);

            foreach (string label in knownLabels)
            {
                if (!string.IsNullOrWhiteSpace(label)) candidates.Add(label.Trim());
            }

            foreach (string alias in LegacyAliases.Keys)
            {
                candidates.Add(alias);
            }

            candidates.Sort(CompareByDescendingLength);

            string remaining = tags;
            List<string> recovered = new List<string>(4);

            foreach (string candidate in candidates)
            {
                if (remaining.IndexOf(candidate, StringComparison.Ordinal) < 0) continue;

                recovered.Add(Canonical(candidate));

                // Blank the matched span so a shorter label cannot match inside it on a later pass.
                remaining = Blank(remaining, candidate);
            }

            return recovered;
        }

        /// <summary>
        /// Replaces every occurrence of a span with a single space, comparing ordinally.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="span">The span to blank out.</param>
        /// <returns>The text with every occurrence of the span replaced.</returns>
        /// <remarks>
        /// Written out rather than done with <c>string.Replace</c>, because that method compares
        /// ordinally only in the overload that this framework does not carry, and the culture-aware
        /// overload would fold two labels that differ only in case or accent and blank the wrong one.
        /// </remarks>
        private static string Blank(string text, string span)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(span)) return text;

            StringBuilder builder = new StringBuilder(text.Length);

            int position = 0;

            while (true)
            {
                int found = text.IndexOf(span, position, StringComparison.Ordinal);

                if (found < 0)
                {
                    builder.Append(text, position, text.Length - position);
                    return builder.ToString();
                }

                builder.Append(text, position, found - position);
                builder.Append(' ');

                position = found + span.Length;
            }
        }

        /// <summary>
        /// Orders two labels longest first, then alphabetically so the order is stable.
        /// </summary>
        /// <param name="left">The left label.</param>
        /// <param name="right">The right label.</param>
        /// <returns>The comparison.</returns>
        private static int CompareByDescendingLength(string left, string right)
        {
            int byLength = right.Length.CompareTo(left.Length);

            return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
        }
    }
}
