namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the PSU Obfuscator 4.5.A output banner.
/// </summary>
/// <remarks>
/// Detects scripts protected by PSU Obfuscator 4.5.A so the two legacy PSU builds can be counted independently.
/// </remarks>
internal sealed class Psu45Rule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "PSU 4.5.A Obfuscation(Verified)";

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
    public string Purpose => "Detects scripts protected by PSU Obfuscator 4.5.A so the two legacy PSU builds can be counted independently.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the exact banner string PSU writes into protected output.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.Contains("this file was obfuscated using psu obfuscator 4.5.a");
}