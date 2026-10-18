using RMS.Devops.Service.Models;

namespace RMS.Devops.Service.Interfaces;

/// <summary>
/// Reads the services of this machine and the history of their runs.
/// </summary>
/// <remarks>
/// <para>
/// Everything this interface answers comes from two places on disk: the plan the deployment wrote and the run
/// store the supervisor writes into. There is no database and no in-memory state that outlives a request, so a
/// service that is stopped, moved or removed while the page is open is reported as it is at the moment it is
/// asked about, not as it was when something cached it.
/// </para>
/// <para>
/// The interface reports its own availability because the absence of a plan is a normal state rather than an
/// error: a machine that has been provisioned but not yet deployed has no plan, and the page that describes it
/// should say so in a sentence rather than by failing.
/// </para>
/// </remarks>
public interface IServiceArchive
{
    /// <summary>
    /// Gets a value indicating whether a plan was found and read.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the reason a plan could not be read, or <see langword="null"/> when it could.
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Gets the path of the plan the archive was given.
    /// </summary>
    string PlanFile { get; }

    /// <summary>
    /// Gets the log root every service's history lives under.
    /// </summary>
    string LogRoot { get; }

    /// <summary>
    /// Gets the publish root the deployment wrote its artefacts into.
    /// </summary>
    /// <remarks>
    /// The publish root is carried here rather than in its own option because it is the same plan entry the log
    /// root comes from, and a second source of truth for it could disagree with the first. An operator who
    /// narrows the plan's output directory expects the artefact page to follow.
    /// </remarks>
    string OutputRoot { get; }

    /// <summary>
    /// Lists the services of this machine, newest activity first.
    /// </summary>
    /// <param name="recentRuns">How many recent runs to attach to each service, or zero for none.</param>
    /// <returns>The snapshots.</returns>
    /// <remarks>
    /// The ordering is by the newest run's start time, with services that have never run last and alphabetical
    /// among themselves. An ordering that shuffled when a service restarted would make an operator re-read the
    /// whole page to find the one that changed.
    /// </remarks>
    IReadOnlyList<ServiceSnapshot> ListServices(int recentRuns);

    /// <summary>
    /// Finds one service.
    /// </summary>
    /// <param name="name">The service's name.</param>
    /// <returns>The snapshot, or <see langword="null"/> when no such service is known.</returns>
    ServiceSnapshot? FindService(string name);

    /// <summary>
    /// Lists runs, newest first.
    /// </summary>
    /// <param name="service">The service to list, or <see langword="null"/> for every service.</param>
    /// <param name="skip">How many runs to skip.</param>
    /// <param name="take">How many runs to return.</param>
    /// <returns>The page.</returns>
    RunPage ListRuns(string? service, int skip, int take);

    /// <summary>
    /// Finds one run of one service, with the details only its own page needs.
    /// </summary>
    /// <param name="service">The service's name.</param>
    /// <param name="runId">The run's identifier.</param>
    /// <returns>The run, or <see langword="null"/> when it does not exist.</returns>
    RunDetail? FindRun(string service, string runId);

    /// <summary>
    /// Finds the directory a service's runs are recorded in.
    /// </summary>
    /// <param name="service">The service's name.</param>
    /// <returns>The absolute path, or <see langword="null"/> when the service has never run.</returns>
    /// <remarks>
    /// This exists so that the log reader can be written against the same validated name resolution as
    /// everything else. A log reader that built its own path from a request would be a second place where a
    /// traversal could be introduced, and the second place is always the one that is forgotten.
    /// </remarks>
    string? FindRunDirectory(string service);
}
