namespace RobloxModelScanner.Features.AssetDelivery;

/// <summary>
/// Resolves the current, signed download location for a Roblox asset.
/// </summary>
/// <remarks>
/// <para>
/// Roblox removed the unauthenticated behaviour of the Asset Delivery API in April 2025:
/// the documented endpoints under <c>https://assetdelivery.roblox.com/v2/</c> now require an
/// authenticated session, and the <c>v1</c> endpoints are explicitly marked as not
/// recommended. A resolver implementation is therefore expected to attach the configured
/// session cookie when it calls the API.
/// </para>
/// <para>
/// The resolved location is a content delivery URL carrying a signed, expiring token. It
/// must be requested promptly and must not be stored, because the token becomes invalid.
/// </para>
/// </remarks>
public interface IAssetDeliveryResolver
{
    /// <summary>
    /// Resolves the current download location for an asset.
    /// </summary>
    /// <param name="assetId">The Roblox asset identifier.</param>
    /// <param name="cancellationToken">A token used to cancel the outbound request.</param>
    /// <returns>
    /// A result describing either the signed location or a caller-safe failure reason. The
    /// method never throws for an ordinary resolution failure such as a moderated asset.
    /// </returns>
    Task<AssetDeliveryResult> ResolveAsync(long assetId, CancellationToken cancellationToken = default);
}
