using RMS.Devops.Service.Models;

namespace RMS.Devops.Service.Interfaces;

/// <summary>
/// Summarizes the scanner's catalogue index.
/// </summary>
/// <remarks>
/// This is a read-only view of a file another process owns. The connection is opened read-only and the queries
/// only touch the derived statistics tables, so a request here can never block the indexing service's
/// migration, and it can never return a number that disagrees with the table it came from.
/// </remarks>
public interface ICatalogStatistics
{
    /// <summary>
    /// Summarizes the index.
    /// </summary>
    /// <returns>The summary, describing an unavailable index when one could not be read.</returns>
    CatalogSummary Get();
}
