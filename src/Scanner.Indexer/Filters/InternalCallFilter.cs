using Scanner.Indexer.Implementation;
using Scanner.Indexer.Interfaces;

namespace Scanner.Indexer.Filters;

/// <summary>
/// Applies the internal call guard to every control-plane endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The guard runs as an endpoint filter rather than as middleware so that it cannot accidentally be
/// bypassed by a route registered later, and so that the refusal happens before model binding or any
/// handler code executes.
/// </para>
/// <para>
/// Two different refusals are produced on purpose. When the control plane is unconfigured it answers
/// <c>404</c>, which makes the surface look absent rather than advertising a misconfigured service to
/// whoever probes it. When the control plane is configured but the caller is not acceptable it answers
/// <c>403</c> with a generic message that names no detail of what was expected.
/// </para>
/// </remarks>
public sealed class InternalCallFilter : IEndpointFilter
{
    private readonly IInternalCallGuard _guard;

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalCallFilter"/> class.
    /// </summary>
    /// <param name="guard">The guard that evaluates the caller.</param>
    public InternalCallFilter(IInternalCallGuard guard) => _guard = guard;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;

        // Status and rebuild outcomes are operational state. They must never be cached by an intermediary,
        // and the answer must not be reused after a rebuild.
        http.Response.Headers.CacheControl = "no-store";

        if (!_guard.IsEnabled)
        {
            return Results.NotFound();
        }

        string? suppliedToken = http.Request.Headers[InternalCallGuard.TokenHeaderName];

        if (!_guard.IsPermitted(http.Connection.RemoteIpAddress, suppliedToken, out _))
        {
            return Results.Json(
                new { error = "forbidden", message = "This endpoint is not available to the caller." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context).ConfigureAwait(false);
    }
}
