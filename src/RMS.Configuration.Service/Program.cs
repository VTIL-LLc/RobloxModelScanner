/*
 * RMS — CONFIGURATION SERVICE HOST
 * ============================================================================
 * PURPOSE
 *   A console over the deployment's own description. It reads and writes the
 *   three manifests the deployer is driven by -- requirements, build and
 *   services -- and reports the plan they generate.
 *
 *   It owns nothing. The manifests are the only description of the deployment
 *   that exists, and this service deliberately keeps no second copy of any part
 *   of them: a console with its own settings would be a console that could
 *   disagree with the thing it is describing, and an operator would have no way
 *   to tell which of the two was right.
 *
 * WHY IT IS SEPARATE FROM THE SITE
 *   The site is a .NET Framework application served by IIS. Reading these files
 *   and checking them against the parser is not a page's job, and the site is
 *   the only thing on the network. So the console lives here, behind a loopback
 *   listener and a shared key, and the site reaches it from the same origin.
 *
 * EXPOSURE
 *   This surface can write the files that decide what a machine runs. It is
 *   therefore treated as the most sensitive surface in the stack:
 *
 *     - It listens on a configured address, loopback by default. A wildcard
 *       bind is refused unless the operator has explicitly opted in.
 *     - The peer address must fall inside a configured allow-list, evaluated
 *       against the socket and never against a forwarding header.
 *     - A shared key must be presented in the X-Configuration-Key header,
 *       compared in fixed time. A key shorter than the minimum disables the
 *       surface rather than being accepted.
 *     - Every route that reports or changes anything is inside one guarded
 *       group, so a route cannot be added to it unguarded by accident, and no
 *       path is ever built from caller text: a caller names one of four known
 *       files and the catalog maps that name to a configured directory.
 *
 * NO MANIFESTS, NO CRASH
 *   A machine that has been provisioned but not yet checked out has none of
 *   these files. That is reported as a set of addresses that do not exist yet
 *   rather than as a failure, because "this is the wrong host" and "nothing has
 *   been put here yet" are different answers and an operator needs to know
 *   which one they are looking at.
 */

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using RMS.Configuration.Service.Extensions;
using RMS.Configuration.Service.Interfaces;
using RMS.Configuration.Service.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

ConfigurationOptions settings = builder.Configuration
    .GetSection(ConfigurationOptions.SectionName)
    .Get<ConfigurationOptions>() ?? new ConfigurationOptions();

IPAddress bindAddress = ResolveBindAddress(
    Environment.GetEnvironmentVariable(ConfigurationOptions.BindAddressVariable) ?? settings.BindAddress);

bool allowWildcard = IsTruthy(Environment.GetEnvironmentVariable(ConfigurationOptions.AllowWildcardVariable));

string? hostingUrls = builder.Configuration["urls"]
                      ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

/*
 * A hosting URL naming every interface is refused before the socket is opened.
 *
 * The check is here rather than left to the guard because the difference is
 * whether the port is open at all. A port that answers with a refusal is still a
 * port that answers, and this service can write the definition of every service
 * on the machine.
 */
if (hostingUrls is not null && ContainsWildcardHost(hostingUrls) && !allowWildcard)
{
    throw new InvalidOperationException(
        $"The hosting URL '{hostingUrls}' binds every interface, which this service refuses. Set " +
        $"{ConfigurationOptions.AllowWildcardVariable}=true if that is intended, or set " +
        $"{ConfigurationOptions.BindAddressVariable} to the address the site reaches this service on.");
}

/* Read the environment for the port so that the manual and supervised runs agree. */
int port = int.TryParse(Environment.GetEnvironmentVariable(ConfigurationOptions.PortVariable), out int configuredPort)
    ? configuredPort
    : settings.Port;

builder.WebHost.ConfigureKestrel(options =>
{
    /* The server header names the framework and its version. There is no reason to volunteer either. */
    options.AddServerHeader = false;

    /*
     * A request body here is a whole manifest, so the limit is not as tight as
     * the DevOps service's. It is still a limit: the largest document this
     * service accepts, plus enough room for the JSON escaping of a document
     * full of quotes and backslashes. A body larger than that is refused by the
     * transport rather than parsed and then refused by the writer, so an
     * oversized request costs a socket and not an allocation.
     */
    options.Limits.MaxRequestBodySize = (settings.MaximumDocumentBytes * 2) + 4096;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
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

builder.Services.AddConfigurationServices(builder.Configuration);

/* Enum values are published by name so a severity is readable rather than a number. */
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

WebApplication app = builder.Build();

app.MapConfigurationEndpoints();

/*
 * Report the guard's state at start-up. A disabled surface is a deployment
 * mistake that would otherwise appear only as an unexplained 404 on every page,
 * and the reason is safe to log because it never contains the key.
 */
IConfigurationKeyGuard guard = app.Services.GetRequiredService<IConfigurationKeyGuard>();

if (!guard.IsEnabled)
{
    app.Logger.LogWarning(
        "The Configuration surface is disabled: {Reason} The service still starts, so the site can explain why, "
        + "but every call it makes will be refused.",
        guard.DisabledReason);
}
else
{
    // The effective options are resolved here rather than read from the raw configuration, so the line reports
    // the addresses the service will actually use. A start-up banner that names a path from a template rather
    // than the path in force is worse than no banner.
    ConfigurationOptions effective = app.Services.GetRequiredService<IOptions<ConfigurationOptions>>().Value;

    app.Logger.LogInformation(
        "The Configuration surface is reachable from the configured networks. Repository root {Root}; manifests "
        + "{Manifests}; plan {Plan}.",
        effective.RepositoryRoot.Length == 0 ? "(unset)" : effective.RepositoryRoot,
        effective.ManifestDirectory.Length == 0 ? "(unset)" : effective.ManifestDirectory,
        effective.PlanFile.Length == 0 ? "(unset)" : effective.PlanFile);
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
        bool optedIn = IsTruthy(Environment.GetEnvironmentVariable(ConfigurationOptions.AllowWildcardVariable));
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
