using System.Diagnostics;

using RMS.ServiceModel.Models;

namespace RMS.ServiceHost.Implementation;

/// <summary>
/// One launch of a service's program, with its output captured and its lifetime observable.
/// </summary>
/// <remarks>
/// <para>
/// The streams are read with explicit readers rather than through the
/// <see cref="Process.OutputDataReceived"/> event. The event is delivered per line and drops whatever the
/// process wrote last if that line had no terminator, which is exactly the line a program writes while it is
/// dying; a reader loop over the pipe keeps it.
/// </para>
/// <para>
/// The child is never asked to share this process's console and is never given a shell. Both of those exist to
/// keep a service's behaviour the same whether it was started by the service control manager, by systemd or by
/// an operator at a prompt.
/// </para>
/// </remarks>
public sealed class LaunchedProcess : IDisposable
{
    /// <summary>How long the readers are given to see end-of-stream after the process exits.</summary>
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(5);

    private readonly Process _process;

    private readonly RunLog _log;

    private readonly Task _drained;

    private bool _disposed;

    private LaunchedProcess(Process process, RunLog log, Task<int> completion, Task drained)
    {
        _process = process;
        _log = log;

        Completion = completion;
        _drained = drained;
    }

    /// <summary>
    /// Gets the process identifier of the child.
    /// </summary>
    public int ProcessId => _process.Id;

    /// <summary>
    /// Gets a task that completes with the child's exit code.
    /// </summary>
    /// <remarks>
    /// The task completes only after both output streams have reached their end, so a line the child wrote
    /// immediately before exiting is in the log by the time the caller observes the exit.
    /// </remarks>
    public Task<int> Completion { get; }

    /// <summary>
    /// Starts a service's program.
    /// </summary>
    /// <param name="plan">The service to start.</param>
    /// <param name="log">The log the output is written to.</param>
    /// <returns>The running process.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the program cannot be started, with the reason the platform gave.</exception>
    public static LaunchedProcess Start(ServicePlan plan, RunLog log)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(log);

        ProcessStartInfo startInfo = new()
        {
            FileName = plan.Executable,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // Input is redirected only when a shutdown line was declared. Redirecting it unconditionally would
            // hand every child a pipe that nothing ever writes to and that closes the moment the supervisor
            // exits, which a program reading its input would see as an immediate end of file.
            RedirectStandardInput = plan.ShutdownLine.Length > 0,
        };

        // ArgumentList, not Arguments: the list form quotes each argument the way the platform's own C runtime
        // expects, so an argument containing a space or a quote survives without this code having to know the
        // quoting rules of three different systems.
        foreach (string argument in plan.Arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (KeyValuePair<string, string> variable in plan.Environment)
            startInfo.Environment[variable.Key] = variable.Value;

        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = false,
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException(
                    "The operating system refused to start '" + plan.Executable + "' without giving a reason.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            process.Dispose();

            throw new InvalidOperationException(
                "Could not start '" + plan.Executable + "': " + exception.Message,
                exception);
        }

        Task drained = Task.WhenAll(
            DrainAsync(process.StandardOutput, RunLog.OutputChannel, log),
            DrainAsync(process.StandardError, RunLog.ErrorChannel, log));

        Task<int> completion = CompleteAsync(process, drained);

        return new LaunchedProcess(process, log, completion, drained);
    }

    /// <summary>
    /// Writes the declared shutdown line to the child's input and closes it.
    /// </summary>
    /// <param name="line">The line to write.</param>
    /// <returns>A task that completes once the line has been written.</returns>
    public async Task WriteShutdownAsync(string line)
    {
        if (_disposed || _process.HasExited)
            return;

        try
        {
            await _process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
            _process.StandardInput.Close();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The child may have exited between the check and the write, or may never have had a usable input
            // pipe. Either way the kill that follows is the answer, so the failure is reported and not thrown.
            _log.Write(RunLog.HostChannel, "could not write the shutdown line: " + exception.Message);
        }
    }

    /// <summary>
    /// Asks the child's window to close, which is the only graceful request available to a process whose
    /// standard input is not being used.
    /// </summary>
    /// <returns><see langword="true"/> when a close request was delivered.</returns>
    public bool TryCloseMainWindow()
    {
        if (_disposed || _process.HasExited)
            return false;

        try
        {
            return _process.CloseMainWindow();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Kills the child and everything it started.
    /// </summary>
    /// <remarks>
    /// The whole tree is killed rather than just the child. A service that has spawned a helper would otherwise
    /// leave it running, holding the port or the index lock that the next run needs, and the restart would fail
    /// for a reason that is invisible in this service's own log.
    /// </remarks>
    public void Kill()
    {
        if (_disposed || _process.HasExited)
            return;

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _log.Write(RunLog.HostChannel, "could not kill the process tree: " + exception.Message);
        }
    }

    /// <summary>
    /// Releases the process handle. The child is not killed: by the time this runs the caller has already
    /// decided what should happen to it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // The handle is released only once the readers have finished, because a handle closed while a pipe is
        // still being read would abandon the last of the output.
        _ = _drained.ContinueWith(
            static (_, state) => ((Process)state!).Dispose(),
            _process,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task<int> CompleteAsync(Process process, Task drained)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);

        // The readers are given a bounded grace period rather than being awaited outright. A child that spawned a
        // grandchild holding the pipe would otherwise keep this run's log open forever, and the run would never be
        // recorded as finished.
        try
        {
            await drained.WaitAsync(DrainGrace).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        return process.ExitCode;
    }

    private static async Task DrainAsync(StreamReader reader, string channel, RunLog log)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                log.Write(channel, line);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Reading ends abnormally when the process dies with unwritten output still in the pipe, which is a
            // normal way for a failing service to end. Nothing was lost that the exit code does not already say.
        }
    }
}
