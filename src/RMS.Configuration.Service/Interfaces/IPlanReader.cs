using RMS.Configuration.Service.Models;

namespace RMS.Configuration.Service.Interfaces;

/// <summary>
/// Reads the plan the deployer generated.
/// </summary>
/// <remarks>
/// The plan is the console's answer to "what did my edit actually become", and it is read with the same reader
/// the service installer uses. A second implementation that agreed with the installer most of the time would be
/// worse than not showing the plan at all, because an operator would have no way to tell which of two plausible
/// answers was the true one.
/// </remarks>
public interface IPlanReader
{
    /// <summary>
    /// Reads the plan file and reports what it declares.
    /// </summary>
    /// <returns>
    /// The plan, or a view that says in a sentence why it is not available. The method does not throw for an
    /// absent or unreadable plan: a machine provisioned but not yet deployed has no plan, and that is a state the
    /// page exists to describe rather than to fail on.
    /// </returns>
    PlanView Read();
}
