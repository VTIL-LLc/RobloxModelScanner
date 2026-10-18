using RMS.Configuration.Service.Enums;

namespace RMS.Configuration.Service.Models;

/// <summary>
/// One line of a manifest, as it was written.
/// </summary>
/// <remarks>
/// The text is carried verbatim, including its indentation and the space around the <c>=</c>, because this is
/// what the console hands back to an editor. A line that had been re-rendered from its parsed parts would come
/// back reformatted, so a save after a save would produce a diff, and an operator reviewing the change would be
/// looking at the console's formatting rather than at their own.
/// </remarks>
public sealed class ManifestLine
{
    /// <summary>
    /// Gets or sets the one-based line number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets what the line is.
    /// </summary>
    public ManifestLineKind Kind { get; set; } = ManifestLineKind.Blank;

    /// <summary>
    /// Gets or sets the line's text, without its terminator.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the section the line belongs to, or an empty string before the first header.
    /// </summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key, for an entry.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value, for an entry.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the platform the key is qualified for.
    /// </summary>
    /// <remarks>
    /// Empty for an unqualified key, which applies to every platform the section applies to. A value here means
    /// the entry overrides the unqualified one for exactly that platform, which is the one piece of the format
    /// that is not obvious from a single line.
    /// </remarks>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what is wrong with the line, or an empty string when nothing is.
    /// </summary>
    public string Problem { get; set; } = string.Empty;
}
