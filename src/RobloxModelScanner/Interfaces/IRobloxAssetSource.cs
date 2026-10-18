using RobloxModelScanner.Models;

namespace RobloxModelScanner.Interfaces;

/// <summary>
/// Fetches an asset's bytes and its thumbnail.
/// </summary>
/// <remarks>
/// <para>
/// The two members look unrelated and are not: both are reads of Roblox content services that carry the
/// same session credential and the same retry behaviour, and the thumbnail is only ever fetched for an
/// asset whose bytes have already been obtained. Keeping them together means the credential and the
/// retry policy are configured once.
/// </para>
/// <para>
/// Resolving an asset's location and downloading it are one operation rather than two because the
/// location is a signed URL that expires within minutes. Exposing the resolution separately would
/// invite a caller to hold a location across other work and then find it rejected, so the interface
/// offers only the combination that can work.
/// </para>
/// </remarks>
public interface IRobloxAssetSource
{
    /// <summary>
    /// Resolves and downloads an asset.
    /// </summary>
    /// <param name="modelId">The asset identifier.</param>
    /// <param name="cancellationToken">A token used to abandon the download.</param>
    /// <returns>The bytes, or the reason they could not be obtained.</returns>
    /// <remarks>
    /// Moderation, deletion and privacy are ordinary answers here rather than exceptional ones, and each
    /// arrives as a refusal from the resolution step. The result carries the reason so the caller can
    /// count refusals without treating one as a fault.
    /// </remarks>
    Task<AssetDownload> DownloadAsync(long modelId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an asset's thumbnail.
    /// </summary>
    /// <param name="modelId">The asset identifier.</param>
    /// <param name="cancellationToken">A token used to abandon the request.</param>
    /// <returns>The thumbnail URL, or <see langword="null"/> when none was produced.</returns>
    /// <remarks>
    /// A missing thumbnail is not an error. Roblox renders thumbnails lazily, so a freshly uploaded
    /// asset has none until it has been rendered, and an asset that is not public never gets one.
    /// </remarks>
    Task<string?> GetThumbnailAsync(long modelId, CancellationToken cancellationToken);
}
