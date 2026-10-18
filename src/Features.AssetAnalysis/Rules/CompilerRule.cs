namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches Lua and Luau bytecode opcode tables.
/// </summary>
/// <remarks>
/// Flags assets that ship pre-compiled Lua or Luau bytecode, which means the asset executes script data that cannot be read as source.
/// </remarks>
internal sealed class CompilerRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Compilers(Verified)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 3;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Compiler;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.Low;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Flags assets that ship pre-compiled Lua or Luau bytecode, which means the asset executes script data that cannot be read as source.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the opcode table identifiers used by the Lua and Luau compilers as whole words; the bare language name is deliberately excluded to avoid false positives.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsAnyWord("luaq", "luap", "luax", "luay", "luak");
}