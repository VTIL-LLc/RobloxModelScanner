using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RMS.Configuration.Service.Enums;
using RMS.Configuration.Service.Interfaces;
using RMS.Configuration.Service.Models;
using RMS.Configuration.Service.Options;

using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Implementation;
using RMS.ServiceModel.Models;

namespace RMS.Configuration.Service.Implementation;

/// <summary>
/// Reads the plan the deployer generated, using the installer's own reader.
/// </summary>
/// <remarks>
/// <para>
/// The plan is read with <see cref="PlanFile"/>, which is the code <c>RMS.ServiceInstaller</c> uses on the same
/// file. A second reader that agreed with the installer most of the time would be worse than showing nothing,
/// because an operator would have no way to tell which of the two plausible answers was the true one.
/// </para>
/// <para>
/// An absent plan is an ordinary answer. A machine that has been provisioned but not yet deployed has no plan,
/// and that is precisely the state an operator is likely to be looking at this page in order to diagnose.
/// </para>
/// </remarks>
public sealed class PlanReader : IPlanReader
{
    private readonly ConfigurationOptions _options;
    private readonly ILogger<PlanReader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanReader"/> class.
    /// </summary>
    /// <param name="options">The bound options, which carry the plan's path.</param>
    /// <param name="logger">The logger used to record why a plan could not be read.</param>
    public PlanReader(IOptions<ConfigurationOptions> options, ILogger<PlanReader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public PlanView Read()
    {
        PlanView view = new() { Path = _options.PlanFile };

        if (_options.PlanFile.Length == 0)
        {
            view.Detail = $"No plan file is configured. Set the {ConfigurationOptions.PlanFileVariable} "
                          + "environment variable.";

            view.Summary = "This service does not know where to look for the plan.";

            return view;
        }

        if (!File.Exists(_options.PlanFile))
        {
            view.Detail = $"There is no plan at {_options.PlanFile}.";

            view.Summary = "The deployment has not been generated on this machine yet, or it was generated "
                           + "somewhere else.";

            return view;
        }

        ServicePlanSet set;

        try
        {
            set = PlanFile.Load(_options.PlanFile);

            // Validated here rather than left to the installer, because a plan whose log root or supervisor is a
            // relative path is one the installer will refuse, and an operator would rather find that out on this
            // page than in an event log at the moment they were trying to start something.
            set.ValidateSettings();
        }
        catch (Exception exception)
            when (exception is InvalidDataException or IOException or UnauthorizedAccessException
                  or InvalidOperationException)
        {
            _logger.LogWarning(exception, "The plan at {Path} could not be read.", _options.PlanFile);

            view.Detail = exception.Message;
            view.Problems.Add(new ManifestProblem
            {
                Severity = ProblemSeverity.Error,
                Line = 0,
                Message = exception.Message,
            });

            view.Summary = "The file is there but is not a plan this build can read, so nothing in it is shown.";

            return view;
        }

        view.Available = true;
        view.Version = set.Version;
        view.Platform = PlatformFacts.NameOf(set.Platform);
        view.Root = set.Root;
        view.OutputRoot = set.OutputRoot;
        view.LogDirectory = set.LogDirectory;
        view.HostExecutable = set.HostExecutable;

        if (set.Root.Length == 0)
        {
            view.Problems.Add(new ManifestProblem
            {
                Severity = ProblemSeverity.Warning,
                Line = 0,
                Message = "The plan does not record the repository root, so a path in it cannot be traced back to "
                          + "the manifest that wrote it.",
            });
        }

        if (set.OutputRoot.Length == 0)
        {
            view.Problems.Add(new ManifestProblem
            {
                Severity = ProblemSeverity.Warning,
                Line = 0,
                Message = "The plan does not record a publish destination, so the deployer was run without an "
                          + "output directory.",
            });
        }

        foreach (ServicePlan service in set.Services)
            view.Services.Add(Describe(set, service));

        int applicable = view.Services.Count(entry => entry.AppliesToThisPlan);

        view.Summary = set.Services.Count == 0
            ? "The plan declares no services. That is what an empty services manifest produces."
            : $"{set.Services.Count} service{(set.Services.Count == 1 ? string.Empty : "s")} for "
              + $"{view.Platform}, of which {applicable} match the plan's own platform list.";

        return view;
    }

    /// <summary>
    /// Builds a plan entry's view.
    /// </summary>
    /// <param name="set">The plan set the service belongs to.</param>
    /// <param name="service">The service to describe.</param>
    /// <returns>The entry.</returns>
    private static PlanServiceView Describe(ServicePlanSet set, ServicePlan service)
    {
        PlanServiceView entry = new()
        {
            Id = service.Id,
            Name = service.EffectiveName,
            Description = service.Description,
            Executable = service.Executable,
            Arguments = [.. service.Arguments],
            WorkingDirectory = service.WorkingDirectory,
            Restart = service.RestartKeyword,
            RestartDelaySeconds = service.RestartDelaySeconds,
            MaximumRestartDelaySeconds = service.MaximumRestartDelaySeconds,
            StopTimeoutSeconds = service.StopTimeoutSeconds,
            ShutdownLine = service.ShutdownLine,
            Website = service.Website,
            Port = service.Port,
            Platforms = service.Platforms,
            User = service.User,
            AppliesToThisPlan = ServicePlanSet.Applies(service, PlatformFacts.NameOf(set.Platform)),
            Command = service.DescribeCommand(),
        };

        // The plan's own log root, resolved to this service's directory. The name it is resolved with has already
        // been checked by the reader, which refuses a plan whose service name cannot be a path component.
        if (set.LogDirectory.Length > 0) entry.LogDirectory = service.LogDirectoryFor(set.LogDirectory);

        // Sorted, because the plan sorts them when it writes them: a page that listed them in dictionary order
        // would show the same plan in a different order on every run.
        List<string> names = [.. service.Environment.Keys];
        names.Sort(StringComparer.Ordinal);

        foreach (string name in names)
            entry.Environment.Add(new PlanVariableView { Key = name, Value = service.Environment[name] });

        if (service.Executable.Length == 0 && service.Website.Length == 0)
        {
            entry.Problems.Add(new ManifestProblem
            {
                Severity = ProblemSeverity.Error,
                Line = 0,
                Message = "This entry has neither an executable nor a web site, so the installer has nothing to "
                          + "register for it.",
            });
        }

        return entry;
    }
}
