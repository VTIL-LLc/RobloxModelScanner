using RMS.Configuration.Service.Enums;

namespace RMS.Configuration.Service.Models;

/// <summary>
/// The vocabulary one manifest kind is written in.
/// </summary>
/// <remarks>
/// The section names, the kinds and the keys are one description of the format, held here, and the validator
/// and the page both read them from here. A page with its own copy of the list would be a page that accepted a
/// kind the validator refused, which is the kind of disagreement that makes a console untrustworthy in exactly
/// the case it exists for.
/// </remarks>
public sealed class VocabularySection
{
    /// <summary>
    /// Gets or sets which manifest this vocabulary describes.
    /// </summary>
    public ManifestKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the file name the deployer looks for.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a sentence saying what the file is for.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a sentence saying what a section of this file is.
    /// </summary>
    public string SectionSummary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the values the section's <c>kind</c> may take, when the set is closed.
    /// </summary>
    public List<string> Kinds { get; set; } = [];

    /// <summary>
    /// Gets or sets the setting names a section of this file may carry.
    /// </summary>
    public List<VocabularyEntry> Keys { get; set; } = [];
}
