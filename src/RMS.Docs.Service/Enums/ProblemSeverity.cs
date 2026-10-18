namespace RMS.Docs.Service.Enums;

/// <summary>
/// How badly a problem with one document affects the catalogue.
/// </summary>
/// <remarks>
/// The distinction is not cosmetic: it decides whether a document is missing from the site or merely worth
/// mentioning. A warning describes a file that was skipped in favour of another, and the catalogue is still
/// complete without it. An error describes a file that is part of the documentation and is not being served,
/// which is a fault an operator has to see.
/// </remarks>
public enum ProblemSeverity
{
    /// <summary>
    /// The document was skipped, and nothing that was meant to be served is missing.
    /// </summary>
    Warning,

    /// <summary>
    /// The document could not be served, so part of the documentation is absent.
    /// </summary>
    Error
}
