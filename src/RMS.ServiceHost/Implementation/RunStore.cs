using System.Globalization;
using System.Text;

using RMS.ServiceModel.Implementation;
using RMS.ServiceModel.Models;

namespace RMS.ServiceHost.Implementation;

/// <summary>
/// The directory a service keeps its history in.
/// </summary>
/// <remarks>
/// <para>
/// The layout is one directory per service below the log root, holding one <c>.log</c> and one <c>.run</c> per
/// run, plus an <c>index.ini</c> describing the service. Every file name begins with the run identifier, which
/// begins with the start time, so sorting a directory listing by name orders it by time and no reader needs to
/// open a file to know where it belongs in the sequence.
/// </para>
/// <para>
/// This is the whole of the persistence. There is no database between a service and the page that shows its
/// logs, which means the DevOps site can display a service's history even when the API is down, and an operator
/// can read, copy or archive the history with the tools they already have.
/// </para>
/// </remarks>
public sealed class RunStore
{
    /// <summary>The suffix of a run record.</summary>
    public const string RecordExtension = RunStoreLayout.RecordExtension;

    /// <summary>The suffix of a run log.</summary>
    public const string LogExtension = RunStoreLayout.LogExtension;

    /// <summary>The name of the file describing the service itself.</summary>
    public const string IndexFileName = RunStoreLayout.IndexFileName;

    /// <summary>The format the run identifier's time component is written in.</summary>
    public const string RunIdTimeFormat = RunStoreLayout.RunIdTimeFormat;

    /// <summary>The format version written into the service index, so a reader can refuse one it predates.</summary>
    public const string IndexVersion = RunStoreLayout.IndexVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunStore"/> class.
    /// </summary>
    /// <param name="logRoot">The root directory every service's logs live under.</param>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="retainedRuns">How many runs to keep. Older runs are removed.</param>
    /// <exception cref="InvalidOperationException">Thrown when the service name cannot be used as a path component.</exception>
    public RunStore(string logRoot, string serviceName, int retainedRuns)
    {
        ArgumentNullException.ThrowIfNull(logRoot);

        ServicePlan.ValidateName(serviceName);

        Directory = Path.GetFullPath(Path.Combine(logRoot, serviceName));
        RetainedRuns = retainedRuns < 1 ? 1 : retainedRuns;

        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>
    /// Gets the absolute path of the directory holding this service's history.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Gets how many runs are kept before the oldest are removed.
    /// </summary>
    public int RetainedRuns { get; }

    /// <summary>
    /// Returns the path of a run's log.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The absolute path.</returns>
    public string LogPath(string runId)
    {
        return Path.Combine(Directory, runId + LogExtension);
    }

    /// <summary>
    /// Returns the path of a run's record.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The absolute path.</returns>
    public string RecordPath(string runId)
    {
        return Path.Combine(Directory, runId + RecordExtension);
    }

    /// <summary>
    /// Builds the identifier for the next run.
    /// </summary>
    /// <returns>A run identifier, unique within the service's directory.</returns>
    /// <remarks>
    /// The sequence number is derived from the records already present rather than from a counter file. A counter
    /// file is one more thing that can be lost or left behind by a machine that was restored from a backup, and a
    /// duplicate identifier would mean two runs sharing a log; a scan cannot produce a duplicate, and the
    /// directory is at most <see cref="RetainedRuns"/> entries long.
    /// </remarks>
    public string CreateRunId()
    {
        long highest = 0;

        foreach (string path in System.IO.Directory.EnumerateFiles(Directory, "*" + RecordExtension))
        {
            long sequence = SequenceOf(Path.GetFileNameWithoutExtension(path));

            if (sequence > highest)
                highest = sequence;
        }

        return DateTimeOffset.UtcNow.ToString(RunIdTimeFormat, CultureInfo.InvariantCulture) +
               "-" +
               (highest + 1).ToString("D5", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads the sequence number out of a run identifier.
    /// </summary>
    /// <param name="runId">The identifier to read.</param>
    /// <returns>The sequence number, or zero when the identifier does not follow the convention.</returns>
    public static long SequenceOf(string runId)
    {
        ArgumentNullException.ThrowIfNull(runId);

        int separator = runId.LastIndexOf('-');

        if (separator < 0 || separator + 1 >= runId.Length)
            return 0;

        return long.TryParse(
            runId[(separator + 1)..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long sequence)
            ? sequence
            : 0;
    }

    /// <summary>
    /// Opens the log of a run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The log, which the caller disposes.</returns>
    public RunLog OpenLog(string runId)
    {
        return new RunLog(LogPath(runId));
    }

    /// <summary>
    /// Removes the oldest runs beyond the retention count.
    /// </summary>
    /// <returns>The number of runs removed.</returns>
    /// <remarks>
    /// A run is a pair: a record and a log. They are removed together, because a record without its log is a
    /// history entry an operator cannot act on, and a log without its record is a file nothing references.
    /// </remarks>
    public int Prune()
    {
        List<string> records = [.. System.IO.Directory.EnumerateFiles(Directory, "*" + RecordExtension)];
        records.Sort(StringComparer.Ordinal);

        int removals = records.Count - RetainedRuns;

        if (removals <= 0)
            return 0;

        for (int index = 0; index < removals; index++)
        {
            string runId = Path.GetFileNameWithoutExtension(records[index]);

            TryDelete(records[index]);
            TryDelete(LogPath(runId));
        }

        return removals;
    }

    /// <summary>
    /// Writes the file that describes the service.
    /// </summary>
    /// <param name="plan">The service being run.</param>
    /// <param name="latest">The most recent run, or <see langword="null"/> when there is none yet.</param>
    /// <param name="retained">How many run records are present.</param>
    /// <remarks>
    /// This file is what the DevOps site reads to list the services on a machine, so it repeats the handful of
    /// facts a listing needs. It is written on every state change of the newest run, which keeps it current
    /// without a reader ever having to open the run files to find out which of them is the newest.
    /// </remarks>
    public void WriteIndex(ServicePlan plan, RunRecord? latest, int retained)
    {
        ArgumentNullException.ThrowIfNull(plan);

        IniDocument document = new();
        IniSection section = document.GetOrAddSection(RunStoreLayout.IndexSectionName);

        section.Add(RunStoreLayout.IndexNameKey, plan.EffectiveName);
        section.Add(RunStoreLayout.IndexDescriptionKey, plan.Description);
        section.Add(RunStoreLayout.IndexCommandKey, plan.DescribeCommand());
        section.Add(RunStoreLayout.IndexWorkingDirectoryKey, plan.WorkingDirectory);
        section.Add(RunStoreLayout.IndexLogDirectoryKey, Directory);
        section.Add(RunStoreLayout.IndexRestartKey, plan.RestartKeyword);
        section.Add(RunStoreLayout.IndexRunsKey, retained.ToString(CultureInfo.InvariantCulture));
        section.Add(RunStoreLayout.IndexRetainedRunsKey, RetainedRuns.ToString(CultureInfo.InvariantCulture));
        section.Add(RunStoreLayout.IndexVersionKey, IndexVersion);
        section.Add(RunStoreLayout.IndexUpdatedKey, RunRecord.Format(DateTimeOffset.UtcNow));

        if (latest is not null)
        {
            section.Add(RunStoreLayout.IndexLatestRunKey, latest.Run);
            section.Add(RunStoreLayout.IndexLatestStatusKey, latest.StatusKeyword);
            section.Add(
                RunStoreLayout.IndexLatestStartedKey,
                latest.Started is { } started ? RunRecord.Format(started) : string.Empty);
            section.Add(
                RunStoreLayout.IndexLatestCompletedKey,
                latest.Completed is { } completed ? RunRecord.Format(completed) : string.Empty);
            section.Add(
                RunStoreLayout.IndexLatestExitCodeKey,
                latest.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            section.Add(
                RunStoreLayout.IndexLatestAttemptsKey,
                latest.Attempts.ToString(CultureInfo.InvariantCulture));
        }

        string path = Path.Combine(Directory, IndexFileName);
        string temporary = path + RunStoreLayout.TemporarySuffix;

        File.WriteAllText(temporary, document.ToText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Counts the run records present.
    /// </summary>
    /// <returns>The number of records.</returns>
    public int CountRuns()
    {
        int count = 0;

        foreach (string _ in System.IO.Directory.EnumerateFiles(Directory, "*" + RecordExtension))
            count++;

        return count;
    }

    /// <summary>
    /// Loads the most recent run record, if there is one.
    /// </summary>
    /// <returns>The newest record, or <see langword="null"/>.</returns>
    public RunRecord? LoadLatest()
    {
        string? newest = null;

        foreach (string path in System.IO.Directory.EnumerateFiles(Directory, "*" + RecordExtension))
        {
            if (newest is null || string.CompareOrdinal(path, newest) > 0)
                newest = path;
        }

        if (newest is null)
            return null;

        try
        {
            return RunRecord.Load(newest);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            // A record that cannot be read is not a reason to refuse to start the service. The run that follows
            // will be recorded normally, and the unreadable file stays on disk for an operator.
            return null;
        }
    }

    /// <summary>
    /// Removes a file, treating a failure as housekeeping rather than as an error.
    /// </summary>
    /// <param name="path">The file to remove.</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Retention is housekeeping. Failing to remove an old log must never stop a service from starting, and
            // the next run will try again.
        }
    }
}
