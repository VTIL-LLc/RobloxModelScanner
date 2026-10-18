using System.Globalization;

namespace RMS.ServiceModel.Implementation;

/// <summary>
/// The names, extensions and keys that make up a service's run store on disk.
/// </summary>
/// <remarks>
/// <para>
/// A run store is a directory per service below the log root, holding one <c>.log</c> and one <c>.run</c> per
/// run, plus an <c>index.ini</c> summarizing the service. That layout is a contract between three processes
/// that are deployed separately: the supervisor writes it, the DevOps service reads it, and an operator reads
/// it with <c>less</c>. A layout three parties depend on and one party defines is a layout that drifts, so the
/// names live in the shared assembly and every process refers to these constants rather than to a string of
/// its own.
/// </para>
/// <para>
/// Nothing here is behaviour. It is the vocabulary, and it is deliberately the whole of it: if a file name or a
/// key is not mentioned in this class, it is not part of the store.
/// </para>
/// </remarks>
public static class RunStoreLayout
{
    /// <summary>
    /// The extension of a run's record file.
    /// </summary>
    public const string RecordExtension = ".run";

    /// <summary>
    /// The extension of a run's log file.
    /// </summary>
    public const string LogExtension = ".log";

    /// <summary>
    /// The name of the file describing a service's store.
    /// </summary>
    public const string IndexFileName = "index.ini";

    /// <summary>
    /// The suffix a half-written index file carries before it is replaced.
    /// </summary>
    /// <remarks>
    /// The index is replaced by writing a sibling and moving it into place, so a reader either sees the whole
    /// of the previous index or the whole of the next one, never a torn file.
    /// </remarks>
    public const string TemporarySuffix = ".tmp";

    /// <summary>
    /// The format of the timestamp prefix of a run identifier.
    /// </summary>
    /// <remarks>
    /// A run identifier begins with the moment the run was created, so sorting a directory listing by name
    /// orders it by time and no reader has to open a file to know where it belongs in the sequence.
    /// </remarks>
    public const string RunIdTimeFormat = "yyyyMMdd-HHmmssfff";

    /// <summary>
    /// The version of the index file's layout.
    /// </summary>
    public const string IndexVersion = "1";

    /// <summary>
    /// The section an index file keeps its single service in.
    /// </summary>
    public const string IndexSectionName = "service";

    /// <summary>
    /// The index key holding the service's name.
    /// </summary>
    public const string IndexNameKey = "name";

    /// <summary>
    /// The index key holding the service's description.
    /// </summary>
    public const string IndexDescriptionKey = "description";

    /// <summary>
    /// The index key holding the command line the supervisor runs.
    /// </summary>
    public const string IndexCommandKey = "command";

    /// <summary>
    /// The index key holding the directory the service runs in.
    /// </summary>
    public const string IndexWorkingDirectoryKey = "working_dir";

    /// <summary>
    /// The index key holding the directory the service's runs are recorded under.
    /// </summary>
    public const string IndexLogDirectoryKey = "log_dir";

    /// <summary>
    /// The index key holding the restart policy.
    /// </summary>
    public const string IndexRestartKey = "restart";

    /// <summary>
    /// The index key holding how many run records are present.
    /// </summary>
    public const string IndexRunsKey = "runs";

    /// <summary>
    /// The index key holding how many run records are kept before the oldest are removed.
    /// </summary>
    public const string IndexRetainedRunsKey = "retained_runs";

    /// <summary>
    /// The index key holding the layout version of the index itself.
    /// </summary>
    public const string IndexVersionKey = "index_version";

    /// <summary>
    /// The index key holding when the index was last written.
    /// </summary>
    public const string IndexUpdatedKey = "updated";

    /// <summary>
    /// The index key holding the identifier of the newest run.
    /// </summary>
    public const string IndexLatestRunKey = "latest_run";

    /// <summary>
    /// The index key holding the status keyword of the newest run.
    /// </summary>
    public const string IndexLatestStatusKey = "latest_status";

    /// <summary>
    /// The index key holding when the newest run's process was first started.
    /// </summary>
    public const string IndexLatestStartedKey = "latest_started";

    /// <summary>
    /// The index key holding when the newest run finished.
    /// </summary>
    public const string IndexLatestCompletedKey = "latest_completed";

    /// <summary>
    /// The index key holding the exit code of the newest run's most recent attempt.
    /// </summary>
    public const string IndexLatestExitCodeKey = "latest_exit_code";

    /// <summary>
    /// The index key holding the number of start attempts in the newest run.
    /// </summary>
    public const string IndexLatestAttemptsKey = "latest_attempts";

    /// <summary>
    /// The longest run identifier the layout admits.
    /// </summary>
    private const int MaximumRunIdLength = 191;

    /// <summary>
    /// Determines whether a string can be used as a run identifier.
    /// </summary>
    /// <param name="value">The candidate identifier.</param>
    /// <returns><see langword="true"/> when the value is a usable identifier.</returns>
    /// <remarks>
    /// A run identifier reaches the file system, so it is validated before it is joined onto a path. Only
    /// letters, digits and hyphens are admitted, which is exactly what <see cref="RunIdTimeFormat"/> produces
    /// followed by a hyphen and a sequence number; a dot is refused with everything else because
    /// <c>.</c> and <c>..</c> are directory entries, and an identifier that is a directory entry is a way out
    /// of the directory it was meant to name.
    /// </remarks>
    public static bool IsValidRunId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumRunIdLength) return false;

        foreach (char character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '-') continue;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Formats the count a run store's index records for its retained-run limit.
    /// </summary>
    /// <param name="retained">The number of runs kept.</param>
    /// <returns>The count as the index writes it.</returns>
    public static string FormatCount(int retained) => retained.ToString(CultureInfo.InvariantCulture);
}
