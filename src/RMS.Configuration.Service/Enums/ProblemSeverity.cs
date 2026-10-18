namespace RMS.Configuration.Service.Enums;

/// <summary>
/// How badly a problem with a manifest matters.
/// </summary>
/// <remarks>
/// <para>
/// The three levels map onto three different things an operator can do, and the mapping is the reason the levels
/// exist rather than a single "problem" flag.
/// </para>
/// <list type="bullet">
/// <item><description>
/// An <see cref="Error"/> is something the deployer would refuse to read, or would read as something other than
/// what the line says. It stops a write, because a manifest that is saved broken is a deployment that fails
/// later, on a different machine, in front of whoever happens to be deploying.
/// </description></item>
/// <item><description>
/// A <see cref="Warning"/> is something the deployer accepts and that has a consequence worth knowing about:
/// a step whose project does not exist yet, a service with nothing to run, a requirement with no way to be
/// installed. It does not stop a write, because every one of these describes the state of this repository at
/// some point in its life, and refusing to save them would mean refusing to describe work in progress.
/// </description></item>
/// <item><description>
/// A <see cref="Note"/> is context: what the value resolves to, which platform it applies to, what the default
/// is. It is shown and never counted against anything.
/// </description></item>
/// </list>
/// </remarks>
public enum ProblemSeverity
{
    /// <summary>
    /// Context that is worth showing and that nothing acts on.
    /// </summary>
    Note,

    /// <summary>
    /// Something the deployer accepts that has a consequence worth knowing about.
    /// </summary>
    Warning,

    /// <summary>
    /// Something the deployer would refuse, or would silently read as something else.
    /// </summary>
    Error,
}
