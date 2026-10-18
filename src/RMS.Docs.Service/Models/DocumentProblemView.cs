using RMS.Docs.Service.Enums;

namespace RMS.Docs.Service.Models;

/// <summary>
/// One file in the document directory that is not being served, and why.
/// </summary>
/// <remarks>
/// The message is written for the operator who owns the directory, and it names the actual limit or fault rather
/// than a category. "This document is 731 KB, over the 512 KB limit" tells somebody what to do; "invalid
/// document" does not.
/// </remarks>
public sealed class DocumentProblemView
{
    /// <summary>
    /// Gets or sets the slug of the document the problem is about.
    /// </summary>
    /// <remarks>
    /// Empty when the problem is about the directory as a whole, which happens when the directory does not
    /// exist. There is no document to attribute that to, and inventing one would be worse than saying so.
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how badly the problem affects the catalogue.
    /// </summary>
    public ProblemSeverity Severity { get; set; }

    /// <summary>
    /// Gets or sets a sentence describing the problem.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
