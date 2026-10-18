namespace RMS.Configuration.Service.Models;

/// <summary>
/// One <c>key = value</c> pair of a manifest section, as the deployer would read it.
/// </summary>
/// <remarks>
/// This is the parsed view rather than the written one: a key's platform qualification has been taken off the
/// front, so that <c>windows.url</c> and <c>url</c> are one field with two possible qualifications and a table
/// of a section's settings is a table of settings rather than a table of spellings.
/// </remarks>
public sealed class ManifestEntryView
{
    /// <summary>
    /// Gets or sets the key with its platform qualification removed.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value as written.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the platform the entry is qualified for, or an empty string for every platform.
    /// </summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the one-based line the entry was written on.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the deployer reads this key at all.
    /// </summary>
    /// <remarks>
    /// The deployer reads a fixed vocabulary and ignores everything else without a word. An entry it would
    /// ignore is therefore shown as such, and the validator reports it as an error, because a misspelled key
    /// that is silently ignored is the single most expensive mistake these files can contain.
    /// </remarks>
    public bool Recognised { get; set; } = true;
}
