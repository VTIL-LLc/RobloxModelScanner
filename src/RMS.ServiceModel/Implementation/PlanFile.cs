using System.Globalization;
using System.Text;

using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Models;

namespace RMS.ServiceModel.Implementation;

/// <summary>
/// Reads and writes the service plan file: the single artefact the deployer produces and both the service host
/// and the service installer consume.
/// </summary>
/// <remarks>
/// <para>
/// The reader and the writer live together and share their key names as constants, so a change to the format
/// cannot be made on one side only. The deployer's C implementation writes files in this same dialect, and
/// <see cref="Save"/> exists so that a plan can be regenerated, normalised or built by a test without a C
/// compiler anywhere in sight.
/// </para>
/// <para>
/// Every key is optional at the reading stage and checked by <see cref="ServicePlanSet.ValidateSettings"/> or by
/// the consumer that needs it, because a plan that is missing a value the current action does not use should
/// still be readable enough to report what is wrong with it.
/// </para>
/// </remarks>
public static class PlanFile
{
    /// <summary>The settings section name.</summary>
    public const string SettingsSection = "settings";

    /// <summary>The key holding the format version, in the settings section.</summary>
    public const string VersionKey = "version";

    /// <summary>The key holding the platform the plan was generated for, in the settings section.</summary>
    public const string PlatformKey = "platform";

    /// <summary>The key holding the repository root, in the settings section.</summary>
    public const string RootKey = "root";

    /// <summary>The key holding the publish destination root, in the settings section.</summary>
    public const string OutputRootKey = "out";

    /// <summary>The key holding the log root, in the settings section.</summary>
    public const string LogDirectoryKey = "log_dir";

    /// <summary>The key holding the path to the supervisor, in the settings section.</summary>
    public const string HostKey = "host";

    /// <summary>The prefix each argument is written under, followed by its position.</summary>
    public const string ArgumentPrefix = "argument.";

    /// <summary>The prefix each environment variable is written under.</summary>
    public const string EnvironmentPrefix = "environment.";

    /// <summary>The key holding a service's name.</summary>
    public const string NameKey = "name";

    /// <summary>The key holding a service's description.</summary>
    public const string DescriptionKey = "description";

    /// <summary>The key holding a service's executable.</summary>
    public const string ExecutableKey = "executable";

    /// <summary>The key holding a service's working directory.</summary>
    public const string WorkingDirectoryKey = "working_dir";

    /// <summary>The key holding a service's restart policy.</summary>
    public const string RestartKey = "restart";

    /// <summary>The key holding a service's base restart delay, in seconds.</summary>
    public const string RestartDelayKey = "restart_delay";

    /// <summary>The key holding the ceiling a service's restart delay grows to, in seconds.</summary>
    public const string MaximumRestartDelayKey = "max_restart_delay";

    /// <summary>The key holding the grace period a service gets between a stop request and a kill.</summary>
    public const string StopTimeoutKey = "stop_timeout";

    /// <summary>The key holding the line written to a service's standard input to request a shutdown.</summary>
    public const string ShutdownLineKey = "shutdown_line";

    /// <summary>The key holding a web site's content root.</summary>
    public const string WebsiteKey = "website";

    /// <summary>The key holding a web site's port.</summary>
    public const string PortKey = "port";

    /// <summary>The key holding the platforms a service applies to.</summary>
    public const string PlatformsKey = "platforms";

    /// <summary>The key holding the account a service runs as.</summary>
    public const string UserKey = "user";

    /// <summary>
    /// Reads a plan file.
    /// </summary>
    /// <param name="path">The plan file to read.</param>
    /// <returns>The parsed plan set.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file is not a plan.</exception>
    public static ServicePlanSet Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("No service plan at '" + path + "'.", path);

        IniDocument document = IniDocument.Load(path);
        ServicePlanSet set = new()
        {
            Directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".",
        };

        if (document.FindSection(SettingsSection) is { } settings)
        {
            set.Version = settings.GetInt32(VersionKey, ServicePlanSet.CurrentVersion);
            set.Root = settings.Get(RootKey);
            set.OutputRoot = settings.Get(OutputRootKey);
            set.LogDirectory = settings.Get(LogDirectoryKey);
            set.HostExecutable = settings.Get(HostKey);

            string platform = settings.Get(PlatformKey);

            if (platform.Length > 0)
                set.Platform = ParsePlatform(platform);
        }

        foreach (IniSection section in document.Sections)
        {
            if (ReferenceEquals(section, document.Leading) ||
                section.Name.Equals(SettingsSection, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            set.Services.Add(ReadService(section));
        }

        return set;
    }

    /// <summary>
    /// Reads a single service from its section.
    /// </summary>
    /// <param name="section">The section to read.</param>
    /// <returns>The service the section describes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the section names no service.</exception>
    /// <remarks>
    /// The name defaults to the section name. The deployer always writes both explicitly, but defaulting means a
    /// hand-written section -- which is how a temporary service is tested without a full deployment -- cannot
    /// end up nameless.
    /// </remarks>
    public static ServicePlan ReadService(IniSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        ServicePlan plan = new()
        {
            Id = section.Name,
            Name = section.Get(NameKey, section.Name),
            Description = section.Get(DescriptionKey),
            Executable = section.Get(ExecutableKey),
            WorkingDirectory = section.Get(WorkingDirectoryKey),
            Restart = ServicePlan.ParseRestartPolicy(section.Get(RestartKey)),
            RestartDelaySeconds = section.GetInt32(RestartDelayKey, 5),
            MaximumRestartDelaySeconds = section.GetInt32(MaximumRestartDelayKey, 60),
            StopTimeoutSeconds = section.GetInt32(StopTimeoutKey, 30),
            ShutdownLine = section.Get(ShutdownLineKey),
            Website = section.Get(WebsiteKey),
            Port = section.Get(PortKey),
            Platforms = section.Get(PlatformsKey),
            User = section.Get(UserKey),
        };

        ServicePlan.ValidateName(plan.EffectiveName);

        if (plan.RestartDelaySeconds < 1)
            plan.RestartDelaySeconds = 1;

        if (plan.MaximumRestartDelaySeconds < plan.RestartDelaySeconds)
            plan.MaximumRestartDelaySeconds = plan.RestartDelaySeconds;

        if (plan.StopTimeoutSeconds < 1)
            plan.StopTimeoutSeconds = 1;

        plan.Arguments.AddRange(section.GetIndexedValues(ArgumentPrefix));

        foreach (KeyValuePair<string, string> pair in section.GetPrefixed(EnvironmentPrefix))
        {
            if (pair.Key.Length > 0)
                plan.Environment[pair.Key] = pair.Value;
        }

        return plan;
    }

    /// <summary>
    /// Writes a plan file.
    /// </summary>
    /// <param name="set">The plan set to write.</param>
    /// <param name="path">The file to write, replacing any file already there.</param>
    /// <remarks>
    /// The file is written whole rather than merged. A plan is generated output, not configuration, so making it
    /// idempotent by editing would mean the deployed definitions could drift from the manifest that is supposed
    /// to describe them.
    /// </remarks>
    public static void Save(ServicePlanSet set, string path)
    {
        ArgumentNullException.ThrowIfNull(set);

        StringBuilder builder = new();

        builder.Append("# Generated by rmsdeploy from deploy/rmsdeploy.services.\n");
        builder.Append("# Edits are overwritten on the next deployment.\n\n");

        builder.Append('[').Append(SettingsSection).Append("]\n");
        AppendKey(builder, VersionKey, set.Version.ToString(CultureInfo.InvariantCulture));
        AppendKey(builder, PlatformKey, PlatformFacts.NameOf(set.Platform));
        AppendKey(builder, RootKey, set.Root);
        AppendKey(builder, OutputRootKey, set.OutputRoot);
        AppendKey(builder, LogDirectoryKey, set.LogDirectory);
        AppendKey(builder, HostKey, set.HostExecutable);

        foreach (ServicePlan service in set.Services)
        {
            builder.Append('\n');
            builder.Append('[').Append(service.Id.Length > 0 ? service.Id : service.EffectiveName).Append("]\n");

            AppendKey(builder, NameKey, service.EffectiveName);
            AppendKey(builder, DescriptionKey, service.Description);
            AppendKey(builder, ExecutableKey, service.Executable);
            AppendKey(builder, WorkingDirectoryKey, service.WorkingDirectory);

            // One key per argument, never a joined command line: this is what lets an argument containing a space
            // survive a deployment path that contains one too.
            for (int index = 0; index < service.Arguments.Count; index++)
            {
                AppendKey(
                    builder,
                    ArgumentPrefix + index.ToString(CultureInfo.InvariantCulture),
                    service.Arguments[index]);
            }

            // Sorted so that regenerating an unchanged plan produces an unchanged file.
            List<string> names = [.. service.Environment.Keys];
            names.Sort(StringComparer.Ordinal);

            foreach (string name in names)
                AppendKey(builder, EnvironmentPrefix + name, service.Environment[name]);

            AppendKey(builder, RestartKey, service.RestartKeyword);
            AppendKey(builder, RestartDelayKey, service.RestartDelaySeconds.ToString(CultureInfo.InvariantCulture));
            AppendKey(builder, MaximumRestartDelayKey, service.MaximumRestartDelaySeconds.ToString(CultureInfo.InvariantCulture));
            AppendKey(builder, StopTimeoutKey, service.StopTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            AppendKey(builder, ShutdownLineKey, service.ShutdownLine);
            AppendKey(builder, WebsiteKey, service.Website);
            AppendKey(builder, PortKey, service.Port);
            AppendKey(builder, PlatformsKey, service.Platforms);
            AppendKey(builder, UserKey, service.User);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Resolves the directory a plan file should be read from.
    /// </summary>
    /// <param name="requested">
    /// The directory the caller asked for, or <see langword="null"/> to search the default locations.
    /// </param>
    /// <returns>The absolute path of a directory containing a plan file.</returns>
    /// <exception cref="FileNotFoundException">Thrown when no plan file can be found.</exception>
    /// <remarks>
    /// The search order is the directory asked for, then the working directory, then the directory holding the
    /// running program and its parent. The last of those exists because the installer is published beside the
    /// host, and a deployment invoked by absolute path from somewhere else should still find the plan the
    /// deployer just wrote next to the program.
    /// </remarks>
    public static string ResolveDirectory(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            string candidate = Path.GetFullPath(requested);

            if (!File.Exists(Path.Combine(candidate, ServicePlanSet.FileName)))
                throw new FileNotFoundException("No " + ServicePlanSet.FileName + " in '" + candidate + "'.", candidate);

            return candidate;
        }

        List<string> probes = [Directory.GetCurrentDirectory()];
        string baseDirectory = AppContext.BaseDirectory;

        if (!string.IsNullOrEmpty(baseDirectory))
        {
            probes.Add(baseDirectory);
            probes.Add(Path.GetFullPath(Path.Combine(baseDirectory, "..")));

            // The layout the deployer produces: a services directory beside the published installer.
            probes.Add(Path.Combine(Path.GetFullPath(Path.Combine(baseDirectory, "..")), "services"));
        }

        foreach (string probe in probes)
        {
            if (File.Exists(Path.Combine(probe, ServicePlanSet.FileName)))
                return Path.GetFullPath(probe);
        }

        throw new FileNotFoundException(
            "No " + ServicePlanSet.FileName + " was found in the working directory or beside this program. " +
            "Run the deployer's 'services' command first, or pass --directory.");
    }

    /// <summary>
    /// Parses a platform name, treating an unrecognised one as the current platform.
    /// </summary>
    /// <param name="value">The platform name to parse.</param>
    /// <returns>The matching platform.</returns>
    public static PlanPlatform ParsePlatform(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "windows" or "win32" or "win" => PlanPlatform.Windows,
            "macos" or "mac" or "darwin" or "osx" => PlanPlatform.MacOs,
            "linux" => PlanPlatform.Linux,
            _ => PlatformFacts.Current,
        };
    }

    private static void AppendKey(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append(" = ").Append(value).Append('\n');
    }
}
