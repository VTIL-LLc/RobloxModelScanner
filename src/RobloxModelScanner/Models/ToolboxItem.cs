using System.Globalization;

namespace RobloxModelScanner.Models;

/// <summary>
/// One model as described by the Roblox toolbox catalogue.
/// </summary>
/// <remarks>
/// <para>
/// This is everything the scanner learns about an asset before it downloads it: the facts Roblox is
/// willing to state, as opposed to the facts the scanner derives from the payload. Both are recorded,
/// and the two are kept apart because they fail differently -- a name is missing when an asset is
/// moderated, a detection is missing when a rule does not match -- and conflating them would make a
/// missing name look like a clean scan.
/// </para>
/// <para>
/// <see cref="DeclaresScripts"/> is Roblox's own answer to the question the payload scan also answers.
/// It is carried separately rather than folded into the result so that the scanner's own analysis
/// remains the primary evidence and Roblox's claim is recorded as corroboration.
/// </para>
/// </remarks>
/// <param name="ModelId">The asset identifier.</param>
/// <param name="Name">The asset name, or an empty string when Roblox does not state one.</param>
/// <param name="Description">The asset description, or an empty string.</param>
/// <param name="CreatorName">The creator's display name, or an empty string.</param>
/// <param name="CreatorId">The creator's identifier as text, or an empty string.</param>
/// <param name="CreatedUtc">The creation instant as stated by Roblox, or an empty string.</param>
/// <param name="DeclaresScripts">
/// Whether the catalogue states that the asset contains scripts. This is <see langword="false"/> both
/// when the catalogue says the asset has none and when it says nothing at all, so it is only ever used
/// to add to the scanner's own finding, never to contradict it.
/// </param>
public sealed record ToolboxItem(
    long ModelId,
    string Name,
    string Description,
    string CreatorName,
    string CreatorId,
    string CreatedUtc,
    bool DeclaresScripts)
{
    /// <summary>
    /// Gets the canonical store address for the asset.
    /// </summary>
    /// <remarks>
    /// The address is derived rather than read. Roblox states no such member, the form has been stable
    /// for the whole life of the store, and deriving it means every row in the catalogue and every link
    /// the website renders agree without a second source to keep in step.
    /// </remarks>
    public string StoreAddress =>
        string.Concat("https://create.roblox.com/store/asset/", ModelId.ToString(CultureInfo.InvariantCulture));
}
