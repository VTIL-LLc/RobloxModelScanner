using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Models;

namespace RMS.ServiceHost.Interfaces;

/// <summary>
/// Runs one service and keeps a faithful record of what it did.
/// </summary>
/// <remarks>
/// The interface exists so that the two front ends this program has -- the Windows service wrapper and the
/// interactive console -- drive the same object rather than each holding their own copy of the supervision
/// loop. A second implementation of the loop would be a second place for the restart policy, the stop timeout
/// and the run record to be got wrong independently.
/// </remarks>
public interface IServiceSupervisor : IDisposable
{
    /// <summary>
    /// Gets the service being supervised.
    /// </summary>
    ServicePlan Service { get; }

    /// <summary>
    /// Gets the status of the current run.
    /// </summary>
    RunStatus Status { get; }

    /// <summary>
    /// Gets the identifier of the current run.
    /// </summary>
    string RunId { get; }

    /// <summary>
    /// Gets the absolute path of the log the current run is writing.
    /// </summary>
    string LogPath { get; }

    /// <summary>
    /// Gets the exit code of the last attempt, once one has exited.
    /// </summary>
    int? ExitCode { get; }

    /// <summary>
    /// Runs the service until it reaches a terminal state.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that asks the supervisor to stop, in the same way <see cref="RequestStop"/> does.
    /// </param>
    /// <returns>The exit code of the last attempt, or zero when the service was stopped before it ran.</returns>
    Task<int> RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asks the supervisor to shut the service down.
    /// </summary>
    /// <param name="reason">Why the shutdown was requested, recorded in the log.</param>
    /// <remarks>
    /// This returns as soon as the request is registered. The caller that needs to wait observes
    /// <see cref="RunAsync"/>, which is the only thing that knows when the child has actually gone. Returning
    /// immediately is what lets a service wrapper call this from a callback that must not block, such as a
    /// signal handler.
    /// </remarks>
    void RequestStop(string reason);
}
