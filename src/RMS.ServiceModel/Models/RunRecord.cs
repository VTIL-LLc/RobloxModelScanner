using System.Globalization;

using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Implementation;

namespace RMS.ServiceModel.Models;

/// <summary>
/// A record of one supervised run of one service, written as the run proceeds.
/// </summary>
/// <remarks>
/// <para>
/// One file per run, under <c>&lt;log root&gt;/&lt;service&gt;/</c>. The file is rewritten in place at each
/// state change, so a reader always sees a complete record rather than a partially appended one, and the
/// record's own fields are the index: a page lists a service's history by enumerating the directory and reading
/// these keys, with no database between the supervisor and the page.
/// </para>
/// <para>
/// The field names follow the deployment task page the DevOps site reproduces -- created, started, completed,
/// run time, attempts, command -- so that a run of a local service and a run of a deployment task are rendered
/// by the same code.
/// </para>
/// </remarks>
public sealed class RunRecord
{
    /// <summary>The key holding the run identifier.</summary>
    public const string RunKey = "run";

    /// <summary>The key holding the service name.</summary>
    public const string ServiceKey = "service";

    /// <summary>The key holding the status.</summary>
    public const string StatusKey = "status";

    /// <summary>The key holding the run's position within its service.</summary>
    public const string SequenceKey = "sequence";

    /// <summary>The key holding the time the run was created.</summary>
    public const string CreatedKey = "created";

    /// <summary>The key holding the time the process was started.</summary>
    public const string StartedKey = "started";

    /// <summary>The key holding the time the record was last changed.</summary>
    public const string UpdatedKey = "updated";

    /// <summary>The key holding the time the run finished.</summary>
    public const string CompletedKey = "completed";

    /// <summary>The key holding the elapsed seconds.</summary>
    public const string RuntimeSecondsKey = "runtime_seconds";

    /// <summary>The key holding the elapsed time as displayed text.</summary>
    public const string RuntimeTextKey = "runtime_text";

    /// <summary>The key holding the number of start attempts.</summary>
    public const string AttemptsKey = "attempts";

    /// <summary>The key holding the process exit code.</summary>
    public const string ExitCodeKey = "exit_code";

    /// <summary>The key holding the process identifier.</summary>
    public const string ProcessIdKey = "pid";

    /// <summary>The key holding the command line.</summary>
    public const string CommandKey = "command";

    /// <summary>The key holding the path of the log file the run wrote.</summary>
    public const string LogFileKey = "log_file";

    /// <summary>The ISO-8601 form the timestamps are written in.</summary>
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>
    /// Gets or sets the identifier of the run, unique within a service's directory.
    /// </summary>
    public required string Run { get; set; }

    /// <summary>
    /// Gets or sets the name of the service that was run.
    /// </summary>
    public required string Service { get; set; }

    /// <summary>
    /// Gets or sets the current status of the run.
    /// </summary>
    public RunStatus Status { get; set; } = RunStatus.Running;

    /// <summary>
    /// Gets or sets the number of this run within its service, counting from one.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// Gets or sets when the supervisor created the record, in UTC.
    /// </summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when the child process was first started, in UTC.
    /// </summary>
    public DateTimeOffset? Started { get; set; }

    /// <summary>
    /// Gets or sets when the record was last written, in UTC.
    /// </summary>
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when the run reached a terminal status, in UTC.
    /// </summary>
    public DateTimeOffset? Completed { get; set; }

    /// <summary>
    /// Gets or sets how many times the process was started during this run.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets the exit code of the most recent attempt, when it has exited.
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Gets or sets the process identifier of the most recent attempt.
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// Gets or sets the command that was run.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the log file the run wrote.
    /// </summary>
    public string LogFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets the elapsed time of the run.
    /// </summary>
    public TimeSpan Runtime
    {
        get
        {
            if (Started is not { } started)
                return TimeSpan.Zero;

            return (Completed ?? DateTimeOffset.UtcNow) - started;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the run has finished.
    /// </summary>
    public bool IsComplete => Status != RunStatus.Running;

    /// <summary>
    /// Gets the status as the keyword the record file and the site's pages use.
    /// </summary>
    public string StatusKeyword => KeywordOf(Status);

    /// <summary>
    /// Gets the keyword for a status.
    /// </summary>
    /// <param name="status">The status to name.</param>
    /// <returns>The lower-case keyword.</returns>
    public static string KeywordOf(RunStatus status)
    {
        return status switch
        {
            RunStatus.Running => "running",
            RunStatus.Succeeded => "succeeded",
            RunStatus.Failed => "failed",
            RunStatus.Stopped => "stopped",
            _ => "abandoned",
        };
    }

    /// <summary>
    /// Parses a status keyword.
    /// </summary>
    /// <param name="value">The keyword to parse.</param>
    /// <returns>
    /// The status the keyword names, or <see cref="RunStatus.Abandoned"/> when it is unrecognised. The
    /// alternative -- a default of success -- would let a page report a run as healthy on the strength of a
    /// word it did not understand.
    /// </returns>
    public static RunStatus ParseStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "running" => RunStatus.Running,
            "succeeded" or "success" => RunStatus.Succeeded,
            "failed" or "failure" => RunStatus.Failed,
            "stopped" => RunStatus.Stopped,
            _ => RunStatus.Abandoned,
        };
    }

    /// <summary>
    /// Formats a timestamp in the form the records are written in.
    /// </summary>
    /// <param name="value">The timestamp to format.</param>
    /// <returns>The UTC timestamp.</returns>
    public static string Format(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a timestamp written by <see cref="Format"/>.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <returns>The timestamp, or <see langword="null"/> when the text is empty or unparsable.</returns>
    public static DateTimeOffset? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Saves the record, replacing whatever was there.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <remarks>
    /// The write is atomic from a reader's point of view: the text goes to a sibling temporary file first and is
    /// then moved over the target. The DevOps site polls this directory while services are running, and a reader
    /// that caught a half-written file would report a status of "abandoned" for a service that is perfectly
    /// healthy.
    /// </remarks>
    public void Save(string path)
    {
        Updated = DateTimeOffset.UtcNow;

        IniDocument document = new();
        IniSection section = document.GetOrAddSection(Service);

        section.Add(RunKey, Run);
        section.Add(ServiceKey, Service);
        section.Add(StatusKey, StatusKeyword);
        section.Add(SequenceKey, Sequence.ToString(CultureInfo.InvariantCulture));
        section.Add(CreatedKey, Format(Created));
        section.Add(StartedKey, Started is { } started ? Format(started) : string.Empty);
        section.Add(UpdatedKey, Format(Updated));
        section.Add(CompletedKey, Completed is { } completed ? Format(completed) : string.Empty);
        section.Add(RuntimeSecondsKey, ((long)Runtime.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        section.Add(RuntimeTextKey, ServicePlan.FormatRunTime(Runtime));
        section.Add(AttemptsKey, Attempts.ToString(CultureInfo.InvariantCulture));
        section.Add(ExitCodeKey, ExitCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        section.Add(ProcessIdKey, ProcessId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        section.Add(CommandKey, Command);
        section.Add(LogFileKey, LogFile);

        string body = document.ToText();
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
            System.IO.Directory.CreateDirectory(directory);

        string temporary = path + ".tmp";

        File.WriteAllText(temporary, body, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Loads a record.
    /// </summary>
    /// <param name="path">The record file to read.</param>
    /// <returns>The record.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file contains no record.</exception>
    public static RunRecord Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("No run record at '" + path + "'.", path);

        IniDocument document = IniDocument.Load(path);

        foreach (IniSection section in document.Sections)
        {
            if (ReferenceEquals(section, document.Leading) ||
                section.Name.Equals(PlanFile.SettingsSection, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return FromSection(section, path);
        }

        throw new InvalidDataException("'" + path + "' contains no run record.");
    }

    /// <summary>
    /// Reads a record from a section.
    /// </summary>
    /// <param name="section">The section to read.</param>
    /// <param name="origin">A label used in exception messages.</param>
    /// <returns>The record the section describes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the section names no run or no service.</exception>
    public static RunRecord FromSection(IniSection section, string origin)
    {
        ArgumentNullException.ThrowIfNull(section);

        string run = section.Get(RunKey);
        string service = section.Get(ServiceKey, section.Name);

        if (string.IsNullOrWhiteSpace(run))
            throw new InvalidDataException(origin + ": the record does not set '" + RunKey + "'.");

        if (string.IsNullOrWhiteSpace(service))
            throw new InvalidDataException(origin + ": the record does not set '" + ServiceKey + "'.");

        RunRecord record = new()
        {
            Run = run,
            Service = service,
            Status = ParseStatus(section.Get(StatusKey)),
            Sequence = int.TryParse(
                section.Get(SequenceKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sequence)
                ? sequence
                : 0,
            Command = section.Get(CommandKey),
            LogFile = section.Get(LogFileKey),
            Attempts = section.GetInt32(AttemptsKey, 0),
            Created = TryParse(section.Get(CreatedKey)) ?? DateTimeOffset.UtcNow,
            Started = TryParse(section.Get(StartedKey)),
            Updated = TryParse(section.Get(UpdatedKey)) ?? DateTimeOffset.UtcNow,
            Completed = TryParse(section.Get(CompletedKey)),
        };

        if (int.TryParse(section.Get(ExitCodeKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int exitCode))
            record.ExitCode = exitCode;

        if (int.TryParse(section.Get(ProcessIdKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
            record.ProcessId = processId;

        return record;
    }
}
