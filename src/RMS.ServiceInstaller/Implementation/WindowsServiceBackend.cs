using System.Runtime.Versioning;

using RMS.ServiceInstaller.Interfaces;
using RMS.ServiceInstaller.Models;

using RMS.ServiceModel.Models;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// Registers a process with the Windows service control manager.
/// </summary>
/// <remarks>
/// <para>
/// What is registered is never the program itself. It is <c>RMS.ServiceHost</c>, told to read the plan and run one
/// named service out of it. That indirection is the whole reason the same definitions work on three operating
/// systems: the service database holds one command line per service, and what varies between platforms is only
/// which mechanism owns that command line.
/// </para>
/// <para>
/// The command line is written with the plan's path and the service's identifier quoted, because the path is chosen
/// by the deployment and a checkout directory may contain a space. Nothing else about the service is written down:
/// the arguments, the environment and the restart policy all live in the plan file, so changing one of them is
/// editing a text file and restarting, not re-registering a service.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceBackend : IServiceBackend
{
    /// <summary>How long a stop is given before the installer gives up on it.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(90);

    /// <summary>How long a start is given to reach the running state.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the short name of the mechanism.
    /// </summary>
    public string Mechanism => "scm";

    /// <summary>
    /// Gets a description of the mechanism.
    /// </summary>
    public string Description => "Windows service control manager";

    /// <summary>
    /// Tests whether this backend owns a service.
    /// </summary>
    /// <param name="plan">The service to test.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns><see langword="true"/> for a process on Windows.</returns>
    public bool CanHandle(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return OperatingSystem.IsWindows() && !plan.IsWebsite;
    }

    /// <summary>
    /// Registers the service, replacing any registration already present.
    /// </summary>
    /// <param name="plan">The service to register.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Install(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string commandLine = BuildCommandLine(plan, set);

        string? failure = NativeServiceControl.Register(
            plan.EffectiveName,
            DisplayName(plan),
            commandLine,
            plan.Description);

        return failure is null
            ? BackendOutcome.Ok("registered '" + plan.EffectiveName + "' as " + commandLine)
            : BackendOutcome.Fail(failure);
    }

    /// <summary>
    /// Stops and unregisters the service.
    /// </summary>
    /// <param name="plan">The service to unregister.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Uninstall(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string? failure = NativeServiceControl.Unregister(plan.EffectiveName);

        return failure is null
            ? BackendOutcome.Ok("removed '" + plan.EffectiveName + "'")
            : BackendOutcome.Fail(failure);
    }

    /// <summary>
    /// Starts the service and waits for it to report running.
    /// </summary>
    /// <param name="plan">The service to start.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Start(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string? failure = NativeServiceControl.Start(plan.EffectiveName);

        if (failure is not null)
            return BackendOutcome.Fail(failure);

        // Starting a service only means the start handler returned. The service is not yet known to be staying up,
        // and reporting success while it is still on its way to a crash is the kind of optimism an operator pays for
        // later, so the running state is waited for.
        long deadline = Environment.TickCount64 + (long)StartTimeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            (int state, int processId)? status = NativeServiceControl.Query(plan.EffectiveName);

            if (status is null)
                return BackendOutcome.Fail("'" + plan.EffectiveName + "' disappeared while it was starting.");

            if (status.Value.state == 4)
            {
                return BackendOutcome.Ok(
                    "started '" + plan.EffectiveName + "', process id " + status.Value.processId);
            }

            if (status.Value.state == 1)
            {
                return BackendOutcome.Fail(
                    "'" + plan.EffectiveName + "' stopped while it was starting. Its log is under the log root's " +
                    plan.EffectiveName + " directory.");
            }

            Thread.Sleep(250);
        }

        return BackendOutcome.Fail(
            "'" + plan.EffectiveName + "' did not report running within " +
            StartTimeout.TotalSeconds + " second(s).");
    }

    /// <summary>
    /// Stops the service and waits for it to stop.
    /// </summary>
    /// <param name="plan">The service to stop.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Stop(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string? failure = NativeServiceControl.Stop(plan.EffectiveName, StopTimeout);

        return failure is null
            ? BackendOutcome.Ok("stopped '" + plan.EffectiveName + "'")
            : BackendOutcome.Fail(failure);
    }

    /// <summary>
    /// Reports the state of the service.
    /// </summary>
    /// <param name="plan">The service to report on.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>The state, and whether it could be read at all.</returns>
    public (BackendOutcome Outcome, ServiceStatusEntry Entry) Status(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        (int state, int processId)? status = NativeServiceControl.Query(plan.EffectiveName);

        if (status is null)
        {
            return (
                BackendOutcome.Fail("'" + plan.EffectiveName + "' is not registered."),
                new ServiceStatusEntry(plan.EffectiveName, Mechanism, "absent", "not registered"));
        }

        string stateName = NativeServiceControl.StateName(status.Value.state);
        string detail = status.Value.processId > 0
            ? "process id " + status.Value.processId
            : plan.DescribeCommand();

        return (
            BackendOutcome.Ok(string.Empty),
            new ServiceStatusEntry(plan.EffectiveName, Mechanism, stateName, detail));
    }

    /// <summary>
    /// Builds the command line the service database is given.
    /// </summary>
    /// <param name="plan">The service to build it for.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>The command line.</returns>
    /// <remarks>
    /// The supervisor is named by its absolute path because a service starts with a working directory of the system
    /// directory and a search path that has nothing to do with the deployment. The plan and the service identifier
    /// are the only two things it is told; everything else it reads from the plan.
    /// </remarks>
    public static string BuildCommandLine(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return ArgumentQuoting.Join(
            set.HostExecutable,
            ["--plan", set.PlanPath, "--service", plan.Id.Length > 0 ? plan.Id : plan.EffectiveName],
            forWindows: true);
    }

    /// <summary>
    /// Builds the name shown in the services console.
    /// </summary>
    /// <param name="plan">The service to name.</param>
    /// <returns>The display name.</returns>
    /// <remarks>
    /// The display name carries the stack's prefix so that the services this deployment registered are grouped
    /// together in the console and are distinguishable at a glance from everything else on the machine. The service
    /// name itself is left unprefixed, because it is the path component of the service's log directory.
    /// </remarks>
    public static string DisplayName(ServicePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return "RMS " + plan.EffectiveName;
    }
}
