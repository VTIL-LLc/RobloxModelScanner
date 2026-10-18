using System.Globalization;

using RMS.ServiceModel.Enums;

namespace RMS.ServiceModel.Models;

/// <summary>
/// One process, or one web site, that the RMS stack runs on a machine.
/// </summary>
/// <remarks>
/// <para>
/// A plan is the platform-neutral description of a service: what to start, where, with which environment, and
/// what to do when it stops. It is produced once by the deployer, already expanded, so that the Windows
/// service control manager, systemd and launchd are all configured from byte-identical values. A plan file
/// that has been expanded is the only input the installer needs, which is what lets an operator inspect
/// exactly what a deployment is about to register.
/// </para>
/// <para>
/// A web site is expressed here rather than in a separate file because it is the same decision from the
/// operator's point of view -- something that should be running -- and only the mechanism differs. The
/// <see cref="Website"/> property carries its root directory; a plan with an empty <see cref="Website"/> is a
/// process.
/// </para>
/// </remarks>
public sealed class ServicePlan
{
    /// <summary>
    /// Gets or sets the plan-file section name this service was read from.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name the platform knows the service by.
    /// </summary>
    /// <remarks>
    /// This is the Windows service name, the systemd unit name (before the <c>.service</c> suffix) and the
    /// launchd label (after the <c>com.rms.</c> prefix). It is also the directory name under the log root, so
    /// it must be safe as a path component.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable description shown by service management tools.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the program to start. Empty for a web site, which the platform's own host runs.
    /// </summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>
    /// Gets the arguments passed to <see cref="Executable"/>, one entry per argument.
    /// </summary>
    /// <remarks>
    /// Held as a list rather than a command line so that an argument containing a space survives every platform
    /// unchanged. The deployer writes one <c>argument.N</c> key per entry precisely so that no consumer has to
    /// guess where one argument ends and the next begins.
    /// </remarks>
    public List<string> Arguments { get; } = [];

    /// <summary>
    /// Gets or sets the directory the process starts in. Resolved to an absolute path by the deployer.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets the additional environment variables for the process.
    /// </summary>
    /// <remarks>
    /// The comparer is ordinal so that the host passes through exactly what the plan declared. On Windows the
    /// process API folds the names itself, because Windows environment names are case-insensitive; on POSIX
    /// they are not, and folding them here would lose the distinction the plan was entitled to make.
    /// </remarks>
    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets what the supervisor should do when the process exits.
    /// </summary>
    public RestartPolicy Restart { get; set; } = RestartPolicy.Always;

    /// <summary>
    /// Gets or sets the base delay, in seconds, before a restart.
    /// </summary>
    /// <remarks>
    /// The supervisor grows this exponentially up to <see cref="MaximumRestartDelaySeconds"/>, so a service
    /// that crashes on start does not become a busy loop.
    /// </remarks>
    public int RestartDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Gets or sets the ceiling the restart delay grows to, in seconds.
    /// </summary>
    public int MaximumRestartDelaySeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets how long, in seconds, the supervisor waits after asking the process to stop before
    /// killing it.
    /// </summary>
    public int StopTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets a line written to the process's standard input to ask it to shut down.
    /// </summary>
    /// <remarks>
    /// This is the graceful-stop channel. A Windows service has no console, so it cannot deliver a control
    /// event to a child in another console group, and a POSIX signal delivered to a .NET process cannot be
    /// observed by application code parked on the thread pool. A line on standard input is the one mechanism
    /// that works identically on all three platforms and can only be read by the child, so a service that
    /// wants an orderly shutdown watches its own input for this text. When the property is empty the
    /// supervisor goes straight to the termination sequence.
    /// </remarks>
    public string ShutdownLine { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the root directory a web site is served from. Empty when this plan is a process.
    /// </summary>
    public string Website { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TCP port a web site binds, as text so that a plan file is never a number format
    /// question.
    /// </summary>
    public string Port { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the platforms the service applies to, separated by spaces or commas.
    /// </summary>
    /// <remarks>
    /// An empty value means all of them. The deployer already resolves each platform-specific key for the
    /// machine it is running on, so this is a guard rather than a mechanism: it stops a plan copied between
    /// machines from being registered on a host it was not written for.
    /// </remarks>
    public string Platforms { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the account the service should run as, when the platform supports choosing one.
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this plan describes a web site rather than a process.
    /// </summary>
    public bool IsWebsite => Website.Length > 0;

    /// <summary>
    /// Gets the identifier to use in messages and in the operating system's own tools.
    /// </summary>
    public string EffectiveName => Name.Length > 0 ? Name : Id;

    /// <summary>
    /// Gets the restart policy as the keyword the plan file and the other manifests use.
    /// </summary>
    public string RestartKeyword => Restart switch
    {
        RestartPolicy.Never => "never",
        RestartPolicy.OnFailure => "on-failure",
        _ => "always",
    };

    /// <summary>
    /// Gets the directory, below a log root, that belongs to this service.
    /// </summary>
    /// <param name="logRoot">The root directory the plan set nominated for logs.</param>
    /// <returns>The absolute path to this service's log directory.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the service name cannot be used as a path component.</exception>
    public string LogDirectoryFor(string logRoot)
    {
        string name = EffectiveName;

        ValidateName(name);

        return Path.Combine(logRoot, name);
    }

    /// <summary>
    /// Builds the display string for the command, for a log header and for the run record.
    /// </summary>
    /// <returns>The executable and its arguments, each argument quoted only when it needs to be.</returns>
    public string DescribeCommand()
    {
        if (IsWebsite)
            return "web site at " + Website + " on port " + Port;

        if (Executable.Length == 0)
            return "(no executable)";

        if (Arguments.Count == 0)
            return Executable;

        List<string> parts = new(Arguments.Count + 1) { Executable };

        foreach (string argument in Arguments)
        {
            bool needsQuotes = argument.Length == 0 ||
                               argument.Contains(' ') ||
                               argument.Contains('\t') ||
                               argument.Contains('"');

            if (!needsQuotes)
            {
                parts.Add(argument);
                continue;
            }

            // Only the characters that would otherwise change how the argument is split are escaped, so the
            // string stays readable in a log rather than becoming an escaped blob.
            parts.Add('"' + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"');
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Parses a manifest argument list.
    /// </summary>
    /// <param name="text">A semicolon-separated list of arguments.</param>
    /// <returns>The arguments, in order, with empty entries dropped.</returns>
    /// <remarks>
    /// Semicolons rather than spaces are the separator in the source manifest because a deployment path can
    /// contain a space, and a space-separated argument list cannot survive that. The plan file does not use
    /// this helper -- the deployer writes one key per argument -- but the manifest reader and the tests do, and
    /// keeping one definition of the rule stops the two from drifting apart.
    /// </remarks>
    public static List<string> SplitArguments(string? text)
    {
        List<string> arguments = [];

        if (string.IsNullOrWhiteSpace(text))
            return arguments;

        foreach (string part in text.Split(';'))
        {
            string trimmed = part.Trim();

            if (trimmed.Length > 0)
                arguments.Add(trimmed);
        }

        return arguments;
    }

    /// <summary>
    /// Splits an environment list.
    /// </summary>
    /// <param name="text">A semicolon-separated list of <c>KEY=VALUE</c> pairs.</param>
    /// <returns>The pairs, in order. Entries without a name are dropped rather than guessed at.</returns>
    public static List<KeyValuePair<string, string>> SplitEnvironment(string? text)
    {
        List<KeyValuePair<string, string>> pairs = [];

        if (string.IsNullOrWhiteSpace(text))
            return pairs;

        foreach (string part in text.Split(';'))
        {
            int separator = part.IndexOf('=');

            if (separator <= 0)
                continue;

            pairs.Add(new KeyValuePair<string, string>(
                part[..separator].Trim(),
                part[(separator + 1)..].Trim()));
        }

        return pairs;
    }

    /// <summary>
    /// Parses a restart policy keyword, falling back to <see cref="RestartPolicy.Always"/>.
    /// </summary>
    /// <param name="value">The keyword to parse.</param>
    /// <returns>The policy the keyword names.</returns>
    public static RestartPolicy ParseRestartPolicy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "never" => RestartPolicy.Never,
            "on-failure" or "onfailure" or "on_failure" => RestartPolicy.OnFailure,
            _ => RestartPolicy.Always,
        };
    }

    /// <summary>
    /// Formats a duration the way the DevOps site reports a task run time.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>A whole number of seconds, or minutes and seconds once it is long enough to need them.</returns>
    public static string FormatRunTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        int totalSeconds = (int)Math.Floor(duration.TotalSeconds);

        if (totalSeconds < 60)
            return totalSeconds.ToString(CultureInfo.InvariantCulture) + " sec";

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} min {1} sec",
            totalSeconds / 60,
            totalSeconds % 60);
    }

    /// <summary>
    /// Verifies that a name is safe to use as a Windows service name, a systemd unit name, a launchd label and
    /// a directory component.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <exception cref="InvalidOperationException">Thrown when the name is empty or contains an unsafe character.</exception>
    /// <remarks>
    /// A service name reaches a file system path, a systemd unit file and a registry key. Rejecting the
    /// separators, the relative-directory names and the characters Windows refuses in a file name closes the
    /// whole class of problems at once, rather than each consumer having to defend itself. The check runs on a
    /// value the deployer wrote, but a deployer is a program that could be edited, and this is the boundary
    /// where such a mistake stops.
    /// </remarks>
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("A service name may not be blank.");

        if (name is "." or "..")
            throw new InvalidOperationException("'" + name + "' is not a usable service name.");

        foreach (char character in name)
        {
            bool unsafeCharacter =
                char.IsControl(character) ||
                character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or ';' or '=' or '\'' or '`' or '$';

            if (unsafeCharacter)
            {
                throw new InvalidOperationException(
                    "The service name '" + name + "' contains '" + character + "', which is not usable in a service name.");
            }
        }
    }
}
