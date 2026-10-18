namespace RMS.Devops.Service.Models;

/// <summary>
/// One entry of the detection frequency table.
/// </summary>
/// <remarks>
/// The category and the severity are integers rather than names because they are written by the indexing
/// service from the detection assembly's own enumerations, and the DevOps service deliberately does not
/// reference that assembly: a page that wants the name of a category asks the scanner API, which shares the
/// definitions. Carrying the numbers here keeps this service from having to be rebuilt whenever a rule is
/// added.
/// </remarks>
public sealed class TagStatistic
{
    /// <summary>
    /// Gets the detection label, including the tag's verdict, for example <c>Compilers(Verified)</c>.
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Gets how many catalogue rows carry the label.
    /// </summary>
    public long Matches { get; init; }

    /// <summary>
    /// Gets the category number the label belongs to.
    /// </summary>
    public int Category { get; init; }
}
