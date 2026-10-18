using System.Net;

namespace RMS.Devops.Service.Interfaces;

/// <summary>
/// Decides whether a caller may read the DevOps surface.
/// </summary>
/// <remarks>
/// <para>
/// The service reports the layout of the machine it runs on: the absolute path of every program, every service
/// and every log file, and the contents of those logs. That is a map of a host, and a map is exactly what an
/// attacker who has reached the network wants first. The surface is therefore guarded independently of the
/// reverse proxy in front of it, on the assumption that the proxy is one layer and not the only one.
/// </para>
/// <para>
/// Every construction path fails closed.
/// </para>
/// </remarks>
public interface IDevopsKeyGuard
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
