using RMS.Devops.Service.Models;

namespace RMS.Devops.Service.Interfaces;

/// <summary>
/// Reads the publish root: the programs this machine has been given.
/// </summary>
/// <remarks>
/// The publish root is the deployer's output, and it is the only complete statement of what has been built for
/// this machine. Reading it directly, rather than recording what a deployment did, means an artefact someone
/// copied into place by hand is reported exactly like one the deployer put there -- which is the truth, and
/// which is also how this stack is meant to reach a machine without the repository.
/// </remarks>
public interface IArtifactInventory
{
    /// <summary>
    /// Gets the publish root the inventory reads.
    /// </summary>
    string Root { get; }

    /// <summary>
    /// Lists the artefacts, most recently changed first.
    /// </summary>
    /// <returns>The snapshots. The list is empty when the root does not exist.</returns>
    IReadOnlyList<ArtifactSnapshot> List();
}
