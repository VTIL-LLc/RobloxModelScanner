namespace Scanner.MicroApi.Enums;

/// <summary>
/// Describes whether the SQLite index can be read.
/// </summary>
/// <remarks>
/// The index is written by the indexing service and read here, so it can legitimately be absent -- a
/// deployment that has never run the indexer, or one whose index is still being built -- and can go
/// missing again if an operator moves the file. Distinguishing the reasons lets the API answer with a
/// sentence an operator can act on instead of a bare server error.
/// </remarks>
public enum IndexAvailability
{
    /// <summary>
    /// No index file exists at the configured location.
    /// </summary>
    Missing = 0,

    /// <summary>
    /// The index exists and was built with a schema this API understands.
    /// </summary>
    Ready = 1,

    /// <summary>
    /// The index exists but was built with a schema this API does not understand.
    /// </summary>
    /// <remarks>
    /// This is the state a half-finished deployment leaves behind: the API has been replaced and the
    /// indexer has not, or the reverse. Refusing to read it turns a silent misread of renamed columns
    /// into an explicit message naming the versions involved.
    /// </remarks>
    Incompatible = 2,

    /// <summary>
    /// The index exists but could not be opened or read.
    /// </summary>
    /// <remarks>
    /// The usual cause is file permissions rather than corruption: a read-only connection to a
    /// write-ahead-logging database needs write access to the shared-memory file beside it, which is the
    /// one place the read-only guarantee is enforced by the operating system rather than by the database.
    /// </remarks>
    Unreadable = 3
}
