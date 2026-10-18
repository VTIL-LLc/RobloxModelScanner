using RMS.Devops.Service.Enums;

namespace RMS.Devops.Service.Models;

/// <summary>
/// The one-line answer to "what is happening on this machine".
/// </summary>
/// <remarks>
/// <para>
/// The counts are computed from the same snapshots the service list is built from, so the header and the table
/// below it can never disagree: a page that said "four healthy" above a table showing three is worse than no
/// header at all, because the reader trusts the header.
/// </para>
/// <para>
/// <see cref="PlanLoaded"/> is reported separately from the counts because a machine with no plan is not a
/// machine with nothing running: it is a machine this process has not been told about. Saying so is the
/// difference between "the deployment is not here" and "this is the wrong host".
/// </para>
/// <para>
/// This is a record because the endpoint that builds it knows things the counter does not: the plan it read,
/// the catalogue it summarized and how many artefacts were published. Those are added to the counted result
/// rather than passed into it, so the counter stays a function of the service list alone and can be exercised
/// without a plan, a publish root or a database.
/// </para>
/// </remarks>
public sealed record FleetSummary
{
    /// <summary>
    /// Gets the name of the machine the service is running on.
    /// </summary>
    public string Machine { get; init; } = string.Empty;

    /// <summary>
    /// Gets the moment the summary was produced, in UTC.
    /// </summary>
    public DateTimeOffset ReportedUtc { get; init; }

    /// <summary>
    /// Gets the path of the plan the service was given.
    /// </summary>
    public string PlanFile { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the plan could be read.
    /// </summary>
    public bool PlanLoaded { get; init; }

    /// <summary>
    /// Gets the reason the plan could not be read, when it could not.
    /// </summary>
    public string PlanDetail { get; init; } = string.Empty;

    /// <summary>
    /// Gets the log root the runs are recorded under.
    /// </summary>
    public string LogRoot { get; init; } = string.Empty;

    /// <summary>
    /// Gets the number of services known to the service, whether from the plan or from the run store.
    /// </summary>
    public int Services { get; init; }

    /// <summary>
    /// Gets how many services are running.
    /// </summary>
    public int Running { get; init; }

    /// <summary>
    /// Gets how many services last succeeded.
    /// </summary>
    public int Healthy { get; init; }

    /// <summary>
    /// Gets how many services last failed.
    /// </summary>
    public int Failed { get; init; }

    /// <summary>
    /// Gets how many services last stopped on request.
    /// </summary>
    public int Stopped { get; init; }

    /// <summary>
    /// Gets how many services were left behind by a supervisor that did not observe the exit.
    /// </summary>
    public int Abandoned { get; init; }

    /// <summary>
    /// Gets how many services have never run.
    /// </summary>
    public int Idle { get; init; }

    /// <summary>
    /// Gets the most recent activity across every service.
    /// </summary>
    public DateTimeOffset? LastActivity { get; init; }

    /// <summary>
    /// Gets how many artefacts have been published.
    /// </summary>
    public int Artifacts { get; init; }

    /// <summary>
    /// Gets what the scanner's index says about the catalogue.
    /// </summary>
    /// <remarks>
    /// The summary travels with the fleet rather than in its own request because the overview page shows both
    /// and a second round trip would let the two halves of one page describe two different moments.
    /// </remarks>
    public CatalogSummary Catalog { get; init; } = new();

    /// <summary>
    /// Gets the name of the service whose newest run is the most recent, when there is one.
    /// </summary>
    public string LastActiveService { get; init; } = string.Empty;

    /// <summary>
    /// Counts the states in a set of snapshots into a summary.
    /// </summary>
    /// <param name="services">The snapshots to count.</param>
    /// <returns>A summary carrying the counts and the newest activity.</returns>
    /// <remarks>
    /// The newest activity is the newest of the services' newest runs, not the newest record the store holds.
    /// A service that has not run for a week must not make the fleet look busy.
    /// </remarks>
    public static FleetSummary From(IReadOnlyList<ServiceSnapshot> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        int running = 0;
        int healthy = 0;
        int failed = 0;
        int stopped = 0;
        int abandoned = 0;
        int idle = 0;

        DateTimeOffset? lastActivity = null;
        string lastActive = string.Empty;

        foreach (ServiceSnapshot service in services)
        {
            switch (service.State)
            {
                case FleetState.Running:
                    running++;
                    break;

                case FleetState.Healthy:
                    healthy++;
                    break;

                case FleetState.Failed:
                    failed++;
                    break;

                case FleetState.Stopped:
                    stopped++;
                    break;

                case FleetState.Abandoned:
                    abandoned++;
                    break;

                default:
                    idle++;
                    break;
            }

            DateTimeOffset? activity = service.LatestCompleted ?? service.LatestStarted ?? service.LatestCreated;

            if (activity is { } moment && (lastActivity is null || moment > lastActivity))
            {
                lastActivity = moment;
                lastActive = service.Name;
            }
        }

        return new FleetSummary
        {
            Machine = Environment.MachineName,
            ReportedUtc = DateTimeOffset.UtcNow,
            Services = services.Count,
            Running = running,
            Healthy = healthy,
            Failed = failed,
            Stopped = stopped,
            Abandoned = abandoned,
            Idle = idle,
            LastActivity = lastActivity,
            LastActiveService = lastActive,
        };
    }
}
