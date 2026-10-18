using System.Collections.Generic;
using System.Globalization;

namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// The complete outcome of analyzing one asset.
/// </summary>
/// <remarks>
/// The result carries both the machine-oriented data (per-rule findings, aggregate
/// score, script presence) and the flattened <see cref="Tags"/> string that is written
/// into the scanner's catalogue. Keeping both means the API can perform precise
/// analytics from the findings while older consumers can continue to read the tag text.
/// </remarks>
public sealed class DetectionResult
{
    /// <summary>
    /// The delimiter used to join tags into <see cref="Tags"/>.
    /// </summary>
    /// <remarks>
    /// The previous revision joined tags with a single space. Because individual tag
    /// labels themselves contain spaces (for example "Good people(Verified)"), that made
    /// the stored string ambiguous and forced downstream consumers to guess where one tag
    /// ended and the next began. A pipe delimiter removes the ambiguity while the API
    /// still tolerates legacy space-joined rows.
    /// </remarks>
    public const string TagSeparator = " | ";

    /// <summary>
    /// Initializes a new instance of the <see cref="DetectionResult"/> class.
    /// </summary>
    /// <param name="hasScript">Whether the payload contained a script marker.</param>
    /// <param name="score">The aggregate score of all matched rules.</param>
    /// <param name="findings">The rules that matched, in evaluation order.</param>
    public DetectionResult(bool hasScript, double score, IReadOnlyList<DetectionFinding> findings)
    {
        HasScript = hasScript;
        Score = score;
        Findings = findings;
        Tags = JoinTags(findings);
    }

    /// <summary>
    /// Gets a value indicating whether the payload contained a script marker.
    /// </summary>
    public bool HasScript { get; }

    /// <summary>
    /// Gets the aggregate score of all matched rules.
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// Gets the rules that matched, in evaluation order.
    /// </summary>
    public IReadOnlyList<DetectionFinding> Findings { get; }

    /// <summary>
    /// Gets the pipe-delimited label of every matched rule.
    /// </summary>
    public string Tags { get; }

    /// <summary>
    /// Joins the tags of a set of findings using the canonical separator.
    /// </summary>
    /// <param name="findings">The findings to flatten.</param>
    /// <returns>A <see cref="TagSeparator"/>-delimited string, or an empty string.</returns>
    private static string JoinTags(IReadOnlyList<DetectionFinding> findings)
    {
        if (findings.Count == 0) return string.Empty;
        if (findings.Count == 1) return findings[0].Tag;

        // String.Join is used instead of a manual builder because findings.Count is tiny
        // (tens) and the framework already bounds the allocation precisely.
        var tags = new string[findings.Count];
        for (int index = 0; index < findings.Count; index++)
            tags[index] = findings[index].Tag;
        return string.Join(TagSeparator, tags);
    }

    /// <summary>
    /// Formats the aggregate score using the invariant culture.
    /// </summary>
    /// <returns>The score formatted with up to two decimal places.</returns>
    /// <remarks>
    /// The scanner writes this value into JSON and the API compares it to fixed
    /// thresholds, so the formatting must not vary with the host locale.
    /// </remarks>
    public string FormatScore() => Score.ToString("0.##", CultureInfo.InvariantCulture);
}
