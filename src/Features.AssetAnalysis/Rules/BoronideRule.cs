namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the Boronide obfuscator watermark.
/// </summary>
/// <remarks>
/// Detects Boronide output, which uses a fixed literary watermark that is difficult to forge accidentally.
/// </remarks>
internal sealed class BoronideRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Boronide Obfuscation(Verified)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 3;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Obfuscator;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.High;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Detects Boronide output, which uses a fixed literary watermark that is difficult to forge accidentally.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches any of the Boronide author markers or its verbatim watermark quotation.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsAny("herrtt's obfuscator", "herrtts obf,", "boronide obfuscation", "what the ancients called a clever fighter is one who not only wins, but excels in winning with ease.");
}