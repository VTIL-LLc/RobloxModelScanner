namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the Black Magic null-behaviour toolkit.
/// </summary>
/// <remarks>
/// Detects the Black Magic toolkit, which relies on undocumented engine behaviour to hide or redirect execution.
/// </remarks>
internal sealed class BlackMagicRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Black Magic(prob)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 3.1;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Heuristic;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.Medium;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Detects the Black Magic toolkit, which relies on undocumented engine behaviour to hide or redirect execution.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the hypernull, supernull and task.defer identifiers as whole words.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsAnyWord("hypernull", "supernull", "task.defer");
}