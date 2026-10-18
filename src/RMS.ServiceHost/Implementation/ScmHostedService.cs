using System.Globalization;
using System.Runtime.Versioning;
using System.ServiceProcess;

using RMS.ServiceHost.Interfaces;
using RMS.ServiceHost.Options;

using RMS.ServiceModel.Models;

namespace RMS.ServiceHost.Implementation;

/// <summary>
/// The host as the Windows service control manager sees it.
/// </summary>
/// <remarks>
/// <para>
/// A console program registered directly with the service control manager is killed outright when the service is
/// stopped, because it never reports that it is running and never accepts a stop control. Registering this class
/// instead is what makes the service stoppable: the manager gets a state machine it understands, and the
/// supervisor gets the chance to shut its child down before the process goes away.
/// </para>
/// <para>
/// The control manager is only one of the three supervisors this host works under. On Linux and macOS the
/// service is the ordinary process this class's absence produces, managed by systemd or launchd, which signal it
/// rather than talking to it.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ScmHostedService : ServiceBase
{
    /// <summary>The largest slice of extra time requested from the manager at once, in milliseconds.</summary>
    private const int AdditionalTimeSliceMilliseconds = 5000;

    /// <summary>How long the stop handler waits between checks on the supervisor, in milliseconds.</summary>
    private const int StopPollMilliseconds = 200;

    private readonly HostOptions _options;

    private IServiceSupervisor? _supervisor;

    private Task<int>? _run;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScmHostedService"/> class.
    /// </summary>
    /// <param name="options">The merged command line and plan the host was started with.</param>
    public ScmHostedService(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;

        ServiceName = options.Service.EffectiveName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = true;
    }

    /// <summary>
    /// Starts the run when the manager starts the service.
    /// </summary>
    /// <param name="args">Arguments the manager passed. Unused: the plan is the whole instruction.</param>
    protected override void OnStart(string[] args)
    {
        ServiceSupervisor supervisor = new(_options);

        _supervisor = supervisor;

        // A state of Running is reported as soon as the supervisor is holding the service, not when the child is
        // confirmed healthy. Waiting would make a service that takes a minute to warm up look like one that failed
        // to start, and the manager would give up on it and report a start failure that is not real.
        _run = Task.Run(() => supervisor.RunAsync(CancellationToken.None));
    }

    /// <summary>
    /// Stops the service when the manager asks.
    /// </summary>
    protected override void OnStop()
    {
        Shutdown("the service control manager stopped the service");
    }

    /// <summary>
    /// Stops the service when the machine is shutting down.
    /// </summary>
    protected override void OnShutdown()
    {
        Shutdown("the machine is shutting down");
    }

    /// <summary>
    /// Releases the supervisor once the run has finished.
    /// </summary>
    /// <param name="disposing">Whether this is a managed disposal.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _supervisor?.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>
    /// Asks the supervisor to stop and waits for it to finish.
    /// </summary>
    /// <param name="reason">Why the shutdown was requested, recorded in the log.</param>
    /// <remarks>
    /// This blocks, which is correct here: the manager calls the stop handler on a thread of its own and treats a
    /// return as "stopped". It asks for extra time as it waits, because the manager's default allowance is short
    /// enough that a service with a sensible stop timeout would otherwise be killed while shutting down cleanly.
    /// </remarks>
    private void Shutdown(string reason)
    {
        IServiceSupervisor? supervisor = _supervisor;
        Task<int>? run = _run;

        if (supervisor is null)
            return;

        supervisor.RequestStop(reason);

        // The supervisor's own stop timeout plus a margin, so the wait ends after the supervisor has finished
        // rather than while it is still killing the child.
        long deadline = Environment.TickCount64 +
                        ((_options.Service.StopTimeoutSeconds + 15L) * 1000L);

        while (run is { IsCompleted: false } && Environment.TickCount64 < deadline)
        {
            try
            {
                RequestAdditionalTime(AdditionalTimeSliceMilliseconds);
            }
            catch (InvalidOperationException)
            {
                // Reported when the process is not being controlled by the manager, which is the case when the
                // host was started interactively for a test. There is nothing to ask and nothing to report.
            }

            Thread.Sleep(StopPollMilliseconds);
            run = _run;
        }

        if (run is { IsCompleted: true })
        {
            try
            {
                ExitCode = run.GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The run failed rather than ending. The log and the record already say what happened; the exit
                // code only has to stop the manager from thinking a clean stop was a crash.
                ExitCode = 1;
            }
        }
        else
        {
            // The supervisor did not finish in time. The child is killed by the runtime when this process goes
            // away, and the record will be read back as abandoned, which is the truth about this run.
            ExitCode = 1;
        }
    }

    /// <summary>
    /// Returns the name the manager knows the service by.
    /// </summary>
    /// <returns>The service name.</returns>
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", ServiceName, _options.LogDirectory);
    }

    /// <summary>
    /// Gets the service the host was asked to run, for the manager's own logging.
    /// </summary>
    /// <returns>The plan.</returns>
    public ServicePlan ServicePlan => _options.Service;
}
