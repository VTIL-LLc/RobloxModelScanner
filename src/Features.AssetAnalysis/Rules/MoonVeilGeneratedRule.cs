namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the original MoonVeil generator banner.
/// </summary>
/// <remarks>
/// Detects the original MoonVeil protection banner so legacy MoonVeil output is not mistaken for unscanned source.
/// </remarks>
internal sealed class MoonVeilGeneratedRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "MoonVeil Obfuscation(Verified)";

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
    public string Purpose => "Detects the original MoonVeil protection banner so legacy MoonVeil output is not mistaken for unscanned source.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the verbatim generation banner emitted by MoonVeil.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.Contains("this script was generated using moonveil");
}