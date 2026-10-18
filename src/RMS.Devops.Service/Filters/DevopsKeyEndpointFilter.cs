using Microsoft.Extensions.Logging;

using RMS.Devops.Service.Implementation;
using RMS.Devops.Service.Interfaces;

namespace RMS.Devops.Service.Filters;

/// <summary>
/// Refuses a DevOps call before its handler runs.
/// </summary>
/// <remarks>
/// <para>
/// The decision belongs to <see cref="IDevopsKeyGuard"/> and this filter's only job is to apply it and translate
/// the outcome into a response. The refusal bodies are deliberately terse: a caller that has not been admitted
/// is told nothing about the machine, not even which of the checks refused it. The reason goes to the log,
/// where an operator who has already been admitted can read it.
/// </para>
/// <para>
/// A surface with no usable configuration answers <c>404</c> rather than <c>403</c>. A forbidden response
/// confirms that there was something here to be forbidden, and the point of disabling the surface is that there
/// is not.
/// </para>
/// <para>
/// The peer address is taken from the connection rather than from a forwarding header. A header is written by
/// the caller, so an allow-list that trusted one would be satisfied by anyone who could type the right string --
/// which is exactly the mistake this filter exists to avoid.
/// </para>
/// </remarks>
public sealed class DevopsKeyEndpointFilter : IEndpointFilter
{
    private readonly IDevopsKeyGuard _guard;
    private readonly ILogger<DevopsKeyEndpointFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DevopsKeyEndpointFilter"/> class.
    /// </summary>
    /// <param name="guard">The guard that decides whether a call may proceed.</param>
    /// <param name="logger">The logger used to record refusals.</param>
    public DevopsKeyEndpointFilter(IDevopsKeyGuard guard, ILogger<DevopsKeyEndpointFilter> logger)
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
                "Refused a DevOps call because the surface is disabled: {Reason}",
                _guard.DisabledReason);

            return Results.NotFound();
        }

        string? supplied = http.Request.Headers[DevopsKeyGuard.KeyHeaderName].ToString();

        if (!_guard.IsPermitted(http.Connection.RemoteIpAddress, supplied, out string? rejection))
        {
            _logger.LogWarning(
                "Refused a DevOps call to {Path}: {Reason}",
                http.Request.Path,
                rejection);

            return Results.Json(
                new { error = "Forbidden." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
