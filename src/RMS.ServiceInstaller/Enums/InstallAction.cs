namespace RMS.ServiceInstaller.Enums;

/// <summary>
/// The verbs the installer accepts.
/// </summary>
/// <remarks>
/// The set is deliberately the same on all three platforms, because the scripts and the deployer that drive this
/// program are written once. A verb that only makes sense on one platform is still accepted everywhere and reports
/// what it did, so an operator does not need to know which mechanism is in charge before typing a command.
/// </remarks>
public enum InstallAction
{
    /// <summary>Register the services, stopping and removing any earlier registration first.</summary>
    Install,

    /// <summary>Stop and unregister the services.</summary>
    Uninstall,

    /// <summary>Start the services that are registered and not already running.</summary>
    Start,

    /// <summary>Stop the services that are registered and running.</summary>
    Stop,

    /// <summary>Stop and start the services, in one pass.</summary>
    Restart,

    /// <summary>Report the state of every service in the plan without changing anything.</summary>
    Status,
}
