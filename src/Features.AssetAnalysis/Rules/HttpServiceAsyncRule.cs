namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Confirms HttpService asynchronous network calls.
/// </summary>
/// <remarks>
/// Confirms that a model performs network requests through the HttpService asynchronous API, the mechanism used to fetch remote payloads or report activity to an operator.
/// </remarks>
internal sealed class HttpServiceAsyncRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "HttpService functions(Verified)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 2.5;

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
    public string Purpose => "Confirms that a model performs network requests through the HttpService asynchronous API, the mechanism used to fetch remote payloads or report activity to an operator.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Requires the HttpService identifier to be present together with at least one asynchronous request method, replacing the previous rule that matched the word async on its own.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsWord("httpservice") && context.ContainsAnyWord("getasync", "postasync", "requestasync");
}