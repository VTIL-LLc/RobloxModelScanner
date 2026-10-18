using System.Text.Json;

using Scanner.MicroApi.Enums;
using Scanner.MicroApi.Exceptions;

namespace Scanner.MicroApi.Implementation;

/// <summary>
/// Answers a request that needed the index with a service-unavailable response naming the reason.
/// </summary>
/// <remarks>
/// <para>
/// The index is produced by a different service, so an operator can legitimately ask this API for
/// catalogue data before that service has ever run, or while its index is being replaced. Nothing about
/// those situations is a fault in this process: the database simply is not readable yet. Left alone the
/// exception would leave the framework's generic server error on the wire, which says nothing an operator
/// can act on and reads like a bug in the API.
/// </para>
/// <para>
/// The translation sits in middleware rather than in each endpoint because it is a property of every
/// endpoint that reads the index, and a rule expressed once cannot be forgotten by an endpoint added
/// later. Only this exception is translated: anything else is left to unwind, so a genuine defect is
/// still a defect and still gets logged as one.
/// </para>
/// </remarks>
/// <seealso cref="IndexUnavailableException"/>
public sealed class IndexUnavailableMiddleware
{
    /// <summary>
    /// The media type of the body written for a refused request.
    /// </summary>
    private const string ContentType = "application/json; charset=utf-8";

    private readonly RequestDelegate _next;
    private readonly ILogger<IndexUnavailableMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexUnavailableMiddleware"/> class.
    /// </summary>
    /// <param name="next">The rest of the request pipeline.</param>
    /// <param name="logger">The logger used to record the refusal once.</param>
    public IndexUnavailableMiddleware(RequestDelegate next, ILogger<IndexUnavailableMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Runs the request, translating an unreadable index into a refusal.
    /// </summary>
    /// <param name="context">The request and its response.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (IndexUnavailableException exception)
        {
            // A response that has already begun cannot be replaced with a different status, so the
            // exception is rethrown and the framework deals with it. This happens only when the failure
            // is discovered after a body has been written, which the index-reading endpoints never do.
            if (context.Response.HasStarted) throw;

            // Logged at warning rather than error: a deployment whose indexer has not run yet is a state,
            // not a crash, and an error-level entry per request would bury the entries that matter.
            _logger.LogWarning(
                "Refused {Method} {Path}: the index is {Availability}.",
                context.Request.Method,
                context.Request.Path.Value,
                exception.Availability);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = ContentType;

            // The availability is published as a stable word so a caller can act on it -- "missing" means
            // start the indexer, "incompatible" means rebuild the index -- while the message is written
            // for the operator who has to do that.
            string body = JsonSerializer.Serialize(new
            {
                error = Availability(exception.Availability),
                availability = exception.Availability.ToString(),
                message = exception.Message
            });

            await context.Response.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Produces the machine-readable reason for a refusal.
    /// </summary>
    /// <param name="availability">The reason the index could not be read.</param>
    /// <returns>A short, stable token.</returns>
    private static string Availability(IndexAvailability availability) => availability switch
    {
        IndexAvailability.Missing => "index_missing",
        IndexAvailability.Incompatible => "index_incompatible",
        _ => "index_unreadable"
    };
}
