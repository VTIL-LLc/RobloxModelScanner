using System.Collections.Generic;

namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// Runs the independent asset-analysis rules against downloaded model data.
/// </summary>
/// <remarks>
/// <para>
/// The analyzer is deliberately stateless and static. It builds a
/// <see cref="DetectionContext"/> once per payload (lower-casing the buffer a single
/// time) and then asks every registered rule to test that shared context. This keeps
/// analysis allocation free and safe to call concurrently from the scanner's many
/// worker threads.
/// </para>
/// <para>
/// A rule contributes to the score purely by matching; there is no short-circuiting,
/// because the caller needs the complete tag set to persist and to report to webhooks.
/// </para>
/// </remarks>
public static class AssetAnalyzer
{
    /// <summary>
    /// Byte markers that prove an asset contains a script container rather than pure
    /// geometry or configuration data.
    /// </summary>
    private static readonly string[] ScriptMarkers = ["linkedsource", "modulescript", "scriptguid"];

    /// <summary>
    /// Gets the shared rule catalogue used by both analysis and reporting.
    /// </summary>
    public static IDetectionRuleRegistry Registry { get; } = DetectionRuleRegistry.Instance;

    /// <summary>
    /// Analyzes a payload and returns the full detection result.
    /// </summary>
    /// <param name="payload">The downloaded asset bytes.</param>
    /// <returns>The matched findings, aggregate score and script-presence flag.</returns>
    public static DetectionResult Analyze(byte[] payload)
    {
        var context = new DetectionContext(payload);
        bool hasScript = context.ContainsAny(ScriptMarkers);

        double score = 0;
        List<DetectionFinding> findings = [];
        foreach (IDetectionRule rule in Registry.Rules)
        {
            if (!rule.IsMatch(in context)) continue;
            findings.Add(new DetectionFinding(rule));
            score += rule.Score;
        }

        return new DetectionResult(hasScript, score, findings);
    }

    /// <summary>
    /// Analyzes a payload and returns only the flattened tag string.
    /// </summary>
    /// <param name="bytes">The downloaded asset bytes.</param>
    /// <param name="score">Receives the aggregate score of every matched rule.</param>
    /// <param name="hasScript">Receives whether a script marker was present.</param>
    /// <returns>The pipe-delimited tag string, or an empty string when nothing matched.</returns>
    /// <remarks>
    /// Retained so existing scanner call sites keep working unchanged while the richer
    /// <see cref="Analyze"/> overload is adopted incrementally.
    /// </remarks>
    public static string Detect(byte[] bytes, out double score, out bool hasScript)
    {
        DetectionResult result = Analyze(bytes);
        score = result.Score;
        hasScript = result.HasScript;
        return result.Tags;
    }
}
