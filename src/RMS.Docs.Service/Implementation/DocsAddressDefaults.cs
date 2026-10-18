using Microsoft.Extensions.Options;

using RMS.Docs.Service.Options;

namespace RMS.Docs.Service.Implementation;

/// <summary>
/// Works out where the documentation is when the deployment has not said.
/// </summary>
/// <remarks>
/// <para>
/// This runs last, after the file and the environment have been applied, and it only fills in what is still
/// empty. That order is what makes it a default rather than an override: a configured address always wins.
/// </para>
/// <para>
/// The guess is that the documentation is in a <c>docs</c> directory in this checkout, identified by the landing
/// document inside it. It is found by walking up from the program's own directory, which works for a published
/// service under <c>artifacts/docs-service</c> and for a developer's <c>dotnet run</c> from the project
/// directory alike.
/// </para>
/// <para>
/// When nothing is found, nothing is set. A guess that names a directory which does not exist is worse than no
/// address at all, because the service would then report an empty documentation set rather than an unconfigured
/// one -- and those are different problems with different fixes.
/// </para>
/// </remarks>
public sealed class DocsAddressDefaults : IConfigureOptions<DocsOptions>
{
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
    public void Configure(DocsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DocumentDirectory.Length == 0)
            options.DocumentDirectory = FindDocumentDirectory() ?? string.Empty;

        if (options.RepositoryRoot.Length == 0 && options.DocumentDirectory.Length > 0)
        {
            // The documentation lives in a directory one level below the root it describes, which is what the
            // deployer's %ROOT% expands to in every value that names it.
            options.RepositoryRoot = Path.GetFullPath(
                Path.Combine(options.DocumentDirectory, "..")).TrimEnd(Path.DirectorySeparatorChar);
        }
    }

    /// <summary>
    /// Walks up from the running program looking for the documentation directory.
    /// </summary>
    /// <returns>The absolute path, or <see langword="null"/> when nothing was found.</returns>
    private static string? FindDocumentDirectory()
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
                string candidate = Path.Combine(current.FullName, DocsOptions.DocumentDirectoryName);

                if (File.Exists(Path.Combine(candidate, DocsOptions.MarkerFileName)))
                    return candidate;

                current = current.Parent;
            }
        }

        return null;
    }
}
