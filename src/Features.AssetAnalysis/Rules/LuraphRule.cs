namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches Luraph-obfuscated scripts.
/// </summary>
/// <remarks>
/// Detects Luraph-protected scripts so reviewers can treat the payload as untrusted and potentially destructive rather than as readable source.
/// </remarks>
internal sealed class LuraphRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Luraph Obfuscation(Verified)";

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
    public string Purpose => "Detects Luraph-protected scripts so reviewers can treat the payload as untrusted and potentially destructive rather than as readable source.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the Luraph watermark token LPH|, the distribution domain, or the product name as a whole word.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.Contains("lph|") || context.Contains("luraph.com") || context.ContainsWord("luraph");
}