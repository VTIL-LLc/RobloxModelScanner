namespace RobloxModelScanner.Models;

/// <summary>
/// The persisted instruction the scanner polls between batches.
/// </summary>
/// <remarks>
/// <para>
/// The file is a request rather than a command channel: the administration API writes intent and the
/// scanner reads it when it next comes up for air. An instruction therefore takes effect after the work
/// already in progress, which is why the API describes a stop as graceful rather than immediate.
/// </para>
/// <para>
/// The member names are the shape already on disk and are read case-insensitively, so both writers --
/// the API's and any earlier revision of the scanner -- are understood.
/// </para>
/// </remarks>
/// <param name="Paused">Whether the scanner should wait before starting its next batch.</param>
/// <param name="StopRequested">Whether the scanner should finish what is in flight and exit.</param>
internal sealed record ScannerControlState(bool Paused = false, bool StopRequested = false);
