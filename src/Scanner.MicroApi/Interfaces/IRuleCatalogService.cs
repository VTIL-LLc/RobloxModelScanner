using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Interfaces;

/// <summary>
/// Publishes the detection rule catalogue.
/// </summary>
/// <remarks>
/// The catalogue is static for the lifetime of the process because the rule set is compiled in.
/// Implementations may therefore cache their projection freely.
/// </remarks>
public interface IRuleCatalogService
{
    /// <summary>
    /// Gets every registered detection rule with its metadata.
    /// </summary>
    /// <returns>The published rule definitions in registry order.</returns>
    IReadOnlyList<RuleDefinition> GetRules();
}
