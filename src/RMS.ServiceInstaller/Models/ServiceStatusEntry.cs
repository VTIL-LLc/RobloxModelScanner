namespace RMS.ServiceInstaller.Models;

/// <summary>
/// The state of one service, as the mechanism that owns it reports it.
/// </summary>
/// <param name="Name">The name the mechanism knows the service by.</param>
/// <param name="Mechanism">The mechanism that owns it, for example <c>scm</c> or <c>systemd</c>.</param>
/// <param name="State">The state, in the vocabulary of the mechanism, lower-cased.</param>
/// <param name="Detail">Anything else the report should carry, such as the executable or the process identifier.</param>
/// <remarks>
/// The state is passed through rather than normalised to a common set. Windows reports
/// <c>running</c> and <c>stopped</c>, systemd reports <c>active</c> and <c>inactive</c>, launchd reports
/// <c>loaded</c> and <c>unloaded</c>, and translating between them would mean inventing an equivalence that is not
/// quite true. Showing the mechanism's own word keeps the report honest and keeps the operator able to act on it
/// with the tool they already have open.
/// </remarks>
public readonly record struct ServiceStatusEntry(string Name, string Mechanism, string State, string Detail)
{
    /// <summary>
    /// Gets a value indicating whether the mechanism reports the service as running.
    /// </summary>
    public bool IsRunning => State is "running" or "active" or "loaded";
}
