using Microsoft.Extensions.Logging;

using RobloxModelScanner.Interfaces;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// Reads the operator's pause and stop request from the control file.
/// </summary>
/// <remarks>
/// <para>
/// The control file is small and is read at most once per batch, so it is read directly rather than
/// cached and watched. Watching it would mean holding a lock on a file the API is writing, or polling it
/// on a timer and being wrong about the interval; reading it when the answer is needed is both simpler
/// and always current.
/// </para>
/// <para>
/// A pause is reported once when it begins and once when it ends, rather than on every poll. The wait is
/// polled every couple of seconds, and a line per poll would turn an operator's lunch break into
/// thousands of identical log entries.
/// </para>
/// </remarks>
/// <seealso cref="IScanControl"/>
public sealed class FileScanControl : IScanControl
{
    /// <summary>
    /// How often the control file is examined while a pause is in force.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The control file's path.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// The logger that records a pause beginning and ending.
    /// </summary>
    private readonly ILogger<FileScanControl> _logger;

    /// <summary>
    /// Whether the current pause has already been reported.
    /// </summary>
    private bool _paused;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileScanControl"/> class.
    /// </summary>
    /// <param name="path">The control file's path.</param>
    /// <param name="logger">The logger that records a pause beginning and ending.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public FileScanControl(string path, ILogger<FileScanControl> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(logger);

        _path = Path.GetFullPath(path);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool StopRequested => ScannerControlFile.Read(_path).StopRequested;

    /// <inheritdoc />
    public async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (ScannerControlFile.Read(_path).Paused)
        {
            if (!_paused)
            {
                _paused = true;
                _logger.LogInformation("Paused at {Path}. Work in flight will finish.", _path);
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        if (!_paused) return;

        _paused = false;
        _logger.LogInformation("Resumed.");
    }
}
