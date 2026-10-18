using System.Net;

using Microsoft.Extensions.Logging;

namespace RobloxModelScanner.Extensions;

/// <summary>
/// Retry behaviour shared by the scanner's outbound requests.
/// </summary>
/// <remarks>
/// <para>
/// Roblox answers a scanner that is working normally with a refusal now and then: the details endpoint
/// throttles per account, and the content delivery network returns a transient gateway error for a
/// signed URL that has only just been issued. Neither is a fault in the scanner, and both succeed on a
/// second attempt moments later, so the requests are retried rather than allowed to surface as a
/// missing asset in the catalogue.
/// </para>
/// <para>
/// A request cannot be resent, because it has already been consumed. The caller therefore supplies a
/// factory that produces a fresh one for each attempt, which is also what makes the retry safe to use
/// with content -- a request carrying a body, or one whose headers were mutated between attempts, would
/// otherwise be sent twice with the second copy half consumed.
/// </para>
/// </remarks>
public static class HttpRequestExtensions
{
    /// <summary>
    /// The statuses that are worth another attempt.
    /// </summary>
    /// <remarks>
    /// A throttle and a server fault are transient by definition. A bad request or a not-found is a
    /// statement about the request itself, and repeating it produces the same answer while spending the
    /// time the scanner could have spent on the next asset.
    /// </remarks>
    private static readonly HttpStatusCode[] Retryable =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    /// <summary>
    /// The longest a throttle is honoured for.
    /// </summary>
    /// <remarks>
    /// A refusal that asks for a longer wait than this is treated as a general back-off rather than as an
    /// instruction. Waiting for an arbitrary length of time would leave the scanner doing nothing at all,
    /// which is indistinguishable from a hang.
    /// </remarks>
    private static readonly TimeSpan MaximumThrottleWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sends a request, retrying transient failures.
    /// </summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="requestFactory">Produces a fresh request for each attempt.</param>
    /// <param name="attempts">The greatest number of attempts to make.</param>
    /// <param name="logger">The logger that records each retry.</param>
    /// <param name="cancellationToken">A token used to abandon the operation.</param>
    /// <returns>The final response, which may be a refusal, or <see langword="null"/> when every attempt failed to connect.</returns>
    /// <remarks>
    /// The returned response belongs to the caller and must be disposed. The last refusal is returned
    /// rather than thrown so that the caller can distinguish a refusal it understands -- a moderated
    /// asset, say -- from a failure it cannot explain, and so that a single unresponsive asset cannot
    /// take down a scan of a hundred thousand of them.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> was cancelled by the caller.</exception>
    public static async Task<HttpResponseMessage?> SendWithRetryAsync(
        this HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        int attempts,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestFactory);
        ArgumentNullException.ThrowIfNull(logger);

        HttpResponseMessage? response = null;

        for (int attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
        {
            // A cancelled token means the scanner is shutting down, not that the network misbehaved, so
            // the cancellation is propagated rather than retried. Retrying it would make a stop request
            // wait for one more round of doomed requests.
            cancellationToken.ThrowIfCancellationRequested();

            response?.Dispose();
            response = null;

            try
            {
                using HttpRequestMessage request = requestFactory();
                response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                if (attempt == Math.Max(1, attempts)) throw;

                TimeSpan wait = Backoff(attempt);
                logger.LogDebug(
                    exception,
                    "Request failed on attempt {Attempt} of {Attempts}; retrying in {Wait} seconds.",
                    attempt,
                    attempts,
                    wait.TotalSeconds);

                await Task.Delay(wait, cancellationToken);
                continue;
            }

            if (!ShouldRetry(response.StatusCode) || attempt == Math.Max(1, attempts)) return response;

            TimeSpan delay = Throttle(response) ?? Backoff(attempt);
            logger.LogDebug(
                "Request answered {Status} on attempt {Attempt} of {Attempts}; retrying in {Wait} seconds.",
                (int)response.StatusCode,
                attempt,
                attempts,
                delay.TotalSeconds);

            await Task.Delay(delay, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Determines whether a status is worth another attempt.
    /// </summary>
    /// <param name="status">The status to test.</param>
    /// <returns><see langword="true"/> when the request may sensibly be repeated.</returns>
    private static bool ShouldRetry(HttpStatusCode status)
    {
        foreach (HttpStatusCode candidate in Retryable)
        {
            if (candidate == status) return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the wait a throttle response asked for.
    /// </summary>
    /// <param name="response">The refusal.</param>
    /// <returns>The requested wait, or <see langword="null"/> when none was stated or it exceeded the cap.</returns>
    private static TimeSpan? Throttle(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return null;

        TimeSpan? requested = response.Headers.RetryAfter?.Delta;
        if (requested is null) return MaximumThrottleWait;
        if (requested.Value <= TimeSpan.Zero) return null;

        return requested.Value > MaximumThrottleWait ? MaximumThrottleWait : requested;
    }

    /// <summary>
    /// Computes the wait before a retry that was not asked for.
    /// </summary>
    /// <param name="attempt">The attempt that failed, counting from one.</param>
    /// <returns>The delay to observe.</returns>
    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(15, Math.Pow(2, Math.Min(attempt, 4) - 1)));
}
