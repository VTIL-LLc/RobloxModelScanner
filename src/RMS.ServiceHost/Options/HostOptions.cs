using System.Globalization;

using RMS.ServiceHost.Enums;

using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Implementation;
using RMS.ServiceModel.Models;

namespace RMS.ServiceHost.Options;

/// <summary>
/// Everything the host was asked to do, after the command line and the plan file have been merged.
/// </summary>
/// <remarks>
/// <para>
/// The host accepts two ways of being told what to run. A plan file is how it is started in service, because a
/// service definition has to name a single fixed command line and the plan is what the deployer generated; the
/// explicit switches are for running the same supervisor by hand against a program that was never part of a
/// deployment, which is how a service is tested before it is registered. The explicit switches win over the
/// plan, so an operator can override one field without editing generated output.
/// </para>
/// <para>
/// Parsing is separated from running so that a bad command line is reported as a sentence rather than as a
/// stack trace from half way through starting a child process.
/// </para>
/// </remarks>
public sealed class HostOptions
{
    /// <summary>The switch that names a plan file.</summary>
    public const string PlanSwitch = "--plan";

    /// <summary>The switch that selects one service out of a plan file.</summary>
    public const string ServiceSwitch = "--service";

    /// <summary>The switch that forces interactive rather than service mode.</summary>
    public const string ConsoleSwitch = "--console";

    /// <summary>The default number of runs of a service to keep.</summary>
    public const int DefaultRetainedRuns = 50;

    private HostOptions()
    {
    }

    /// <summary>
    /// Gets the plan file that was read, or <see langword="null"/> when the switches were used instead.
    /// </summary>
    public string? PlanPath { get; init; }

    /// <summary>
    /// Gets the plan set the plan file produced, when one was read.
    /// </summary>
    public ServicePlanSet? PlanSet { get; init; }

    /// <summary>
    /// Gets the service to supervise.
    /// </summary>
    public required ServicePlan Service { get; init; }

    /// <summary>
    /// Gets the root directory the service's logs are written under.
    /// </summary>
    public required string LogDirectory { get; init; }

    /// <summary>
    /// Gets a value indicating whether the supervisor runs the process exactly once and exits with its exit
    /// code.
    /// </summary>
    /// <remarks>
    /// This exists for verification. It turns the supervisor into a plain wrapper whose only added behaviour is
    /// the log and the run record, which makes the two things that are hard to test in service -- capturing
    /// output and recording an outcome -- testable without a service manager in the way.
    /// </remarks>
    public bool RunOnce { get; init; }

    /// <summary>
    /// Gets a value indicating whether every action should be echoed, including the ones that normally say
    /// nothing.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Gets a value indicating whether the host was asked to force interactive mode.
    /// </summary>
    public bool ForceConsole { get; init; }

    /// <summary>
    /// Gets the number of runs of a service to keep before the oldest are removed.
    /// </summary>
    public int RetainedRuns { get; init; } = DefaultRetainedRuns;

    /// <summary>
    /// Parses a command line.
    /// </summary>
    /// <param name="arguments">The arguments, without the program name.</param>
    /// <param name="options">The parsed options, set only when the outcome is <see cref="ParseOutcome.Success"/>.</param>
    /// <param name="message">A message describing a failure, or <see langword="null"/>.</param>
    /// <returns>What came of the parse.</returns>
    public static ParseOutcome Parse(string[] arguments, out HostOptions? options, out string? message)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        options = null;
        message = null;

        string? planPath = null;
        string? serviceId = null;
        bool forceConsole = false;
        bool verbose = false;
        bool runOnce = false;
        int retainedRuns = DefaultRetainedRuns;

        // The explicit overrides. Each is nullable so that "not given" and "given as empty" stay distinct:
        // clearing a working directory is a meaningful request, and it must not be confused with silence.
        string? name = null;
        string? description = null;
        string? executable = null;
        string? workingDirectory = null;
        string? logDirectory = null;
        string? restart = null;
        string? shutdownLine = null;
        int? restartDelay = null;
        int? maximumRestartDelay = null;
        int? stopTimeout = null;
        List<string> argumentsGiven = [];
        List<KeyValuePair<string, string>> environmentGiven = [];

        for (int index = 0; index < arguments.Length; index++)
        {
            string switchName = arguments[index];

            if (switchName is "--help" or "-h" or "-?" or "/?" or "help")
                return ParseOutcome.HelpRequested;

            if (switchName == ConsoleSwitch)
            {
                forceConsole = true;
                continue;
            }

            if (switchName is "--verbose" or "-v")
            {
                verbose = true;
                continue;
            }

            if (switchName == "--once")
            {
                runOnce = true;
                continue;
            }

            if (!TryReadValue(arguments, ref index, switchName, out string value, out message))
                return ParseOutcome.Failure;

            switch (switchName)
            {
                case PlanSwitch:
                    planPath = value;
                    break;

                case ServiceSwitch:
                    serviceId = value;
                    break;

                case "--name":
                    name = value;
                    break;

                case "--description":
                    description = value;
                    break;

                case "--executable":
                    executable = value;
                    break;

                case "--argument":
                    argumentsGiven.Add(value);
                    break;

                case "--working-dir":
                    workingDirectory = value;
                    break;

                case "--log-dir":
                    logDirectory = value;
                    break;

                case "--restart":
                    restart = value;
                    break;

                case "--restart-delay":
                    if (!TryReadInt(value, switchName, out int delay, out message))
                        return ParseOutcome.Failure;

                    restartDelay = delay;
                    break;

                case "--max-restart-delay":
                    if (!TryReadInt(value, switchName, out int maximum, out message))
                        return ParseOutcome.Failure;

                    maximumRestartDelay = maximum;
                    break;

                case "--stop-timeout":
                    if (!TryReadInt(value, switchName, out int timeout, out message))
                        return ParseOutcome.Failure;

                    stopTimeout = timeout;
                    break;

                case "--shutdown-line":
                    shutdownLine = value;
                    break;

                case "--keep-runs":
                    if (!TryReadInt(value, switchName, out int keep, out message))
                        return ParseOutcome.Failure;

                    retainedRuns = keep;
                    break;

                case "--environment":
                {
                    int separator = value.IndexOf('=');

                    if (separator <= 0)
                    {
                        message = "--environment needs KEY=VALUE. '" + value + "' has no key.";
                        return ParseOutcome.Failure;
                    }

                    environmentGiven.Add(new KeyValuePair<string, string>(
                        value[..separator],
                        value[(separator + 1)..]));

                    break;
                }

                default:
                    message = "Unknown switch '" + switchName + "'. Use --help for the list.";
                    return ParseOutcome.Failure;
            }
        }

        ServicePlanSet? set = null;

        if (planPath is not null)
        {
            string resolved = Path.GetFullPath(planPath);

            try
            {
                set = PlanFile.Load(resolved);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                message = exception.Message;
                return ParseOutcome.Failure;
            }

            planPath = resolved;
        }

        ServicePlan? service;

        if (set is not null)
        {
            (service, message) = SelectService(set, serviceId);

            if (service is null)
                return ParseOutcome.Failure;
        }
        else if (executable is not null)
        {
            service = new ServicePlan();
        }
        else
        {
            message = "Nothing to run. Pass " + PlanSwitch + " <file>, or --executable <program>.";
            return ParseOutcome.Failure;
        }

        ApplyOverrides(
            service,
            name,
            description,
            executable,
            workingDirectory,
            restart,
            shutdownLine,
            restartDelay,
            maximumRestartDelay,
            stopTimeout,
            argumentsGiven,
            environmentGiven);

        if (!Validate(service, out message))
            return ParseOutcome.Failure;

        if (service.WorkingDirectory.Length == 0)
        {
            // A process started without a working directory inherits one the operator did not choose, which is
            // how a service ends up reading a configuration file belonging to somebody else.
            service.WorkingDirectory = set?.Root is { Length: > 0 } root
                ? root
                : Path.GetDirectoryName(Path.GetFullPath(service.Executable)) ?? Directory.GetCurrentDirectory();
        }

        string effectiveLogDirectory = logDirectory
            ?? (set?.LogDirectory is { Length: > 0 } planLogRoot
                ? planLogRoot
                : Path.Combine(AppContext.BaseDirectory, "logs"));

        options = new HostOptions
        {
            PlanPath = planPath,
            PlanSet = set,
            Service = service,
            LogDirectory = Path.GetFullPath(effectiveLogDirectory),
            RunOnce = runOnce,
            Verbose = verbose,
            ForceConsole = forceConsole,
            RetainedRuns = retainedRuns < 1 ? 1 : retainedRuns,
        };

        return ParseOutcome.Success;
    }

    /// <summary>
    /// Writes the usage text.
    /// </summary>
    /// <param name="writer">Where to write.</param>
    public static void WriteUsage(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("RMS.ServiceHost -- supervise one service and record everything it does");
        writer.WriteLine();
        writer.WriteLine("  RMS.ServiceHost [options]");
        writer.WriteLine();
        writer.WriteLine("Running from a plan, which is how the installer starts it:");
        writer.WriteLine("  " + PlanSwitch + " <file>            The plan written by the deployer.");
        writer.WriteLine("  " + ServiceSwitch + " <id>          Which service in the plan to run, when it holds more than one.");
        writer.WriteLine();
        writer.WriteLine("Running a program directly:");
        writer.WriteLine("  --name <n>                The name to record the service under. Required in this mode.");
        writer.WriteLine("  --executable <path>       The program to start.");
        writer.WriteLine("  --argument <value>        One argument. Repeat for each.");
        writer.WriteLine("  --environment K=V         One variable. Repeat for each.");
        writer.WriteLine("  --working-dir <dir>       The directory to start in.");
        writer.WriteLine("  --log-dir <dir>           Where logs and run records are written.");
        writer.WriteLine("  --restart <policy>        always | on-failure | never. Default always.");
        writer.WriteLine("  --restart-delay <secs>    Base delay between restarts. Default 5.");
        writer.WriteLine("  --max-restart-delay <s>   Ceiling the delay grows to. Default 60.");
        writer.WriteLine("  --stop-timeout <secs>     Grace period before the process is killed. Default 30.");
        writer.WriteLine("  --shutdown-line <text>    Line written to the process's input to ask it to stop.");
        writer.WriteLine("  --keep-runs <count>       How many runs of this service to keep. Default 50.");
        writer.WriteLine();
        writer.WriteLine("Behaviour:");
        writer.WriteLine("  " + ConsoleSwitch + "                Run in the foreground even when a service manager started us.");
        writer.WriteLine("  --once                    Run the program once, then exit with its exit code.");
        writer.WriteLine("  --verbose                 Echo the actions taken as they happen.");
        writer.WriteLine("  --help                    Show this text.");
    }

    /// <summary>
    /// Chooses the service to run from a plan set.
    /// </summary>
    /// <param name="set">The plan set to choose from.</param>
    /// <param name="serviceId">The requested service, or <see langword="null"/>.</param>
    /// <returns>The service, and a message when there is none.</returns>
    private static (ServicePlan? Service, string? Message) SelectService(ServicePlanSet set, string? serviceId)
    {
        if (serviceId is not null)
        {
            ServicePlan? match = set.Services.Find(
                candidate => candidate.Id.Equals(serviceId, StringComparison.OrdinalIgnoreCase) ||
                             candidate.EffectiveName.Equals(serviceId, StringComparison.OrdinalIgnoreCase));

            return match is null
                ? (null, "The plan does not contain a service called '" + serviceId + "'.")
                : (match, null);
        }

        return set.Services.Count switch
        {
            1 => (set.Services[0], null),
            0 => (null, "The plan contains no services."),
            _ => (null, "The plan contains more than one service; pass --service to choose one."),
        };
    }

    /// <summary>
    /// Applies the command-line overrides on top of whatever the plan produced.
    /// </summary>
    /// <param name="service">The service to modify.</param>
    /// <param name="name">The overriding name, or <see langword="null"/>.</param>
    /// <param name="description">The overriding description, or <see langword="null"/>.</param>
    /// <param name="executable">The overriding executable, or <see langword="null"/>.</param>
    /// <param name="workingDirectory">The overriding working directory, or <see langword="null"/>.</param>
    /// <param name="restart">The overriding restart policy, or <see langword="null"/>.</param>
    /// <param name="shutdownLine">The overriding shutdown line, or <see langword="null"/>.</param>
    /// <param name="restartDelay">The overriding base restart delay, or <see langword="null"/>.</param>
    /// <param name="maximumRestartDelay">The overriding delay ceiling, or <see langword="null"/>.</param>
    /// <param name="stopTimeout">The overriding stop timeout, or <see langword="null"/>.</param>
    /// <param name="arguments">The overriding arguments, which replace the plan's when non-empty.</param>
    /// <param name="environment">The overriding environment entries, which are merged over the plan's.</param>
    private static void ApplyOverrides(
        ServicePlan service,
        string? name,
        string? description,
        string? executable,
        string? workingDirectory,
        string? restart,
        string? shutdownLine,
        int? restartDelay,
        int? maximumRestartDelay,
        int? stopTimeout,
        List<string> arguments,
        List<KeyValuePair<string, string>> environment)
    {
        if (name is not null)
        {
            service.Name = name;
            service.Id = name;
        }

        if (description is not null)
            service.Description = description;

        if (executable is not null)
            service.Executable = executable;

        if (workingDirectory is not null)
            service.WorkingDirectory = workingDirectory;

        if (restart is not null)
            service.Restart = ServicePlan.ParseRestartPolicy(restart);

        if (shutdownLine is not null)
            service.ShutdownLine = shutdownLine;

        if (restartDelay is { } delay)
            service.RestartDelaySeconds = delay;

        if (maximumRestartDelay is { } maximum)
            service.MaximumRestartDelaySeconds = maximum;

        if (stopTimeout is { } timeout)
            service.StopTimeoutSeconds = timeout;

        if (arguments.Count > 0)
        {
            service.Arguments.Clear();
            service.Arguments.AddRange(arguments);
        }

        foreach (KeyValuePair<string, string> pair in environment)
            service.Environment[pair.Key] = pair.Value;
    }

    /// <summary>
    /// Checks that the service describes something this program can supervise.
    /// </summary>
    /// <param name="service">The service to check.</param>
    /// <param name="message">A message describing the problem, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the service can be run.</returns>
    private static bool Validate(ServicePlan service, out string? message)
    {
        try
        {
            ServicePlan.ValidateName(service.EffectiveName);
        }
        catch (InvalidOperationException exception)
        {
            message = exception.Message;
            return false;
        }

        if (service.Executable.Length == 0)
        {
            // A web site without a program is served by the platform's own host rather than by a child process: IIS
            // on Windows owns the application pool and the port, and there is nothing for this program to watch. A
            // web site *with* a program is a different thing -- the platform has no such host, so the plan names the
            // one to run and this supervisor is what keeps it alive, which is how the sites run off Windows.
            message = service.IsWebsite
                ? "'" + service.EffectiveName + "' is a web site with no program to run, so the platform serves it " +
                  "rather than this host. Register it with the web server backend."
                : "'" + service.EffectiveName + "' does not name an executable.";

            return false;
        }

        message = null;
        return true;
    }

    private static bool TryReadValue(
        string[] arguments, ref int index, string switchName, out string value, out string? message)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        value = string.Empty;
        message = null;

        if (index + 1 >= arguments.Length)
        {
            message = "'" + switchName + "' needs a value.";
            return false;
        }

        index++;
        value = arguments[index];
        return true;
    }

    private static bool TryReadInt(string value, string switchName, out int parsed, out string? message)
    {
        message = null;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            return true;

        message = "'" + switchName + "' needs a whole number; '" + value + "' is not one.";
        return false;
    }
}
