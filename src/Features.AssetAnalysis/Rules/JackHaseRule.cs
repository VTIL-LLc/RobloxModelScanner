namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the Jack_Hase obfuscator watermark.
/// </summary>
/// <remarks>
/// Detects Jack_Hase protected scripts using a fixed content hash that cannot appear by chance.
/// </remarks>
internal sealed class JackHaseRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Jack_Hase Obfuscation(Verified)";

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
    public string Purpose => "Detects Jack_Hase protected scripts using a fixed content hash that cannot appear by chance.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the SHA-256 watermark embedded by the obfuscator.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.Contains("a650d95dcda67172cb89e7719aaac813941ca062b176d687d0975b99fe924a28");
}