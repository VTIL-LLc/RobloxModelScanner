using RMS.Configuration.Service.Enums;

namespace RMS.Configuration.Service.Models;

/// <summary>
/// One manifest as a list entry, without its contents.
/// </summary>
/// <remarks>
/// The overview page shows four of these and asks one question of each: is it there, how big is it, when was it
/// last written, and does it have anything wrong with it. Answering that without reading the file's sections is
/// the difference between an overview that is ready before the operator has finished reading it and one that
/// makes them wait for four full parses.
/// </remarks>
public sealed class ManifestFileView
{
    /// <summary>
    /// Gets or sets which manifest this is.
    /// </summary>
    public ManifestKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the file exists.
    /// </summary>
    public bool Exists { get; set; }

    /// <summary>
    /// Gets or sets why the file could not be read, when it could not.
    /// </summary>
    public string Unreadable { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file's length in bytes.
    /// </summary>
    public long Length { get; set; }

    /// <summary>
    /// Gets or sets when the file was last written, in UTC.
    /// </summary>
    public DateTimeOffset? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 of the file's bytes.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of sections the file declares.
    /// </summary>
    public int SectionCount { get; set; }

    /// <summary>
    /// Gets or sets the number of errors found, or a negative number when the file was not parsed.
    /// </summary>
    /// <remarks>
    /// Negatives mean "not asked". The overview reads each file's header and hash, and parses only the ones
    /// whose size is small enough that the cost is invisible; a file that was skipped reports a negative count
    /// so that a page cannot render it as "no errors found", which is a different and much more confident claim.
    /// </remarks>
    public int ErrorCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the number of warnings found, or a negative number when the file was not parsed.
    /// </summary>
    public int WarningCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets a value indicating whether the file may be written back.
    /// </summary>
    public bool Editable { get; set; } = true;

    /// <summary>
    /// Gets or sets a sentence describing the file.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}
