using System.Collections.Generic;

namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// Exposes the complete set of detection rules as a queryable catalogue.
/// </summary>
/// <remarks>
/// The analyzer consumes the registry to evaluate payloads; the API consumes it to
/// publish the "what rules and what for" reference used by the analytics surface.
/// Both therefore share one source of truth for rule identity and metadata.
/// </remarks>
public interface IDetectionRuleRegistry
{
    /// <summary>
    /// Gets every registered rule in evaluation order.
    /// </summary>
    /// <value>A read-only list that is safe to enumerate from multiple threads.</value>
    IReadOnlyList<IDetectionRule> Rules { get; }

    /// <summary>
    /// Finds a rule by its exact <see cref="IDetectionRule.Tag"/>.
    /// </summary>
    /// <param name="tag">The tag to look up.</param>
    /// <returns>The matching rule, or <see langword="null"/> when the tag is unknown.</returns>
    /// <remarks>
    /// The lookup is used when parsing legacy catalogue rows whose tag strings were
    /// produced by an earlier revision of the rule set.
    /// </remarks>
    IDetectionRule? FindByTag(string tag);
}
