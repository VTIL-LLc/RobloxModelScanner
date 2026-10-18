namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches known custom Lua virtual machine implementations.
/// </summary>
/// <remarks>
/// Detects hand-written virtual machines such as Fione and Rerubi, which execute hidden bytecode and are a common delivery vehicle for remote code.
/// </remarks>
internal sealed class VirtualMachineRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "VMs(Verified)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 3;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.VirtualMachine;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.High;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Detects hand-written virtual machines such as Fione and Rerubi, which execute hidden bytecode and are a common delivery vehicle for remote code.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the Fione or Rerubi project names as whole words.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsAnyWord("fione", "rerubi");
}