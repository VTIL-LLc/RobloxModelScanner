namespace RMS.Docs.Service.Models;

/// <summary>
/// Every document the service can serve, plus whatever it could not read.
/// </summary>
/// <remarks>
/// <para>
/// The problems are part of the answer rather than an error. A documentation directory with one unreadable file
/// in it is still a documentation directory, and answering the whole request with a failure would hide the
/// twenty documents that are fine because of the one that is not. What the operator needs is both facts.
/// </para>
/// <para>
/// <see cref="Summary"/> exists so that a caller that renders nothing else still says something true. It is
/// written here rather than assembled by each consumer, because two consumers summarising the same catalogue
/// differently is how a status line starts disagreeing with the table under it.
/// </para>
/// </remarks>
public sealed class DocumentListView
{
    /// <summary>
    /// Gets or sets the directory the documents were read from.
    /// </summary>
    /// <remarks>
    /// Reported so that a site pointed at the wrong directory is visible immediately. A catalogue that is empty
    /// because nothing has been written yet and one that is empty because the address is wrong look identical
    /// without it.
    /// </remarks>
    public string DocumentDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the documents, in reading order.
    /// </summary>
    /// <remarks>
    /// Sorted by section, then by declared order, then by title. Callers may re-sort, but the order that arrives
    /// is the documentation's own, which is the right default for a navigation that was not asked to do anything
    /// else.
    /// </remarks>
    public List<DocumentSummaryView> Documents { get; set; } = [];

    /// <summary>
    /// Gets or sets the files that could not be served.
    /// </summary>
    public List<DocumentProblemView> Problems { get; set; } = [];

    /// <summary>
    /// Gets or sets a sentence describing the state of the documentation.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets the number of documents that can be served.
    /// </summary>
    public int DocumentCount => Documents.Count;
}
