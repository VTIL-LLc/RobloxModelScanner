using System;
using System.Collections.Generic;
using RobloxModelScanner.Features.AssetAnalysis.Rules;

namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// The single source of truth for which detection rules exist.
/// </summary>
/// <remarks>
/// <para>
/// Two consumers depend on this catalogue: <see cref="AssetAnalyzer"/> uses it to
/// evaluate payloads, and the scanner HTTP API publishes it so the website can show
/// "what rules exist and what each one is for". Centralising the list prevents the two
/// from drifting apart.
/// </para>
/// <para>
/// Rules are ordered so that the most specific, highest-signal signatures are evaluated
/// first. Order does not affect the aggregate score (every rule contributes independently)
/// but it does determine the order tags appear in a catalogue row.
/// </para>
/// </remarks>
public sealed class DetectionRuleRegistry : IDetectionRuleRegistry
{
    /// <summary>
    /// The process-wide registry instance. It is immutable after construction, so it is
    /// safe to share across every thread without synchronisation.
    /// </summary>
    public static DetectionRuleRegistry Instance { get; } = new();

    private readonly IDetectionRule[] _rules;
    private readonly Dictionary<string, IDetectionRule> _rulesByTag;

    /// <summary>
    /// Initializes the registry and builds its tag index.
    /// </summary>
    private DetectionRuleRegistry()
    {
        _rules = BuildRules();
        _rulesByTag = new Dictionary<string, IDetectionRule>(_rules.Length, StringComparer.Ordinal);
        foreach (IDetectionRule rule in _rules)
            _rulesByTag[rule.Tag] = rule;
    }

    /// <inheritdoc />
    public IReadOnlyList<IDetectionRule> Rules => _rules;

    /// <inheritdoc />
    public IDetectionRule? FindByTag(string tag) =>
        tag is not null && _rulesByTag.TryGetValue(tag, out IDetectionRule? rule) ? rule : null;

    /// <summary>
    /// Creates the ordered rule set.
    /// </summary>
    /// <returns>Every rule, from the most specific to the most heuristic.</returns>
    private static IDetectionRule[] BuildRules() =>
    [
        // Verified obfuscator signatures first: a definitive match should not be
        // diluted by the order heuristics run in.
        new Amx1390Rule(),
        new Psu50Rule(),
        new LuraphRule(),
        new MoonSecRule(),
        new MoonVeilProtectedRule(),
        new MoonVeilGeneratedRule(),
        new WynfuscateRule(),
        new SynapseXenRule(),
        new SeventySevenfuscatorRule(),
        new BoronideRule(),
        new Psu40Rule(),
        new Psu45Rule(),
        new JackHaseRule(),
        new Mau3Rule(),

        // Virtual machines and compilers.
        new VirtualMachineRule(),
        new CompilerRule(),

        // Network and webhook behaviour.
        new WebhookRule(),
        new HttpServiceAsyncRule(),
        new HttpServiceRule(),
        new GetAsyncRule(),
        new PostAsyncRule(),
        new RequestAsyncRule(),

        // Concurrency markers.
        new ParallelRule(),
        new StallRule(),
        new EventPriorityRule(),

        // Heuristics last: lowest confidence, lowest weight.
        new BlackMagicRule(),
        new PossibleObfuscationRule(),

        // Trusted-creator watermark.
        new P57Rule()
    ];
}
