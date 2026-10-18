using System.Net;

namespace RMS.Configuration.Service.Interfaces;

/// <summary>
/// Decides whether a caller may read or write the deployment's manifests.
/// </summary>
/// <remarks>
/// <para>
/// This service is worse to expose than the DevOps one. DevOps reports where the deployment's files are; this one
/// hands out the files themselves and, when asked, rewrites them. A manifest names the programs a machine runs,
/// the ports they listen on and the environment they are started with, so read access here is a map of the
/// deployment and write access here is the ability to change what the next deployment does.
/// </para>
/// <para>
/// Every construction path fails closed: a missing key, a short key, an unusable allow-list and an unreadable key
/// file all disable the surface rather than relaxing it.
/// </para>
/// </remarks>
public interface IConfigurationKeyGuard
{
    /// <summary>
    /// Gets a value indicating whether the surface is usable at all.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets the reason the surface is disabled, or <see langword="null"/> when it is enabled.
    /// </summary>
    string? DisabledReason { get; }

    /// <summary>
    /// Decides whether a call may proceed.
    /// </summary>
    /// <param name="remoteAddress">The peer address of the connection.</param>
    /// <param name="suppliedKey">The key the caller presented, if any.</param>
    /// <param name="rejectionReason">The reason for a refusal, when one is returned.</param>
    /// <returns><see langword="true"/> when the call is permitted.</returns>
    bool IsPermitted(IPAddress? remoteAddress, string? suppliedKey, out string? rejectionReason);
}
