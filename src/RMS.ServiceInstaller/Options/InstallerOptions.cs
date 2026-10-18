using RMS.ServiceInstaller.Enums;

using RMS.ServiceModel.Models;

namespace RMS.ServiceInstaller.Options;

/// <summary>
/// Everything the installer was asked to do.
/// </summary>
/// <remarks>
/// <para>
/// The installer is driven by the deployer with one verb and a directory, which is the only form a generated
/// deployment ever uses. The remaining switches exist so that the same program can be used by an operator against a
/// plan they are looking at, and so that a test can drive it without a plan file on a real machine.
/// </para>
/// <para>
/// As with the host, parsing is separated from doing, so that a mistyped command is answered with a sentence rather
/// than with a stack trace from half way through reconfiguring a service.
/// </para>
/// </remarks>
public sealed class InstallerOptions
{
    /// <summary>The switch that names the directory holding the plan file.</summary>
    public const string DirectorySwitch = "--directory";

    /// <summary>The switch that names the plan file directly.</summary>
    public const string PlanSwitch = "--plan";

    /// <summary>The switch that limits the run to one named service. May be repeated.</summary>
    public const string ServiceSwitch = "--service";

    /// <summary>The switch that overrides the path of the supervisor recorded in the plan.</summary>
    public const string HostSwitch = "--host";

    /// <summary>The name of the plan file, as the usage text and the plan model both spell it.</summary>
    private const string ServicePlanFileName = ServicePlanSet.FileName;

    private InstallerOptions()
    {
    }

    /// <summary>
    /// Gets the verb to carry out.
    /// </summary>
    public InstallAction Action { get; private init; }

    /// <summary>
    /// Gets the directory holding the plan file, or <see langword="null"/> to search the default locations.
    /// </summary>
    public string? Directory { get; private init; }

    /// <summary>
    /// Gets the plan file named directly, or <see langword="null"/> when a directory was given or searched.
    /// </summary>
    public string? PlanPath { get; private init; }

    /// <summary>
    /// Gets the services to act on. An empty list means every service in the plan.
    /// </summary>
    public List<string> Services { get; } = [];

    /// <summary>
    /// Gets the overriding path of the supervisor, or <see langword="null"/>.
    /// </summary>
    public string? HostExecutable { get; private init; }

    /// <summary>
    /// Gets a value indicating whether progress should be reported for every step.
    /// </summary>
    public bool Verbose { get; private init; }

    /// <summary>
    /// Parses a command line.
    /// </summary>
    /// <param name="arguments">The arguments, without the program name.</param>
    /// <param name="options">The parsed options, set only when the outcome is <see cref="ParseOutcome.Success"/>.</param>
    /// <param name="message">A message describing a failure, or <see langword="null"/>.</param>
    /// <returns>What came of the parse.</returns>
    public static ParseOutcome Parse(string[] arguments, out InstallerOptions? options, out string? message)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        options = null;
        message = null;

        InstallAction? action = null;
        string? directory = null;
        string? planPath = null;
        string? host = null;
        bool verbose = false;
        List<string> services = [];

        for (int index = 0; index < arguments.Length; index++)
        {
            string token = arguments[index];

            if (token is "--help" or "-h" or "-?" or "/?" or "help")
                return ParseOutcome.HelpRequested;

            if (token == "--verbose")
            {
                verbose = true;
                continue;
            }

            if (token.StartsWith('-'))
            {
                if (index + 1 >= arguments.Length)
                {
                    message = "'" + token + "' needs a value.";
                    return ParseOutcome.Failure;
                }

                index++;
                string value = arguments[index];

                switch (token)
                {
                    case DirectorySwitch:
                        directory = value;
                        break;

                    case PlanSwitch:
                        planPath = value;
                        break;

                    case ServiceSwitch:
                        services.Add(value);
                        break;

                    case HostSwitch:
                        host = value;
                        break;

                    default:
                        message = "Unknown switch '" + token + "'. Use --help for the list.";
                        return ParseOutcome.Failure;
                }

                continue;
            }

            if (action is not null)
            {
                message = "Only one verb may be given; '" + token + "' is a second one.";
                return ParseOutcome.Failure;
            }

            if (!TryParseAction(token, out InstallAction parsed))
            {
                message = "Unknown verb '" + token + "'. Use --help for the list.";
                return ParseOutcome.Failure;
            }

            action = parsed;
        }

        if (action is null)
        {
            message = "No verb given. Say what to do: install, uninstall, start, stop, restart or status.";
            return ParseOutcome.Failure;
        }

        options = new InstallerOptions
        {
            Action = action.Value,
            Directory = directory,
            PlanPath = planPath,
            HostExecutable = host,
            Verbose = verbose,
        };

        options.Services.AddRange(services);

        return ParseOutcome.Success;
    }

    /// <summary>
    /// Writes the usage text.
    /// </summary>
    /// <param name="writer">Where to write.</param>
    public static void WriteUsage(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("RMS.ServiceInstaller -- register, stop, start and report on the RMS services");
        writer.WriteLine();
        writer.WriteLine("  RMS.ServiceInstaller <verb> [" + DirectorySwitch + " <dir>] [options]");
        writer.WriteLine();
        writer.WriteLine("Verbs:");
        writer.WriteLine("  install     Register every service in the plan, replacing earlier registrations.");
        writer.WriteLine("  uninstall   Stop and unregister every service in the plan.");
        writer.WriteLine("  start       Start the services that are not already running.");
        writer.WriteLine("  stop        Stop the services that are running.");
        writer.WriteLine("  restart     Stop, then start.");
        writer.WriteLine("  status      Report the state of every service. Changes nothing.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  " + DirectorySwitch + " <dir>    Where " + ServicePlanFileName + " lives.");
        writer.WriteLine("  " + PlanSwitch + " <file>         Read this plan file instead of a directory's.");
        writer.WriteLine("  " + ServiceSwitch + " <id>       Act on one service only. Repeatable.");
        writer.WriteLine("  " + HostSwitch + " <path>       Use this supervisor instead of the one in the plan.");
        writer.WriteLine("  --verbose              Report each step, not only each service's result.");
        writer.WriteLine("  --help                 Show this text.");
        writer.WriteLine();
        writer.WriteLine("Which mechanism is used is decided per service and per platform: the service control manager");
        writer.WriteLine("and IIS on Windows, systemd on Linux, launchd on macOS. The plan does not name one.");
    }

    private static bool TryParseAction(string token, out InstallAction action)
    {
        switch (token.ToLowerInvariant())
        {
            case "install":
                action = InstallAction.Install;
                return true;

            case "uninstall":
                action = InstallAction.Uninstall;
                return true;

            case "start":
                action = InstallAction.Start;
                return true;

            case "stop":
                action = InstallAction.Stop;
                return true;

            case "restart":
                action = InstallAction.Restart;
                return true;

            case "status":
                action = InstallAction.Status;
                return true;

            default:
                action = default;
                return false;
        }
    }
}
