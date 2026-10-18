using Scanner.MicroApi.Interfaces;

namespace Scanner.MicroApi.Filters;

/// <summary>
/// Rejects requests that do not carry the configured API key.
/// </summary>
/// <remarks>
/// <para>
/// The key travels in the <c>apiKey</c> query parameter because that is the contract the existing
/// website and tooling depend on; changing it would break every caller. A query parameter is visible to
/// intermediaries and to access logs, so the deployment is expected to terminate TLS and to keep the
/// parameter out of request logging. <see cref="Implementation.ApiKeyValidator"/> compares values in a
/// way that does not leak the length of a correct prefix.
/// </para>
/// <para>
/// The filter is attached to the whole API route group, so every endpoint -- including the health
/// probe -- requires a valid key. A deployment that starts without one therefore rejects traffic
/// instead of silently serving the catalogue.
/// </para>
/// </remarks>
public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    /// <summary>
    /// The name of the query parameter carrying the key.
    /// </summary>
    private const string ApiKeyParameterName = "apiKey";

    private readonly IApiKeyValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyEndpointFilter"/> class.
    /// </summary>
    /// <param name="validator">The validator that compares the supplied key with the configured one.</param>
    public ApiKeyEndpointFilter(IApiKeyValidator validator) => _validator = validator;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string? supplied = context.HttpContext.Request.Query[ApiKeyParameterName];

        if (!_validator.IsAuthorized(supplied))
        {
            // The rejection is never cached: a client that supplies the correct key immediately afterwards
            // must not have a cached failure replayed by an intermediary.
            context.HttpContext.Response.Headers.CacheControl = "no-store";

            return Results.Json(
                new
                {
                    error = "unauthorized",
                    message = "A valid apiKey query parameter is required."
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context).ConfigureAwait(false);
    }
}
