namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// Represents a single, independent asset-detection rule.
/// </summary>
/// <remarks>
/// Implementations must be stateless and thread-safe. A single rule instance is
/// shared across the analyzer's worker threads and may be evaluated against many
/// payloads concurrently, so rules must never mutate shared state during
/// <see cref="IsMatch"/>.
/// </remarks>
public interface IDetectionRule
{
    /// <summary>
    /// Gets the stable, human-readable label written into catalogue records.
    /// </summary>
    /// <remarks>
    /// The tag is the public identity of the rule. Changing it invalidates how
    /// previously stored catalogue rows are grouped, so only change it when the
    /// underlying signature itself changes.
    /// </remarks>
    string Tag { get; }

    /// <summary>
    /// Gets the weight this rule contributes to a model's aggregate score when matched.
    /// </summary>
    /// <value>
    /// A non-negative value. Verified signatures carry roughly three points; heuristic
    /// signals carry a fraction of a point so that they cannot, on their own, push a
    /// model over the download/alert threshold.
    /// </value>
    double Score { get; }

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    RuleCategory Category { get; }

    /// <summary>
    /// Gets the relative risk of a match, used for presentation and alert thresholds.
    /// </summary>
    RuleSeverity Severity { get; }

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists ("what for").
    /// </summary>
    /// <remarks>
    /// This text is surfaced verbatim in the analytics rule table of the website, so it
    /// must be a complete sentence describing the risk a match represents.
    /// </remarks>
    string Purpose { get; }

    /// <summary>
    /// Gets the technical description of the bytes the rule searches for.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Determines whether the supplied payload contains this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    bool IsMatch(in DetectionContext context);
}
