using System.Globalization;
using System.Net;
using System.Text.Json;

namespace RobloxModelScanner.Features.AssetDelivery;

/// <summary>
/// Resolves signed Roblox asset download locations through the documented Asset Delivery API.
/// </summary>
/// <remarks>
/// <para>
/// The resolver calls <c>GET https://assetdelivery.roblox.com/v2/assetId/{assetId}</c>, which is
/// the endpoint Roblox documents and recommends in its Open Cloud reference. The response is a
/// JSON document whose <c>locations</c> array carries one or more signed content delivery URLs;
/// the first usable entry is returned.
/// </para>
/// <para>
/// Because Roblox now requires an authenticated session for these endpoints, the supplied
/// <see cref="HttpClient"/> is expected to carry the configured <c>.ROBLOSECURITY</c> cookie.
/// The class deliberately does not configure that cookie itself so that the caller retains full
/// control of the credential and of the message handler it lives in.
/// </para>
/// <para>
/// Every returned location is validated against the trusted Roblox content network before it is
/// handed back, which prevents a compromised or spoofed response from turning the resolver into an
/// open redirect.
/// </para>
/// </remarks>
public sealed class AssetDeliveryResolver : IAssetDeliveryResolver
{
    /// <summary>
    /// The documented, recommended Asset Delivery resolve endpoint. The asset identifier is
    /// appended directly; values are formatted with the invariant culture so the request is
    /// identical on every host.
    /// </summary>
    private const string ResolveEndpoint = "https://assetdelivery.roblox.com/v2/assetId/";

    /// <summary>
    /// Host suffixes that a resolved content location must belong to. Roblox serves content from
    /// <c>*.roblox.com</c> and <c>*.rbxcdn.com</c>; anything else is rejected.
    /// </summary>
    private static readonly string[] TrustedHostSuffixes = [".roblox.com", ".rbxcdn.com"];

    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetDeliveryResolver"/> class.
    /// </summary>
    /// <param name="client">
    /// The HTTP client used for resolve requests. It must already carry any required session cookie.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
    public AssetDeliveryResolver(HttpClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <inheritdoc />
    public async Task<AssetDeliveryResult> ResolveAsync(long assetId, CancellationToken cancellationToken = default)
    {
        string requestUri = ResolveEndpoint + assetId.ToString(CultureInfo.InvariantCulture);
        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(requestUri, cancellationToken)
                .ConfigureAwait(false);

            // Authentication and moderation are expected, routine outcomes. They are translated
            // into caller-safe reasons rather than surfaced as exceptions or as raw Roblox payloads,
            // and each carries the word naming its cause so that a caller can act on which one it is.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return AssetDeliveryResult.Failure(
                    AssetDeliveryResult.NotAuthenticated,
                    "The asset delivery session is not authenticated.");

            if (response.StatusCode == HttpStatusCode.NotFound)
                return AssetDeliveryResult.Failure(
                    AssetDeliveryResult.Removed,
                    "The asset does not exist or has been moderated.");

            if (!response.IsSuccessStatusCode)
                return AssetDeliveryResult.Failure(
                    AssetDeliveryResult.Refused,
                    $"The asset delivery API returned status {(int)response.StatusCode}.");

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            /*
             * Roblox answers this endpoint with a successful status and an errors array when it will not
             * serve the asset: a session it does not accept arrives as 200 with an error whose code is 401,
             * and a removed asset arrives with 404. Reading only the locations array made every one of those
             * look like the same thing -- an answer that was shaped wrongly -- which is how a stale cookie
             * and a moderated asset became one sentence with one wrong remedy attached to it. The code is
             * read first, and only when there is no error is the locations array the thing being answered.
             */
            if (FirstErrorCode(document.RootElement) is int error)
            {
                if (error is 401 or 403)
                    return AssetDeliveryResult.Failure(
                        AssetDeliveryResult.NotAuthenticated,
                        "The asset delivery session is not authenticated.");

                if (error is 404)
                    return AssetDeliveryResult.Failure(
                        AssetDeliveryResult.Removed,
                        "The asset does not exist or has been moderated.");

                return AssetDeliveryResult.Failure(
                    AssetDeliveryResult.Refused,
                    $"The asset delivery API refused the asset with code {error}.");
            }

            if (!document.RootElement.TryGetProperty("locations", out JsonElement locations) ||
                locations.ValueKind != JsonValueKind.Array)
            {
                return AssetDeliveryResult.Failure(
                    AssetDeliveryResult.NoLocation,
                    "The asset delivery response did not contain a locations array.");
            }

            foreach (JsonElement entry in locations.EnumerateArray())
            {
                if (!entry.TryGetProperty("location", out JsonElement locationElement) ||
                    locationElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? location = locationElement.GetString();
                if (string.IsNullOrWhiteSpace(location)) continue;

                // Untrusted entries are skipped rather than aborting the loop, because a response
                // may legitimately list several content locations and only one may be acceptable.
                if (!IsTrustedLocation(location)) continue;

                return AssetDeliveryResult.Success(location);
            }

            return AssetDeliveryResult.Failure(
                AssetDeliveryResult.NoLocation,
                "The asset delivery response did not contain a trusted content location.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a caller decision and must propagate unchanged.
            throw;
        }
        catch (JsonException)
        {
            return AssetDeliveryResult.Failure(
                AssetDeliveryResult.Malformed,
                "The asset delivery response was not valid JSON.");
        }
        catch (HttpRequestException exception)
        {
            return AssetDeliveryResult.Failure(
                AssetDeliveryResult.Unreachable,
                "The asset delivery request failed: " + exception.Message);
        }
    }

    /// <summary>
    /// Reads the code out of the first entry of an errors array, when the answer has one.
    /// </summary>
    /// <param name="root">The parsed answer.</param>
    /// <returns>The code, or <see langword="null"/> when there is no usable one.</returns>
    /// <remarks>
    /// An answer that carries neither an errors array nor a locations array is not this method's business:
    /// it returns nothing and the caller reports the shape it expected instead. Only a numeric code counts,
    /// because the wording beside it belongs to Roblox and is not repeated here.
    /// </remarks>
    private static int? FirstErrorCode(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out JsonElement errors) ||
            errors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement entry in errors.EnumerateArray())
        {
            if (entry.TryGetProperty("code", out JsonElement code) &&
                code.ValueKind == JsonValueKind.Number &&
                code.TryGetInt32(out int value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a resolved location points at the trusted Roblox content network.
    /// </summary>
    /// <param name="location">The absolute URL returned by the Asset Delivery API.</param>
    /// <returns><see langword="true"/> when the URL is HTTPS and hosted on Roblox infrastructure.</returns>
    /// <remarks>
    /// This check is the defence against a tampered or spoofed resolve response steering a client
    /// (or a redirect) at an attacker-controlled host.
    /// </remarks>
    public static bool IsTrustedLocation(string location)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out Uri? uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)) return false;

        foreach (string suffix in TrustedHostSuffixes)
        {
            if (uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
