namespace RMS.ServiceModel.Enums;

/// <summary>
/// How a supervisor should react when the process it is watching exits.
/// </summary>
/// <remarks>
/// The three values are exactly the ones the service manifests can spell. A policy the manifests cannot
/// express is not modelled here, because a value the deployer can never write is a value no deployment would
/// ever be tested against.
/// </remarks>
public enum RestartPolicy
{
    /// <summary>Never start the process again once it has exited for any reason.</summary>
    Never,

    /// <summary>Start the process again only when it exited with a non-zero status.</summary>
    OnFailure,

    /// <summary>Start the process again whatever its exit status was. The default for every service.</summary>
    Always,
}
