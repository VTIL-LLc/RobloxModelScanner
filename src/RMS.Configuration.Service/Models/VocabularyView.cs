namespace RMS.Configuration.Service.Models;

/// <summary>
/// The whole format, as the deployer reads it.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that the editor can offer the right keys while an operator is typing rather than after they
/// have saved and been told. It is published by the service, computed from one table, and read by the page: a
/// second copy of the vocabulary living in the site would be a copy that could accept a key the deployer
/// ignores, which is exactly the failure this console exists to prevent.
/// </para>
/// <para>
/// It is also the answer to "what can I write here", which is the question an operator asks most often and the
/// one the manifest comments answer worst -- a comment can say what a key does, but it cannot be searched,
/// sorted or checked against the file being edited.
/// </para>
/// </remarks>
public sealed class VocabularyView
{
    /// <summary>
    /// Gets or sets the vocabularies, one per manifest kind.
    /// </summary>
    public List<VocabularySection> Sections { get; set; } = [];

    /// <summary>
    /// Gets or sets the platform names a key may be qualified with.
    /// </summary>
    public List<string> Platforms { get; set; } = [];

    /// <summary>
    /// Gets or sets the ceiling the deployer's parser enforces, in the deployer's own names.
    /// </summary>
    public List<DeployerLimitView> Limits { get; set; } = [];

    /// <summary>
    /// Gets or sets a sentence describing the format.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}
