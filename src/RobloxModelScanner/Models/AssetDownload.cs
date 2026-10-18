namespace RobloxModelScanner.Models;

/// <summary>
/// The outcome of trying to obtain an asset's bytes.
/// </summary>
/// <remarks>
/// <para>
/// Failed downloads are routine rather than exceptional: assets are moderated, deleted and made
/// private continuously, and each of those answers the resolve step with a refusal. Returning the
/// reason instead of throwing lets the caller record that it happened without treating an ordinary
/// refusal as a fault in the scanner.
/// </para>
/// <para>
/// The resolved location is deliberately not carried on a success. It is a signed URL whose token
/// expires within minutes, so the only thing a caller can usefully do with it is fetch it immediately,
/// which is what produced this result.
/// </para>
/// </remarks>
/// <param name="Bytes">The downloaded bytes, or <see langword="null"/> when the download failed.</param>
/// <param name="FailureReason">A short explanation when the download failed, otherwise <see langword="null"/>.</param>
public sealed record AssetDownload(byte[]? Bytes, string? FailureReason)
{
    /// <summary>
    /// Gets a value indicating whether bytes were obtained.
    /// </summary>
    public bool Succeeded => Bytes is not null;

    /// <summary>
    /// Creates a successful outcome.
    /// </summary>
    /// <param name="bytes">The downloaded bytes.</param>
    /// <returns>An <see cref="AssetDownload"/> carrying the bytes.</returns>
    public static AssetDownload Success(byte[] bytes) => new(bytes, null);

    /// <summary>
    /// Creates a failed outcome.
    /// </summary>
    /// <param name="reason">A short explanation of the failure.</param>
    /// <returns>An <see cref="AssetDownload"/> carrying the reason.</returns>
    public static AssetDownload Failure(string reason) => new(null, reason);
}
