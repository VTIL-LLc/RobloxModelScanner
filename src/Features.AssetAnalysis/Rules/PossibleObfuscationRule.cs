namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Low-confidence heuristic for hand-rolled obfuscation.
/// </summary>
/// <remarks>
/// Raises a weak signal when standard Lua bit operations appear in a pattern typical of hand-rolled byte or string obfuscation, to be weighed alongside higher-confidence rules rather than acted on alone.
/// </remarks>
internal sealed class PossibleObfuscationRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Obfuscation(Possible)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 0.5;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Heuristic;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.Low;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Raises a weak signal when standard Lua bit operations appear in a pattern typical of hand-rolled byte or string obfuscation, to be weighed alongside higher-confidence rules rather than acted on alone.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Requires the bit32 library to be present together with at least one bit-manipulation function, which removes the false positives caused by matching bit32 on its own.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsWord("bit32") && context.ContainsAnyWord("bxor", "bnot", "bror", "band", "lshift", "rshift");
}