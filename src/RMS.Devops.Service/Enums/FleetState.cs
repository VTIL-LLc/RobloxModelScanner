namespace RMS.Devops.Service.Enums;

/// <summary>
/// The state of one service, as its most recent run leaves it.
/// </summary>
/// <remarks>
/// <para>
/// The state is derived from the newest run record rather than stored. A supervisor that is killed cannot
/// write "I stopped"; what it can do is leave a record whose status never reached a terminal value, and the
/// state below names that case on its own. That is why there is no "unknown" member: every combination of a
/// record and its absence has a meaning, and the one that says least is
/// <see cref="Idle"/>, which is honest about having nothing to report.
/// </para>
/// <para>
/// The descriptions exist because the site renders each state as a word with a title attribute, following the
/// server detail page this dashboard reproduces, where "Live" carries "Servers are fully configured and taking
/// traffic". A state an operator has to look up is a state that will be misread at three in the morning.
/// </para>
/// </remarks>
public enum FleetState
{
    /// <summary>
    /// The service has never run, so there is nothing to report.
    /// </summary>
    Idle,

    /// <summary>
    /// The newest run of the service is still in progress.
    /// </summary>
    Running,

    /// <summary>
    /// The newest run finished successfully.
    /// </summary>
    Healthy,

    /// <summary>
    /// The newest run failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The newest run was asked to stop and did.
    /// </summary>
    Stopped,

    /// <summary>
    /// The newest run's supervisor stopped writing without observing the process exit.
    /// </summary>
    Abandoned
}
