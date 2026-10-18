namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the HttpService GetAsync method.
/// </summary>
/// <remarks>
/// Records use of GetAsync, which is used to download remote content into a running experience.
/// </remarks>
internal sealed class GetAsyncRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "GetAsync";

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
    public RuleSeverity Severity => RuleSeverity.Low;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Records use of GetAsync, which is used to download remote content into a running experience.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the GetAsync identifier as a whole word.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsWord("getasync");
}