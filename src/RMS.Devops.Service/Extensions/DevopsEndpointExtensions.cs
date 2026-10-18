using RMS.Devops.Service.Filters;
using RMS.Devops.Service.Interfaces;
using RMS.Devops.Service.Models;

namespace RMS.Devops.Service.Extensions;

/// <summary>
/// Maps the DevOps service's HTTP surface.
/// </summary>
/// <remarks>
/// <para>
/// The shape of the surface follows the pages the site renders: one call per panel, not one call per field. A
/// page that had to assemble its own view from a dozen requests would spend a round trip on each, and on a
/// machine that is having a bad day the page would be the last thing to finish loading.
/// </para>
/// <para>
/// Every route that reports anything about the machine is behind the guard, in one group, so that adding a route
/// cannot accidentally add an unguarded one: a route added to the group is guarded by construction, and a route
/// added outside it is visible in the one place where routes are declared.
/// </para>
/// <para>
/// Identifiers are passed through to the archive, which validates them against the layout's own rule before
/// they reach the file system. Nothing in this class builds a path.
/// </para>
/// </remarks>
public static class DevopsEndpointExtensions
{
    /// <summary>
    /// Maps the DevOps service's endpoints.
    /// </summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapDevopsEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Liveness is deliberately outside the guarded group: it reports nothing but the fact that this process
        // is answering, and a supervisor that had to hold a secret to ask would be a supervisor with a secret.
        app.MapGet("/", () => Results.Ok(new { service = "RMS.Devops.Service", status = "ok" }));

        RouteGroupBuilder api = app.MapGroup("/api").AddEndpointFilter<DevopsKeyEndpointFilter>();

        api.MapGet("/fleet", (
            IServiceArchive archive,
            IArtifactInventory artifacts,
            ICatalogStatistics catalog) =>
        {
            // The counts come from the snapshots the service list is built from, so the header and the table on
            // the overview page are the same data seen twice rather than two answers to one question.
            FleetSummary summary = FleetSummary.From(archive.ListServices(0)) with
            {
                PlanFile = archive.PlanFile,
                PlanLoaded = archive.IsAvailable,
                PlanDetail = archive.UnavailableReason ?? string.Empty,
                LogRoot = archive.LogRoot,
                Artifacts = artifacts.List().Count,
                Catalog = catalog.Get(),
            };

            return Results.Ok(summary);
        });

        api.MapGet("/services", (IServiceArchive archive, int recent = 0) =>
            Results.Ok(archive.ListServices(recent)));

        api.MapGet("/services/{name}", (IServiceArchive archive, string name) =>
            archive.FindService(name) is { } service
                ? Results.Ok(service)
                : Results.NotFound());

        api.MapGet("/services/{name}/runs", (IServiceArchive archive, string name, int skip = 0, int take = 0) =>
            Results.Ok(archive.ListRuns(name, skip, take)));

        api.MapGet("/services/{name}/runs/{runId}", (IServiceArchive archive, string name, string runId) =>
            archive.FindRun(name, runId) is { } run
                ? Results.Ok(run)
                : Results.NotFound());

        api.MapGet("/services/{name}/runs/{runId}/log", (
            ILogArchive logs,
            string name,
            string runId,
            long offset = 0,
            int count = 0,
            bool tail = false) =>
            logs.Read(name, runId, offset, count, tail) is { } slice
                ? Results.Ok(slice)
                : Results.NotFound());

        api.MapGet("/runs", (IServiceArchive archive, string? service, int skip = 0, int take = 0) =>
            Results.Ok(archive.ListRuns(service, skip, take)));

        api.MapGet("/artifacts", (IArtifactInventory artifacts) => Results.Ok(new
        {
            root = artifacts.Root,
            artifacts = artifacts.List(),
        }));

        api.MapGet("/catalogue", (ICatalogStatistics catalog) => Results.Ok(catalog.Get()));

        return app;
    }
}
