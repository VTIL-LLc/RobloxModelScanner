using System.Globalization;

using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Models;

namespace RMS.Devops.Service.Models;

/// <summary>
/// One supervised run of one service, as a listing shows it.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape of a row in the run tables, and it is deliberately the same set of fields the deployment
/// task page this dashboard reproduces shows: created, started, completed, run time, attempts. An operator who
/// has read a deployment task already knows how to read a run.
/// </para>
/// <para>
/// The timestamps are carried as instants and formatted by whoever renders them, so that the same record can
/// appear in a page, in a JSON response and in a log line without three different spellings of the same
/// moment. The one exception is <see cref="RuntimeText"/>, which is taken from the record itself: the text a
/// supervisor wrote when it last touched the file is the text the run's own log banner shows, and a page that
/// recomputed it could disagree with the log it is describing.
/// </para>
/// </remarks>
public sealed class RunSnapshot
{
    /// <summary>
    /// Gets the identifier of the run, unique within its service.
    /// </summary>
    public required string Run { get; init; }

    /// <summary>
    /// Gets the name of the service the run belongs to.
    /// </summary>
    public required string Service { get; init; }

    /// <summary>
    /// Gets the position of the run within its service, counting from one.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// Gets the status as the keyword the record file uses.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the run has reached a terminal status.
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Gets when the supervisor created the record.
    /// </summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>
    /// Gets when the child process was first started.
    /// </summary>
    public DateTimeOffset? Started { get; init; }

    /// <summary>
    /// Gets when the record was last written.
    /// </summary>
    public DateTimeOffset Updated { get; init; }

    /// <summary>
    /// Gets when the run reached a terminal status.
    /// </summary>
    public DateTimeOffset? Completed { get; init; }

    /// <summary>
    /// Gets the elapsed seconds of the run.
    /// </summary>
    public long RuntimeSeconds { get; init; }

    /// <summary>
    /// Gets the elapsed time as the supervisor last rendered it.
    /// </summary>
    public string RuntimeText { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many times the process was started during the run.
    /// </summary>
    public int Attempts { get; init; }

    /// <summary>
    /// Gets the exit code of the most recent attempt, when it exited.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the size of the run's log file in bytes.
    /// </summary>
    /// <remarks>
    /// Zero means the run has produced no output yet or its log was removed while the record survived. A page
    /// shows the difference between "nothing to read" and "cannot read it" by the presence of a link, which is
    /// why the size is carried here instead of being discovered by opening the file.
    /// </remarks>
    public long LogBytes { get; init; }

    /// <summary>
    /// Builds a snapshot from a run record.
    /// </summary>
    /// <param name="record">The record to snapshot.</param>
    /// <param name="logBytes">The size of the record's log file.</param>
    /// <returns>The snapshot.</returns>
    public static RunSnapshot From(RunRecord record, long logBytes)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new RunSnapshot
        {
            Run = record.Run,
            Service = record.Service,
            Sequence = record.Sequence,
            Status = record.StatusKeyword,
            IsComplete = record.IsComplete,
            Created = record.Created,
            Started = record.Started,
            Updated = record.Updated,
            Completed = record.Completed,
            RuntimeSeconds = (long)record.Runtime.TotalSeconds,
            RuntimeText = FormatRuntime(record.Runtime),
            Attempts = record.Attempts,
            ExitCode = record.ExitCode,
            LogBytes = logBytes,
        };
    }

    /// <summary>
    /// Renders an elapsed time the way the record and the site both do.
    /// </summary>
    /// <param name="runtime">The elapsed time.</param>
    /// <returns>
    /// Whole seconds below a minute, and minutes with two decimal places above it, matching the deployment
    /// pages this dashboard reproduces.
    /// </returns>
    public static string FormatRuntime(TimeSpan runtime)
    {
        if (runtime < TimeSpan.Zero)
            runtime = TimeSpan.Zero;

        return runtime.TotalSeconds < 60
            ? ((long)runtime.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " sec"
            : runtime.TotalMinutes.ToString("0.00", CultureInfo.InvariantCulture) + " minutes";
    }

    /// <summary>
    /// Maps a status onto the state a service is in when that run is its newest.
    /// </summary>
    /// <param name="status">The status of the newest run.</param>
    /// <returns>The state.</returns>
    public static Enums.FleetState StateOf(RunStatus status)
    {
        return status switch
        {
            RunStatus.Running => Enums.FleetState.Running,
            RunStatus.Succeeded => Enums.FleetState.Healthy,
            RunStatus.Failed => Enums.FleetState.Failed,
            RunStatus.Stopped => Enums.FleetState.Stopped,
            _ => Enums.FleetState.Abandoned,
        };
    }
}
