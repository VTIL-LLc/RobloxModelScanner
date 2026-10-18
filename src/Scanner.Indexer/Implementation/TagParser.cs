using RobloxModelScanner.Features.AssetAnalysis;

namespace Scanner.Indexer.Implementation;

/// <summary>
/// Recovers the individual rule tags stored in a catalogue row.
/// </summary>
/// <remarks>
/// <para>
/// Current catalogue rows join their tags with <see cref="DetectionResult.TagSeparator"/>, so those rows
/// are split directly. Rows written by earlier revisions of the scanner joined the same tags with a single
/// space, which is ambiguous in isolation because several labels themselves contain spaces, for example
/// "Good people(Verified)".
/// </para>
/// <para>
/// Legacy rows are therefore decoded by scanning for the longest known label first and blanking each match
/// before the next pass. Every real label is a contiguous substring of the stored text, so this recovers
/// the exact tag set, and the longest-first ordering stops a short label from matching inside a longer one
/// that contains it. Labels that were renamed since are mapped through <see cref="LegacyAliases"/> so
/// historical rows still roll up against the current rule set instead of degrading into unroutable noise.
/// </para>
/// <para>
/// Parsing is deliberately conservative: anything that cannot be attributed to a known or aliased label is
/// dropped rather than invented, because a detection count that does not correspond to a real rule is
/// worse than a missing one.
/// </para>
/// </remarks>
internal static class TagParser
{
    /// <summary>
    /// Labels emitted by earlier revisions of the scanner, mapped onto the current label for the same
    /// signal. Keeping this table lets the analytics span the rename without re-scanning history.
    /// </summary>
    private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal)
    {
        // The original HttpService rule matched the bare word "async" and was tagged "Possible"; it now
        // requires HttpService itself, so its label was corrected.
        ["HttpService functions(Possible)"] = "HttpService functions(Verified)",

        // Two labels used to embed a Discord mention. A mention is presentation, not identity, so it was
        // removed from the stored tag.
        ["mau3 ??! <@1112708936332759170>"] = "mau3 Obfuscation(Possible)",
        ["Jack_Hase Obfuscation(Verified) <@1112708936332759170>"] = "Jack_Hase Obfuscation(Verified)",

        // The trusted-creator watermark used to name two of the handles it looks for. It now carries a label
        // that describes what the rule means rather than which usernames happened to trigger it.
        ["Player_57 or NopeUsername(Verified)"] = "Good people(Verified)",

        // The AMX1390 mark has been written two ways that the rule no longer uses: with a space before its
        // parenthesis, and with a word that called a match a find rather than a mention. A row is keyed on
        // the label it was written with, and recovery searches the stored text for the label the rule
        // carries now, so without these entries a row written under either older spelling keeps its text and
        // loses its mark: the token is there in the row, and nothing ever matches it.
        ["AMX1390 (RARE INCOMING)"] = "AMX1390(Mentioned)",
        ["AMX1390(RARE INCOMING)"] = "AMX1390(Mentioned)"
    };

    /// <summary>
    /// The searchable labels, ordered longest first. Each entry maps the text to find to the canonical tag
    /// that text represents.
    /// </summary>
    private static readonly KeyValuePair<string, string>[] SearchLabels = BuildSearchLabels();

    /// <summary>
    /// Extracts the canonical rule tags stored in a catalogue row.
    /// </summary>
    /// <param name="tags">The raw tag column, which may be empty.</param>
    /// <returns>The canonical tags, or an empty list when none can be attributed.</returns>
    internal static IReadOnlyList<string> Parse(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return Array.Empty<string>();

        // Modern rows carry an unambiguous delimiter, so trust it.
        if (tags.Contains(DetectionResult.TagSeparator, StringComparison.Ordinal))
        {
            string[] parts = tags.Split(
                DetectionResult.TagSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var canonical = new List<string>(parts.Length);
            foreach (string part in parts)
            {
                canonical.Add(LegacyAliases.TryGetValue(part, out string? mapped) ? mapped : part);
            }

            return canonical;
        }

        // Legacy rows are space-joined: recover each known label by longest-first matching.
        var recovered = new List<string>(3);
        string remaining = tags;

        foreach (KeyValuePair<string, string> label in SearchLabels)
        {
            if (remaining.IndexOf(label.Key, StringComparison.Ordinal) < 0) continue;

            recovered.Add(label.Value);

            // Blank the matched span so a shorter label cannot match inside it on a later pass.
            remaining = remaining.Replace(label.Key, " ", StringComparison.Ordinal);
        }

        return recovered;
    }

    /// <summary>
    /// Builds the longest-first label table from the current rule set plus the legacy aliases.
    /// </summary>
    /// <returns>The labels to search for, ordered by descending length.</returns>
    private static KeyValuePair<string, string>[] BuildSearchLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (IDetectionRule rule in AssetAnalyzer.Registry.Rules)
        {
            labels[rule.Tag] = rule.Tag;
        }

        foreach (KeyValuePair<string, string> alias in LegacyAliases)
        {
            labels[alias.Key] = alias.Value;
        }

        return labels
            .OrderByDescending(label => label.Key.Length)
            .ToArray();
    }
}
