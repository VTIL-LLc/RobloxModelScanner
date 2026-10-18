using Scanner.MicroApi.Enums;

namespace Scanner.MicroApi.Models;

/// <summary>
/// The validated, normalized form of a catalogue search request.
/// </summary>
/// <remarks>
/// Query-string values are parsed and clamped into this type before they reach the repository.
/// Paging is bounded here so that a caller cannot request an unbounded result set, and the sort
/// key is expressed as an enum so it can never be concatenated into SQL as free text.
/// </remarks>
/// <param name="Search">A free-text term matched against the asset's searchable columns.</param>
/// <param name="HasScripts">When set, restricts results to assets with or without scripts.</param>
/// <param name="HasObfuscation">When set, restricts results to assets with or without detections.</param>
/// <param name="RuleTag">When set, restricts results to assets that matched a specific rule.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The number of items per page.</param>
/// <param name="SortBy">The column to order by.</param>
/// <param name="Descending">Whether to order from highest to lowest.</param>
public sealed record ModelQuery(
    string? Search = null,
    bool? HasScripts = null,
    bool? HasObfuscation = null,
    string? RuleTag = null,
    int Page = 1,
    int PageSize = 50,
    ModelSortField SortBy = ModelSortField.Found,
    bool Descending = true)
{
    /// <summary>
    /// The smallest page size the API will honour.
    /// </summary>
    public const int MinimumPageSize = 1;

    /// <summary>
    /// The largest page size the API will honour.
    /// </summary>
    /// <remarks>
    /// Capping the page size protects the API and the website from a single caller requesting an
    /// arbitrarily large slice of the catalogue.
    /// </remarks>
    public const int MaximumPageSize = 500;

    /// <summary>
    /// Clamps an untrusted page number and page size into the supported range.
    /// </summary>
    /// <param name="search">The raw search term, which is trimmed and blanked to null when empty.</param>
    /// <param name="hasScripts">The raw script filter.</param>
    /// <param name="hasObfuscation">The raw detection filter.</param>
    /// <param name="ruleTag">The raw rule tag filter, which is trimmed and blanked to null when empty.</param>
    /// <param name="page">The requested one-based page number.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <param name="sortBy">The requested sort field.</param>
    /// <param name="descending">Whether to order from highest to lowest.</param>
    /// <returns>A query with a page of at least one and a page size inside the supported bounds.</returns>
    public static ModelQuery Normalize(
        string? search,
        bool? hasScripts,
        bool? hasObfuscation,
        string? ruleTag,
        int page,
        int pageSize,
        ModelSortField sortBy,
        bool descending) =>
        new(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            hasScripts,
            hasObfuscation,
            string.IsNullOrWhiteSpace(ruleTag) ? null : ruleTag.Trim(),
            page < 1 ? 1 : page,
            Math.Clamp(pageSize, MinimumPageSize, MaximumPageSize),
            sortBy,
            descending);
}
