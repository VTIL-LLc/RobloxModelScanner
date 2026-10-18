namespace RMS.Devops.Service.Models;

/// <summary>
/// One run, with the details that only its own page needs.
/// </summary>
/// <remarks>
/// The command line and the process identifier are here rather than on <see cref="RunSnapshot"/> because a
/// listing that carried them would be describing how to start a service on a page whose reader is looking for
/// which one stopped. The distinction is not tidiness: it is the difference between a table an operator can
/// scan and one they have to read.
/// </remarks>
public sealed class RunDetail
{
    /// <summary>
    /// Gets the run as a listing shows it.
    /// </summary>
    public required RunSnapshot Summary { get; init; }

    /// <summary>
    /// Gets the command line the supervisor ran.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// Gets the identifier of the process of the most recent attempt.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// Gets the absolute path of the run's log file.
    /// </summary>
    public string LogFile { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tail of the run's log, so the page has something to show before its first poll.
    /// </summary>
    /// <remarks>
    /// Rendering the tail server-side means the page is useful with scripts disabled and on the first paint,
    /// which is the same reason the deployment pages this dashboard reproduces render their initial rows
    /// rather than waiting for an AJAX call to fill an empty table.
    /// </remarks>
    public string LogTail { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the tail is a suffix of a longer log.
    /// </summary>
    public bool LogTruncated { get; init; }
}
