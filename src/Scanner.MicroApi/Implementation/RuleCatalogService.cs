using RobloxModelScanner.Features.AssetAnalysis;

using Scanner.MicroApi.Interfaces;
using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Implementation;

/// <summary>
/// Projects the shared detection registry into the API's published rule catalogue.
/// </summary>
/// <remarks>
/// The projection is computed once, during construction, and cached for the life of the process.
/// The rule set is compiled into the application, so it cannot change while the host is running;
/// caching it removes a per-request allocation and guarantees every caller sees an identical
/// ordering.
/// </remarks>
public sealed class RuleCatalogService : IRuleCatalogService
{
    private readonly IReadOnlyList<RuleDefinition> _rules;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuleCatalogService"/> class.
    /// </summary>
    public RuleCatalogService()
    {
        IDetectionRuleRegistry registry = AssetAnalyzer.Registry;
        var definitions = new List<RuleDefinition>(registry.Rules.Count);

        foreach (IDetectionRule rule in registry.Rules)
        {
            definitions.Add(new RuleDefinition(
                rule.Tag,
                rule.Score,
                rule.Category,
                rule.Severity,
                rule.Purpose,
                rule.Description));
        }

        _rules = definitions;
    }

    /// <inheritdoc />
    public IReadOnlyList<RuleDefinition> GetRules() => _rules;
}
