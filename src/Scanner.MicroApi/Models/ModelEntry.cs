namespace Scanner.MicroApi.Models;

/// <summary>
/// One row of the scanner's model catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The shape is deliberately compatible with the JSON written by the scanner process and with
/// the payload the website already consumes. <see cref="System.Text.Json"/> is configured with
/// case-insensitive property matching, so the PascalCase members here deserialize the scanner's
/// PascalCase document and the website's lowercase query parameters with one type.
/// </para>
/// <para>
/// Every member except the identifier has a default. Older catalogue rows were written before
/// the richer metadata existed, so a row containing only <c>Model</c> and <c>Date</c> must still
/// deserialize rather than fail the whole index pass.
/// </para>
/// </remarks>
/// <param name="Model">The Roblox asset identifier.</param>
/// <param name="Date">The ISO-8601 timestamp at which the scanner first recorded the asset.</param>
/// <param name="Name">The asset name, or an empty string when the scanner did not capture one.</param>
/// <param name="Description">The asset description, or an empty string.</param>
/// <param name="CreatorName">The creator's display name, or an empty string.</param>
/// <param name="CreatorId">The creator's numeric identifier as text, or an empty string.</param>
/// <param name="HasScripts">Whether the asset contains a script container.</param>
/// <param name="Tags">The pipe-delimited labels of every detection rule that matched.</param>
/// <param name="Score">The aggregate score of all matched detection rules.</param>
/// <param name="AssetUrl">The canonical Roblox store URL for the asset.</param>
/// <param name="Thumbnail">The asset thumbnail URL, when the scanner captured one.</param>
public sealed record ModelEntry(
    long Model,
    string Date = "",
    string Name = "",
    string Description = "",
    string CreatorName = "",
    string CreatorId = "",
    bool HasScripts = false,
    string Tags = "",
    double Score = 0,
    string AssetUrl = "",
    string? Thumbnail = null);
