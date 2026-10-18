using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Interfaces;

/// <summary>
/// Reads the model catalogue for browsing and lookup.
/// </summary>
/// <remarks>
/// Implementations query the SQLite read model, so filtering, ordering and paging are all performed
/// by the database engine against indexed columns rather than in memory.
/// </remarks>
public interface IModelCatalogRepository
{
    /// <summary>
    /// Searches the catalogue with the supplied filters, ordering and paging.
    /// </summary>
    /// <param name="query">The normalized query produced from the request.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The requested page together with the size of the whole matching set.</returns>
    Task<PagedResult<ModelEntry>> SearchAsync(ModelQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a single catalogue row by asset identifier.
    /// </summary>
    /// <param name="modelId">The Roblox asset identifier.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The matching row, or <see langword="null"/> when the asset is not indexed.</returns>
    Task<ModelEntry?> GetByIdAsync(long modelId, CancellationToken cancellationToken);
}
