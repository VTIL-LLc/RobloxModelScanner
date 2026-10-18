namespace RMS.Configuration.Service.Models;

/// <summary>
/// One setting name a manifest section may carry.
/// </summary>
/// <remarks>
/// The vocabulary is published by the service rather than written into the page, because the page has to be
/// able to say what a key is <em>for</em> as well as that it exists. An operator who is told that
/// <c>strip</c> is accepted but not what it counts writes a plausible number and wonders later why the archive
/// extracted oddly.
/// </remarks>
public sealed class VocabularyEntry
{
    /// <summary>
    /// Gets or sets the setting's name, unqualified.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the key may be qualified with a platform.
    /// </summary>
    /// <remarks>
    /// The deployer reads some keys through its platform-aware lookup and some directly. A key read directly is
    /// one a platform prefix would silently kill, so the distinction is reported rather than left to be
    /// discovered.
    /// </remarks>
    public bool PerPlatform { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the deployer refuses the section without the key.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets the values the key accepts, when it accepts a closed set.
    /// </summary>
    public List<string> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the default the deployer applies when the key is absent.
    /// </summary>
    public string Default { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the longest value the deployer can hold for this key, in characters.
    /// </summary>
    /// <remarks>
    /// Zero means the deployer's general value buffer. The keys that carry a shorter one -- a version, a note, a
    /// path -- are declared with it, because a value that overflows its buffer is refused by the deployer with a
    /// message about the section rather than about the line, which is a hard message to act on.
    /// </remarks>
    public int MaximumLength { get; set; }

    /// <summary>
    /// Gets or sets a sentence saying what the key is for.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}
