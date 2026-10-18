using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

using RMS.ServiceInstaller.Interfaces;
using RMS.ServiceInstaller.Models;

using RMS.ServiceModel.Models;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// Registers a web site with IIS.
/// </summary>
/// <remarks>
/// <para>
/// A web site is the one shape that is not supervised by a child process on Windows, because IIS already is the
/// supervisor: it owns the application pool, it decides when a worker is recycled, and it is the thing listening on
/// the port. Registering the site here rather than starting a host of our own is what keeps the sites on the
/// platform they were written for -- the WebForms projects are .NET Framework 4.8 and need the full framework's own
/// pipeline.
/// </para>
/// <para>
/// IIS is driven through <c>appcmd.exe</c>, which ships with it and needs no assembly reference. The alternative,
/// the <c>Microsoft.Web.Administration</c> library, would tie the installer to a specific version of IIS and add a
/// dependency that has to be found at run time on a machine that may not have IIS at all.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class IisSiteBackend : IServiceBackend
{
    /// <summary>Matches the state appcmd prints inside a site or pool listing.</summary>
    private static readonly Regex StatePattern = new(@"state:(?<state>[A-Za-z]+)", RegexOptions.Compiled);

    /// <summary>
    /// Gets the short name of the mechanism.
    /// </summary>
    public string Mechanism => "iis";

    /// <summary>
    /// Gets a description of the mechanism.
    /// </summary>
    public string Description => "Internet Information Services";

    /// <summary>
    /// Tests whether this backend owns a service.
    /// </summary>
    /// <param name="plan">The service to test.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns><see langword="true"/> for a web site on Windows.</returns>
    public bool CanHandle(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        return OperatingSystem.IsWindows() && plan.IsWebsite;
    }

    /// <summary>
    /// Registers the application pool and the site, replacing whatever was there.
    /// </summary>
    /// <param name="plan">The site to register.</param>
    /// <param name="set">The plan the site came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Install(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        if (AppCmd() is not { } appCmd)
            return BackendOutcome.Fail("IIS is not installed on this machine: " + AppCmdPath + " does not exist.");

        string name = plan.EffectiveName;
        string contentRoot = Path.GetFullPath(plan.Website);
        string port = string.IsNullOrWhiteSpace(plan.Port) ? "80" : plan.Port.Trim();

        if (!Directory.Exists(contentRoot))
            return BackendOutcome.Fail("The site's content root '" + contentRoot + "' does not exist.");

        // The pool first: a site cannot be pointed at a pool that does not exist, and pointing it at one that is
        // about to be reconfigured would leave a window in which the site is running under the old settings.
        BackendOutcome pool = EnsureApplicationPool(appCmd, name);

        if (!pool.Succeeded)
            return pool;

        string? existing = ReadState(appCmd, "site", name);

        if (existing is null)
        {
            BackendOutcome added = Run(
                appCmd,
                ["add", "site", "/name:" + name, "/physicalPath:" + contentRoot, "/bindings:http/*:" + port + ":"]);

            if (!added.Succeeded)
                return added;
        }
        else
        {
            BackendOutcome updated = Run(
                appCmd,
                ["set", "site", "/site.name:" + name, "/[path='/'].physicalPath:" + contentRoot]);

            if (!updated.Succeeded)
                return updated;
        }

        BackendOutcome bound = Run(
            appCmd,
            ["set", "site", "/site.name:" + name, "/bindings:http/*:" + port + ":"]);

        if (!bound.Succeeded)
            return bound;

        // The application pool is a property of the application inside the site rather than of the site, which is
        // why it is set on the root application.
        BackendOutcome applied = Run(
            appCmd,
            ["set", "app", "/app.name:" + name + "/", "/applicationPool:" + name]);

        return applied.Succeeded
            ? BackendOutcome.Ok("registered '" + name + "' serving " + contentRoot + " on port " + port)
            : applied;
    }

    /// <summary>
    /// Removes the site and its application pool.
    /// </summary>
    /// <param name="plan">The site to remove.</param>
    /// <param name="set">The plan the site came from.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The site goes first. Removing the pool underneath a running site would leave the site in an error state that
    /// an operator would have to clear by hand, whereas a site with no pool is simply stopped.
    /// </remarks>
    public BackendOutcome Uninstall(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        if (AppCmd() is not { } appCmd)
            return BackendOutcome.Fail("IIS is not installed on this machine: " + AppCmdPath + " does not exist.");

        string name = plan.EffectiveName;

        if (ReadState(appCmd, "site", name) is not null && !Run(appCmd, ["delete", "site", name]).Succeeded)
            return BackendOutcome.Fail("Could not remove the site '" + name + "'.");

        if (ReadState(appCmd, "apppool", name) is not null && !Run(appCmd, ["delete", "apppool", name]).Succeeded)
            return BackendOutcome.Fail("Could not remove the application pool '" + name + "'.");

        return BackendOutcome.Ok("removed '" + name + "'");
    }

    /// <summary>
    /// Starts the site and its application pool.
    /// </summary>
    /// <param name="plan">The site to start.</param>
    /// <param name="set">The plan the site came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Start(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        if (AppCmd() is not { } appCmd)
            return BackendOutcome.Fail("IIS is not installed on this machine: " + AppCmdPath + " does not exist.");

        string name = plan.EffectiveName;

        BackendOutcome pool = Run(appCmd, ["start", "apppool", name]);

        if (!pool.Succeeded)
            return pool;

        BackendOutcome result = Run(appCmd, ["start", "site", name]);

        return result.Succeeded
            ? BackendOutcome.Ok("started '" + name + "'")
            : result;
    }

    /// <summary>
    /// Stops the site and its application pool.
    /// </summary>
    /// <param name="plan">The site to stop.</param>
    /// <param name="set">The plan the site came from.</param>
    /// <returns>What happened.</returns>
    public BackendOutcome Stop(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        if (AppCmd() is not { } appCmd)
            return BackendOutcome.Fail("IIS is not installed on this machine: " + AppCmdPath + " does not exist.");

        string name = plan.EffectiveName;

        Run(appCmd, ["stop", "site", name]);
        BackendOutcome result = Run(appCmd, ["stop", "apppool", name]);

        return result.Succeeded
            ? BackendOutcome.Ok("stopped '" + name + "'")
            : result;
    }

    /// <summary>
    /// Reports the state of the site.
    /// </summary>
    /// <param name="plan">The site to report on.</param>
    /// <param name="set">The plan the site came from.</param>
    /// <returns>The state, and whether it could be read at all.</returns>
    public (BackendOutcome Outcome, ServiceStatusEntry Entry) Status(ServicePlan plan, ServicePlanSet set)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(set);

        string name = plan.EffectiveName;

        if (AppCmd() is not { } appCmd)
        {
            return (
                BackendOutcome.Fail("IIS is not installed on this machine."),
                new ServiceStatusEntry(name, Mechanism, "absent", "IIS is not installed"));
        }

        string? state = ReadState(appCmd, "site", name);

        if (state is null)
        {
            return (
                BackendOutcome.Fail("'" + name + "' is not registered."),
                new ServiceStatusEntry(name, Mechanism, "absent", "no such site"));
        }

        return (
            BackendOutcome.Ok(string.Empty),
            new ServiceStatusEntry(name, Mechanism, state.ToLowerInvariant(), "port " + plan.Port + ", " + plan.Website));
    }

    /// <summary>
    /// Gets the absolute path of <c>appcmd.exe</c>.
    /// </summary>
    private static string AppCmdPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "system32",
        "inetsrv",
        "appcmd.exe");

    /// <summary>
    /// Creates the application pool if it is missing, and configures it for a site that stays up.
    /// </summary>
    /// <param name="appCmd">The resolved <c>appcmd.exe</c> path.</param>
    /// <param name="name">The application pool name, which is the service name.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The defaults IIS gives a new pool are wrong for a site that is meant to be available: the pool is stopped
    /// when it has been idle for twenty minutes, it recycles on a schedule, and it is only started when the first
    /// request arrives, which puts the startup cost of a cold site in front of whoever makes that request. All three
    /// are turned off or set to always-running here, because a service in a deployment is expected to be up rather
    /// than to be woken.
    /// </remarks>
    private static BackendOutcome EnsureApplicationPool(string appCmd, string name)
    {
        if (ReadState(appCmd, "apppool", name) is null)
        {
            BackendOutcome added = Run(
                appCmd,
                ["add", "apppool", "/name:" + name, "/managedRuntimeVersion:v4.0", "/managedPipelineMode:Integrated"]);

            if (!added.Succeeded)
                return added;
        }

        // Always-running is set through the pool object rather than through config, because it is a start-mode
        // property rather than a processModel one.
        BackendOutcome startMode = Run(appCmd, ["set", "apppool", "/apppool.name:" + name, "/startMode:AlwaysRunning"]);

        if (!startMode.Succeeded)
            return startMode;

        // No idle shutdown, no periodic recycle: an operator restarts this site with the installer, and nothing else
        // should be closing it behind their back.
        Run(
            appCmd,
            [
                "set", "config",
                "/section:applicationPools",
                "/[name='" + name + "'].processModel.idleTimeout:00:00:00",
            ]);

        Run(
            appCmd,
            [
                "set", "config",
                "/section:applicationPools",
                "/[name='" + name + "'].recycling.periodicRestart.time:00:00:00",
            ]);

        return BackendOutcome.Silent();
    }

    /// <summary>
    /// Reads the state of a site or pool.
    /// </summary>
    /// <param name="appCmd">The resolved <c>appcmd.exe</c> path.</param>
    /// <param name="kind">Either <c>site</c> or <c>apppool</c>.</param>
    /// <param name="name">The object's name.</param>
    /// <returns>The state, or <see langword="null"/> when the object does not exist.</returns>
    private static string? ReadState(string appCmd, string kind, string name)
    {
        (int exitCode, string output) = ProcessRunner.Run(appCmd, ["list", kind, name]);

        if (exitCode != 0)
            return null;

        Match match = StatePattern.Match(output);

        return match.Success ? match.Groups["state"].Value : null;
    }

    /// <summary>
    /// Runs <c>appcmd</c> and turns a refusal into an outcome.
    /// </summary>
    /// <param name="appCmd">The resolved <c>appcmd.exe</c> path.</param>
    /// <param name="arguments">The arguments to pass.</param>
    /// <returns>What happened.</returns>
    private static BackendOutcome Run(string appCmd, IReadOnlyList<string> arguments)
    {
        (int exitCode, string output) = ProcessRunner.Run(appCmd, arguments);

        if (exitCode == 0)
        {
            // appcmd reports some refusals with a zero exit code and an error line on the output, so a success code
            // alone is not enough to call this a success.
            if (!output.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                return BackendOutcome.Silent();
        }

        string reason = output.Length > 0 ? output : "appcmd returned " + exitCode.ToString(CultureInfo.InvariantCulture) + ".";

        return BackendOutcome.Fail("IIS refused 'appcmd " + string.Join(' ', arguments) + "': " + reason);
    }

    /// <summary>
    /// Resolves <c>appcmd.exe</c>.
    /// </summary>
    /// <returns>The path, or <see langword="null"/> when IIS is not installed.</returns>
    private static string? AppCmd()
    {
        return File.Exists(AppCmdPath) ? AppCmdPath : null;
    }
}
