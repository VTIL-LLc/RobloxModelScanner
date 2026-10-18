using RMS.Devops.Service.Enums;

namespace RMS.Devops.Service.Models;

/// <summary>
/// One program on this machine: what it is, how it is started, and how its most recent run ended.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is assembled from two places, because the two know different things. The plan knows what the
/// deployment intends to run -- the executable, the arguments, the web site, the port. The run store knows what
/// has actually happened -- how many runs, when the last one started, how it ended. Neither is authoritative
/// alone, and a page that showed only one of them would either describe a machine that does not exist or omit
/// the service that a previous deployment left behind.
/// </para>
/// <para>
/// <see cref="IsInPlan"/> is therefore not decoration. A service with runs but no plan entry is one that was
/// deployed once and has since been removed from the manifest, and its history is exactly what an operator
/// needs to see while deciding whether the removal was a mistake.
/// </para>
/// </remarks>
public sealed class ServiceSnapshot
{
    /// <summary>
    /// Gets the service's name, which is also its log directory and the value of the host's <c>--service</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the service's identifier in the plan, which is the name of the manifest section.
    /// </summary>
    public string PlanId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the human description of the service.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the command line the supervisor runs, as the plan describes it.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// Gets the working directory the service runs in.
    /// </summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Gets the directory the service's runs are recorded under.
    /// </summary>
    public string LogDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Gets the executable, when the service is a process.
    /// </summary>
    public string Executable { get; init; } = string.Empty;

    /// <summary>
    /// Gets the directory served, when the service is a web site rather than a process.
    /// </summary>
    public string Website { get; init; } = string.Empty;

    /// <summary>
    /// Gets the port the web site listens on, when there is one.
    /// </summary>
    public string Port { get; init; } = string.Empty;

    /// <summary>
    /// Gets the restart policy in force for the service.
    /// </summary>
    public string Restart { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many runs the service keeps before the oldest are removed.
    /// </summary>
    public int RetainedRuns { get; init; }

    /// <summary>
    /// Gets how many runs the service has on disk right now.
    /// </summary>
    public int RunCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether the service appears in the current plan.
    /// </summary>
    public bool IsInPlan { get; init; }

    /// <summary>
    /// Gets a value indicating whether the service is a web site rather than a process.
    /// </summary>
    public bool IsWebsite => Website.Length > 0;

    /// <summary>
    /// Gets the state the service's newest run leaves it in.
    /// </summary>
    public FleetState State { get; init; }

    /// <summary>
    /// Gets the sentence explaining the state, rendered as a title attribute.
    /// </summary>
    public string StateDescription => Describe(State);

    /// <summary>
    /// Gets the identifier of the newest run, when there is one.
    /// </summary>
    public string? LatestRun { get; init; }

    /// <summary>
    /// Gets the status keyword of the newest run.
    /// </summary>
    public string LatestStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets when the newest run was created.
    /// </summary>
    public DateTimeOffset? LatestCreated { get; init; }

    /// <summary>
    /// Gets when the newest run's process was started.
    /// </summary>
    public DateTimeOffset? LatestStarted { get; init; }

    /// <summary>
    /// Gets when the newest run finished.
    /// </summary>
    public DateTimeOffset? LatestCompleted { get; init; }

    /// <summary>
    /// Gets the exit code of the newest run's most recent attempt.
    /// </summary>
    public int? LatestExitCode { get; init; }

    /// <summary>
    /// Gets the number of start attempts in the newest run.
    /// </summary>
    public int LatestAttempts { get; init; }

    /// <summary>
    /// Gets the elapsed time of the newest run, rendered as the record renders it.
    /// </summary>
    public string LatestRuntimeText { get; init; } = string.Empty;

    /// <summary>
    /// Gets the size of the newest run's log in bytes.
    /// </summary>
    public long LatestLogBytes { get; init; }

    /// <summary>
    /// Gets the newest runs, newest first, so a detail page has a table before it polls.
    /// </summary>
    public IReadOnlyList<RunSnapshot> RecentRuns { get; init; } = [];

    /// <summary>
    /// Explains a state in one sentence.
    /// </summary>
    /// <param name="state">The state to explain.</param>
    /// <returns>The explanation.</returns>
    public static string Describe(FleetState state)
    {
        return state switch
        {
            FleetState.Running => "The service is running and the supervisor is watching it",
            FleetState.Healthy => "The last run finished successfully",
            FleetState.Failed => "The last run failed",
            FleetState.Stopped => "The last run was asked to stop and did",
            FleetState.Abandoned => "The supervisor stopped writing without observing the process exit",
            _ => "The service has not run yet",
        };
    }
}
