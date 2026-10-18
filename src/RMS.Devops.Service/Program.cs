/*
 * RMS — DEVOPS SERVICE HOST
 * ============================================================================
 * PURPOSE
 *   Answers what is running on this machine and what those programs have been
 *   saying. It reads two things and nothing else:
 *
 *     1. The service plan the deployment wrote, which says what is meant to be
 *        running, where its files are and where its logs go.
 *     2. The run store the supervisor writes, which says what actually happened.
 *
 *   It owns no state. Every answer is a walk of a directory and a parse of a
 *   handful of small files, which is why there is no database here and why the
 *   service can be restarted at any moment without losing anything: there is
 *   nothing to lose.
 *
 * EXPOSURE
 *   The service describes the layout of a host -- the absolute path of every
 *   program, every service and every log, and the contents of those logs -- so
 *   the surface is treated as internal:
 *
 *     - It listens on a configured address, loopback by default. A wildcard
 *       bind is refused unless the operator has explicitly opted in.
 *     - The peer address must fall inside a configured allow-list, evaluated
 *       against the socket and never against a forwarding header.
 *     - A shared key must be presented, compared in fixed time. A key shorter
 *       than the minimum disables the surface rather than being accepted.
 *     - Every route that reports anything is inside one guarded group, so a
 *       route cannot be added to it unguarded by accident.
 *
 *   The site in front of it holds the key on the server side and calls this
 *   service from the same origin, so the browser never sees a secret.
 *
 * NO PLAN, NO CRASH
 *   A machine that has been provisioned but not yet deployed has no plan. That
 *   is reported in a sentence rather than as a failure, because "the deployment
 *   is not here" and "this is the wrong host" are different answers and an
 *   operator needs to know which one they are looking at.
 */

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using RMS.Devops.Service.Extensions;
using RMS.Devops.Service.Interfaces;
using RMS.Devops.Service.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

DevopsOptions settings = builder.Configuration
    .GetSection(DevopsOptions.SectionName)
    .Get<DevopsOptions>() ?? new DevopsOptions();

IPAddress bindAddress = ResolveBindAddress(
    Environment.GetEnvironmentVariable(DevopsOptions.BindAddressVariable) ?? settings.BindAddress);

bool allowWildcard = IsTruthy(Environment.GetEnvironmentVariable(DevopsOptions.AllowWildcardVariable));

string? hostingUrls = builder.Configuration["urls"]
                      ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

/*
 * A hosting URL naming every interface is refused before the socket is opened.
 *
 * The check is here rather than left to the guard because the difference is
 * whether the port is open at all. A port that answers with a refusal is still a
 * port that answers, and the machine this service describes is one an operator
 * would rather not advertise.
 */
if (hostingUrls is not null && ContainsWildcardHost(hostingUrls) && !allowWildcard)
{
    throw new InvalidOperationException(
        $"The hosting URL '{hostingUrls}' binds every interface, which this service refuses. Set " +
        $"{DevopsOptions.AllowWildcardVariable}=true if that is intended, or set {DevopsOptions.BindAddressVariable} " +
        "to the address the site reaches this service on.");
}

/* Read the environment for the port so that the manual and supervised runs agree. */
int port = int.TryParse(Environment.GetEnvironmentVariable(DevopsOptions.PortVariable), out int configuredPort)
    ? configuredPort
    : settings.Port;

builder.WebHost.ConfigureKestrel(options =>
{
    /* The server header names the framework and its version. There is no reason to volunteer either. */
    options.AddServerHeader = false;

    /*
     * The surface has no request bodies beyond a few query parameters. Refusing
     * large ones removes a class of memory-pressure request before a handler
     * sees it, which matters more here than on a general web application
     * because this one is expected to be reachable only by the site.
     */
    options.Limits.MaxRequestBodySize = 4 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);

    /*
     * When the hosting environment already named a URL, that URL decides the
     * address and this only adds the protocol constraint. When it did not, the
     * listener is built here from the configured address and port, so a
     * hand-started run lands where the supervised run would have.
     */
    if (string.IsNullOrWhiteSpace(hostingUrls))
    {
        options.Listen(
            bindAddress,
            port,
            listen => listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
    }
});

builder.Services.AddDevopsServices(builder.Configuration);

/* Enum values are published by name so a run state is readable rather than a number. */
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

WebApplication app = builder.Build();

app.MapDevopsEndpoints();

/*
 * Report the guard's state at start-up. A disabled surface is a deployment
 * mistake that would otherwise appear only as an unexplained 404 on every page,
 * and the reason is safe to log because it never contains the key.
 */
IDevopsKeyGuard guard = app.Services.GetRequiredService<IDevopsKeyGuard>();

if (!guard.IsEnabled)
{
    app.Logger.LogWarning(
        "The DevOps surface is disabled: {Reason} The service still starts, so the site can explain why, but " +
        "every call it makes will be refused.",
        guard.DisabledReason);
}
else
{
    // The effective options are resolved here rather than read from the raw configuration, so the line
    // reports what the service will actually use after the environment has been applied. A start-up banner
    // that names the path from a template rather than the path in force is worse than no banner.
    DevopsOptions effective = app.Services.GetRequiredService<IOptions<DevopsOptions>>().Value;

    app.Logger.LogInformation(
        "The DevOps surface is reachable from the configured networks. Plan file {PlanFile}; catalogue index {Index}.",
        effective.PlanFile.Length == 0 ? "(unset)" : effective.PlanFile,
        effective.CatalogIndexPath.Length == 0 ? "(unset)" : effective.CatalogIndexPath);
}

app.Run();

/*
 * Resolve the address the listener is built from.
 *
 * A wildcard puts this service on every network the host is attached to,
 * including any that were never meant to see it. The key and the allow-list
 * still apply, but a defence that depends on two other defences is not a
 * defence, so a wildcard is turned into loopback and the operator is told which
 * variable unlocks the behaviour if that is truly what they want.
 */
static IPAddress ResolveBindAddress(string configured)
{
    string value = configured.Trim();

    if (value.Length == 0) return IPAddress.Loopback;

    if (value is "0.0.0.0" or "::" or "*" or "+")
    {
        bool optedIn = IsTruthy(Environment.GetEnvironmentVariable(DevopsOptions.AllowWildcardVariable));
        if (!optedIn) return IPAddress.Loopback;
    }

    if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;

    return IPAddress.TryParse(value, out IPAddress? address) ? address : IPAddress.Loopback;
}

/*
 * Determine whether a hosting URL list names every interface.
 *
 * The test is made against the text rather than a parsed URI because the
 * wildcard spellings a hosting URL permits are not all valid URI hosts, and a
 * check that fails to parse is a check that silently passes.
 */
static bool ContainsWildcardHost(string urls)
{
    foreach (string entry in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        int authority = entry.IndexOf("//", StringComparison.Ordinal);
        string host = authority >= 0 ? entry[(authority + 2)..] : entry;

        int portSeparator = host.IndexOfAny([':', '/']);
        if (portSeparator >= 0) host = host[..portSeparator];

        if (host is "*" or "+" or "0.0.0.0" or "[::]" or "::") return true;
    }

    return false;
}

/*
 * Interpret an environment variable as an explicit opt-in. Only a positive
 * spelling counts: an unset or unrecognised value must leave the safe default in
 * place, because an operator who typed something unexpected did not consent.
 */
static bool IsTruthy(string? value) =>
    value is not null &&
    (value.Equals("1", StringComparison.Ordinal) ||
     value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
     value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
     value.Equals("on", StringComparison.OrdinalIgnoreCase));
