using Scanner.Indexer.Models;

namespace Scanner.Indexer.Interfaces;

/// <summary>
/// Produces the service's current view of the catalogue and the index.
/// </summary>
/// <remarks>
/// The snapshot is assembled from the index's own bookkeeping and a single stat of the catalogue, so it is
/// safe to poll and cannot itself trigger work.
/// </remarks>
public interface IIndexStatusProvider
{
    /// <summary>
    /// Builds the current status snapshot.
    /// </summary>
    /// <returns>The status snapshot.</returns>
    IndexStatus GetStatus();
}
