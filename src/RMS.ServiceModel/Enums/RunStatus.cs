namespace RMS.ServiceModel.Enums;

/// <summary>
/// The life cycle state of one supervised run of a service.
/// </summary>
/// <remarks>
/// The names are the words the DevOps site displays on a service page, so a status is never translated twice.
/// <see cref="Running"/> is the only state a process is ever in while the supervisor holds it; the rest are
/// terminal.
/// </remarks>
public enum RunStatus
{
    /// <summary>The process is running and the supervisor is watching it.</summary>
    Running,

    /// <summary>The process exited with a success status and was not restarted.</summary>
    Succeeded,

    /// <summary>The process exited with a failure status and was not restarted.</summary>
    Failed,

    /// <summary>The process was stopped because the service, the deployment or the machine was shutting down.</summary>
    Stopped,

    /// <summary>
    /// The supervisor itself stopped without observing the process exit, so the child's fate is unknown.
    /// </summary>
    /// <remarks>
    /// This is recorded rather than guessed at. A supervisor that is killed outright -- by losing power, by an
    /// operator with a termination tool -- leaves a record claiming <see cref="Running"/>, and reporting that
    /// as a success would be a lie the next reader could not detect. It is also the value an unrecognised
    /// status keyword parses to, so a record written by a newer version is never read as healthy.
    /// </remarks>
    Abandoned,
}
