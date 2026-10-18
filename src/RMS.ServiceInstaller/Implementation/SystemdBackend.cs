using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

using RMS.ServiceInstaller.Interfaces;
using RMS.ServiceInstaller.Models;

using RMS.ServiceModel.Models;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// Registers a service with systemd.
/// </summary>
/// <remarks>
/// <para>
/// Linux has no equivalent of a service database that a program writes into; a service is a text file in a
/// directory, and the mechanism is the tool that reads it. So this backend writes the file and then asks the tool,
/// which is both simpler and more inspectable than the Windows path: an operator can read the unit, diff it against
/// the plan, and understand exactly what the machine will do.
/// </para>
/// <para>
/// The unit runs the supervisor, not the program. The supervisor restarts the program it was told to run and
/// records every attempt, so systemd's own restart handling is set to the narrower job of restarting the supervisor
/// if the supervisor itself dies -- <c>on-failure</c>, which leaves a clean stop alone.
/// </para>
/// <para>
/// The rendering is separated from the writing so that the text of a unit can be produced and inspected without
/// systemd being present. That is what makes this path verifiable from a machine that is not Linux, which is the
/// only kind of machine the deployer itself is usually built on.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("windows")]
public sealed class SystemdBackend : IServiceBackend
{
    /// <summary>The directory systemd reads unit files from.</summary>
    public const string UnitDirectory = "/etc/systemd/system";

    /// <summary>
    /// Gets the short name of the mechanism.
    /// </summary>
    public string Mechanism => "systemd";

    /// <summary>
    /// Gets a description of the mechanism.
    /// </summary>
    public string Description => "systemd";

    /// <summary>
    /// Tests whether this backend owns a service.
    /// </summary>
    /// <param name="plan">The service to test.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns><see langword="true"/> on Linux, for both processes and sites.</returns>
    public bool CanHandle(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return OperatingSystem.IsLinux();
    }

    /// <summary>
    /// Writes the unit and enables it.
    /// </summary>
    /// <param name="plan">The service to register.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Install(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string path = UnitPath(plan);
        string body = RenderUnit(plan, set);

        try
        {
            Directory.CreateDirectory(UnitDirectory);
            File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BackendOutcome.Fail(
                "Could not write '" + path + "': " + exception.Message +
                ". Registering a system service needs to run as root.");
        }

        BackendOutcome reloaded = Run("daemon-reload");

        if (!reloaded.Succeeded)
            return reloaded;

        return Run("enable", UnitName(plan));
    }

    /// <summary>
    /// Disables the unit and removes it.
    /// </summary>
    /// <param name="plan">The service to remove.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Uninstall(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        // Disabled before the file is removed, so that systemd's own record of the unit goes as well as the unit.
        Run("disable", "--now", UnitName(plan));

        string path = UnitPath(plan);

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BackendOutcome.Fail("Could not remove '" + path + "': " + exception.Message);
        }

        return Run("daemon-reload");
    }

    /// <summary>
    /// Starts the unit.
    /// </summary>
    /// <param name="plan">The service to start.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Start(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return Run("start", UnitName(plan));
    }

    /// <summary>
    /// Stops the unit.
    /// </summary>
    /// <param name="plan">The service to stop.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Stop(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return Run("stop", UnitName(plan));
    }

    /// <summary>
    /// Reports the state of the unit.
    /// </summary>
    /// <param name="plan">The service to report on.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>The state, and whether it could be read at all.</returns>
    public (BackendOutcome Outcome, ServiceStatusEntry Entry) Status(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string name = UnitName(plan);

        // 'is-active' answers 'inactive' with a non-zero exit code for a unit that exists and is stopped, which is a
        // perfectly good answer, so the output is read rather than the exit code.
        (int _, string active) = ProcessRunner.Run("systemctl", ["is-active", name]);
        string state = active.Trim();

        if (state.Length == 0)
            state = "unknown";

        if (state is "inactive" or "unknown")
        {
            (int _, string enabled) = ProcessRunner.Run("systemctl", ["is-enabled", name]);

            if (enabled.Trim() == "not-found" || enabled.Trim().Length == 0)
            {
                return (
                    BackendOutcome.Fail("'" + name + "' is not registered."),
                    new ServiceStatusEntry(plan.EffectiveName, Mechanism, "absent", "no unit file"));
            }
        }

        (int _, string mainPid) = ProcessRunner.Run("systemctl", ["show", "--property=MainPID", "--value", name]);
        string pid = mainPid.Trim();

        string detail = pid is "0" or ""
            ? plan.DescribeCommand()
            : "process id " + pid;

        return (
            BackendOutcome.Ok(string.Empty),
            new ServiceStatusEntry(plan.EffectiveName, Mechanism, state, detail));
    }

    /// <summary>
    /// Renders the unit file for a service.
    /// </summary>
    /// <param name="plan">The service to render.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>The unit file's text.</returns>
    public static string RenderUnit(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        StringBuilder builder = new();

        builder.Append("[Unit]\n");
        builder.Append("Description=").Append(Describe(plan)).Append('\n');
        builder.Append("After=network-online.target\n");
        builder.Append("Wants=network-online.target\n");
        builder.Append('\n');

        builder.Append("[Service]\n");
        builder.Append("Type=simple\n");

        if (plan.WorkingDirectory.Length > 0)
            builder.Append("WorkingDirectory=").Append(plan.WorkingDirectory).Append('\n');

        // One line, not one directive per argument: systemd's own parser splits ExecStart on whitespace unless the
        // arguments are quoted, and the quoting is applied here rather than left to each caller.
        builder.Append("ExecStart=")
               .Append(ArgumentQuoting.Join(
                   set.HostExecutable,
                   ["--plan", set.PlanPath, "--service", plan.Id.Length > 0 ? plan.Id : plan.EffectiveName],
                   forWindows: false))
               .Append('\n');

        // The supervisor restarts the program; systemd restarts the supervisor only when it failed. A clean stop is
        // therefore left alone, which is what makes 'systemctl stop' mean stop.
        builder.Append("Restart=on-failure\n");
        builder.Append("RestartSec=2\n");

        // The stop request is delivered as SIGTERM, which is the signal the supervisor handles. The grace period is
        // the supervisor's own stop timeout plus a margin, so systemd's final SIGKILL never arrives while the
        // supervisor is still shutting its child down politely.
        builder.Append("KillSignal=SIGTERM\n");
        builder.Append("TimeoutStopSec=")
               .Append((plan.StopTimeoutSeconds + 10).ToString(CultureInfo.InvariantCulture))
               .Append('\n');

        foreach (KeyValuePair<string, string> variable in plan.Environment)
        {
            builder.Append("Environment=")
                   .Append('"')
                   .Append(variable.Key.Replace("\\", "\\\\").Replace("\"", "\\\""))
                   .Append('=')
                   .Append(variable.Value.Replace("\\", "\\\\").Replace("\"", "\\\""))
                   .Append('"')
                   .Append('\n');
        }

        if (plan.User.Length > 0)
            builder.Append("User=").Append(plan.User).Append('\n');

        // The supervisor's own output goes to the journal as well as to the run log. The run log is the record; the
        // journal is what an operator gets when they type 'journalctl -u' without knowing where the deployment put
        // its files.
        builder.Append("StandardOutput=journal\n");
        builder.Append("StandardError=journal\n");
        builder.Append('\n');

        builder.Append("[Install]\n");
        builder.Append("WantedBy=multi-user.target\n");

        return builder.ToString();
    }

    /// <summary>
    /// Builds the unit name of a service.
    /// </summary>
    /// <param name="plan">The service.</param>
    /// <returns>The unit name, including its suffix.</returns>
    public static string UnitName(ServicePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.EffectiveName + ".service";
    }

    /// <summary>
    /// Builds the path of a service's unit file.
    /// </summary>
    /// <param name="plan">The service.</param>
    /// <returns>The absolute path.</returns>
    public static string UnitPath(ServicePlan plan)
    {
        return PosixPath.Join(UnitDirectory, UnitName(plan));
    }

    private static string Describe(ServicePlan plan)
    {
        return plan.Description.Length > 0
            ? "RMS " + plan.EffectiveName + ": " + plan.Description
            : "RMS " + plan.EffectiveName;
    }

    private static BackendOutcome Run(params string[] arguments)
    {
        (int exitCode, string output) = ProcessRunner.Run("systemctl", arguments);

        return exitCode == 0
            ? BackendOutcome.Ok("systemctl " + string.Join(' ', arguments))
            : BackendOutcome.Fail("systemctl " + string.Join(' ', arguments) + " failed: " + output);
    }
}
