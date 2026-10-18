using System.Globalization;

using RMS.ServiceInstaller.Enums;
using RMS.ServiceInstaller.Models;
using RMS.ServiceInstaller.Options;
using RMS.ServiceInstaller.Interfaces;

using RMS.ServiceModel.Implementation;
using RMS.ServiceModel.Models;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// Carries out one verb against every service in a plan.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place that decides which mechanism owns a service, and it decides it by asking. The Windows
/// service control manager, IIS, systemd and launchd each answer for the shape of service they can own, and the
/// first one that claims a service gets it. Adding a mechanism is therefore adding a class to the list below, not
/// editing a branch here.
/// </para>
/// <para>
/// A failure is reported and the walk continues where continuing makes sense, and is reported and unwound where it
/// does not. Registering eight services and stopping at the sixth would leave a machine in a state that matches
/// neither the old deployment nor the new one, so a failed install removes the registrations this run itself
/// created, and leaves everything that was already there alone. That distinction -- created by this run versus
/// found already present -- is why the state of each service is read before it is touched.
/// </para>
/// </remarks>
public sealed class ServiceInstaller
{
    private readonly InstallerOptions _options;

    private readonly ServicePlanSet _set;

    private readonly TextWriter _report;

    private readonly TextWriter _errors;

    private readonly List<IServiceBackend> _backends;

    private ServiceInstaller(
        InstallerOptions options, ServicePlanSet set, TextWriter report, TextWriter errors)
    {
        _options = options;
        _set = set;
        _report = report;
        _errors = errors;
        _backends = CreateBackends();
    }

    /// <summary>
    /// Loads a plan and carries out one verb.
    /// </summary>
    /// <param name="options">The parsed command line.</param>
    /// <param name="report">Where progress and results are written.</param>
    /// <param name="errors">Where failures are written.</param>
    /// <returns>Zero when everything asked for was done, otherwise one.</returns>
    public static int Execute(InstallerOptions options, TextWriter report, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(errors);

        ServicePlanSet set;

        try
        {
            string path = options.PlanPath is { Length: > 0 } explicitPath
                ? Path.GetFullPath(explicitPath)
                : Path.Combine(PlanFile.ResolveDirectory(options.Directory), ServicePlanSet.FileName);

            set = PlanFile.Load(path);
            set.ValidateSettings();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FileNotFoundException)
        {
            errors.WriteLine(exception.Message);
            return 1;
        }

        if (options.HostExecutable is { Length: > 0 } overridingHost)
            set.HostExecutable = Path.GetFullPath(overridingHost);

        if (!File.Exists(set.HostExecutable))
        {
            errors.WriteLine(
                "The plan names '" + set.HostExecutable + "' as the supervisor, and there is no such file. " +
                "Build and publish RMS.ServiceHost first, or pass --host <path>.");

            return 1;
        }

        // The plan file's own path goes into every service definition. It is absolute when the plan was loaded, and
        // this check is what keeps that an invariant rather than a coincidence: registering a service against a
        // relative path would give it a definition that depends on the directory it happens to start in.
        if (!Path.IsPathRooted(set.PlanPath))
        {
            errors.WriteLine(
                "The plan resolved to the relative path '" + set.PlanPath + "', which cannot be written into a " +
                "service definition. Pass " + InstallerOptions.DirectorySwitch + " with an absolute path.");

            return 1;
        }

        return new ServiceInstaller(options, set, report, errors).Run();
    }

    /// <summary>
    /// Carries out the verb against the selected services.
    /// </summary>
    /// <returns>Zero when everything asked for was done, otherwise one.</returns>
    private int Run()
    {
        List<(ServicePlan Plan, IServiceBackend Backend)> selected = Select();

        if (selected.Count == 0)
        {
            _errors.WriteLine(_options.Services.Count > 0
                ? "None of the requested services are in the plan, or they do not apply to this platform."
                : "The plan contains no service that applies to this platform (" +
                  PlatformFacts.CurrentName + ").");

            return 1;
        }

        _report.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} service(s) on {2}, registered with {3}.",
                _options.Action.ToString().ToLowerInvariant(),
                selected.Count,
                PlatformFacts.CurrentName,
                string.Join(" and ", DistinctMechanisms(selected))));

        return _options.Action switch
        {
            InstallAction.Status => ReportStatus(selected),
            InstallAction.Install => InstallAll(selected),
            _ => ApplyToAll(selected),
        };
    }

    /// <summary>
    /// Works out which backend owns each selected service.
    /// </summary>
    /// <returns>The services and their backends, in the plan's order.</returns>
    private List<(ServicePlan Plan, IServiceBackend Backend)> Select()
    {
        List<(ServicePlan, IServiceBackend)> selected = [];

        foreach (ServicePlan plan in _set.For(_set.Platform))
        {
            if (_options.Services.Count > 0 &&
                !_options.Services.Exists(
                    wanted => wanted.Equals(plan.EffectiveName, StringComparison.OrdinalIgnoreCase) ||
                              wanted.Equals(plan.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            IServiceBackend? backend = _backends.Find(candidate => candidate.CanHandle(plan, _set));

            if (backend is null)
            {
                _errors.WriteLine(
                    "No mechanism on this platform registers '" + plan.EffectiveName + "'. " +
                    (plan.IsWebsite
                        ? "A web site needs the platform's own web server, which this machine does not offer."
                        : "No service manager was found for this platform."));

                continue;
            }

            selected.Add((plan, backend));
        }

        return selected;
    }

    /// <summary>
    /// Registers every service, unwinding what this run created if any of them fails.
    /// </summary>
    /// <param name="selected">The services and their backends.</param>
    /// <returns>Zero when all of them were registered, otherwise one.</returns>
    private int InstallAll(List<(ServicePlan Plan, IServiceBackend Backend)> selected)
    {
        List<(ServicePlan Plan, IServiceBackend Backend)> created = [];
        bool failed = false;

        foreach ((ServicePlan plan, IServiceBackend backend) in selected)
        {
            // Read before writing. A service that was already registered is not undone by a later failure, because
            // undoing it would remove a working service that the new deployment merely wanted to reconfigure.
            bool alreadyPresent = backend.Status(plan, _set).Entry.State != "absent";

            if (alreadyPresent && _options.Verbose)
                _report.WriteLine("  " + plan.EffectiveName + ": already registered, reconfiguring");

            BackendOutcome outcome = backend.Install(plan, _set);

            _report.WriteLine("  " + plan.EffectiveName + ": " + Describe(outcome, "registered"));

            if (outcome.Succeeded)
            {
                if (!alreadyPresent)
                    created.Add((plan, backend));

                continue;
            }

            failed = true;
            break;
        }

        if (!failed)
            return 0;

        _errors.WriteLine();
        _errors.WriteLine("The install did not complete. Removing the " +
                          created.Count.ToString(CultureInfo.InvariantCulture) +
                          " registration(s) this run created, and leaving anything that was already present alone.");

        for (int index = created.Count - 1; index >= 0; index--)
        {
            (ServicePlan plan, IServiceBackend backend) = created[index];

            BackendOutcome removal = backend.Uninstall(plan, _set);

            _report.WriteLine(
                "  " + plan.EffectiveName + ": " + Describe(removal, "rolled back"));
        }

        return 1;
    }

    /// <summary>
    /// Applies the verb to every service, reporting each result.
    /// </summary>
    /// <param name="selected">The services and their backends.</param>
    /// <returns>Zero when all of them succeeded, otherwise one.</returns>
    private int ApplyToAll(List<(ServicePlan Plan, IServiceBackend Backend)> selected)
    {
        // A stop walks the plan backwards, so a service that something depends on goes down after its dependents; a
        // start walks it forwards. The plan lists its services in dependency order, which is what that ordering is
        // for, and reversing it is the whole of how teardown gets the order right.
        bool reverse = _options.Action == InstallAction.Stop;
        bool allSucceeded = true;

        for (int offset = 0; offset < selected.Count; offset++)
        {
            (ServicePlan plan, IServiceBackend backend) =
                selected[reverse ? selected.Count - 1 - offset : offset];

            BackendOutcome outcome = _options.Action switch
            {
                InstallAction.Start => backend.Start(plan, _set),
                InstallAction.Stop => backend.Stop(plan, _set),
                InstallAction.Restart => Restart(plan, backend),
                _ => BackendOutcome.Fail("Unsupported action " + _options.Action + "."),
            };

            _report.WriteLine("  " + plan.EffectiveName + ": " + Describe(outcome, ActionWord()));

            allSucceeded = allSucceeded && outcome.Succeeded;
        }

        return allSucceeded ? 0 : 1;
    }

    /// <summary>
    /// Stops and then starts one service.
    /// </summary>
    /// <param name="plan">The service.</param>
    /// <param name="backend">The backend that owns it.</param>
    /// <returns>What happened.</returns>
    private BackendOutcome Restart(ServicePlan plan, IServiceBackend backend)
    {
        BackendOutcome stopped = backend.Stop(plan, _set);

        // A service that was not registered is already in the state a restart begins from, so its absence is not a
        // failure to stop. Anything else the stop reported -- a stop that timed out, a service that would not go --
        // is passed back, because starting on top of it would hide the reason the restart was needed.
        if (!stopped.Succeeded && !stopped.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase))
            return stopped;

        return backend.Start(plan, _set);
    }

    /// <summary>
    /// Gets the word the report uses when a backend says nothing about its own success.
    /// </summary>
    /// <returns>The verb for the current action.</returns>
    private string ActionWord()
    {
        return _options.Action switch
        {
            InstallAction.Install => "registered",
            InstallAction.Uninstall => "removed",
            InstallAction.Start => "started",
            InstallAction.Stop => "stopped",
            InstallAction.Restart => "restarted",
            _ => "done",
        };
    }

    /// <summary>
    /// Reads and prints the state of every service.
    /// </summary>
    /// <param name="selected">The services and their backends.</param>
    /// <returns>Zero, because a service that is stopped is an answer rather than a failure.</returns>
    /// <remarks>
    /// This always reports success. The verb asks a question, and a stopped service is a valid answer to it; exiting
    /// non-zero because something is stopped would make a health check that uses this command unable to tell "I
    /// looked and it is down" from "I could not look".
    /// </remarks>
    private int ReportStatus(List<(ServicePlan Plan, IServiceBackend Backend)> selected)
    {
        int nameWidth = "Service".Length;
        int mechanismWidth = "Mechanism".Length;
        int stateWidth = "State".Length;
        List<ServiceStatusEntry> entries = [];

        foreach ((ServicePlan plan, IServiceBackend backend) in selected)
        {
            (BackendOutcome _, ServiceStatusEntry entry) = backend.Status(plan, _set);

            entries.Add(entry);

            nameWidth = Math.Max(nameWidth, entry.Name.Length);
            mechanismWidth = Math.Max(mechanismWidth, entry.Mechanism.Length);
            stateWidth = Math.Max(stateWidth, entry.State.Length);
        }

        _report.WriteLine();

        _report.WriteLine(
            "  " + "Service".PadRight(nameWidth) + "  " +
            "Mechanism".PadRight(mechanismWidth) + "  " +
            "State".PadRight(stateWidth) + "  Detail");

        _report.WriteLine(
            "  " + new string('-', nameWidth) + "  " +
            new string('-', mechanismWidth) + "  " +
            new string('-', stateWidth) + "  " + new string('-', 6));

        foreach (ServiceStatusEntry entry in entries)
        {
            _report.WriteLine(
                "  " + entry.Name.PadRight(nameWidth) + "  " +
                entry.Mechanism.PadRight(mechanismWidth) + "  " +
                entry.State.PadRight(stateWidth) + "  " + entry.Detail);
        }

        _report.WriteLine();
        _report.WriteLine(
            "  Run logs for each service are under " + _set.LogDirectory + ", one file per run.");

        return 0;
    }

    /// <summary>
    /// Builds the mechanisms available on this machine, in the order they are offered a service.
    /// </summary>
    /// <returns>The backends.</returns>
    private static List<IServiceBackend> CreateBackends()
    {
        List<IServiceBackend> backends = [];

        if (OperatingSystem.IsWindows())
        {
            // The web server is offered the service first. Its scope is a web site and the service manager's is a
            // process, so the two never disagree, but the order makes that explicit rather than incidental.
            backends.Add(new IisSiteBackend());
            backends.Add(new WindowsServiceBackend());
        }
        else if (OperatingSystem.IsMacOS())
        {
            backends.Add(new MacosLaunchdBackend());
        }
        else if (OperatingSystem.IsLinux())
        {
            backends.Add(new SystemdBackend());
        }

        return backends;
    }

    private static List<string> DistinctMechanisms(List<(ServicePlan Plan, IServiceBackend Backend)> selected)
    {
        List<string> names = [];

        foreach ((ServicePlan _, IServiceBackend backend) in selected)
        {
            if (!names.Contains(backend.Description))
                names.Add(backend.Description);
        }

        return names;
    }

    private static string Describe(BackendOutcome outcome, string verb)
    {
        return outcome.Message.Length > 0 ? outcome.Message : verb;
    }
}
