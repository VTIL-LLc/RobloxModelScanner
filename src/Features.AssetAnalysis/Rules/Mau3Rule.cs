namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the mau3 obfuscator marker.
/// </summary>
/// <remarks>
/// Flags the mau3 obfuscator, which is rare but occasionally reappears in circulating models, so the asset can be queued for manual review.
/// </remarks>
internal sealed class Mau3Rule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "mau3 Obfuscation(Possible)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 2;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Obfuscator;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.Medium;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Flags the mau3 obfuscator, which is rare but occasionally reappears in circulating models, so the asset can be queued for manual review.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the token mau3 as a whole word so unrelated identifiers that merely contain the characters are ignored.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsWord("mau3");
}