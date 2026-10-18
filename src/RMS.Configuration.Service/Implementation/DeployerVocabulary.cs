using RMS.Configuration.Service.Enums;
using RMS.Configuration.Service.Models;

namespace RMS.Configuration.Service.Implementation;

/// <summary>
/// The vocabulary the deployer's manifests are written in, and the one table that defines it.
/// </summary>
/// <remarks>
/// <para>
/// Every key below was read out of <c>rmsdeploy.c</c> rather than out of the manifests' own comments, and every
/// buffer size is the C struct field's. That matters because the two disagree in places: the comments describe
/// the format as it was designed, and the parser describes it as it is. A console that validated against the
/// comments would refuse manifests that work and accept manifests that fail.
/// </para>
/// <para>
/// The distinction between <see cref="VocabularyEntry.PerPlatform"/> and everything else is not cosmetic. The
/// deployer reads a key through <c>platform_value</c>, which tries <c>windows.key</c> and then <c>key</c>, or
/// through <c>section_value</c>, which reads the key exactly as written. Qualifying a key of the second kind
/// does not override it -- it hides it, silently, because the unqualified spelling is then simply absent. That
/// is the one mistake in this format that produces no message at all, so it is reported here.
/// </para>
/// </remarks>
public static class DeployerVocabulary
{
    /// <summary>The key naming what a section is: a requirement kind or a build kind.</summary>
    public const string KindKey = "kind";

    /// <summary>The key naming the platform qualification a key may carry.</summary>
    public const string PlatformsKey = "platforms";

    /// <summary>The key naming a requirement's version.</summary>
    public const string VersionKey = "version";

    /// <summary>The key naming where a requirement is downloaded from.</summary>
    public const string UrlKey = "url";

    /// <summary>The key naming the command that installs a package.</summary>
    public const string InstallKey = "install";

    /// <summary>The key naming where an extracted archive is placed.</summary>
    public const string InstallDirectoryKey = "install_dir";

    /// <summary>The key naming a shell command whose exit status decides presence.</summary>
    public const string VerifyKey = "verify";

    /// <summary>The key naming a tool the deployer's own search decides presence for.</summary>
    public const string VerifyToolKey = "verify_tool";

    /// <summary>The key naming paths one of which decides presence.</summary>
    public const string VerifyPathKey = "verify_path";

    /// <summary>The key naming a directory added to the path once a requirement is installed.</summary>
    public const string ProvidesKey = "provides";

    /// <summary>The key naming an installer's unattended arguments.</summary>
    public const string SilentArgumentsKey = "silent_args";

    /// <summary>The key naming how many leading path components an archive wraps its payload in.</summary>
    public const string StripKey = "strip";

    /// <summary>The key naming a build step's project, package or command.</summary>
    public const string PathKey = "path";

    /// <summary>The key naming a build step's configuration.</summary>
    public const string ConfigurationKey = "config";

    /// <summary>The key naming a build step's one-line description.</summary>
    public const string NoteKey = "note";

    /// <summary>The key naming a service.</summary>
    public const string NameKey = "name";

    /// <summary>The key naming a service's description.</summary>
    public const string DescriptionKey = "description";

    /// <summary>The key naming a service's program.</summary>
    public const string ExecutableKey = "executable";

    /// <summary>The key naming a service's argument list.</summary>
    public const string ArgumentsKey = "arguments";

    /// <summary>The key naming a service's working directory.</summary>
    public const string WorkingDirectoryKey = "working_dir";

    /// <summary>The key naming a service's environment.</summary>
    public const string EnvironmentKey = "environment";

    /// <summary>The key naming a service's restart policy.</summary>
    public const string RestartKey = "restart";

    /// <summary>The key naming a service's first restart delay.</summary>
    public const string RestartDelayKey = "restart_delay";

    /// <summary>The key naming the ceiling a service's restart delay grows to.</summary>
    public const string MaximumRestartDelayKey = "max_restart_delay";

    /// <summary>The key naming the grace period between a stop request and a kill.</summary>
    public const string StopTimeoutKey = "stop_timeout";

    /// <summary>The key naming the line written to a service's input to ask it to stop.</summary>
    public const string ShutdownLineKey = "shutdown_line";

    /// <summary>The key naming the directory a web site is served from.</summary>
    public const string WebsiteKey = "website";

    /// <summary>The key naming a web site's port.</summary>
    public const string PortKey = "port";

    /// <summary>The key naming the account a service runs as.</summary>
    public const string UserKey = "user";

    /// <summary>
    /// The separator a manifest uses to write a list of arguments or environment pairs.
    /// </summary>
    /// <remarks>
    /// Semicolons rather than spaces, because every value in these files is a path or a URL and both may contain
    /// a space. The consequence, which is worth knowing when writing an environment value, is that an
    /// environment value may not itself contain a semicolon.
    /// </remarks>
    public const char ListSeparator = ';';

    /// <summary>The requirement kinds the deployer recognises.</summary>
    public static readonly string[] RequirementKinds =
        ["dotnet", "node", "msbuild", "archive", "package", "installer"];

    /// <summary>The build kinds the deployer recognises.</summary>
    public static readonly string[] BuildKinds =
        ["dotnet-solution", "dotnet-project", "framework", "node-package", "shell"];

    /// <summary>The tools the deployer's own search can decide presence for.</summary>
    public static readonly string[] VerifiableTools = ["dotnet", "node", "npm", "msbuild", "xsp"];

    /// <summary>The restart policies the plan format and the supervisor understand.</summary>
    public static readonly string[] RestartPolicies = ["always", "on-failure", "never"];

    /// <summary>The platform names a key may be qualified with.</summary>
    public static readonly string[] Platforms = ["windows", "linux", "macos"];

    private static readonly VocabularySection[] All = [Requirements(), Build(), Services(), Plan()];

    private static readonly Dictionary<ManifestKind, VocabularySection> ByKind = BuildIndex();

    /// <summary>
    /// Returns the vocabulary for a manifest kind.
    /// </summary>
    /// <param name="kind">The kind to describe.</param>
    /// <returns>The vocabulary, which is never absent for a kind that exists.</returns>
    public static VocabularySection For(ManifestKind kind) => ByKind[kind];

    /// <summary>
    /// Describes the whole format.
    /// </summary>
    /// <returns>The vocabulary, the platform names and the parser's limits.</returns>
    public static VocabularyView Describe()
    {
        return new VocabularyView
        {
            Sections = [.. All],
            Platforms = [.. Platforms],
            Limits =
            [
                new DeployerLimitView
                {
                    Name = nameof(DeployerLimits.MaximumSections),
                    Value = DeployerLimits.MaximumSections,
                    Summary = "The most sections one manifest may hold. The deployer refuses the file beyond it.",
                },
                new DeployerLimitView
                {
                    Name = nameof(DeployerLimits.MaximumKeysPerSection),
                    Value = DeployerLimits.MaximumKeysPerSection,
                    Summary = "The most keys one section may hold, counted with all platform qualifications.",
                },
                new DeployerLimitView
                {
                    Name = nameof(DeployerLimits.MaximumIdentifierLength),
                    Value = DeployerLimits.MaximumIdentifierLength,
                    Summary = "The longest a section name, a key, a tool name or an account name may be.",
                },
                new DeployerLimitView
                {
                    Name = "RMS_MAX_VALUE",
                    Value = DeployerLimits.MaximumValueLength,
                    Summary = "The longest most values may be. A subscripted key may declare a shorter one.",
                },
                new DeployerLimitView
                {
                    Name = nameof(DeployerLimits.MaximumPathLength),
                    Value = DeployerLimits.MaximumPathLength,
                    Summary = "The longest a value that is a path may be.",
                },
                new DeployerLimitView
                {
                    Name = nameof(DeployerLimits.MaximumLineLength),
                    Value = DeployerLimits.MaximumLineLength,
                    Summary = "The longest one line may be. Beyond it the deployer reads the line as two, and the "
                              + "second half is a key with no value.",
                },
            ],
            Summary = "Three hand-written manifests and one generated plan, read by one parser with one "
                      + "vocabulary. A key qualified with a platform overrides the unqualified key for that "
                      + "platform only.",
        };
    }

    /// <summary>
    /// Separates a written key from its platform qualification.
    /// </summary>
    /// <param name="key">The key as written.</param>
    /// <param name="platform">The platform the key is qualified for, or an empty string.</param>
    /// <param name="unqualified">The key with any qualification removed.</param>
    /// <returns>
    /// <see langword="true"/> when the key carried a platform qualification, including one naming a platform this
    /// deployer does not know -- which is reported as a mistake rather than ignored, because a typo in a platform
    /// name silently removes the setting.
    /// </returns>
    public static bool TrySplitKey(string key, out string platform, out string unqualified)
    {
        platform = string.Empty;
        unqualified = key;

        int separator = key.IndexOf('.');

        // The qualification is the part before the first dot, and a key may not itself contain one. A dot
        // anywhere else is therefore part of a key nobody declared, which the caller reports as an unknown key.
        if (separator <= 0) return false;

        platform = key[..separator];
        unqualified = key[(separator + 1)..];

        return true;
    }

    /// <summary>
    /// Determines whether a platform name is one a key may be qualified with.
    /// </summary>
    /// <param name="platform">The name to test.</param>
    /// <returns><see langword="true"/> when the name is a platform.</returns>
    public static bool IsPlatformName(string platform)
    {
        foreach (string candidate in Platforms)
        {
            if (candidate.Equals(platform, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the entry describing an unqualified key name.
    /// </summary>
    /// <param name="section">The vocabulary to search.</param>
    /// <param name="key">The unqualified key name.</param>
    /// <returns>The entry, or <see langword="null"/> when the key is not one the deployer reads.</returns>
    public static VocabularyEntry? Find(VocabularySection section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);

        foreach (VocabularyEntry entry in section.Keys)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal)) return entry;
        }

        return null;
    }

    /// <summary>
    /// Builds the requirements vocabulary.
    /// </summary>
    /// <returns>The vocabulary for <c>rmsdeploy.requirements</c>.</returns>
    private static VocabularySection Requirements()
    {
        return new VocabularySection
        {
            Kind = ManifestKind.Requirements,
            FileName = Options.ConfigurationOptions.RequirementsFileName,
            Summary = "Every prerequisite the stack needs on this machine, one section per requirement.",
            SectionSummary = "One requirement. Its section name is an identifier the deployer reports in every "
                             + "message about it.",
            Kinds = [.. RequirementKinds],
            Keys =
            [
                Entry(KindKey, false, "Which kind of prerequisite this is. Required: the deployer refuses the "
                                      + "section without it.", values: RequirementKinds,
                      maximum: DeployerLimits.MaximumKindLength),
                Entry(VersionKey, false, "Free text, reported by the deployer's probe command and not used for "
                                         + "logic. It is what an operator reads to know what is installed.",
                      maximum: DeployerLimits.MaximumVersionLength),
                Entry(UrlKey, true, "Where the requirement is downloaded from. Only https is accepted: the "
                                    + "deployer refuses anything else, and on Windows the choice of TLS is made "
                                    + "from the scheme."),
                Entry(InstallKey, true, "The command that installs a package with the platform's own package "
                                        + "manager. It is also the fallback when a download fails."),
                Entry(InstallDirectoryKey, true, "Where an extracted archive is placed, relative to the repository "
                                                 + "root unless it is absolute.",
                      maximum: DeployerLimits.MaximumPathLength),
                Entry(StripKey, true, "How many leading path components to remove while extracting. Official "
                                      + "toolchain archives wrap their payload differently per platform, so this "
                                      + "is declared rather than guessed."),
                Entry(VerifyToolKey, true, "Presence is decided by the deployer's own toolchain search, which is "
                                           + "the same resolution the build steps use. This is the preferred "
                                           + "check.", values: VerifiableTools,
                      maximum: DeployerLimits.MaximumKindLength),
                Entry(VerifyPathKey, true, "Semicolon separated paths, any one of which satisfies the requirement. "
                                           + "For a toolchain that installs into a known place without putting "
                                           + "itself on the path."),
                Entry(VerifyKey, true, "A shell command, run by the platform's shell; its exit status decides. "
                                       + "Checked last and silent on failure."),
                Entry(ProvidesKey, false, "A directory, relative to the repository root, prepended to the path "
                                          + "once the requirement has been installed. NOT platform qualified: "
                                          + "a prefixed spelling would be ignored.",
                      maximum: DeployerLimits.MaximumPathLength),
                Entry(SilentArgumentsKey, true, "The unattended installation arguments, for the installer kind."),
                Entry(PlatformsKey, false, "Which platforms the requirement applies to, separated by spaces or "
                                           + "commas. Absent means all of them. NOT platform qualified."),
            ],
        };
    }

    /// <summary>
    /// Builds the build vocabulary.
    /// </summary>
    /// <returns>The vocabulary for <c>rmsdeploy.build</c>.</returns>
    private static VocabularySection Build()
    {
        return new VocabularySection
        {
            Kind = ManifestKind.Build,
            FileName = Options.ConfigurationOptions.BuildFileName,
            Summary = "The ordered list of things to build, and how to build each one.",
            SectionSummary = "One build step. Steps run in the order they appear, and a step whose source does not "
                             + "exist yet is reported as not present and skipped rather than failing, so this "
                             + "file can describe the finished stack while parts of it are still being written.",
            Kinds = [.. BuildKinds],
            Keys =
            [
                Entry(KindKey, false, "How to build it. Required: the deployer refuses the section without it. "
                                      + "Which kind a .NET Framework project needs is decided by how its project "
                                      + "file is written, not by the framework it targets: an SDK-style project "
                                      + "is a dotnet kind even when it targets net48, and a legacy-style one is "
                                      + "a framework kind.", values: BuildKinds,
                      maximum: DeployerLimits.MaximumKindLength),
                Entry(PathKey, false, "The solution, project or package directory, relative to the repository "
                                      + "root. For the shell kind, the command itself.",
                      maximum: DeployerLimits.MaximumPathLength),
                Entry(ConfigurationKey, false, "Build configuration. The deployer defaults it to Release.",
                      defaultvalue: "Release", maximum: DeployerLimits.MaximumKindLength),
                Entry(PlatformsKey, false, "Which platforms the step applies to. Absent means all of them. NOT "
                                           + "platform qualified."),
                Entry(NoteKey, false, "Free text, shown by the deployer's build command.",
                      maximum: DeployerLimits.MaximumNoteLength),
            ],
        };
    }

    /// <summary>
    /// Builds the services vocabulary.
    /// </summary>
    /// <returns>The vocabulary for <c>rmsdeploy.services</c>.</returns>
    private static VocabularySection Services()
    {
        return new VocabularySection
        {
            Kind = ManifestKind.Services,
            FileName = Options.ConfigurationOptions.ServicesFileName,
            Summary = "What to run once everything is built, and how each platform should run it.",
            SectionSummary = "One service. Every delay and timeout here is read by the supervisor rather than by "
                             + "the platform's own service manager, because the supervisor is what restarts a "
                             + "service; the manager only restarts the supervisor.",
            Kinds = [],
            Keys =
            [
                Entry(NameKey, false, "The identifier used for the service, the systemd unit, the launchd label "
                                      + "and the log directory. The deployer defaults it to the section name. "
                                      + "NOT platform qualified.", maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(DescriptionKey, false, "Shown by the supervisor, by the platform's own service tooling and "
                                             + "on the DevOps page.",
                      maximum: DeployerLimits.MaximumDescriptionLength),
                Entry(ExecutableKey, true, "The program to start. Use %ROOT% for the repository root so that the "
                                           + "manifest does not depend on where it was checked out. Empty for a "
                                           + "web site the platform serves rather than runs.",
                      maximum: DeployerLimits.MaximumPathLength),
                Entry(ArgumentsKey, true, "The command line, SEMICOLON separated. Semicolons rather than spaces "
                                          + "because a deployment path can contain a space; each entry becomes "
                                          + "its own key in the plan, so no consumer ever splits a command line "
                                          + "and no shell ever re-splits an argument."),
                Entry(WorkingDirectoryKey, true, "The working directory. The deployer defaults it to the "
                                                 + "repository root, because a process started without one "
                                                 + "inherits a directory the operator did not choose.",
                      maximum: DeployerLimits.MaximumPathLength),
                Entry(EnvironmentKey, true, "SEMICOLON separated KEY=VALUE pairs. Semicolons for the same reason "
                                            + "as arguments, which means an environment value may not itself "
                                            + "contain a semicolon."),
                Entry(RestartKey, true, "What the supervisor should do when the process exits. The platform's own "
                                        + "manager only restarts the supervisor; this is what restarts the "
                                        + "program. A value the supervisor does not recognise is read as "
                                        + "'always'.", values: RestartPolicies,
                      defaultvalue: "always", maximum: DeployerLimits.MaximumKindLength),
                Entry(RestartDelayKey, true, "Seconds before the first restart. The supervisor grows it "
                                             + "exponentially up to the ceiling, so a service that crashes on "
                                             + "start does not become a busy loop.",
                      defaultvalue: "5", maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(MaximumRestartDelayKey, true, "The ceiling the delay doubles up to, in seconds.",
                      defaultvalue: "60", maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(StopTimeoutKey, true, "Seconds between a stop request and a kill. The deployer raises a "
                                            + "value below one to one.",
                      defaultvalue: "30", maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(ShutdownLineKey, true, "Text written to the service's standard input to ask it to stop. A "
                                             + "Windows service has no console to receive a control event and a "
                                             + "POSIX signal cannot be observed by .NET application code, so a "
                                             + "line on standard input is the one channel that works identically "
                                             + "on all three platforms -- but only a program that reads it "
                                             + "benefits."),
                Entry(WebsiteKey, true, "Set when the entry is an IIS web site on Windows rather than a process, "
                                        + "and names the directory to serve. There is then no process for the "
                                        + "supervisor to own.", maximum: DeployerLimits.MaximumPathLength),
                Entry(PortKey, true, "The TCP port for a web site entry, as text.",
                      maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(PlatformsKey, false, "Which platforms the service applies to. Absent means all of them. NOT "
                                           + "platform qualified. It is a guard rather than a mechanism: the "
                                           + "deployer has already resolved each platform-qualified value for "
                                           + "the machine it is running on, and this stops a plan copied to "
                                           + "another machine from being registered on a host it was not "
                                           + "written for."),
                Entry(UserKey, false, "The account to run as, where the platform allows choosing one. NOT "
                                      + "platform qualified.", maximum: DeployerLimits.MaximumIdentifierLength),
            ],
        };
    }

    /// <summary>
    /// Builds the generated plan's vocabulary, which is documentation rather than a validation rule.
    /// </summary>
    /// <returns>The vocabulary for the plan file.</returns>
    /// <remarks>
    /// Nothing here is checked against anything, because the plan is not written by hand: it is generated, and the
    /// console reports it read-only. It is described anyway so that an operator who opens the generated file has
    /// one place to look up what a key means, which is otherwise a question about a C program.
    /// </remarks>
    private static VocabularySection Plan()
    {
        return new VocabularySection
        {
            Kind = ManifestKind.Plan,
            FileName = "services.ini",
            Summary = "The expanded, platform-neutral plan the deployer writes and the service installer reads.",
            SectionSummary = "The [settings] section carries the version, the platform, the repository root, the "
                             + "publish destination, the log root and the supervisor. Every other section is one "
                             + "service, with the same keys as the services manifest plus one key per argument "
                             + "(argument.0, argument.1, ...) and one per environment variable (environment.NAME). "
                             + "Edits are overwritten on the next deployment.",
            Kinds = [],
            Keys =
            [
                Entry(KindKey, false, "Not used by the plan.", maximum: DeployerLimits.MaximumKindLength),
                Entry(NameKey, false, "The service's effective name.", maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(DescriptionKey, false, "The description the platform's service tooling shows.",
                      maximum: DeployerLimits.MaximumDescriptionLength),
                Entry(ExecutableKey, false, "The program, already expanded and with the host platform's "
                                            + "separators.", maximum: DeployerLimits.MaximumPathLength),
                Entry(WorkingDirectoryKey, false, "The working directory, already absolute and expanded.",
                      maximum: DeployerLimits.MaximumPathLength),
                Entry(RestartKey, false, "The restart policy.", values: RestartPolicies,
                      maximum: DeployerLimits.MaximumKindLength),
                Entry(RestartDelayKey, false, "Seconds before the first restart.",
                      maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(MaximumRestartDelayKey, false, "The ceiling the delay grows to, in seconds.",
                      maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(StopTimeoutKey, false, "Seconds between a stop request and a kill.",
                      maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(ShutdownLineKey, false, "The line written to the service's input to ask it to stop."),
                Entry(WebsiteKey, false, "The directory a web site is served from, when the entry is a site.",
                      maximum: DeployerLimits.MaximumPathLength),
                Entry(PortKey, false, "The web site's port.", maximum: DeployerLimits.MaximumIdentifierLength),
                Entry(PlatformsKey, false, "The platforms the entry applies to."),
                Entry(UserKey, false, "The account to run as.", maximum: DeployerLimits.MaximumIdentifierLength),
            ],
        };
    }

    /// <summary>
    /// Builds the kind-to-vocabulary index.
    /// </summary>
    /// <returns>The index.</returns>
    /// <remarks>
    /// Built in a static constructor's helper rather than as a field initializer, so that the failure to keep the
    /// two tables in step is a start-up exception naming the kind rather than a null returned to a caller.
    /// </remarks>
    private static Dictionary<ManifestKind, VocabularySection> BuildIndex()
    {
        Dictionary<ManifestKind, VocabularySection> index = [];

        foreach (VocabularySection section in All)
            index[section.Kind] = section;

        foreach (ManifestKind kind in Enum.GetValues<ManifestKind>())
        {
            if (!index.ContainsKey(kind))
                throw new InvalidOperationException($"The vocabulary does not describe the '{kind}' manifest kind.");
        }

        return index;
    }

    /// <summary>
    /// Builds one vocabulary entry.
    /// </summary>
    /// <param name="key">The key's name, unqualified.</param>
    /// <param name="perPlatform">Whether the deployer reads the key through its platform-aware lookup.</param>
    /// <param name="summary">A sentence saying what the key is for.</param>
    /// <param name="values">The closed set of values the key accepts, when it accepts one.</param>
    /// <param name="defaultvalue">The value the deployer applies when the key is absent.</param>
    /// <param name="maximum">
    /// The longest value the deployer can hold for the key, or zero for the general value buffer.
    /// </param>
    /// <returns>The entry.</returns>
    private static VocabularyEntry Entry(
        string key,
        bool perPlatform,
        string summary,
        string[]? values = null,
        string defaultvalue = "",
        int maximum = 0)
    {
        return new VocabularyEntry
        {
            Key = key,
            PerPlatform = perPlatform,
            Required = key == KindKey,
            Values = values is null ? [] : [.. values],
            Default = defaultvalue,
            MaximumLength = maximum,
            Summary = summary,
        };
    }
}
