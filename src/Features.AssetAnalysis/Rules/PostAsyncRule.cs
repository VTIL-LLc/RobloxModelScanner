namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches the HttpService PostAsync method.
/// </summary>
/// <remarks>
/// Records use of PostAsync, which is used to push data from a running experience to a remote endpoint.
/// </remarks>
internal sealed class PostAsyncRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "PostAsync";

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
    public string Purpose => "Records use of PostAsync, which is used to push data from a running experience to a remote endpoint.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the PostAsync identifier as a whole word.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsWord("postasync");
}