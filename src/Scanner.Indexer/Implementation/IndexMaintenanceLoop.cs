using Microsoft.Extensions.Options;

using Scanner.Indexer.Enums;
using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Options;

namespace Scanner.Indexer.Implementation;

/// <summary>
/// Keeps the index in step with the catalogue without anyone having to ask.
/// </summary>
/// <remarks>
/// <para>
/// The loop inspects the catalogue on an interval and migrates it when it has changed. It is a
/// <see cref="BackgroundService"/> rather than a timer callback so that a host shutdown waits for an
/// in-flight migration to finish instead of tearing the process down in the middle of a transaction.
/// </para>
/// <para>
/// A failed pass never stops the loop. The failure is recorded and reported through the status endpoint,
/// and the next tick tries again, which is what lets the service survive the scanner publishing a document
/// while it is being read.
/// </para>
/// </remarks>
public sealed class IndexMaintenanceLoop : BackgroundService
{
    private readonly IIndexWriter _writer;
    private readonly ILogger<IndexMaintenanceLoop> _logger;
    private readonly TimeSpan _interval;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexMaintenanceLoop"/> class.
    /// </summary>
    /// <param name="writer">The index writer that performs the migration.</param>
    /// <param name="options">The bound indexer options, supplying the inspect interval.</param>
    /// <param name="logger">The logger used to report migration outcomes.</param>
    public IndexMaintenanceLoop(
        IIndexWriter writer,
        IOptions<IndexerOptions> options,
        ILogger<IndexMaintenanceLoop> logger)
    {
        _writer = writer;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.RefreshSeconds));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield first so that start-up is not blocked by the first migration. The host finishes starting,
        // the control plane begins answering status, and the migration proceeds in parallel.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IndexBuildOutcome outcome = await _writer
                    .EnsureCurrentAsync(ignoreThrottle: false, stoppingToken)
                    .ConfigureAwait(false);

                // Only the interesting outcomes are logged. A quiet catalogue would otherwise fill the log
                // with one line every few seconds, which is exactly the noise that hides a real failure.
                if (outcome is not IndexBuildOutcome.UpToDate and not IndexBuildOutcome.Busy)
                {
                    _logger.LogInformation("Catalogue migration pass finished with outcome {Outcome}.", outcome);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A background loop that dies on an unexpected exception would leave the index silently
                // frozen, which is worse than the exception itself.
                _logger.LogError(exception, "The catalogue migration pass failed unexpectedly.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
