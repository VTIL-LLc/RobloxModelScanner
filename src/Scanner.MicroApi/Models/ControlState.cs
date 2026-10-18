namespace Scanner.MicroApi.Models;

/// <summary>
/// The persisted scanner control contract shared by the API and the scanner process.
/// </summary>
/// <remarks>
/// The file is a request, not a command channel. The API writes intent; the scanner reads it
/// between batches and performs the action. A scanner that is mid-request will therefore finish
/// that request before a pause or stop takes effect, which is why the API documents the stop
/// command as a graceful request rather than a guaranteed immediate termination.
/// </remarks>
/// <param name="Paused">Whether scanning should wait before starting the next batch.</param>
/// <param name="StopRequested">Whether the scanner should terminate once in-flight work completes.</param>
public sealed record ControlState(bool Paused = false, bool StopRequested = false);
