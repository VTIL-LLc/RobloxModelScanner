using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Interfaces;

/// <summary>
/// Produces aggregate analytics over the indexed catalogue.
/// </summary>
/// <remarks>
/// Every figure is computed by the database from indexed columns and normalized detection rows, so
/// the cost of the analytics surface is independent of how large the JSON catalogue has grown.
/// </remarks>
public interface IAnalyticsService
{
    /// <summary>
    /// Builds the complete analytics summary for the indexed catalogue.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The totals, rule breakdown, category roll-up, histogram, timeline and creator leaderboard.</returns>
    Task<AnalyticsSummary> GetSummaryAsync(CancellationToken cancellationToken);
}
