namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the PSU Obfuscator 5.0A output banner.
/// </summary>
/// <remarks>
/// Detects scripts protected by PSU Obfuscator 5.0A, a commercial protector frequently used to conceal malicious payloads, so reviewers treat the asset as untrusted.
/// </remarks>
internal sealed class Psu50Rule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "PSU 5.0A Obfuscation(Verified)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 5;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Obfuscator;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.Critical;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Detects scripts protected by PSU Obfuscator 5.0A, a commercial protector frequently used to conceal malicious payloads, so reviewers treat the asset as untrusted.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the exact banner string PSU writes into protected output.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.Contains("this script is obfuscated by psu obfuscator v5.0a");
}