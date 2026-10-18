namespace RMS.Devops.Service.Models;

/// <summary>
/// One published program on this machine.
/// </summary>
/// <remarks>
/// <para>
/// An artefact is a directory under the publish root. Its identity is its name, its size, how many files it
/// holds and when it last changed, and its version comes from the file version of the assembly the publish
/// produced. Those four facts answer the question a deployment raises: is the thing that is running the thing
/// that was published, and when was it published.
/// </para>
/// <para>
/// The version is read from the build's own metadata rather than from a file the deployer writes, so it cannot
/// disagree with the binary it describes, and it survives an artefact being copied by hand -- which is exactly
/// how this stack reaches a machine that was never given the repository.
/// </para>
/// </remarks>
public sealed class ArtifactSnapshot
{
    /// <summary>
    /// Gets the artefact's name, which is its directory name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the absolute path of the artefact's directory.
    /// </summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>
    /// Gets the assembly name the publish produced, when one could be identified.
    /// </summary>
    public string Assembly { get; init; } = string.Empty;

    /// <summary>
    /// Gets the file version of the published assembly.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Gets the informational version of the published assembly, when it differs from the file version.
    /// </summary>
    public string InformationalVersion { get; init; } = string.Empty;

    /// <summary>
    /// Gets the total size of the artefact in bytes.
    /// </summary>
    public long Bytes { get; init; }

    /// <summary>
    /// Gets how many files the artefact holds.
    /// </summary>
    public int Files { get; init; }

    /// <summary>
    /// Gets when the artefact last changed.
    /// </summary>
    public DateTimeOffset? Updated { get; init; }

    /// <summary>
    /// Gets the name of the service the artefact belongs to, or an empty string when none does.
    /// </summary>
    /// <remarks>
    /// The join is made through the plan: a service whose executable or web-site directory falls inside this
    /// artefact's directory is the service this artefact is. Matching on the directory name alone would not
    /// work, because a publish destination is named for the project and a service is named for its role, and
    /// the two are deliberately not the same string.
    /// </remarks>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many runs the artefact's service has recorded.
    /// </summary>
    /// <remarks>
    /// Zero against a non-empty <see cref="ServiceName"/> means the artefact is published and the service is
    /// in the plan but has never started. That is a state worth showing rather than hiding: it is the
    /// difference between a deployment that was never applied and one that failed.
    /// </remarks>
    public int Runs { get; init; }
}
