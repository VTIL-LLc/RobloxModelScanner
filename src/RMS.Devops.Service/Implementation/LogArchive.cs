using Microsoft.Extensions.Options;

using RMS.Devops.Service.Interfaces;
using RMS.Devops.Service.Models;
using RMS.Devops.Service.Options;
using RMS.ServiceModel.Implementation;

namespace RMS.Devops.Service.Implementation;

/// <summary>
/// Serves slices of a run's log file.
/// </summary>
/// <remarks>
/// <para>
/// The log is the only artefact in this stack with no natural size limit, so it is the only one that is
/// served in pieces. A page that opens a finished run asks for the tail and gets the last few kilobytes; a page
/// that is following a running run asks for everything after the offset it has already shown and gets nothing
/// until the service says something else. Neither request transfers a file.
/// </para>
/// <para>
/// Reads never take a lock that would stop the writer. The supervisor opens its log for append and this opens
/// the same file for read with sharing permitted, so a service that has been running for a month can be watched
/// without a moment's interruption -- which is the only time watching it matters.
/// </para>
/// <para>
/// The offsets are byte offsets. A slice may therefore begin or end inside a multi-byte character, and the
/// decoded text will contain a replacement character at that boundary. That is the honest outcome: the caller
/// asked for bytes, the writer wrote bytes, and pretending otherwise would mean reading to a character boundary
/// the file may not reach for a long time. A caller that wants whole lines asks for a tail, which is aligned by
/// its own nature.
/// </para>
/// </remarks>
public sealed class LogArchive : ILogArchive
{
    private readonly IServiceArchive _archive;
    private readonly DevopsOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogArchive"/> class.
    /// </summary>
    /// <param name="archive">The archive that resolves a service name to its run directory.</param>
    /// <param name="options">The bound DevOps options, which carry the size limit.</param>
    public LogArchive(IServiceArchive archive, IOptions<DevopsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(options);

        _archive = archive;
        _options = options.Value;
    }

    /// <inheritdoc />
    public LogSlice? Read(string service, string runId, long offset, int count, bool tail)
    {
        if (!RunStoreLayout.IsValidRunId(runId)) return null;

        if (_archive.FindRunDirectory(service) is not { } directory) return null;

        FileInfo file = new(Path.Combine(directory, runId + RunStoreLayout.LogExtension));
        if (!file.Exists) return null;

        int limit = count > 0 ? Math.Min(count, _options.MaximumLogBytes) : _options.MaximumLogBytes;
        long total = file.Length;
        long start = tail ? Math.Max(0, total - limit) : Math.Clamp(offset, 0, total);

        (string text, long at, int bytes, bool complete) = ServiceArchive.ReadSlice(file, start, limit);

        return new LogSlice
        {
            Service = service,
            Run = runId,
            Text = text,
            Offset = at,
            Length = bytes,
            TotalBytes = total,
            Complete = complete,
            LimitReached = !complete,
        };
    }
}
