namespace RMS.Devops.Service.Models;

/// <summary>
/// One page of runs, together with enough context to render a pager.
/// </summary>
/// <remarks>
/// <para>
/// The total travels with the page rather than in a header or a second request. The pager the site renders is
/// a list of numbered links, so it needs to know how many pages there are before it can draw the last one, and
/// asking twice would double the directory scan that produced the answer.
/// </para>
/// <para>
/// <see cref="Service"/> is empty when the page spans every service, which is the shape of the history page:
/// it lists runs from the whole machine and lets the reader narrow that down.
/// </para>
/// </remarks>
public sealed class RunPage
{
    /// <summary>
    /// Gets the service the page was drawn from, or an empty string when it spans every service.
    /// </summary>
    public string Service { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many runs were skipped.
    /// </summary>
    public int Skip { get; init; }

    /// <summary>
    /// Gets how many runs were asked for.
    /// </summary>
    public int Take { get; init; }

    /// <summary>
    /// Gets the total number of runs the page was drawn from.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// Gets the number of the current page, counting from one.
    /// </summary>
    public int Page => Take <= 0 ? 1 : (Skip / Take) + 1;

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int PageCount => Take <= 0 ? 1 : ((Total + Take - 1) / Take);

    /// <summary>
    /// Gets the runs on this page, newest first.
    /// </summary>
    public IReadOnlyList<RunSnapshot> Runs { get; init; } = [];
}
