namespace RMS.Docs.Service.Models;

/// <summary>
/// The answer to a search, whether or not it found anything.
/// </summary>
/// <remarks>
/// <para>
/// An empty result is a success carrying <see cref="Count"/> zero, not a <c>404</c>. The question was "which
/// documents contain this", and "none" is a complete answer to it.
/// </para>
/// <para>
/// <see cref="Limit"/> is reported so that a caller can tell a search that found exactly the limit from one that
/// found only as many as the limit allowed, which are different answers that look the same in a result list.
/// </para>
/// </remarks>
public sealed class SearchResponseView
{
    /// <summary>
    /// Gets or sets the phrase that was searched for.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of results returned.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the largest number of results that would have been returned.
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Gets or sets the matching documents.
    /// </summary>
    public List<SearchResultView> Results { get; set; } = [];
}
