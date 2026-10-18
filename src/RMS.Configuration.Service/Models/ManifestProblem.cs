using RMS.Configuration.Service.Enums;

namespace RMS.Configuration.Service.Models;

/// <summary>
/// One thing wrong with a manifest, and where it is.
/// </summary>
/// <remarks>
/// <para>
/// Every problem carries the line it was found on, because a message without one sends an operator looking
/// through a hundred and twenty lines of comments for a key they cannot see. A problem that is about a whole
/// file rather than a line reports line zero, which the console renders as "the file".
/// </para>
/// <para>
/// The message is a sentence rather than a code. There is no catalogue of codes to look up, and an operator
/// reading this is reading it at the moment they have just made the mistake, which is the one moment a
/// sentence is worth more than a number.
/// </para>
/// </remarks>
public sealed class ManifestProblem
{
    /// <summary>
    /// Gets or sets how badly the problem matters.
    /// </summary>
    public ProblemSeverity Severity { get; set; } = ProblemSeverity.Error;

    /// <summary>
    /// Gets or sets the one-based line, or zero when the problem is about the file.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets the section the problem is in, or an empty string.
    /// </summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key the problem is about, or an empty string.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sentence describing the problem, and where it can, what to write instead.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
