namespace Scanner.Indexer.Enums;

/// <summary>
/// Describes how a migration pass ended.
/// </summary>
/// <remarks>
/// The outcome is returned to a caller that explicitly requested a rebuild, so it can distinguish
/// "nothing to do" from "the document is unusable" without parsing the status payload.
/// </remarks>
public enum IndexBuildOutcome
{
    /// <summary>No migration ran because the index already reflects the current catalogue revision.</summary>
    UpToDate = 0,

    /// <summary>A migration committed and the index now reflects the current catalogue revision.</summary>
    Rebuilt = 1,

    /// <summary>The catalogue document is absent, so there was nothing to index.</summary>
    CatalogueMissing = 2,

    /// <summary>The catalogue document could not be parsed; the previous index was retained.</summary>
    CatalogueInvalid = 3,

    /// <summary>The index database could not be written; the previous index was retained.</summary>
    StorageFailed = 4,

    /// <summary>Another migration was already running and the caller did not wait for it.</summary>
    Busy = 5
}
