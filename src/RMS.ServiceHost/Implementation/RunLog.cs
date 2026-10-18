using System.Text;
using System.Globalization;

using RMS.ServiceModel.Models;

namespace RMS.ServiceHost.Implementation;

/// <summary>
/// The log of one run of one service.
/// </summary>
/// <remarks>
/// <para>
/// One file per run, rather than one file that is appended to and rotated. A rotated log answers "what has this
/// service been doing lately", which is not the question anybody asks; the question is "what did it do during
/// the run that failed", and a file that belongs to exactly one run answers it without any search, without any
/// rotation arithmetic and without a reader having to know where the boundary was.
/// </para>
/// <para>
/// The file is opened for share-all so that the DevOps site, or an operator with a tail, can read it while the
/// run is still going. It is flushed on every line for the same reason: the interesting moment to look at a log
/// is while a service is failing, and a buffer that has not been flushed is a log of the past.
/// </para>
/// </remarks>
public sealed class RunLog : IDisposable
{
    /// <summary>The channel a line written to standard output is tagged with.</summary>
    public const string OutputChannel = "out";

    /// <summary>The channel a line written to standard error is tagged with.</summary>
    public const string ErrorChannel = "err";

    /// <summary>The channel a line written by the supervisor itself is tagged with.</summary>
    public const string HostChannel = "rms";

    private readonly StreamWriter _writer;

    private readonly object _gate = new();

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunLog"/> class.
    /// </summary>
    /// <param name="path">The absolute path of the log file.</param>
    public RunLog(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Append, share everything: the file survives a reader holding it open, and reopening the same run's log
        // after the supervisor restarts continues rather than truncating what is already there.
        FileStream stream = new(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.WriteThrough);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

        Path = path;
    }

    /// <summary>
    /// Gets the absolute path of the file this log writes to.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Writes the banner that opens a run: what was asked to run, and from where.
    /// </summary>
    /// <param name="plan">The service being run.</param>
    /// <param name="runId">The identifier of this run.</param>
    /// <param name="attempt">Which attempt this start is, counting from one.</param>
    /// <param name="hostVersion">The version of the supervisor writing the log.</param>
    public void WriteBanner(ServicePlan plan, string runId, int attempt, string hostVersion)
    {
        ArgumentNullException.ThrowIfNull(plan);

        WriteRaw(new string('=', 78));
        Write(HostChannel, "run " + runId + ", attempt " + attempt + ", started by RMS.ServiceHost " + hostVersion);
        Write(HostChannel, "service   " + plan.EffectiveName);
        Write(HostChannel, "command   " + plan.DescribeCommand());
        Write(HostChannel, "directory " + plan.WorkingDirectory);

        if (plan.Description.Length > 0)
            Write(HostChannel, "about     " + plan.Description);

        // The policy is printed rather than implied. The banner is what an operator reads when a service is not
        // behaving, and "was it even supposed to restart" is the first question, asked while looking at a plan they
        // did not write.
        Write(
            HostChannel,
            "restart   " + plan.RestartKeyword +
            " after " + plan.RestartDelaySeconds.ToString(CultureInfo.InvariantCulture) +
            "s, backing off to " + plan.MaximumRestartDelaySeconds.ToString(CultureInfo.InvariantCulture) + "s");

        Write(
            HostChannel,
            "stop      up to " + plan.StopTimeoutSeconds.ToString(CultureInfo.InvariantCulture) + "s" +
            (plan.ShutdownLine.Length > 0
                ? ", asked to shut down on its standard input"
                : ", asked to close its window"));

        if (plan.Environment.Count > 0)
        {
            // The names are listed but not the values. Several of these carry a credential -- the indexer's token
            // path, the scanner's API key -- and a log that an operator pastes into a report is one of the ways a
            // secret escapes a machine.
            List<string> names = [.. plan.Environment.Keys];
            names.Sort(StringComparer.Ordinal);

            Write(HostChannel, "environment " + names.Count + " variable(s): " + string.Join(", ", names));
        }

        WriteRaw(new string('=', 78));
    }

    /// <summary>
    /// Writes a line from one of the child's streams, or from the supervisor.
    /// </summary>
    /// <param name="channel">The channel, which is one of the constants on this class.</param>
    /// <param name="line">The line to write, without its terminator.</param>
    public void Write(string channel, string line)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _writer.Write('[');
            _writer.Write(RunRecord.Format(DateTimeOffset.UtcNow));
            _writer.Write("] [");
            _writer.Write(channel);
            _writer.Write("] ");
            _writer.Write(line);
            _writer.Write('\n');
        }
    }

    /// <summary>
    /// Writes text exactly as given, with no timestamp or channel prefix.
    /// </summary>
    /// <param name="text">The text to write.</param>
    public void WriteRaw(string text)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _writer.Write(text);
            _writer.Write('\n');
        }
    }

    /// <summary>
    /// Flushes anything buffered. Every write already flushes; this exists for the shutdown path, where the
    /// caller wants the file current before it stops waiting.
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _writer.Flush();
        }
    }

    /// <summary>
    /// Closes the log.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _writer.Dispose();
        }
    }
}
