namespace Scanner.Indexer.Models;

/// <summary>
/// The resolved, absolute file locations the indexing service operates on.
/// </summary>
/// <remarks>
/// Resolving the paths once, at start-up, removes a whole class of errors: the service can no longer read a
/// relative catalogue from an unpredictable working directory, and the index is guaranteed to be written
/// beside the catalogue rather than somewhere a reader will not look for it.
/// </remarks>
/// <param name="CataloguePath">The absolute path to the scanner's catalogue database.</param>
/// <param name="IndexPath">The absolute path to the SQLite index database this service owns.</param>
public sealed record IndexerPaths(string CataloguePath, string IndexPath);
