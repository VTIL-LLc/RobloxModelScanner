using System.Net;

namespace Scanner.Indexer.Interfaces;

/// <summary>
/// Decides whether a caller may use the internal control plane.
/// </summary>
/// <remarks>
/// Two independent checks are applied because either one alone is a single point of failure. The network
/// check keeps the surface unreachable from anywhere the operator did not explicitly allow, and the token
/// check means that a process which is already on an allowed network still cannot drive the service.
/// </remarks>
public interface IInternalCallGuard
{
    /// <summary>
    /// Gets a value indicating whether the control plane is available at all.
    /// </summary>
    /// <value>
    /// When <see langword="false"/> no control-plane endpoint may be served. This is the state a service
    /// starts in when no token has been configured, so an unconfigured deployment exposes nothing.
    /// </value>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets the reason the control plane is disabled, for logging at start-up.
    /// </summary>
    string? DisabledReason { get; }

    /// <summary>
    /// Evaluates a control-plane request.
    /// </summary>
    /// <param name="remoteAddress">The peer address of the connection.</param>
    /// <param name="suppliedToken">The value of the token header, or <see langword="null"/> when absent.</param>
    /// <param name="rejectionReason">
    /// A caller-safe explanation when the request is refused, or <see langword="null"/> when it is allowed.
    /// </param>
    /// <returns><see langword="true"/> when the caller may proceed.</returns>
    bool IsPermitted(IPAddress? remoteAddress, string? suppliedToken, out string? rejectionReason);
}
