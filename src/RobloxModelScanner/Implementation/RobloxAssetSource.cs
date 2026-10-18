using System.Text.Json;

using Microsoft.Extensions.Logging;

using RobloxModelScanner.Extensions;
using RobloxModelScanner.Features.AssetDelivery;
using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// Obtains an asset's bytes and its thumbnail from Roblox.
/// </summary>
/// <remarks>
/// <para>
/// Downloading is two steps that only make sense together. The asset is resolved through the delivery
/// API, which answers with a signed content location, and the bytes are then read from that location.
/// The location expires within minutes, so the resolution is never returned to a caller and never held
/// across other work; the interface offers only the combined operation.
/// </para>
/// <para>
/// A refusal is an ordinary outcome. Assets are moderated, deleted and made private continuously, and
/// each answers the resolve step with a plain rejection. The result carries the reason so the caller can
/// count refusals without treating one as a fault in the scanner.
/// </para>
/// </remarks>
/// <seealso cref="IRobloxAssetSource"/>
public sealed class RobloxAssetSource : IRobloxAssetSource
{
    /// <summary>
    /// The thumbnail endpoint.
    /// </summary>
    private const string ThumbnailEndpoint = "https://thumbnails.roblox.com/v1/assets?assetIds=";

    /// <summary>
    /// The thumbnail rendering parameters appended to the endpoint.
    /// </summary>
    /// <remarks>
    /// The size is the largest square Roblox renders for an asset. It is stored as a link rather than as
    /// an image, so the size costs the scanner nothing and gives the site a preview worth looking at.
    /// </remarks>
    private const string ThumbnailParameters = "&size=420x420&format=Png&isCircular=false";

    /// <summary>
    /// The number of attempts a content request may take.
    /// </summary>
    private const int Attempts = 3;

    /// <summary>
    /// The largest payload the scanner will hold in memory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bound is necessary rather than tidy. The analysis lower-cases the whole payload to search it,
    /// which doubles its footprint, and it runs for many assets at once; an asset of unbounded size would
    /// therefore exhaust memory on a hostile upload rather than merely take a while. Sixty-four megabytes
    /// is far above the size of any model this scanner has ever recorded -- the largest kept payloads are
    /// single-digit megabytes -- so the cap rejects nothing that would otherwise have been useful.
    /// </para>
    /// <para>
    /// A payload over the cap is refused rather than truncated, because a truncated Roblox model is not a
    /// smaller model: it is a corrupt one whose detections would be meaningless.
    /// </para>
    /// </remarks>
    private const int MaximumPayloadBytes = 64 * 1024 * 1024;

    /// <summary>
    /// The size of the buffer a payload is read through.
    /// </summary>
    private const int ReadBufferBytes = 80 * 1024;

    /// <summary>
    /// The resolver that turns an identifier into a signed location.
    /// </summary>
    private readonly IAssetDeliveryResolver _resolver;

    /// <summary>
    /// The client the content requests are sent with.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// The logger that records retries and refusals worth knowing about.
    /// </summary>
    private readonly ILogger<RobloxAssetSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RobloxAssetSource"/> class.
    /// </summary>
    /// <param name="resolver">The resolver that turns an identifier into a signed location.</param>
    /// <param name="client">The client to send with. It is expected to carry the session cookie.</param>
    /// <param name="logger">The logger that records retries and refusals worth knowing about.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public RobloxAssetSource(
        IAssetDeliveryResolver resolver,
        HttpClient client,
        ILogger<RobloxAssetSource> logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _resolver = resolver;
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AssetDownload> DownloadAsync(long modelId, CancellationToken cancellationToken)
    {
        AssetDeliveryResult delivery = await _resolver.ResolveAsync(modelId, cancellationToken);
        if (!delivery.Succeeded || delivery.Location is null)
        {
            return AssetDownload.Failure(delivery.FailureReason ?? "The asset could not be resolved.");
        }

        using HttpResponseMessage? response = await _client.SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, delivery.Location),
            Attempts,
            _logger,
            cancellationToken);

        if (response is null)
        {
            return AssetDownload.Failure("The content host did not answer.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return AssetDownload.Failure($"The content host answered {(int)response.StatusCode}.");
        }

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);

        byte[]? bytes;
        try
        {
            bytes = await ReadBoundedAsync(body, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            // A connection that drops part way through a payload is a transport failure like any other,
            // and it arrives as an exception rather than as a status. Reporting it keeps a truncated read
            // from being mistaken for a short asset.
            return AssetDownload.Failure("The payload could not be read: " + exception.Message);
        }

        if (bytes is null)
        {
            _logger.LogWarning(
                "Asset {ModelId} exceeds the {Maximum} byte limit and was not examined.",
                modelId,
                MaximumPayloadBytes);

            return AssetDownload.Failure("The payload exceeds the size limit.");
        }

        return AssetDownload.Success(bytes);
    }

    /// <inheritdoc />
    public async Task<string?> GetThumbnailAsync(long modelId, CancellationToken cancellationToken)
    {
        string address = string.Concat(
            ThumbnailEndpoint,
            modelId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ThumbnailParameters);

        using HttpResponseMessage? response = await _client.SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, address),
            Attempts,
            _logger,
            cancellationToken);

        if (response is null || !response.IsSuccessStatusCode) return null;

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);

        try
        {
            return ParseThumbnail(body);
        }
        catch (JsonException exception)
        {
            _logger.LogDebug(exception, "The thumbnail response for asset {ModelId} was not valid JSON.", modelId);
            return null;
        }
    }

    /// <summary>
    /// Reads a payload, refusing one that grows past the limit.
    /// </summary>
    /// <param name="source">The stream to read.</param>
    /// <param name="cancellationToken">A token used to abandon the read.</param>
    /// <returns>The bytes, or <see langword="null"/> when the payload exceeded the limit.</returns>
    /// <remarks>
    /// The limit is enforced while reading rather than afterwards, because the check that matters is the
    /// one that prevents the allocation. Reading the response into an array first and measuring it then
    /// would have already paid the cost the limit exists to avoid.
    /// </remarks>
    private static async Task<byte[]?> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[ReadBufferBytes];

        while (true)
        {
            int read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumPayloadBytes) return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Reads a thumbnail URL out of a thumbnails response.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The URL, or <see langword="null"/> when the response carried no usable one.</returns>
    /// <exception cref="JsonException">Thrown when the body is not a JSON document.</exception>
    /// <remarks>
    /// The URL is checked against the same trust rule the asset location is checked against. The site
    /// links to it, so a response that had been tampered with could otherwise put an arbitrary address
    /// behind a link that the page presents as Roblox's.
    /// </remarks>
    private static string? ParseThumbnail(Stream body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!entry.TryGetProperty("imageUrl", out JsonElement url)) continue;
            if (url.ValueKind != JsonValueKind.String) continue;

            string? location = url.GetString();
            if (string.IsNullOrWhiteSpace(location)) continue;
            if (!AssetDeliveryResolver.IsTrustedLocation(location)) continue;

            return location;
        }

        return null;
    }
}
