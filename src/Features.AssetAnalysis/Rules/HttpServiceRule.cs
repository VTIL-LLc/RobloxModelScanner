namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches any reference to Roblox HttpService.
/// </summary>
/// <remarks>
/// Detects any reference to HttpService, indicating the asset is capable of contacting the network, which is the precondition for most remote-code behaviour.
/// </remarks>
internal sealed class HttpServiceRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "HttpService(Verified)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 1.5;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.HttpService;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.Medium;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Detects any reference to HttpService, indicating the asset is capable of contacting the network, which is the precondition for most remote-code behaviour.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the HttpService identifier as a whole word.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsWord("httpservice");
}