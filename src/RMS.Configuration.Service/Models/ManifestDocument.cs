using RMS.Configuration.Service.Enums;

namespace RMS.Configuration.Service.Models;

/// <summary>
/// One hand-written manifest, read and checked.
/// </summary>
/// <remarks>
/// <para>
/// The document carries both readings of the file: the lines exactly as written, for the editor, and the
/// sections as the deployer would parse them, for everything else. Both come out of one pass, so the two
/// readings cannot disagree -- which they would if a separate parser produced each, and which would be
/// invisible until an operator edited a file and watched a setting change meaning.
/// </para>
/// <para>
/// The document is what it is even when the file is broken. A console that refused to describe a manifest
/// because it had a syntax error would be a console that is unavailable exactly when it is needed, which is the
/// trade this stack refuses to make anywhere else.
/// </para>
/// </remarks>
public sealed class ManifestDocument
{
    /// <summary>
    /// Gets or sets which manifest this is.
    /// </summary>
    public ManifestKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the manifest's name, which is the file name the deployer looks for.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path the document was read from.
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
    /// Gets or sets the SHA-256 of the file's bytes, which is what a write is conditioned on.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the line ending the file uses.
    /// </summary>
    /// <remarks>
    /// Reported and preserved. A file written with CRLF on a host that had it with LF produces a diff in every
    /// line, which buries the one line that actually changed -- and on a repository where these files are
    /// reviewed, that is the difference between a readable change and an unreadable one.
    /// </remarks>
    public string LineEnding { get; set; } = "\n";

    /// <summary>
    /// Gets or sets every line, in order.
    /// </summary>
    public List<ManifestLine> Lines { get; set; } = [];

    /// <summary>
    /// Gets or sets every section, in the order the headers appeared.
    /// </summary>
    public List<ManifestSectionView> Sections { get; set; } = [];

    /// <summary>
    /// Gets or sets every problem found, in severity then line order.
    /// </summary>
    public List<ManifestProblem> Problems { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the document may be written back.
    /// </summary>
    /// <remarks>
    /// False for the plan, which the deployer generates. A generated file that could be edited through this
    /// console would be a file that appears to accept a change and then loses it on the next deployment.
    /// </remarks>
    public bool Editable { get; set; } = true;

    /// <summary>
    /// Gets the number of sections the document declares.
    /// </summary>
    public int SectionCount => Sections.Count;

    /// <summary>
    /// Gets the number of errors found.
    /// </summary>
    public int ErrorCount => Problems.Count(problem => problem.Severity == ProblemSeverity.Error);

    /// <summary>
    /// Gets the number of warnings found.
    /// </summary>
    public int WarningCount => Problems.Count(problem => problem.Severity == ProblemSeverity.Warning);

    /// <summary>
    /// Gets a sentence describing the document, for a page that has room for one line.
    /// </summary>
    /// <returns>The sentence.</returns>
    public string Describe()
    {
        if (Unreadable.Length > 0) return $"This manifest could not be read: {Unreadable}";

        if (!Exists)
        {
            return $"There is no {Name} at {Path}, so the deployer will report this part of the plan as absent.";
        }

        string counts = $"{SectionCount} section{(SectionCount == 1 ? string.Empty : "s")}, "
                        + $"{Lines.Count} lines";

        if (ErrorCount == 0 && WarningCount == 0) return $"{counts}, and nothing to report.";

        List<string> parts = [];

        if (ErrorCount > 0) parts.Add($"{ErrorCount} error{(ErrorCount == 1 ? string.Empty : "s")}");
        if (WarningCount > 0) parts.Add($"{WarningCount} warning{(WarningCount == 1 ? string.Empty : "s")}");

        return $"{counts}, with {string.Join(" and ", parts)}.";
    }
}
