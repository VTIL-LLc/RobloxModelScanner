using Microsoft.Extensions.Options;

using RMS.Configuration.Service.Options;

namespace RMS.Configuration.Service.Implementation;

/// <summary>
/// Works out where the manifests are when the deployment has not said.
/// </summary>
/// <remarks>
/// <para>
/// This runs last, after the file and the environment have been applied, and it only fills in what is still
/// empty. That order is what makes it a default rather than an override: a configured address always wins, and a
/// value that was deliberately left blank is indistinguishable from one that was never set -- which is why the
/// console reports the addresses it resolved on every page.
/// </para>
/// <para>
/// The guess is that the deployer's manifests are in a <c>deploy</c> directory in this checkout. It is found by
/// walking up from the program's own directory, which works for a published service under
/// <c>artifacts/configuration-service</c> and for a developer's <c>dotnet run</c> from the project directory
/// alike. When nothing is found, nothing is set: a guess that names a directory which does not exist is worse
/// than no address at all, because the console would then report an empty deployment rather than an unconfigured
/// one.
/// </para>
/// </remarks>
public sealed class ManifestAddressDefaults : IConfigureOptions<ConfigurationOptions>
{
    /// <summary>
    /// The name of the directory this repository keeps the deployer's manifests in.
    /// </summary>
    public const string ManifestDirectoryName = "deploy";

    /// <summary>
    /// The file whose presence identifies the directory as the one being looked for.
    /// </summary>
    public const string ManifestMarkerFileName = ConfigurationOptions.ServicesFileName;

    /// <summary>
    /// How many levels of parent directory to search.
    /// </summary>
    /// <remarks>
    /// Four is enough for a published service two directories below the repository root and for a worktree, and
    /// few enough that the walk cannot wander out of the checkout and into a directory that belongs to something
    /// else.
    /// </remarks>
    private const int MaximumLevels = 4;

    /// <inheritdoc />
    public void Configure(ConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ManifestDirectory.Length == 0)
            options.ManifestDirectory = FindManifestDirectory() ?? string.Empty;

        if (options.RepositoryRoot.Length == 0 && options.ManifestDirectory.Length > 0)
        {
            // The manifests live in a directory one level below the root they describe, which is where %ROOT%
            // comes from in every value they contain.
            options.RepositoryRoot = Path.GetFullPath(
                Path.Combine(options.ManifestDirectory, "..")).TrimEnd(Path.DirectorySeparatorChar);
        }

        if (options.PlanFile.Length == 0 && options.RepositoryRoot.Length > 0)
        {
            options.PlanFile = Path.GetFullPath(
                Path.Combine(options.RepositoryRoot, ConfigurationOptions.DefaultPlanRelativePath));
        }
    }

    /// <summary>
    /// Walks up from the running program looking for the deployer's manifests.
    /// </summary>
    /// <returns>The absolute path, or <see langword="null"/> when nothing was found.</returns>
    private static string? FindManifestDirectory()
    {
        List<string> starts = [Directory.GetCurrentDirectory()];

        if (AppContext.BaseDirectory.Length > 0) starts.Add(AppContext.BaseDirectory);

        foreach (string start in starts)
        {
            DirectoryInfo? current;

            try
            {
                current = new DirectoryInfo(Path.GetFullPath(start));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            for (int level = 0; level <= MaximumLevels && current is not null; level++)
            {
                string candidate = Path.Combine(current.FullName, ManifestDirectoryName);

                if (File.Exists(Path.Combine(candidate, ManifestMarkerFileName)))
                    return candidate;

                current = current.Parent;
            }
        }

        return null;
    }
}
