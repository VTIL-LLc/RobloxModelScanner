using System.Diagnostics;

using Microsoft.Extensions.Logging;

using RMS.Devops.Service.Interfaces;
using RMS.Devops.Service.Models;

namespace RMS.Devops.Service.Implementation;

/// <summary>
/// Describes the programs this machine has been given, by reading the publish root.
/// </summary>
/// <remarks>
/// <para>
/// An artefact is a directory under the publish root and is described by what is inside it: how many files,
/// how many bytes, when it last changed, and the version of the assembly the publish produced. Nothing is
/// recorded about the deployment itself, so this listing cannot go stale and cannot lie. What it cannot say is
/// which deployment produced an artefact -- that question belongs to whatever wrote the plan, and answering it
/// here would mean inventing a history this service never saw.
/// </para>
/// <para>
/// The version comes from the assembly's own metadata rather than from a file the deployer writes. That means
/// it cannot disagree with the binary it describes, and it survives the artefact being copied onto a machine
/// that never had the repository -- which is how this stack reaches its servers.
/// </para>
/// <para>
/// Counting files and bytes is a walk of the publish root. The root holds a handful of directories that were
/// each produced by a single publish, so the walk is bounded by what was actually published rather than by what
/// is running, and unreadable subtrees are skipped instead of failing the page.
/// </para>
/// </remarks>
public sealed class PublishedArtifactInventory : IArtifactInventory
{
    private readonly IServiceArchive _archive;
    private readonly ILogger<PublishedArtifactInventory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishedArtifactInventory"/> class.
    /// </summary>
    /// <param name="archive">The archive that supplies the publish root and the plan's services.</param>
    /// <param name="logger">The logger used to report a publish root that could not be enumerated.</param>
    public PublishedArtifactInventory(IServiceArchive archive, ILogger<PublishedArtifactInventory> logger)
    {
        ArgumentNullException.ThrowIfNull(archive);

        _archive = archive;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Root => _archive.OutputRoot;

    /// <inheritdoc />
    public IReadOnlyList<ArtifactSnapshot> List()
    {
        string root = _archive.OutputRoot;

        if (root.Length == 0 || !Directory.Exists(root)) return [];

        string[] directories;

        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The publish root {Root} could not be enumerated.", root);
            return [];
        }

        // The services are read once for the whole listing rather than once per artefact: the join is the same
        // question asked of every row, and asking it repeatedly would multiply the plan read by the artefact
        // count for no additional information.
        IReadOnlyList<ServiceSnapshot> services = _archive.ListServices(0);

        List<ArtifactSnapshot> artifacts = [];

        foreach (string directory in directories)
        {
            string name = Path.GetFileName(directory);
            if (name.Length == 0) continue;

            ArtifactSnapshot? artifact = Describe(name, directory, services);
            if (artifact is not null) artifacts.Add(artifact);
        }

        artifacts.Sort(static (left, right) =>
        {
            int byUpdated = Nullable.Compare(right.Updated, left.Updated);
            return byUpdated != 0
                ? byUpdated
                : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return artifacts;
    }

    /// <summary>
    /// Describes one artefact.
    /// </summary>
    /// <param name="name">The artefact's directory name.</param>
    /// <param name="directory">The artefact's directory.</param>
    /// <param name="services">The plan's services, used to join an artefact to the service it belongs to.</param>
    /// <returns>The snapshot, or <see langword="null"/> when the directory cannot be walked.</returns>
    private ArtifactSnapshot? Describe(string name, string directory, IReadOnlyList<ServiceSnapshot> services)
    {
        long bytes = 0;
        int files = 0;
        DateTime updated = default;

        try
        {
            DirectoryInfo root = new(directory);

            foreach (FileInfo file in root.EnumerateFiles("*", new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                     }))
            {
                bytes += file.Length;
                files++;

                if (file.LastWriteTimeUtc > updated) updated = file.LastWriteTimeUtc;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The artefact under {Directory} could not be enumerated.", directory);
            return null;
        }

        string assembly = FindEntryAssembly(directory);
        ServiceSnapshot? owner = FindOwner(directory, services);

        return new ArtifactSnapshot
        {
            Name = name,
            Directory = directory,
            Assembly = assembly,
            Version = ReadVersion(directory, assembly, informational: false),
            InformationalVersion = ReadVersion(directory, assembly, informational: true),
            Bytes = bytes,
            Files = files,
            Updated = updated == default ? null : new DateTimeOffset(updated, TimeSpan.Zero),
            ServiceName = owner?.Name ?? string.Empty,
            Runs = owner?.RunCount ?? 0,
        };
    }

    /// <summary>
    /// Finds the name of the assembly a publish produced.
    /// </summary>
    /// <param name="directory">The artefact's directory.</param>
    /// <returns>
    /// The assembly name, or an empty string when the publish left no dependency manifest -- which is the case
    /// for the artefact that holds the site's static files.
    /// </returns>
    /// <remarks>
    /// The publish writes <c>Name.deps.json</c> beside the assembly it describes, so the name is taken from
    /// there rather than guessed from the directory. Guessing would break the moment a publish destination was
    /// renamed, and it would be silently wrong rather than loudly wrong.
    /// </remarks>
    private static string FindEntryAssembly(string directory)
    {
        try
        {
            foreach (string candidate in Directory.EnumerateFiles(directory, "*.deps.json"))
            {
                string file = Path.GetFileNameWithoutExtension(candidate);
                string assembly = Path.GetFileNameWithoutExtension(file);

                if (assembly.Length == 0 || assembly.Equals("runtimeconfig", StringComparison.OrdinalIgnoreCase))
                    continue;

                return assembly;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// Reads a version out of an assembly's metadata.
    /// </summary>
    /// <param name="directory">The artefact's directory.</param>
    /// <param name="assembly">The assembly name, which may be empty.</param>
    /// <param name="informational">Whether the informational version is wanted instead of the file version.</param>
    /// <returns>The version, or an empty string when it cannot be read.</returns>
    private static string ReadVersion(string directory, string assembly, bool informational)
    {
        if (assembly.Length == 0) return string.Empty;

        string path = Path.Combine(directory, assembly + ".dll");
        if (!File.Exists(path)) return string.Empty;

        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            string value = informational ? info.ProductVersion ?? string.Empty : info.FileVersion ?? string.Empty;

            return value.Length > 0 && !value.Equals(assembly, StringComparison.Ordinal)
                ? value
                : value.Trim();
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Finds the service an artefact belongs to.
    /// </summary>
    /// <param name="directory">The artefact's directory.</param>
    /// <param name="services">The plan's services.</param>
    /// <returns>The service, or <see langword="null"/> when no service runs out of this artefact.</returns>
    private static ServiceSnapshot? FindOwner(string directory, IReadOnlyList<ServiceSnapshot> services)
    {
        foreach (ServiceSnapshot service in services)
        {
            if (IsUnder(directory, service.Executable) || IsUnder(directory, service.Website)) return service;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a path lies inside a directory.
    /// </summary>
    /// <param name="directory">The containing directory.</param>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the path is inside the directory.</returns>
    private static bool IsUnder(string directory, string path) =>
        path.Length > directory.Length
        && path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
        && (path[directory.Length] == Path.DirectorySeparatorChar
            || path[directory.Length] == Path.AltDirectorySeparatorChar);
}
