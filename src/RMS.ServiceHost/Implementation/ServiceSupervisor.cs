using System.Globalization;
using System.Reflection;

using RMS.ServiceHost.Interfaces;
using RMS.ServiceHost.Options;

using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Models;

namespace RMS.ServiceHost.Implementation;

/// <summary>
/// Runs one service, restarts it according to its policy, and records every attempt.
/// </summary>
/// <remarks>
/// <para>
/// The loop is the whole of the supervision: start the program, wait for it to exit or for a stop request,
/// decide whether the exit earns a restart, wait, and go round again. Everything it observes is written to the
/// run's log and to the run's record, so the state of a service is readable after the fact without any
/// instrumentation inside the service itself.
/// </para>
/// <para>
/// One run covers every attempt. A service that crashes and is restarted four times produces four attempts in a
/// single run, which is what makes the record answer the question an operator actually has -- "is this thing
/// staying up" -- rather than only "what happened last time".
/// </para>
/// </remarks>
public sealed class ServiceSupervisor : IServiceSupervisor
{
    private readonly HostOptions _options;

    private readonly RunStore _store;

    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// The version of this assembly, resolved once because the banner is written on every attempt.
    /// </summary>
    private static readonly string HostVersionText = ResolveHostVersion();

    private string _stopReason = "the supervisor was asked to stop";

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceSupervisor"/> class.
    /// </summary>
    /// <param name="options">The merged command line and plan the host was started with.</param>
    /// <exception cref="InvalidOperationException">Thrown when the service name cannot be used as a path component.</exception>
    public ServiceSupervisor(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _store = new RunStore(options.LogDirectory, options.Service.EffectiveName, options.RetainedRuns);
    }

    /// <summary>
    /// Gets the service being supervised.
    /// </summary>
    public ServicePlan Service => _options.Service;

    /// <summary>
    /// Gets the status of the current run.
    /// </summary>
    public RunStatus Status { get; private set; } = RunStatus.Running;

    /// <summary>
    /// Gets the identifier of the current run.
    /// </summary>
    public string RunId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the absolute path of the log the current run is writing.
    /// </summary>
    public string LogPath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the exit code of the last attempt, once one has exited.
    /// </summary>
    public int? ExitCode { get; private set; }

    /// <summary>
    /// Asks the supervisor to shut the service down.
    /// </summary>
    /// <param name="reason">Why the shutdown was requested, recorded in the log.</param>
    public void RequestStop(string reason)
    {
        if (reason.Length > 0)
            _stopReason = reason;

        if (_disposed)
            return;

        try
        {
            _stopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run already finished and the token source was released. Nothing is left to stop.
        }
    }

    /// <summary>
    /// Runs the service until it reaches a terminal state.
    /// </summary>
    /// <param name="cancellationToken">A token that asks the supervisor to stop.</param>
    /// <returns>The exit code of the last attempt, or zero when the service was stopped before it ran.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration =
            cancellationToken.Register(static state => ((ServiceSupervisor)state!).RequestStop("the host is shutting down"), this);

        ServicePlan plan = _options.Service;

        RunId = _store.CreateRunId();
        LogPath = _store.LogPath(RunId);

        RunRecord record = new()
        {
            Run = RunId,
            Service = plan.EffectiveName,
            Sequence = RunStore.SequenceOf(RunId),
            Command = plan.DescribeCommand(),
            LogFile = LogPath,
        };

        record.Save(_store.RecordPath(RunId));

        // The index is written before the first attempt, not only when the run ends. A service that dies with its
        // supervisor -- or that is stopped from outside -- would otherwise leave no index behind at all, and the
        // DevOps site lists services by reading these files, so the service would vanish from the listing exactly
        // when somebody wants to look at it.
        _store.WriteIndex(plan, record, _store.CountRuns());

        using RunLog log = _store.OpenLog(RunId);

        Status = RunStatus.Running;
        int lastExitCode = 0;

        // A run that ended badly and was superseded leaves a record claiming to be running. Marking the previous
        // one abandoned before this run starts is what stops a reader from treating a service that died with its
        // supervisor as a service that is healthy.
        AbandonPreviousRuns(record);

        try
        {
            lastExitCode = await SuperviseAsync(plan, record, log).ConfigureAwait(false);
        }
        finally
        {
            record.Completed ??= DateTimeOffset.UtcNow;

            if (record.Status == RunStatus.Running)
                record.Status = Status;

            record.Save(_store.RecordPath(RunId));

            _store.Prune();
            _store.WriteIndex(plan, record, _store.CountRuns());

            log.Write(RunLog.HostChannel, "run finished with status " + record.StatusKeyword);

            // The log is flushed and closed before this returns, so an operator who sees the record has a complete
            // log to read beside it rather than one still being written.
            log.Flush();
        }

        return lastExitCode;
    }

    /// <summary>
    /// Releases the stop request's token source.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stopping.Dispose();
    }

    /// <summary>
    /// Gets the version of the supervisor, for the log banner.
    /// </summary>
    /// <returns>The informational version, or the assembly version when none was set.</returns>
    private static string ResolveHostVersion()
    {
        Assembly assembly = typeof(ServiceSupervisor).Assembly;

        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }

    /// <summary>
    /// Marks any record still claiming to be running as abandoned.
    /// </summary>
    /// <param name="current">The record of the run that is starting, which is left alone.</param>
    private void AbandonPreviousRuns(RunRecord current)
    {
        foreach (string path in System.IO.Directory.EnumerateFiles(_store.Directory, "*" + RunStore.RecordExtension))
        {
            if (Path.GetFileNameWithoutExtension(path).Equals(current.Run, StringComparison.Ordinal))
                continue;

            RunRecord previous;

            try
            {
                previous = RunRecord.Load(path);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                continue;
            }

            if (previous.Status != RunStatus.Running)
                continue;

            previous.Status = RunStatus.Abandoned;
            previous.Completed ??= previous.Updated;

            try
            {
                previous.Save(path);
            }
            catch (IOException)
            {
                // A record that cannot be rewritten is reported by the next reader as abandoned anyway, because
                // its updated time no longer moves.
            }
        }
    }

    /// <summary>
    /// Starts the program and restarts it until the policy says to stop.
    /// </summary>
    /// <param name="plan">The service to run.</param>
    /// <param name="record">The record being written.</param>
    /// <param name="log">The log being written.</param>
    /// <returns>The exit code of the last attempt.</returns>
    private async Task<int> SuperviseAsync(ServicePlan plan, RunRecord record, RunLog log)
    {
        Task stopRequested = WaitForStopAsync();
        int attempt = 0;
        int lastExitCode = 0;

        while (true)
        {
            if (_stopping.IsCancellationRequested)
            {
                Finish(record, log, RunStatus.Stopped, null);
                return 0;
            }

            attempt++;
            record.Attempts = attempt;
            log.WriteBanner(plan, record.Run, attempt, HostVersionText);

            LaunchedProcess? child = TryStart(plan, log);

            if (child is null)
            {
                // A program that cannot be started is the same situation as a program that started and exited
                // with a failure, and it is restarted on the same terms: the missing file may be a package being
                // installed, the locked binary may be one being replaced by the deployment in progress.
                Finish(record, log, RunStatus.Failed, null);

                if (!ShouldRestart(plan, 1, stopped: false))
                    return 1;

                if (!await DelayBeforeRestartAsync(plan, attempt, log).ConfigureAwait(false))
                {
                    Finish(record, log, RunStatus.Stopped, null);
                    return 0;
                }

                continue;
            }

            using (child)
            {
                record.Started ??= DateTimeOffset.UtcNow;
                record.ProcessId = child.ProcessId;
                record.ExitCode = null;
                record.Save(_store.RecordPath(record.Run));

                log.Write(RunLog.HostChannel, "started, process id " + child.ProcessId.ToString(CultureInfo.InvariantCulture));

                bool stopped = await AwaitExitOrStopAsync(plan, child, stopRequested, log).ConfigureAwait(false);

                if (stopped)
                {
                    lastExitCode = 0;
                    Finish(record, log, RunStatus.Stopped, child);
                    return lastExitCode;
                }

                lastExitCode = await child.Completion.ConfigureAwait(false);
                record.ExitCode = lastExitCode;
                ExitCode = lastExitCode;

                log.Write(
                    RunLog.HostChannel,
                    "exited with code " + lastExitCode.ToString(CultureInfo.InvariantCulture));

                if (!ShouldRestart(plan, lastExitCode, stopped: false))
                {
                    Finish(record, log, lastExitCode == 0 ? RunStatus.Succeeded : RunStatus.Failed, child);
                    return lastExitCode;
                }

                if (!await DelayBeforeRestartAsync(plan, attempt, log).ConfigureAwait(false))
                {
                    Finish(record, log, RunStatus.Stopped, child);
                    return 0;
                }
            }
        }
    }

    /// <summary>
    /// Starts the program, reporting a failure to start rather than throwing.
    /// </summary>
    /// <param name="plan">The service to run.</param>
    /// <param name="log">The log to report to.</param>
    /// <returns>The running process, or <see langword="null"/> when it could not be started.</returns>
    private static LaunchedProcess? TryStart(ServicePlan plan, RunLog log)
    {
        try
        {
            return LaunchedProcess.Start(plan, log);
        }
        catch (InvalidOperationException exception)
        {
            log.Write(RunLog.ErrorChannel, exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Waits until either the child exits or a stop is requested.
    /// </summary>
    /// <param name="plan">The service being run.</param>
    /// <param name="child">The running child.</param>
    /// <param name="stopRequested">A task that completes when a stop is requested.</param>
    /// <param name="log">The log to report to.</param>
    /// <returns><see langword="true"/> when a stop was requested and the child has been dealt with.</returns>
    private async Task<bool> AwaitExitOrStopAsync(
        ServicePlan plan, LaunchedProcess child, Task stopRequested, RunLog log)
    {
        Task finished = await Task.WhenAny(child.Completion, stopRequested).ConfigureAwait(false);

        if (finished == child.Completion)
            return false;

        log.Write(RunLog.HostChannel, "stopping: " + _stopReason);

        if (plan.ShutdownLine.Length > 0)
        {
            log.Write(RunLog.HostChannel, "writing the shutdown line to the process's input");
            await child.WriteShutdownAsync(plan.ShutdownLine).ConfigureAwait(false);
        }
        else
        {
            log.Write(RunLog.HostChannel, "asking the process's window to close");
            child.TryCloseMainWindow();
        }

        Task timeout = Task.Delay(TimeSpan.FromSeconds(plan.StopTimeoutSeconds));

        if (await Task.WhenAny(child.Completion, timeout).ConfigureAwait(false) != child.Completion)
        {
            log.Write(
                RunLog.HostChannel,
                "did not stop within " + plan.StopTimeoutSeconds.ToString(CultureInfo.InvariantCulture) +
                " second(s); killing it");

            child.Kill();

            // The kill is asynchronous. Waiting here means the record that is written next describes a process that
            // has really gone, rather than one that is about to.
            await child.Completion.ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Waits for the restart delay, growing it so that a crash loop cannot become a busy loop.
    /// </summary>
    /// <param name="plan">The service being run.</param>
    /// <param name="attempt">The attempt that just failed, counting from one.</param>
    /// <param name="log">The log to report to.</param>
    /// <returns><see langword="false"/> when a stop was requested during the wait.</returns>
    private async Task<bool> DelayBeforeRestartAsync(ServicePlan plan, int attempt, RunLog log)
    {
        // The shift is bounded before it is applied, because 1 << 40 is undefined behaviour in C and an
        // absurd delay here. Sixteen doublings already exceed every ceiling the plans use.
        int doublings = Math.Min(attempt - 1, 16);
        long candidate = (long)plan.RestartDelaySeconds << doublings;
        int delay = (int)Math.Min(candidate, plan.MaximumRestartDelaySeconds);

        log.Write(
            RunLog.HostChannel,
            "restarting in " + delay.ToString(CultureInfo.InvariantCulture) + " second(s)");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delay), _stopping.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decides whether an exit earns a restart.
    /// </summary>
    /// <param name="plan">The service being run.</param>
    /// <param name="exitCode">The exit code the process produced.</param>
    /// <param name="stopped">Whether the exit was the result of a stop request.</param>
    /// <returns><see langword="true"/> when the process should be started again.</returns>
    private bool ShouldRestart(ServicePlan plan, int exitCode, bool stopped)
    {
        // A run that was asked to run once never restarts, whatever the policy says. That mode exists to make the
        // supervisor's behaviour observable, and a restart would turn a single measured run into an endless one.
        if (_options.RunOnce || stopped || _stopping.IsCancellationRequested)
            return false;

        return plan.Restart switch
        {
            RestartPolicy.Never => false,
            RestartPolicy.OnFailure => exitCode != 0,
            _ => true,
        };
    }

    /// <summary>
    /// Records the terminal status of the run.
    /// </summary>
    /// <param name="record">The record being written.</param>
    /// <param name="log">The log to report to.</param>
    /// <param name="status">The status the run ended in.</param>
    /// <param name="child">The child that just ended, or <see langword="null"/>.</param>
    private void Finish(RunRecord record, RunLog log, RunStatus status, LaunchedProcess? child)
    {
        Status = status;
        record.Status = status;
        record.Completed ??= DateTimeOffset.UtcNow;

        if (child is not null)
            record.ProcessId = child.ProcessId;

        log.Write(RunLog.HostChannel, "status " + record.StatusKeyword);
    }

    /// <summary>
    /// Returns a task that completes when a stop has been requested.
    /// </summary>
    /// <returns>The task.</returns>
    private Task WaitForStopAsync()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _stopping.Token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), completion);

        return completion.Task;
    }
}
