using Microsoft.Extensions.Logging;

using RMS.Configuration.Service.Exceptions;
using RMS.Configuration.Service.Implementation;
using RMS.Configuration.Service.Interfaces;

namespace RMS.Configuration.Service.Filters;

/// <summary>
/// Refuses a Configuration call before its handler runs.
/// </summary>
/// <remarks>
/// <para>
/// The decision belongs to <see cref="IConfigurationKeyGuard"/> and this filter's only job is to apply it and
/// translate the outcome into a response. The refusal bodies are deliberately terse: a caller that has not been
/// admitted is told nothing about the machine, not even which of the checks refused it. The reason goes to the
/// log, where an operator who has already been admitted can read it.
/// </para>
/// <para>
/// A surface with no usable configuration answers <c>404</c> rather than <c>403</c>. A forbidden response
/// confirms that there was something here to be forbidden, and the point of disabling the surface is that there
/// is not.
/// </para>
/// <para>
/// The peer address is taken from the connection rather than from a forwarding header. A header is written by the
/// caller, so an allow-list that trusted one would be satisfied by anyone who could type the right string --
/// which is exactly the mistake this filter exists to avoid.
/// </para>
/// </remarks>
public sealed class ConfigurationKeyEndpointFilter : IEndpointFilter
{
    private readonly IConfigurationKeyGuard _guard;
    private readonly ILogger<ConfigurationKeyEndpointFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationKeyEndpointFilter"/> class.
    /// </summary>
    /// <param name="guard">The guard that decides whether a call may proceed.</param>
    /// <param name="logger">The logger used to record refusals.</param>
    public ConfigurationKeyEndpointFilter(
        IConfigurationKeyGuard guard,
        ILogger<ConfigurationKeyEndpointFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(guard);

        _guard = guard;
        _logger = logger;
    }

    /// <summary>
    /// Applies the guard to a call.
    /// </summary>
    /// <param name="context">The invocation context.</param>
    /// <param name="next">The next filter or handler in the pipeline.</param>
    /// <returns>The handler's result, or a refusal.</returns>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);

        HttpContext http = context.HttpContext;

        if (!_guard.IsEnabled)
        {
            _logger.LogWarning(
                "Refused a Configuration call because the surface is disabled: {Reason}",
                _guard.DisabledReason);

            return Results.NotFound();
        }

        string? supplied = http.Request.Headers[ConfigurationKeyGuard.KeyHeaderName].ToString();

        if (!_guard.IsPermitted(http.Connection.RemoteIpAddress, supplied, out string? rejection))
        {
            _logger.LogWarning(
                "Refused a Configuration call to {Path}: {Reason}",
                http.Request.Path,
                rejection);

            return Results.Json(
                new { error = "Forbidden." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    /// <summary>
    /// Translates a failed precondition or an unwritable manifest into a response.
    /// </summary>
    /// <param name="exception">The exception a handler threw.</param>
    /// <returns>
    /// A <c>409</c> naming the file's current hash for a precondition failure, a <c>400</c> for a request that
    /// cannot be satisfied at all, or <see langword="null"/> when the exception is not one of those.
    /// </returns>
    /// <remarks>
    /// This is the only place the surface answers an error status for something an operator did rather than
    /// something the guard refused. A write that fails validation is a <c>200</c> carrying the problems, because
    /// there is a useful thing to render; a write that lost a race has nothing to render, only a hash to send
    /// back.
    /// </remarks>
    internal static IResult? Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ManifestPreconditionException conflict => Results.Json(
                new { error = conflict.Message, sha256 = conflict.CurrentSha256 },
                statusCode: StatusCodes.Status409Conflict),

            InvalidOperationException refusal => Results.Json(
                new { error = refusal.Message },
                statusCode: StatusCodes.Status400BadRequest),

            _ => null,
        };
    }
}
