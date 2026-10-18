using RMS.ServiceInstaller.Models;

using RMS.ServiceModel.Models;

namespace RMS.ServiceInstaller.Interfaces;

/// <summary>
/// Owns the registration of one kind of service on one platform.
/// </summary>
/// <remarks>
/// <para>
/// There is one implementation per combination of platform and kind that exists: the Windows service control
/// manager for processes, IIS for web sites, systemd on Linux and launchd on macOS. The installer knows nothing
/// about any of them -- it walks the plan, asks each backend whether it is the right one for a entry, and reports
/// what came back. Adding a mechanism is adding a class, not editing a switch.
/// </para>
/// <para>
/// Every method takes the whole plan set as well as the single service, because a service definition needs the
/// path of the plan file, the path of the supervisor and the log root, and all three live on the set rather than
/// being repeated on each entry.
/// </para>
/// </remarks>
public interface IServiceBackend
{
    /// <summary>
    /// Gets the short name of the mechanism, as the status report spells it.
    /// </summary>
    string Mechanism { get; }

    /// <summary>
    /// Gets a description of the mechanism, for the header of a report.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Tests whether this backend is the one that should own a service.
    /// </summary>
    /// <param name="plan">The service to test.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns><see langword="true"/> when this backend owns services of that shape.</returns>
    bool CanHandle(ServicePlan plan, ServicePlanSet set);

    /// <summary>
    /// Registers the service, replacing any registration already present.
    /// </summary>
    /// <param name="plan">The service to register.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    BackendOutcome Install(ServicePlan plan, ServicePlanSet set);

    /// <summary>
    /// Stops and unregisters the service.
    /// </summary>
    /// <param name="plan">The service to unregister.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// Unregistering something that was never registered is reported as success. The verb means "make sure this is
    /// not running and is not registered", which is true either way, and a teardown script that failed because a
    /// service had already been removed would be a false alarm on every second run.
    /// </remarks>
    BackendOutcome Uninstall(ServicePlan plan, ServicePlanSet set);

    /// <summary>
    /// Starts the service.
    /// </summary>
    /// <param name="plan">The service to start.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    BackendOutcome Start(ServicePlan plan, ServicePlanSet set);

    /// <summary>
    /// Stops the service.
    /// </summary>
    /// <param name="plan">The service to stop.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>What happened.</returns>
    BackendOutcome Stop(ServicePlan plan, ServicePlanSet set);

    /// <summary>
    /// Reports the state of the service.
    /// </summary>
    /// <param name="plan">The service to report on.</param>
    /// <param name="set">The plan the service came from.</param>
    /// <returns>The state, and whether it could be read at all.</returns>
    (BackendOutcome Outcome, ServiceStatusEntry Entry) Status(ServicePlan plan, ServicePlanSet set);
}
