using System.Net;

namespace RMS.Docs.Service.Interfaces;

/// <summary>
/// Decides whether a caller may read the documentation.
/// </summary>
/// <remarks>
/// <para>
/// This service is guarded like its neighbours rather than left open because the documentation is not secret.
/// The reason is uniformity: every internal surface in this stack is behind a key, so a port that is open never
/// means a surface that is readable. A rule with an exception in it is a rule somebody has to remember, and the
/// one time it is forgotten is the time it matters.
/// </para>
/// <para>
/// Every construction path fails closed: a missing key, a short key, an unusable allow-list and an unreadable
/// key file all disable the surface rather than relaxing it.
/// </para>
/// </remarks>
public interface IDocsKeyGuard
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
