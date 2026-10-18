namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// Describes a single rule that matched an asset.
/// </summary>
/// <remarks>
/// A finding is a flattened, self-describing projection of the rule that produced it.
/// Consumers such as the website can therefore render a detection without holding a
/// reference to the rule instance or reflecting over the registry.
/// </remarks>
public sealed class DetectionFinding
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DetectionFinding"/> class from a matched rule.
    /// </summary>
    /// <param name="rule">The rule that matched the asset.</param>
    public DetectionFinding(IDetectionRule rule)
    {
        Tag = rule.Tag;
        Score = rule.Score;
        Category = rule.Category;
        Severity = rule.Severity;
        Purpose = rule.Purpose;
        Description = rule.Description;
    }

    /// <summary>
    /// Gets the stable label of the matched rule.
    /// </summary>
    public string Tag { get; }

    /// <summary>
    /// Gets the score the match contributed to the asset total.
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// Gets the behaviour group of the matched rule.
    /// </summary>
    public RuleCategory Category { get; }

    /// <summary>
    /// Gets the relative risk of the match.
    /// </summary>
    public RuleSeverity Severity { get; }

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose { get; }

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description { get; }
}
