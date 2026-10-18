namespace RMS.ServiceModel.Enums;

/// <summary>
/// The platforms the deployer and the services distinguish between.
/// </summary>
/// <remarks>
/// These are the three platforms the stack runs on, not the platforms .NET runs on. A value here names which
/// service manager will be asked to own a process, which is the only platform fact the plans depend on.
/// </remarks>
public enum PlanPlatform
{
    /// <summary>Windows. The service control manager registers the services.</summary>
    Windows,

    /// <summary>Linux. systemd registers the services.</summary>
    Linux,

    /// <summary>macOS. launchd registers the services.</summary>
    MacOs,
}
