namespace RMS.ServiceInstaller.Models;

/// <summary>
/// The result of one operation against one service.
/// </summary>
/// <param name="Succeeded">Whether the operation did what was asked.</param>
/// <param name="Message">A sentence describing what happened, suitable for an operator to read.</param>
/// <remarks>
/// A failed operation is returned rather than thrown. Installing eight services and discovering that the sixth
/// cannot be registered is a normal outcome -- a port already in use, a missing runtime -- and the operator needs
/// the report for the five that worked beside the reason for the one that did not, which an exception on the way
/// out would destroy.
/// </remarks>
public readonly record struct BackendOutcome(bool Succeeded, string Message)
{
    /// <summary>
    /// Builds a successful outcome.
    /// </summary>
    /// <param name="message">What was done.</param>
    /// <returns>The outcome.</returns>
    public static BackendOutcome Ok(string message)
    {
        return new BackendOutcome(true, message);
    }

    /// <summary>
    /// Builds a successful outcome that says nothing, for an operation whose success is implied by the next line of
    /// the report.
    /// </summary>
    /// <returns>The outcome.</returns>
    public static BackendOutcome Silent()
    {
        return new BackendOutcome(true, string.Empty);
    }

    /// <summary>
    /// Builds a failed outcome.
    /// </summary>
    /// <param name="message">Why the operation failed.</param>
    /// <returns>The outcome.</returns>
    public static BackendOutcome Fail(string message)
    {
        return new BackendOutcome(false, message);
    }
}
