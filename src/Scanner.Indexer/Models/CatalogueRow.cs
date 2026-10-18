namespace Scanner.Indexer.Models;

/// <summary>
/// One row of the scanner's catalogue, as read during a migration.
/// </summary>
/// <remarks>
/// <para>
/// The shape mirrors the columns the catalogue's schema declares. It is not read by position: the reader
/// selects by name, and every member except the identifier has a default, so a catalogue written by an
/// earlier revision of the scanner still migrates instead of aborting the pass.
/// </para>
/// <para>
/// This type is intentionally separate from the scanner API's published model. The two evolve for
/// different reasons: this one follows the catalogue's table, the other is a response contract. Sharing
/// one type would couple an internal storage detail to a published API surface.
/// </para>
/// </remarks>
/// <param name="Model">The Roblox asset identifier.</param>
/// <param name="Sequence">The order in which the catalogue found the row.</param>
/// <param name="Date">The ISO-8601 instant at which the scanner first recorded the asset.</param>
/// <param name="Name">The asset name, or an empty string.</param>
/// <param name="Description">The asset description, or an empty string.</param>
/// <param name="CreatorName">The creator's display name, or an empty string.</param>
/// <param name="CreatorId">The creator's numeric identifier as text, or an empty string.</param>
/// <param name="HasScripts">Whether the asset contains a script container.</param>
/// <param name="Tags">The delimited labels of every detection rule that matched.</param>
/// <param name="Score">The aggregate score of all matched detection rules.</param>
/// <param name="AssetUrl">The canonical Roblox store URL for the asset.</param>
/// <param name="Thumbnail">The asset thumbnail URL, when the scanner captured one.</param>
public sealed record CatalogueRow(
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
    string? Thumbnail = null);
