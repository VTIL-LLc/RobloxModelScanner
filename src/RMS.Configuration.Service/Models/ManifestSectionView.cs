namespace RMS.Configuration.Service.Models;

/// <summary>
/// One section of a manifest, as the deployer would read it.
/// </summary>
/// <remarks>
/// A section is one requirement, one build step or one service, depending on the file. The identifier is the
/// section's name in brackets and is what the deployer reports in every message about it, which is why it is
/// carried here rather than being rebuilt from the first entry.
/// </remarks>
public sealed class ManifestSectionView
{
    /// <summary>
    /// Gets or sets the identifier, which is the text inside the brackets.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the one-based line the header was written on.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets the value of the section's <c>kind</c>, or an empty string when it has none.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value of the section's <c>platforms</c>, or an empty string when it applies to all.
    /// </summary>
    public string Platforms { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the short description the console shows beside the identifier.
    /// </summary>
    /// <remarks>
    /// A service declares one as its <c>description</c> and a build step as its <c>note</c>. Taking whichever
    /// the file provides means the overview reads as a list of what the deployment is rather than as a list of
    /// identifiers that have to be looked up.
    /// </remarks>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets every setting the section carries.
    /// </summary>
    public List<ManifestEntryView> Entries { get; set; } = [];

    /// <summary>
    /// Gets or sets every problem attributed to this section.
    /// </summary>
    public List<ManifestProblem> Problems { get; set; } = [];
}
