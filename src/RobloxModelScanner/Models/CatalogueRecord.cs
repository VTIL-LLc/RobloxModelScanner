namespace RobloxModelScanner.Models;

/// <summary>
/// One row of the scanner's JSON catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The property names and their order are the catalogue's column list, not a private detail: the indexing
/// service reads the table and projects it into the index. Renaming a member or reordering the declaration
/// changes the shape both sides are written against, so it is treated as a contract and changed
/// deliberately, in step with the column list declared by the catalogue's schema.
/// </para>
/// <para>
/// The row is a positional record so that a repair can produce an updated row with <c>with</c> rather than
/// by hand. Every member except the identifier has a default, because rows written by earlier revisions of
/// the scanner are missing whole fields and must still load.
/// </para>
/// <para>
/// The found order is the catalogue's own. It is not something an inspection learns about an asset, so it
/// is never supplied by one: the catalogue assigns it on insert and carries it here so that a repair
/// rewrites a row without moving it.
/// </para>
/// </remarks>
/// <param name="Model">The Roblox asset identifier, which is unique across the catalogue.</param>
/// <param name="Sequence">The order in which the catalogue found the row, assigned when it was first inserted.</param>
/// <param name="Date">The instant the asset was created, as reported by Roblox.</param>
/// <param name="Name">The asset name, or an empty string when it could not be read.</param>
/// <param name="Description">The asset description, or an empty string.</param>
/// <param name="CreatorName">The creator's display name, or an empty string.</param>
/// <param name="CreatorId">The creator's identifier as text, or an empty string.</param>
/// <param name="HasScripts">Whether the asset carries a script container.</param>
/// <param name="Tags">The delimited labels of every detection rule that matched.</param>
/// <param name="Score">The aggregate score of the rules that matched.</param>
/// <param name="AssetUrl">The canonical store URL for the asset.</param>
/// <param name="Thumbnail">The thumbnail URL, or <see langword="null"/> when none was captured.</param>
public sealed record CatalogueRecord(
    long Model,
    long Sequence = 0,
    string Date = "",
    string Name = "",
    string Description = "",
    string CreatorName = "",
    string CreatorId = "",
    bool HasScripts = false,
    string Tags = "",
    double Score = 0,
    string AssetUrl = "",
    string? Thumbnail = null)
{
    /// <summary>
    /// Determines whether the row carries no information beyond the asset's existence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test is deliberately all-or-nothing. A row that has a name but no creator is far more likely
    /// to be describing an asset Roblox genuinely withholds a creator for than to be a row the scanner
    /// failed to fill in, and re-fetching it would cost an inspection to learn nothing. A row where every
    /// descriptive field is empty is the signature of a revision that recorded only the identifier, and
    /// that is what the backfill exists to repair.
    /// </para>
    /// <para>
    /// The store URL is included in the test even though the scanner can derive it from the identifier
    /// for free. Its absence is evidence about how the row was written rather than about the asset, and
    /// it is the cheapest proof that a row predates the current writer.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> when nothing descriptive is known about the asset.</returns>
    public bool HasNoMetadata() =>
        Name.Length == 0 &&
        Description.Length == 0 &&
        CreatorName.Length == 0 &&
        CreatorId.Length == 0 &&
        AssetUrl.Length == 0;
}
