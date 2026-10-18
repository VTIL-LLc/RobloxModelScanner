namespace RobloxModelScanner.Features.AssetDelivery;

/// <summary>
/// A single signed content-location returned by the Roblox Asset Delivery API.
/// </summary>
/// <remarks>
/// The API answers a resolve request with an array of these entries. Each entry names the
/// encoding of the payload (<see cref="AssetFormat"/>, for example <c>source</c> for a raw
/// model) and a <see cref="Location"/> that is a fully qualified, time-limited URL on the
/// content delivery network.
/// </remarks>
/// <param name="AssetFormat">The format/encoding of the payload at this location.</param>
/// <param name="Location">The signed content delivery URL.</param>
public sealed record AssetLocation(string AssetFormat, string Location);
