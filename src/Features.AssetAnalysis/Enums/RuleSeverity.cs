namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// Describes the relative risk represented by a single rule match.
/// </summary>
/// <remarks>
/// Severity is deliberately separate from <see cref="IDetectionRule.Score"/>. Score is
/// an additive confidence value used to rank models; severity is an operator-facing
/// label used to colour analytics and drive alert thresholds. A single match can be
/// high severity but contribute only a small amount of score.
/// </remarks>
public enum RuleSeverity
{
    /// <summary>Purely informational, such as a trusted-creator watermark.</summary>
    Informational = 0,

    /// <summary>Weak signal that is useful only in aggregate.</summary>
    Low = 1,

    /// <summary>Meaningful signal that should be reviewed in context.</summary>
    Medium = 2,

    /// <summary>Strong signal that usually indicates hostile intent.</summary>
    High = 3,

    /// <summary>Definitive match for a known, actively abused tool.</summary>
    Critical = 4
}
