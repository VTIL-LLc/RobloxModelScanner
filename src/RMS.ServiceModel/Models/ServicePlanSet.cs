using System.Globalization;

using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Implementation;

namespace RMS.ServiceModel.Models;

/// <summary>
/// A complete set of service plans: the machine-wide settings the deployer resolved, plus one entry per
/// service.
/// </summary>
/// <remarks>
/// The settings section exists so that a service plan never has to repeat a path that only the deployment
/// knows. The log root in particular is needed by the supervisor, by the installer and by the DevOps site, and
/// resolving it once here is what lets all three agree without any of them re-deriving it from a relative path.
/// </remarks>
public sealed class ServicePlanSet
{
    /// <summary>
    /// The plan format version.
    /// </summary>
    /// <remarks>
    /// A reader refuses a version it does not understand rather than guessing, because a plan written by a
    /// newer deployer could describe a field this reader would silently drop.
    /// </remarks>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The name of the plan file, as the deployer writes it and the installer expects to find it.
    /// </summary>
    public const string FileName = "services.ini";

    /// <summary>
    /// Gets or sets the version the plan file declared.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Gets or sets the platform the plan was generated for.
    /// </summary>
    public PlanPlatform Platform { get; set; } = PlatformFacts.Current;

    /// <summary>
    /// Gets or sets the repository root, made absolute when the plan was written.
    /// </summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publish destination root, made absolute when the plan was written.
    /// </summary>
    public string OutputRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the directory every service writes its log under.
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the supervisor the installer must register.
    /// </summary>
    /// <remarks>
    /// The services are not registered against their own executables. Every one of them is registered against
    /// <c>RMS.ServiceHost</c>, which starts the real program as its child, whichever platform supervisor is in
    /// charge. That is what gives every service the same log capture, the same restart policy and the same
    /// recorded history, on all three platforms.
    /// </remarks>
    public string HostExecutable { get; set; } = string.Empty;

    /// <summary>
    /// Gets the services, in the order the manifest listed them.
    /// </summary>
    public List<ServicePlan> Services { get; } = [];

    /// <summary>
    /// Gets the directory the plan file lives in.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// Gets the absolute path of the plan file this set was read from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the services are registered against: the supervisor is told to read this file and run one
    /// named service out of it, so the definition a service is running under is the same file an operator can open
    /// and read.
    /// </para>
    /// <para>
    /// The separator is the one the plan's own platform uses, not the one this process happens to run on. A plan is
    /// a document about another machine's service definitions, and the path inside it ends up in a unit file or a
    /// plist; building it with the host's separator would put a backslash into a Linux unit whenever the text was
    /// produced anywhere but Linux.
    /// </para>
    /// </remarks>
    public string PlanPath
    {
        get
        {
            if (Directory.Length == 0)
                return FileName;

            char separator = PlatformFacts.Separator(Platform);

            return Directory.TrimEnd('\\', '/') + separator + FileName;
        }
    }

    /// <summary>
    /// Returns the services that apply to a platform.
    /// </summary>
    /// <param name="platform">The platform to filter for.</param>
    /// <returns>The services whose <see cref="ServicePlan.Platforms"/> admits the platform.</returns>
    public IEnumerable<ServicePlan> For(PlanPlatform platform)
    {
        string name = PlatformFacts.NameOf(platform);

        foreach (ServicePlan service in Services)
        {
            if (Applies(service, name))
                yield return service;
        }
    }

    /// <summary>
    /// Tests whether a service applies to a platform.
    /// </summary>
    /// <param name="service">The service to test.</param>
    /// <param name="platformName">The lower-case platform name.</param>
    /// <returns><see langword="true"/> when the service declares no platforms, or declares the one given.</returns>
    public static bool Applies(ServicePlan service, string platformName)
    {
        if (service is null || string.IsNullOrWhiteSpace(service.Platforms))
            return true;

        foreach (string token in service.Platforms.Split([' ', ',', ';']))
        {
            string trimmed = token.Trim();

            if (trimmed.Length > 0 && trimmed.Equals(platformName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Verifies that the settings a plan cannot work without are present.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when a required setting is missing, naming it.</exception>
    public void ValidateSettings()
    {
        if (Version != CurrentVersion)
        {
            throw new InvalidDataException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The plan was written in format version {0}. This build understands version {1}; " +
                    "regenerate the plan with the matching deployer.",
                    Version,
                    CurrentVersion));
        }

        // Both of these are written into a service definition that another process will read, so neither can be a
        // relative path: a service starts in a working directory of the plan's choosing, and a relative path would be
        // resolved against that rather than against wherever the plan happened to be generated. The check is here,
        // at the boundary, rather than in each consumer, because each consumer would otherwise have to remember it.
        RequireRooted(HostExecutable, "host");
        RequireRooted(LogDirectory, "log_dir");
    }

    /// <summary>
    /// Describes the plan in a single line, for a log header or a status listing.
    /// </summary>
    /// <returns>The number of services and the platform they were generated for.</returns>
    public string Describe()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} service(s) for {1}",
            Services.Count,
            PlatformFacts.NameOf(Platform));
    }

    private static void RequireRooted(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("The plan's [settings] section does not set '" + key + "'.");

        if (!Path.IsPathRooted(value))
        {
            throw new InvalidDataException(
                "The plan's [settings] section sets '" + key + "' to '" + value + "', which is a relative path. " +
                "Regenerate the plan, so that the value is absolute and does not depend on where a service starts.");
        }
    }
}
