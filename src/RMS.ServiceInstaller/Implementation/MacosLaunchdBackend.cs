using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

using RMS.ServiceInstaller.Interfaces;
using RMS.ServiceInstaller.Models;

using RMS.ServiceModel.Models;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// Registers a service with launchd.
/// </summary>
/// <remarks>
/// <para>
/// macOS is the platform where the difference between "a service manager" and "something that starts a program at
/// boot" is widest, so the plist written here says less than the systemd unit does and relies on the supervisor for
/// more. launchd is asked to start the supervisor at boot and to keep it going after a failure; everything else --
/// the arguments, the environment, the restart policy for the program itself, the logs -- is in the plan file, which
/// the supervisor reads.
/// </para>
/// <para>
/// The label is prefixed with <c>com.rms.</c> because launchd labels are drawn from a namespace the whole machine
/// shares, and an unprefixed label is how a deployment ends up colliding with a third-party package.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("windows")]
public sealed class MacosLaunchdBackend : IServiceBackend
{
    /// <summary>The directory launchd reads system daemons from.</summary>
    public const string DaemonDirectory = "/Library/LaunchDaemons";

    /// <summary>The prefix every label gets, so that this stack's jobs are grouped and do not collide.</summary>
    public const string LabelPrefix = "com.rms.";

    /// <summary>Matches the process identifier in the output of <c>launchctl list</c>.</summary>
    private static readonly Regex ProcessIdPattern = new(@"PID\s*=\s*(?<pid>\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Gets the short name of the mechanism.
    /// </summary>
    public string Mechanism => "launchd";

    /// <summary>
    /// Gets a description of the mechanism.
    /// </summary>
    public string Description => "launchd";

    /// <summary>
    /// Tests whether this backend owns a service.
    /// </summary>
    /// <param name="plan">The service to test.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns><see langword="true"/> on macOS, for both processes and sites.</returns>
    public bool CanHandle(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return OperatingSystem.IsMacOS();
    }

    /// <summary>
    /// Writes the plist and loads it.
    /// </summary>
    /// <param name="plan">The service to register.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Install(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string path = PlistPath(plan);

        try
        {
            Directory.CreateDirectory(DaemonDirectory);
            File.WriteAllText(path, RenderPlist(plan, set), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BackendOutcome.Fail(
                "Could not write '" + path + "': " + exception.Message +
                ". Registering a system daemon needs to run as root.");
        }

        // Unloaded first: a plist that replaces one already loaded is not picked up by reload alone, and the removal
        // of a job that is not there is reported and ignored.
        ProcessRunner.Run("launchctl", ["unload", "-w", path]);

        return Run("load", "-w", path);
    }

    /// <summary>
    /// Unloads the plist and removes it.
    /// </summary>
    /// <param name="plan">The service to remove.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Uninstall(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string path = PlistPath(plan);

        ProcessRunner.Run("launchctl", ["unload", "-w", path]);

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BackendOutcome.Fail("Could not remove '" + path + "': " + exception.Message);
        }

        return BackendOutcome.Ok("removed '" + Label(plan) + "'");
    }

    /// <summary>
    /// Starts the job.
    /// </summary>
    /// <param name="plan">The service to start.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Start(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return Run("start", Label(plan));
    }

    /// <summary>
    /// Stops the job.
    /// </summary>
    /// <param name="plan">The service to stop.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Stop(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return Run("stop", Label(plan));
    }

    /// <summary>
    /// Reports the state of the job.
    /// </summary>
    /// <param name="plan">The service to report on.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>The state, and whether it could be read at all.</returns>
    public (BackendOutcome Outcome, ServiceStatusEntry Entry) Status(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string label = Label(plan);

        // A job that is loaded but idle has no process identifier, and a job that is not loaded at all is reported by
        // a non-zero exit code with nothing on the output, so both answers are read from the same call.
        (int exitCode, string output) = ProcessRunner.Run("launchctl", ["list", label]);

        if (exitCode != 0 || output.Length == 0)
        {
            return (
                BackendOutcome.Fail("'" + label + "' is not registered."),
                new ServiceStatusEntry(plan.EffectiveName, Mechanism, "absent", "not loaded"));
        }

        Match match = ProcessIdPattern.Match(output);

        if (match.Success && match.Groups["pid"].Value != "0")
        {
            return (
                BackendOutcome.Ok(string.Empty),
                new ServiceStatusEntry(
                    plan.EffectiveName, Mechanism, "running", "process id " + match.Groups["pid"].Value));
        }

        return (
            BackendOutcome.Ok(string.Empty),
            new ServiceStatusEntry(plan.EffectiveName, Mechanism, "loaded", "loaded, no process"));
    }

    /// <summary>
    /// Renders the plist for a service.
    /// </summary>
    /// <param name="plan">The service to render.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>The plist's text.</returns>
    /// <remarks>
    /// The program arguments are written one element each rather than as a shell command, because launchd accepts a
    /// list and doing the quoting by hand would be one more place for a path with a space in it to go wrong.
    /// </remarks>
    public static string RenderPlist(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        StringBuilder builder = new();

        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" ");
        builder.Append("\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
        builder.Append("<plist version=\"1.0\">\n");
        builder.Append("<dict>\n");

        AppendString(builder, "Label", Label(plan));

        builder.Append("  <key>ProgramArguments</key>\n");
        builder.Append("  <array>\n");

        AppendElement(builder, set.HostExecutable);
        AppendElement(builder, "--plan");
        AppendElement(builder, set.PlanPath);
        AppendElement(builder, "--service");
        AppendElement(builder, plan.Id.Length > 0 ? plan.Id : plan.EffectiveName);

        builder.Append("  </array>\n");

        if (plan.WorkingDirectory.Length > 0)
            AppendString(builder, "WorkingDirectory", plan.WorkingDirectory);

        if (plan.Environment.Count > 0)
        {
            builder.Append("  <key>EnvironmentVariables</key>\n");
            builder.Append("  <dict>\n");

            foreach (KeyValuePair<string, string> variable in plan.Environment)
            {
                builder.Append("    <key>").Append(Escape(variable.Key)).Append("</key>\n");
                builder.Append("    <string>").Append(Escape(variable.Value)).Append("</string>\n");
            }

            builder.Append("  </dict>\n");
        }

        // Started when it is loaded, and restarted after a failure but not after a clean stop. launchd has no
        // 'on-failure' keyword, but SuccessfulExit=false under KeepAlive means exactly that.
        builder.Append("  <key>RunAtLoad</key>\n");
        builder.Append("  <true/>\n");
        builder.Append("  <key>KeepAlive</key>\n");
        builder.Append("  <dict>\n");
        builder.Append("    <key>SuccessfulExit</key>\n");
        builder.Append("    <false/>\n");
        builder.Append("  </dict>\n");

        // launchd's own copy of the output goes beside the run logs rather than to a path of its choosing. This is
        // what catches anything the supervisor itself printed before it managed to open its run log.
        string logDirectory = string.IsNullOrWhiteSpace(set.LogDirectory)
            ? AppContext.BaseDirectory
            : PosixPath.Join(set.LogDirectory, plan.EffectiveName);

        AppendString(builder, "StandardOutPath", PosixPath.Join(logDirectory, "launchd.out.log"));
        AppendString(builder, "StandardErrorPath", PosixPath.Join(logDirectory, "launchd.err.log"));

        AppendString(builder, "ProcessType", "Background");

        builder.Append("</dict>\n");
        builder.Append("</plist>\n");

        return builder.ToString();
    }

    /// <summary>
    /// Builds the label of a service.
    /// </summary>
    /// <param name="plan">The service.</param>
    /// <returns>The label.</returns>
    public static string Label(ServicePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return LabelPrefix + plan.EffectiveName;
    }

    /// <summary>
    /// Builds the path of a service's plist.
    /// </summary>
    /// <param name="plan">The service.</param>
    /// <returns>The absolute path.</returns>
    public static string PlistPath(ServicePlan plan)
    {
        return PosixPath.Join(DaemonDirectory, Label(plan) + ".plist");
    }

    private static void AppendString(StringBuilder builder, string key, string value)
    {
        builder.Append("  <key>").Append(Escape(key)).Append("</key>\n");
        builder.Append("  <string>").Append(Escape(value)).Append("</string>\n");
    }

    private static void AppendElement(StringBuilder builder, string value)
    {
        builder.Append("    <string>").Append(Escape(value)).Append("</string>\n");
    }

    /// <summary>
    /// Escapes a value for an XML text node.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value.</returns>
    /// <remarks>
    /// Ampersand first, or the escapes introduced for the other characters would themselves be escaped.
    /// </remarks>
    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static BackendOutcome Run(params string[] arguments)
    {
        (int exitCode, string output) = ProcessRunner.Run("launchctl", arguments);

        return exitCode == 0
            ? BackendOutcome.Ok("launchctl " + string.Join(' ', arguments))
            : BackendOutcome.Fail("launchctl " + string.Join(' ', arguments) + " failed: " + output);
    }
}
