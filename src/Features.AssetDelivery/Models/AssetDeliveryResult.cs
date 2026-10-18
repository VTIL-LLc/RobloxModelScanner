namespace RobloxModelScanner.Features.AssetDelivery;

/// <summary>
/// The outcome of resolving an asset's current download location.
/// </summary>
/// <remarks>
/// Resolution is separated from download because the returned location is a signed, time
/// limited URL that must be requested immediately and must never be cached or persisted.
/// Returning a result object rather than throwing lets callers decide whether a failure is
/// fatal, which matters because moderated or deleted assets fail resolution routinely.
/// </remarks>
/// <param name="Location">The signed content delivery URL, or <see langword="null"/> on failure.</param>
/// <param name="FailureReason">A short, caller-safe explanation when resolution failed.</param>
/// <param name="FailureCode">
/// The word naming which failure this is, or <see langword="null"/> when the sentence is the whole of it.
/// </param>
public sealed record AssetDeliveryResult(string? Location, string? FailureReason, string? FailureCode = null)
{
    /// <summary>
    /// The word for a session Roblox would not accept: absent, stale, or signed out.
    /// </summary>
    public const string NotAuthenticated = "unauthenticated";

    /// <summary>
    /// The word for an asset that does not exist or that Roblox has moderated.
    /// </summary>
    public const string Removed = "removed";

    /// <summary>
    /// The word for an answer that carried no usable location.
    /// </summary>
    public const string NoLocation = "nolocation";

    /// <summary>
    /// The word for a request Roblox answered with a status that means none of the others.
    /// </summary>
    public const string Refused = "refused";

    /// <summary>
    /// The word for an answer that could not be read as JSON.
    /// </summary>
    public const string Malformed = "malformed";

    /// <summary>
    /// The word for a request that never reached Roblox.
    /// </summary>
    public const string Unreachable = "unreachable";

    /// <summary>
    /// Gets a value indicating whether a usable location was produced.
    /// </summary>
    public bool Succeeded => Location is not null;

    /// <summary>
    /// Creates a successful result for a resolved location.
    /// </summary>
    /// <param name="location">The signed content delivery URL.</param>
    /// <returns>A successful <see cref="AssetDeliveryResult"/>.</returns>
    public static AssetDeliveryResult Success(string location) => new(location, null);

    /// <summary>
    /// Creates a failed result carrying a caller-safe reason.
    /// </summary>
    /// <param name="reason">A short explanation of the failure.</param>
    /// <returns>A failed <see cref="AssetDeliveryResult"/>.</returns>
    public static AssetDeliveryResult Failure(string reason) => new(null, reason);

    /// <summary>
    /// Creates a failed result carrying a caller-safe reason and the word that names it.
    /// </summary>
    /// <param name="code">One of the words this type declares.</param>
    /// <param name="reason">A short explanation of the failure.</param>
    /// <returns>A failed <see cref="AssetDeliveryResult"/>.</returns>
    /// <remarks>
    /// <para>
    /// The word exists because a sentence is not enough for a caller that has to decide what to say next.
    /// "The session is not authenticated" and "the asset has been moderated" are both routine outcomes from
    /// the same endpoint, and the first is an operator's problem while the second is nobody's. A caller with
    /// only the sentence would have to match on prose to tell them apart, and that breaks the first time the
    /// prose is improved -- which is exactly what happened to the page that reads these.
    /// </para>
    /// <para>
    /// The words are deliberately coarse. They name the causes a reader can act on differently, not every
    /// way the far side can disappoint us.
    /// </para>
    /// </remarks>
    public static AssetDeliveryResult Failure(string code, string reason) => new(null, reason, code);
}
