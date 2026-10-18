namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches hard-coded Discord webhook endpoints.
/// </summary>
/// <remarks>
/// Detects embedded Discord webhook URLs, the endpoint scripts use to exfiltrate captured data such as cookies, inventory or player information.
/// </remarks>
internal sealed class WebhookRule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    public string Tag => "Webhook";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    public double Score => 3;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    public RuleCategory Category => RuleCategory.Webhook;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    public RuleSeverity Severity => RuleSeverity.High;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Detects embedded Discord webhook URLs, the endpoint scripts use to exfiltrate captured data such as cookies, inventory or player information.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the known Discord webhook URL prefixes rather than the bare word webhook, which appears in many benign configuration strings.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsAny("discord.com/api/webhooks", "discordapp.com/api/webhooks", "discord.com/api/v10/webhooks");
}