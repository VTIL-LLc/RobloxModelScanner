using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using RobloxModelScanner.Extensions;
using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// Asks the Roblox catalogue which of a batch of identifiers are models.
/// </summary>
/// <remarks>
/// <para>
/// The details endpoint is the only batch lookup Roblox offers, which is what makes a sweep of the
/// identifier space viable at all: a hundred identifiers cost one request and one response, and the
/// alternative -- a request each -- would need a hundred times as many connections to learn the same
/// thing. The response states each asset's kind, so the caller receives models and nothing else.
/// </para>
/// <para>
/// The endpoint states the creator and the creation instant alongside the description. Those are
/// recorded as they arrive, because they are free once the response is parsed and because a model
/// without them is exactly the row the backfill is needed to repair.
/// </para>
/// <para>
/// A batch in which nothing exists is answered with 404 rather than with an empty list, and that is
/// translated into an empty result here. The two are the same statement, and an idle sweep makes the
/// request constantly: the identifier space holds far more nothing than it holds models.
/// </para>
/// </remarks>
/// <seealso cref="IRobloxToolbox"/>
public sealed class ToolboxClient : IRobloxToolbox
{
    /// <summary>
    /// The details endpoint.
    /// </summary>
    private const string DetailsEndpoint =
        "https://apis.roblox.com/toolbox-service/v1/items/details?assetIds=";

    /// <summary>
    /// The asset kind that denotes a model.
    /// </summary>
    /// <remarks>
    /// Every other kind -- places, audio, images, meshes -- shares the identifier space but is not what
    /// this scanner examines, so the filter is applied here rather than being left to each caller.
    /// </remarks>
    private const int ModelTypeId = 10;

    /// <summary>
    /// The number of attempts a batch may take.
    /// </summary>
    private const int Attempts = 3;

    /// <summary>
    /// The client the requests are sent with.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// The least time allowed between two requests.
    /// </summary>
    private readonly TimeSpan _minimumInterval;

    /// <summary>
    /// Serialises the pacing decision, so that two callers cannot both decide they are within the
    /// interval and then issue their requests together.
    /// </summary>
    private readonly SemaphoreSlim _pacing = new(1, 1);

    /// <summary>
    /// When the previous request was issued.
    /// </summary>
    private DateTime _lastRequestUtc = DateTime.MinValue;

    /// <summary>
    /// The logger that records retries and unparseable responses.
    /// </summary>
    private readonly ILogger<ToolboxClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolboxClient"/> class.
    /// </summary>
    /// <param name="client">The client to send with. It is expected to carry the session cookie.</param>
    /// <param name="requestInterval">The least time allowed between two requests, or zero for no pacing.</param>
    /// <param name="logger">The logger that records retries and unparseable responses.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="requestInterval"/> is negative.</exception>
    public ToolboxClient(HttpClient client, TimeSpan requestInterval, ILogger<ToolboxClient> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegative(requestInterval.Ticks);

        _client = client;
        _minimumInterval = requestInterval;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolboxItem>?> GetModelsAsync(
        IReadOnlyList<long> modelIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modelIds);

        if (modelIds.Count == 0) return [];

        await PaceAsync(cancellationToken);

        string address = DetailsEndpoint + Uri.EscapeDataString(string.Join(',', modelIds));

        using HttpResponseMessage? response = await _client.SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, address),
            Attempts,
            _logger,
            cancellationToken);

        if (response is null)
        {
            _logger.LogWarning("The catalogue did not answer for {Count} identifier(s).", modelIds.Count);
            return null;
        }

        // A refusal, with one exception that matters a great deal. Roblox answers 404 when none of the
        // identifiers in the batch exists, which is a statement about the identifiers rather than about
        // the request -- and it is the ordinary answer for the long stretches of the identifier space
        // that hold nothing at all. Treating it as a failure would leave the sweep asking the same block
        // forever, because a block it cannot advance past is a block it never leaves.
        if (response.StatusCode == HttpStatusCode.NotFound) return [];

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "The catalogue answered {Status} for {Count} identifier(s).",
                (int)response.StatusCode,
                modelIds.Count);

            return null;
        }

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);

        try
        {
            return Parse(body);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "The catalogue response for {Count} identifier(s) was not valid JSON.",
                modelIds.Count);

            return null;
        }
    }

    /// <summary>
    /// Waits, when necessary, before issuing another request.
    /// </summary>
    /// <param name="cancellationToken">A token used to abandon the wait.</param>
    /// <returns>A task that completes when a request may be issued.</returns>
    /// <remarks>
    /// The wait is measured from the previous request's start rather than from its answer, so a slow
    /// response is not waited for twice. The decision and the timestamp are made together under a lock,
    /// because two callers that measured the interval independently would both conclude they were within
    /// it and then issue their requests at the same moment -- which is exactly the burst the pacing
    /// exists to prevent.
    /// </remarks>
    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        if (_minimumInterval <= TimeSpan.Zero) return;

        await _pacing.WaitAsync(cancellationToken);
        try
        {
            TimeSpan elapsed = DateTime.UtcNow - _lastRequestUtc;
            if (elapsed < _minimumInterval)
            {
                await Task.Delay(_minimumInterval - elapsed, cancellationToken);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _pacing.Release();
        }
    }

    /// <summary>
    /// Reads the models out of a details response.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The models the response describes.</returns>
    /// <exception cref="JsonException">Thrown when the body is not a JSON document.</exception>
    /// <remarks>
    /// Every field is read defensively. Roblox omits a description for an asset that has none, weakens
    /// the answer for an asset that is moderated, and returns assets whose technical details are an empty
    /// object -- and all three arrive in the same shape, so a reader that assumed any field was present
    /// would fail on ordinary data.
    /// </remarks>
    private static List<ToolboxItem> Parse(Stream body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        List<ToolboxItem> models = [];
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return models;
        }

        foreach (JsonElement entry in data.EnumerateArray())
        {
            if (!entry.TryGetProperty("asset", out JsonElement asset)) continue;
            if (asset.ValueKind != JsonValueKind.Object) continue;
            if (GetInt32(asset, "typeId") != ModelTypeId) continue;
            if (!asset.TryGetProperty("id", out JsonElement id) || !id.TryGetInt64(out long modelId)) continue;

            JsonElement creator = default;
            bool hasCreator = entry.TryGetProperty("creator", out creator) &&
                creator.ValueKind == JsonValueKind.Object;

            models.Add(new ToolboxItem(
                ModelId: modelId,
                Name: GetString(asset, "name"),
                Description: GetString(asset, "description"),
                CreatorName: hasCreator ? GetString(creator, "name") : string.Empty,
                CreatorId: hasCreator ? GetText(creator, "id") : string.Empty,
                CreatedUtc: GetString(asset, "createdUtc"),
                DeclaresScripts: GetBoolean(asset, "hasScripts")));
        }

        return models;
    }

    /// <summary>
    /// Reads a string member, tolerating its absence and its not being a string.
    /// </summary>
    /// <param name="element">The object to read from.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member's text, or an empty string.</returns>
    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Reads a whole number member as text, tolerating its absence and its being a JSON number.
    /// </summary>
    /// <param name="element">The object to read from.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member's value as text, or an empty string.</returns>
    /// <remarks>
    /// A creator identifier arrives as a number, but the catalogue stores it as text because it is an
    /// opaque key rather than something any caller does arithmetic on. Reading it through the number's
    /// own text keeps the value exactly as Roblox stated it.
    /// </remarks>
    private static string GetText(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)) return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Reads a whole number member.
    /// </summary>
    /// <param name="element">The object to read from.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member's value, or zero.</returns>
    private static int GetInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int parsed) ? parsed : 0;

    /// <summary>
    /// Reads a switch member.
    /// </summary>
    /// <param name="element">The object to read from.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member's value, or <see langword="false"/> when it is absent or not a switch.</returns>
    /// <remarks>
    /// Absent and false are the same answer here, and that is safe because the flag is only ever used to
    /// add to the scanner's own finding. An asset whose scripts the catalogue does not declare is still
    /// examined for script markers, so a missing flag costs nothing but a missed corroboration.
    /// </remarks>
    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
