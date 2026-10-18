using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using RobloxModelScanner.Features.AssetAnalysis;
using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// Publishes findings to Discord.
/// </summary>
/// <remarks>
/// <para>
/// Reporting never fails the scan. The scan's durable product is the catalogue row it produces; the
/// notification is a courtesy on top of it. If a webhook were allowed to throw back into the inspection,
/// a rejected message would destroy the detections that had just been computed -- losing the valuable
/// half of the work because the courtesy half failed. Every transport fault is therefore caught, logged
/// and swallowed, and the caller is free to ignore the returned task.
/// </para>
/// <para>
/// The embed's colour is the worst severity among the findings rather than a fixed colour. The channel is
/// read at a glance, and a model that matched a definitive signature for an actively abused tool should
/// not look the same as one that tripped a weak heuristic.
/// </para>
/// <para>
/// Every value is trimmed to Discord's documented limits before it is sent. A description of four
/// thousand characters is rejected by the endpoint outright, and a rejected report is worse than a
/// shortened one, because the shortened one still tells the operator that something was found.
/// </para>
/// </remarks>
/// <seealso cref="IScanReporter"/>
public sealed class DiscordReporter : IScanReporter
{
    /// <summary>
    /// The greatest length accepted for an embed title.
    /// </summary>
    private const int MaximumTitleLength = 256;

    /// <summary>
    /// The greatest length accepted for an embed description.
    /// </summary>
    private const int MaximumDescriptionLength = 4096;

    /// <summary>
    /// The greatest length accepted for a field's value.
    /// </summary>
    private const int MaximumFieldValueLength = 1024;

    /// <summary>
    /// The greatest number of fields accepted on one embed.
    /// </summary>
    private const int MaximumFields = 25;

    /// <summary>
    /// The name the messages are attributed to.
    /// </summary>
    private const string DisplayName = "RMS Scanner";

    /// <summary>
    /// The colours used for each severity, from informational upward, in RGB as an integer.
    /// </summary>
    /// <remarks>
    /// The order matches <see cref="RuleSeverity"/>, and the array is indexed by it, so a new severity
    /// added to the enumeration in its documented order does not silently take an existing colour.
    /// </remarks>
    private static readonly int[] SeverityColors =
    [
        0x3498DB, // Informational -- blue
        0xF1C40F, // Low           -- yellow
        0xE67E22, // Medium        -- orange
        0xE74C3C, // High          -- red
        0xB00020  // Critical      -- dark red
    ];

    /// <summary>
    /// The colour used when nothing matched.
    /// </summary>
    private const int NeutralColor = 0x95A5A6;

    /// <summary>
    /// The settings that carry the endpoints and the keep threshold.
    /// </summary>
    private readonly ScannerSettings _settings;

    /// <summary>
    /// The client the messages are posted with.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// The logger that records a report that could not be delivered.
    /// </summary>
    private readonly ILogger<DiscordReporter> _logger;

    /// <summary>
    /// The serializer settings for Discord's wire format.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordReporter"/> class.
    /// </summary>
    /// <param name="client">The client the messages are posted with.</param>
    /// <param name="settings">The settings that carry the endpoints and the keep threshold.</param>
    /// <param name="logger">The logger that records a report that could not be delivered.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public DiscordReporter(HttpClient client, ScannerSettings settings, ILogger<DiscordReporter> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task ReportFoundAsync(
        ToolboxItem item,
        DetectionResult detection,
        string? thumbnail,
        CancellationToken cancellationToken) =>
        SendAsync(_settings.FoundWebhook, BuildEmbed(item, detection, thumbnail, keepReason: null), cancellationToken);

    /// <inheritdoc />
    public Task ReportKeptAsync(
        ToolboxItem item,
        DetectionResult detection,
        string? thumbnail,
        int payloadBytes,
        CancellationToken cancellationToken)
    {
        string reason = string.Concat(
            detection.FormatScore(),
            " is at or above the keep threshold of ",
            _settings.KeepScore.ToString("0.##", CultureInfo.InvariantCulture),
            " on a ",
            payloadBytes.ToString("N0", CultureInfo.InvariantCulture),
            " byte payload");

        return SendAsync(_settings.DownloadWebhook, BuildEmbed(item, detection, thumbnail, reason), cancellationToken);
    }

    /// <summary>
    /// Builds the embed describing one asset.
    /// </summary>
    /// <param name="item">The asset's catalogue metadata.</param>
    /// <param name="detection">The analysis of its payload.</param>
    /// <param name="thumbnail">The thumbnail URL, or <see langword="null"/>.</param>
    /// <param name="keepReason">The sentence explaining why the payload was kept, or <see langword="null"/>.</param>
    /// <returns>The embed to send.</returns>
    private static WebhookEmbed BuildEmbed(
        ToolboxItem item,
        DetectionResult detection,
        string? thumbnail,
        string? keepReason)
    {
        List<WebhookField> fields = [];

        if (keepReason is not null)
        {
            fields.Add(new WebhookField("Keep reason", Trim(keepReason, MaximumFieldValueLength), Inline: false));
        }

        fields.Add(new WebhookField(
            "Creator",
            Trim(
                item.CreatorName.Length > 0
                    ? string.Concat(item.CreatorName, " (", item.CreatorId, ")")
                    : "not stated",
                MaximumFieldValueLength),
            Inline: true));

        fields.Add(new WebhookField("Model", item.ModelId.ToString(CultureInfo.InvariantCulture), Inline: true));
        fields.Add(new WebhookField("Created", item.CreatedUtc.Length > 0 ? item.CreatedUtc : "unknown", Inline: true));
        fields.Add(new WebhookField("Score", detection.FormatScore(), Inline: true));
        fields.Add(new WebhookField("Detections", detection.Findings.Count.ToString(CultureInfo.InvariantCulture), Inline: true));
        fields.Add(new WebhookField("Scripts", detection.HasScript ? "present" : "none found", Inline: true));

        string tags = detection.Tags.Length > 0 ? detection.Tags : "none";
        fields.Add(new WebhookField("Tags", Trim(tags, MaximumFieldValueLength), Inline: false));

        return new WebhookEmbed(
            Title: Trim(item.Name.Length > 0 ? item.Name : string.Concat("Asset ", item.ModelId), MaximumTitleLength),
            Url: item.StoreAddress,
            Description: Trim(
                item.Description.Length > 0 ? item.Description : "This asset states no description.",
                MaximumDescriptionLength),
            Color: WorstSeverityColor(detection))
        {
            Author = item.CreatorName.Length > 0 ? new WebhookAuthor(item.CreatorName) : null,
            Thumbnail = string.IsNullOrWhiteSpace(thumbnail) ? null : new WebhookThumbnail(thumbnail),
            Footer = new WebhookFooter(string.Concat("RMS Scanner | asset ", item.ModelId)),
            Fields = fields.Take(MaximumFields).ToArray(),
            Timestamp = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Chooses the embed colour from the findings.
    /// </summary>
    /// <param name="detection">The analysis to read.</param>
    /// <returns>The colour of the worst severity, or the neutral colour when nothing matched.</returns>
    private static int WorstSeverityColor(DetectionResult detection)
    {
        int worst = -1;
        foreach (DetectionFinding finding in detection.Findings)
        {
            int severity = (int)finding.Severity;
            if (severity > worst) worst = severity;
        }

        if (worst < 0 || worst >= SeverityColors.Length) return NeutralColor;

        return SeverityColors[worst];
    }

    /// <summary>
    /// Posts one embed to a webhook.
    /// </summary>
    /// <param name="webhook">The webhook address, which may be unconfigured.</param>
    /// <param name="embed">The embed to send.</param>
    /// <param name="cancellationToken">A token used to abandon the post.</param>
    /// <returns>A task that completes when the post has been attempted.</returns>
    /// <remarks>
    /// An unconfigured webhook is not a fault: a scanner run without a channel set is a supported
    /// configuration, and it is the way the backfill is meant to be run.
    /// </remarks>
    private async Task SendAsync(string webhook, WebhookEmbed embed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(webhook)) return;

        try
        {
            WebhookPayload payload = new(DisplayName, [embed]);

            using HttpResponseMessage response = await _client.PostAsJsonAsync(
                webhook,
                payload,
                PayloadOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "A report was refused with {Status}. The finding is still recorded in the catalogue.",
                    (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A shutdown is not a reporting failure.
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(
                exception,
                "A report could not be delivered. The finding is still recorded in the catalogue.");
        }
    }

    /// <summary>
    /// Shortens text to a limit.
    /// </summary>
    /// <param name="value">The text to shorten.</param>
    /// <param name="maximum">The greatest acceptable length.</param>
    /// <returns>The text, shortened with a trailing ellipsis when it did not fit.</returns>
    private static string Trim(string value, int maximum)
    {
        if (value.Length <= maximum) return value;

        return string.Concat(value.AsSpan(0, maximum - 3), "...");
    }

    /// <summary>
    /// The message body Discord expects.
    /// </summary>
    /// <param name="Username">The name the message is attributed to.</param>
    /// <param name="Embeds">The embeds to attach.</param>
    private sealed record WebhookPayload(string Username, IReadOnlyList<WebhookEmbed> Embeds)
    {
        /// <summary>
        /// Gets the file attachments, which are never used and are stated as empty.
        /// </summary>
        /// <remarks>
        /// The member is sent even though it carries nothing, because the client in use treats its absence
        /// as a malformed body rather than as an empty list.
        /// </remarks>
        public IReadOnlyList<string> Attachments { get; init; } = [];
    }

    /// <summary>
    /// One embed within a message.
    /// </summary>
    /// <param name="Title">The heading.</param>
    /// <param name="Url">The address the title links to.</param>
    /// <param name="Description">The body text.</param>
    /// <param name="Color">The accent colour.</param>
    private sealed record WebhookEmbed(string Title, string Url, string Description, int Color)
    {
        /// <summary>
        /// Gets the author line, or <see langword="null"/> when the creator is unknown.
        /// </summary>
        public WebhookAuthor? Author { get; init; }

        /// <summary>
        /// Gets the thumbnail, or <see langword="null"/> when none was captured.
        /// </summary>
        public WebhookThumbnail? Thumbnail { get; init; }

        /// <summary>
        /// Gets the footer line.
        /// </summary>
        public WebhookFooter? Footer { get; init; }

        /// <summary>
        /// Gets the fields, which carry the facts.
        /// </summary>
        public IReadOnlyList<WebhookField> Fields { get; init; } = [];

        /// <summary>
        /// Gets the moment the report was produced.
        /// </summary>
        public string Timestamp { get; init; } = string.Empty;
    }

    /// <summary>
    /// The author line of an embed.
    /// </summary>
    /// <param name="Name">The name shown.</param>
    private sealed record WebhookAuthor(string Name);

    /// <summary>
    /// The thumbnail of an embed.
    /// </summary>
    /// <param name="Url">The image address.</param>
    private sealed record WebhookThumbnail(string Url);

    /// <summary>
    /// The footer of an embed.
    /// </summary>
    /// <param name="Text">The text shown.</param>
    private sealed record WebhookFooter(string Text);

    /// <summary>
    /// One field of an embed.
    /// </summary>
    /// <param name="Name">The field's label.</param>
    /// <param name="Value">The field's value.</param>
    /// <param name="Inline">Whether the field may share a line with the next one.</param>
    private sealed record WebhookField(string Name, string Value, bool Inline);
}
