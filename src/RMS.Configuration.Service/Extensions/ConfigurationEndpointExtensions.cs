using RMS.Configuration.Service.Enums;
using RMS.Configuration.Service.Filters;
using RMS.Configuration.Service.Implementation;
using RMS.Configuration.Service.Interfaces;
using RMS.Configuration.Service.Models;

namespace RMS.Configuration.Service.Extensions;

/// <summary>
/// Maps the Configuration service's HTTP surface.
/// </summary>
/// <remarks>
/// <para>
/// The shape follows the pages the site renders: the overview asks for all four files at once, a page asks for the
/// one it is showing, and the editor asks for the vocabulary so that it can offer the keys while an operator is
/// typing. One call per panel, not one call per field.
/// </para>
/// <para>
/// Nothing here builds a path. A caller names one of four known manifests, and the catalog turns that name into a
/// configured directory plus a fixed file name, so a request has no text that could reach a file system.
/// </para>
/// <para>
/// Every route that reports or changes anything is inside one guarded group, so adding a route cannot add an
/// unguarded one by accident. Liveness is outside it, because it reports nothing but the fact that this process is
/// answering and a supervisor that had to hold a secret to ask would be a supervisor with a secret.
/// </para>
/// </remarks>
public static class ConfigurationEndpointExtensions
{
    /// <summary>
    /// Maps the Configuration service's endpoints.
    /// </summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapConfigurationEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/", () => Results.Ok(new { service = "RMS.Configuration.Service", status = "ok" }));

        RouteGroupBuilder api = app.MapGroup("/api").AddEndpointFilter<ConfigurationKeyEndpointFilter>();

        api.MapGet("/manifests", (IManifestCatalog catalog) => Results.Ok(catalog.List()));

        api.MapGet("/manifests/{name}", (IManifestCatalog catalog, string name) =>
        {
            if (!catalog.TryResolve(name, out ManifestKind kind)) return Results.NotFound();

            return Results.Ok(catalog.Read(kind));
        });

        api.MapPost("/manifests/{name}/validate", (
            IManifestCatalog catalog,
            ManifestWriter writer,
            string name,
            ManifestWriteRequest request) =>
        {
            if (!catalog.TryResolve(name, out ManifestKind kind)) return Results.NotFound();

            return Attempt(() => writer.Check(kind, request));
        });

        api.MapPost("/manifests/{name}", (
            IManifestCatalog catalog,
            ManifestWriter writer,
            string name,
            ManifestWriteRequest request) =>
        {
            if (!catalog.TryResolve(name, out ManifestKind kind)) return Results.NotFound();

            // The hash the caller sends is the precondition: it is what makes two operators editing one file a
            // refusal rather than a silent last-writer-wins.
            return Attempt(() => writer.Write(kind, request));
        });

        api.MapGet("/plan", (IPlanReader plan) => Results.Ok(plan.Read()));

        api.MapGet("/vocabulary", () => Results.Ok(DeployerVocabulary.Describe()));

        return app;
    }

    /// <summary>
    /// Runs a request that may refuse it, and translates the refusal.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <returns>The result, or a refusal.</returns>
    /// <remarks>
    /// A manifest that fails validation is a success carrying the problems, because there is a useful thing to
    /// render. Only a lost precondition and a request that cannot be satisfied at all become error statuses, and
    /// both of those come from <see cref="ConfigurationKeyEndpointFilter.Describe"/> so that the pages and the
    /// handlers say the same thing about the same failure.
    /// </remarks>
    private static IResult Attempt(Func<ManifestWriteResult> action)
    {
        try
        {
            return Results.Ok(action());
        }
        catch (Exception exception)
        {
            IResult? refusal = ConfigurationKeyEndpointFilter.Describe(exception);

            if (refusal is null) throw;

            return refusal;
        }
    }
}
