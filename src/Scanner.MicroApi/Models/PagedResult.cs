namespace Scanner.MicroApi.Models;

/// <summary>
/// A single page of results together with the size of the complete matching set.
/// </summary>
/// <remarks>
/// <see cref="Total"/> always describes the entire filtered result set rather than the current
/// page. The website relies on that so it can render an accurate page count and so the header
/// metrics reflect the whole catalogue instead of one slice of it.
/// </remarks>
/// <typeparam name="T">The type of item in the page.</typeparam>
/// <param name="Total">The number of items matching the filter, before paging.</param>
/// <param name="Page">The one-based page number that was returned.</param>
/// <param name="PageSize">The number of items requested per page.</param>
/// <param name="Items">The items on the requested page.</param>
public sealed record PagedResult<T>(int Total, int Page, int PageSize, IReadOnlyList<T> Items);
