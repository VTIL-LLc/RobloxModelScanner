using RMS.Devops.Service.Models;

namespace RMS.Devops.Service.Interfaces;

/// <summary>
/// Reads slices of a run's log.
/// </summary>
/// <remarks>
/// A log is the one thing in this stack that has no size limit: a service that has been running for a month has
/// written everything it had to say. The interface is therefore framed in slices from the start, so that no
/// caller can be written against a method that reads a whole file and then discover the cost in production.
/// </remarks>
public interface ILogArchive
{
    /// <summary>
    /// Reads a slice of a run's log.
    /// </summary>
    /// <param name="service">The service's name.</param>
    /// <param name="runId">The run's identifier.</param>
    /// <param name="offset">The byte offset to start at. Ignored when <paramref name="tail"/> is set.</param>
    /// <param name="count">
    /// The greatest number of bytes to return. A value of zero or less uses the service's configured maximum.
    /// </param>
    /// <param name="tail">
    /// When set, the slice is the last <paramref name="count"/> bytes rather than the bytes from
    /// <paramref name="offset"/>.
    /// </param>
    /// <returns>The slice, or <see langword="null"/> when there is no log to read.</returns>
    LogSlice? Read(string service, string runId, long offset, int count, bool tail);
}
