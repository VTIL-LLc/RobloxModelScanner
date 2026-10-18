using RobloxModelScanner.Features.AssetAnalysis;

namespace Scanner.MicroApi.Models;

/// <summary>
/// The published definition of a detection rule.
/// </summary>
/// <remarks>
/// This is the API's answer to "what rules are there and what are they for". It is projected
/// directly from the shared detection registry, so the reference an operator reads in the browser
/// is guaranteed to be the same rule the scanner applied to the asset.
/// </remarks>
/// <param name="Tag">The stable label written into catalogue records.</param>
/// <param name="Score">The score the rule contributes when it matches.</param>
/// <param name="Category">The behaviour group the rule belongs to.</param>
/// <param name="Severity">The relative risk of a match.</param>
/// <param name="Purpose">The operator-facing explanation of why the rule exists.</param>
/// <param name="Description">The technical description of the signature.</param>
/// <param name="Matches">The number of indexed models that triggered this rule.</param>
/// <param name="Share">The proportion of indexed models that triggered this rule, from 0 to 1.</param>
public sealed record RuleDefinition(
    string Tag,
    double Score,
    RuleCategory Category,
    RuleSeverity Severity,
    string Purpose,
    string Description,
    int Matches = 0,
    double Share = 0);
