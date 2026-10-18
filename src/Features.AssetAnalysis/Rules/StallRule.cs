namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches task library synchronisation primitives.
/// </summary>
/// <remarks>
/// Flags deliberate task synchronisation and desynchronisation, a pattern used to stall the scheduler or to force code onto a specific thread as part of an evasion technique.
/// </remarks>
internal sealed class StallRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Stall(prob)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 3.5;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Concurrency;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.Medium;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Flags deliberate task synchronisation and desynchronisation, a pattern used to stall the scheduler or to force code onto a specific thread as part of an evasion technique.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the task.synchronize and task.desynchronize call targets.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsAny("task.synchronize", "task.desynchronize");
}