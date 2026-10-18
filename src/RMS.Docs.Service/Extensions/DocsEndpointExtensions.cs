using RMS.Docs.Service.Filters;
using RMS.Docs.Service.Interfaces;
using RMS.Docs.Service.Models;

namespace RMS.Docs.Service.Extensions;

/// <summary>
/// Maps the documentation service's HTTP surface.
/// </summary>
/// <remarks>
/// <para>
/// Three routes, because there are three questions: what is there, give me one, and find this phrase. The site
/// asks the first when it renders a navigation, the second when somebody opens a page, and the third when
/// somebody uses the search box.
/// </para>
/// <para>
/// Nothing here builds a path. A caller names a slug, and the slug only ever selects from the list the catalogue
/// built by walking the directory itself. A request has no text that reaches a file system call.
/// </para>
/// <para>
/// Every route that reports anything is inside one guarded group, so adding a route cannot add an unguarded one
/// by accident. Liveness is outside it, because it reports nothing but the fact that this process is answering
/// and a supervisor that had to hold a secret to ask would be a supervisor with a secret.
/// </para>
/// </remarks>
public static class DocsEndpointExtensions
{
    /// <summary>
    /// The number of search results returned when the caller does not say.
    /// </summary>
    private const int DefaultSearchLimit = 25;

    /// <summary>
    /// Maps the documentation service's endpoints.
    /// </summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapDocsEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/", () => Results.Ok(new { service = "RMS.Docs.Service", status = "ok" }));

        RouteGroupBuilder api = app.MapGroup("/api").AddEndpointFilter<DocsKeyEndpointFilter>();

        api.MapGet("/documents", (IDocumentCatalog catalog) => Results.Ok(catalog.List()));

        /*
         * The catch-all is required rather than merely convenient: a slug names a path below the documentation
         * directory, so a document filed in a section has a slug such as "guides/deployer". A single segment route
         * would answer every nested document as absent while the catalogue listed it, which is a contradiction an
         * operator would spend a long time chasing.
         */
        api.MapGet("/documents/{**slug}", (IDocumentCatalog catalog, string slug) =>
        {
            DocumentView? document = catalog.Read(slug);

            return document is null
                ? Results.NotFound(new { error = "not_found", slug })
                : Results.Ok(document);
        });

        api.MapGet("/search", (IDocumentCatalog catalog, string? q, int? limit) =>
            Results.Ok(catalog.Search(q ?? string.Empty, limit ?? DefaultSearchLimit)));

        return app;
    }
}
