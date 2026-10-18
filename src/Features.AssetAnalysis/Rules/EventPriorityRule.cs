namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches modified event priority identifiers.
/// </summary>
/// <remarks>
/// Flags use of event-priority manipulation, which is typically combined with obfuscation to control the order in which injected behaviour runs.
/// </remarks>
internal sealed class EventPriorityRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Event Priority(prob)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 3.4;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Concurrency;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.Low;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Flags use of event-priority manipulation, which is typically combined with obfuscation to control the order in which injected behaviour runs.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the breakshift, dimensionshift and priorityv1 identifiers as whole words.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsAnyWord("breakshift", "dimensionshift", "priorityv1");
}