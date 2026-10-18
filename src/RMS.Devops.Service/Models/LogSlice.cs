namespace RMS.Devops.Service.Models;

/// <summary>
/// A slice of a run's log.
/// </summary>
/// <remarks>
/// <para>
/// The log of a long-running service is read in slices rather than as a file. A page that follows a running
/// service asks for what has been appended since the last thing it saw, and a page that opens an old log asks
/// for its tail; neither of them should have to transfer a file that may be hundreds of megabytes to show
/// twenty lines.
/// </para>
/// <para>
/// <see cref="Offset"/> and <see cref="Length"/> are byte offsets into the file, not character offsets. The
/// two are the same for the ASCII that the stack mostly writes and different for everything else, which is why
/// the reader works in bytes and why <see cref="Complete"/> records whether the slice reached the end: a
/// caller that has the end can stop asking.
/// </para>
/// </remarks>
public sealed class LogSlice
{
    /// <summary>
    /// Gets the name of the service the log belongs to.
    /// </summary>
    public required string Service { get; init; }

    /// <summary>
    /// Gets the identifier of the run the log belongs to.
    /// </summary>
    public required string Run { get; init; }

    /// <summary>
    /// Gets the text of the slice.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the byte offset the slice starts at.
    /// </summary>
    public long Offset { get; init; }

    /// <summary>
    /// Gets the number of bytes the slice holds.
    /// </summary>
    public long Length { get; init; }

    /// <summary>
    /// Gets the size of the whole log in bytes.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Gets a value indicating whether the slice reached the end of the log.
    /// </summary>
    public bool Complete { get; init; }

    /// <summary>
    /// Gets a value indicating whether the slice was cut short by the service's own size limit.
    /// </summary>
    /// <remarks>
    /// A slice can fail to reach the end because the file ended or because the caller was given as much as it
    /// may have. The two are different situations for a client: the first means stop asking, the second means
    /// ask again from <see cref="Offset"/> plus <see cref="Length"/>.
    /// </remarks>
    public bool LimitReached { get; init; }
}
