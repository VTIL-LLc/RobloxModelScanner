using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Interfaces;

/// <summary>
/// Reads and writes the scanner's control file.
/// </summary>
/// <remarks>
/// Writes replace the control file atomically so the scanner can never observe a partially written
/// document. The service records intent only; it cannot force a scanner that is blocked in a
/// network request to react immediately.
/// </remarks>
public interface IScannerControlService
{
    /// <summary>
    /// Reads the last persisted control state.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// The persisted state, or the default state when no control file exists or the file is
    /// unreadable. A missing file is treated as "running normally" rather than as an error.
    /// </returns>
    Task<ControlState> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists a new control state for the scanner to pick up.
    /// </summary>
    /// <param name="state">The state to write.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The state that was written.</returns>
    Task<ControlState> WriteAsync(ControlState state, CancellationToken cancellationToken);
}
