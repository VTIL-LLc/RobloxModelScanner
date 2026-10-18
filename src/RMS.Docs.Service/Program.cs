/*
 * RMS — DOCUMENTATION SERVICE HOST
 * ============================================================================
 * PURPOSE
 *   Serves the repository's documentation directory to the documentation site.
 *   It reads files, resolves each one's title and reading order, and answers
 *   three questions: what is there, give me one, and find this phrase.
 *
 *   It owns nothing. The documentation is a directory of files in the
 *   repository, and this service deliberately keeps no copy of any of it: a
 *   documentation service with its own store would be a second version of the
 *   documentation, and a second version is one that can disagree with the file
 *   somebody just corrected.
 *
 * WHY IT IS SEPARATE FROM THE SITE
 *   The site is a .NET Framework application served by IIS. Reading a directory
 *   and resolving titles is not a page's job, and the site is the only thing on
 *   the network. So the reader lives here, behind a loopback listener and a
 *   shared key, and the site reaches it from the same origin -- which is what
 *   lets the site keep a strict content security policy instead of being
 *   trusted to read the file system itself.
 *
 * EXPOSURE
 *   The documentation is not secret, and this surface is still guarded:
 *
 *     - It listens on a configured address, loopback by default. A wildcard
 *       bind is refused unless the operator has explicitly opted in.
 *     - The peer address must fall inside a configured allow-list, evaluated
 *       against the socket and never against a forwarding header.
 *     - A shared key must be presented in the X-Docs-Key header, compared in
 *       fixed time. A key shorter than the minimum disables the surface rather
 *       than being accepted.
 *     - Every route that answers anything is inside one guarded group, and no
 *       path is ever built from caller text: a caller names a slug, and a slug
 *       selects from the list the directory itself produced.
 *
 * NO DOCUMENTS, NO CRASH
 *   A machine that has been provisioned but not yet checked out has no
 *   documentation directory. That is reported as an address that does not exist
 *   rather than as a failure, because "this is the wrong host" and "nothing has
 *   been put here yet" are different answers and an operator needs to know
 *   which one they are looking at.
 */

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using RMS.Docs.Service.Extensions;
using RMS.Docs.Service.Interfaces;
using RMS.Docs.Service.Models;
using RMS.Docs.Service.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

DocsOptions settings = builder.Configuration
    .GetSection(DocsOptions.SectionName)
    .Get<DocsOptions>() ?? new DocsOptions();

IPAddress bindAddress = ResolveBindAddress(
    Environment.GetEnvironmentVariable(DocsOptions.BindAddressVariable) ?? settings.BindAddress);

bool allowWildcard = IsTruthy(Environment.GetEnvironmentVariable(DocsOptions.AllowWildcardVariable));

string? hostingUrls = builder.Configuration["urls"]
                      ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

/*
 * A hosting URL naming every interface is refused before the socket is opened.
 *
 * The check is here rather than left to the guard because the difference is
 * whether the port is open at all. A port that answers with a refusal is still a
 * port that answers, and this service reads a directory on the host.
 */
if (hostingUrls is not null && ContainsWildcardHost(hostingUrls) && !allowWildcard)
{
    throw new InvalidOperationException(
        $"The hosting URL '{hostingUrls}' binds every interface, which this service refuses. Set "
        + $"{DocsOptions.AllowWildcardVariable}=true if that is intended, or set "
        + $"{DocsOptions.BindAddressVariable} to the address the site reaches this service on.");
}

/* Read the environment for the port so that the manual and supervised runs agree. */
int port = int.TryParse(Environment.GetEnvironmentVariable(DocsOptions.PortVariable), out int configuredPort)
    ? configuredPort
    : settings.Port;

builder.WebHost.ConfigureKestrel(options =>
{
    /* The server header names the framework and its version. There is no reason to volunteer either. */
    options.AddServerHeader = false;

    /*
     * This surface is read-only, so a request body is never meaningful. The limit is one kilobyte rather than
     * zero because zero is not a value the transport accepts, and one kilobyte is enough for a caller that sends
     * an empty JSON object out of habit and small enough that nothing can be smuggled in a body. A request that
     * tries to send more is refused by the transport rather than read and discarded.
     */
    options.Limits.MaxRequestBodySize = 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);

    /*
     * When the hosting environment already named a URL, that URL decides the address and this only adds the
     * protocol constraint. When it did not, the listener is built here from the configured address and port, so a
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

builder.Services.AddDocsServices(builder.Configuration);

/* Enum values are published by name so a format is readable rather than a number. */
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

WebApplication app = builder.Build();

app.MapDocsEndpoints();

/*
 * Report the guard's state at start-up. A disabled surface is a deployment mistake that would otherwise appear
 * only as an unexplained 404 on every page, and the reason is safe to log because it never contains the key.
 */
IDocsKeyGuard guard = app.Services.GetRequiredService<IDocsKeyGuard>();

if (!guard.IsEnabled)
{
    app.Logger.LogWarning(
        "The documentation surface is disabled: {Reason} The service still starts, so the site can explain why, "
        + "but every call it makes will be refused.",
        guard.DisabledReason);
}
else
{
    // The effective options are resolved here rather than read from the raw configuration, so the line reports
    // the addresses the service will actually use. A start-up banner that names a path from a template rather
    // than the path in force is worse than no banner.
    DocsOptions effective = app.Services.GetRequiredService<IOptions<DocsOptions>>().Value;

    // Read once so the line reports what is actually there. A service that says it is serving documentation and
    // is in fact pointed at an empty directory is a fault that is easier to see here than on a rendered page.
    DocumentListView catalogue = app.Services.GetRequiredService<IDocumentCatalog>().List();

    app.Logger.LogInformation(
        "The documentation surface is reachable from the configured networks. Repository root {Root}; documents "
        + "{Directory}; {Count} document(s).",
        effective.RepositoryRoot.Length == 0 ? "(unset)" : effective.RepositoryRoot,
        effective.DocumentDirectory.Length == 0 ? "(unset)" : effective.DocumentDirectory,
        catalogue.DocumentCount);

    foreach (DocumentProblemView problem in catalogue.Problems)
    {
        app.Logger.LogWarning(
            "Documentation problem{Slug}: {Severity}: {Message}",
            problem.Slug.Length == 0 ? string.Empty : $" in {problem.Slug}",
            problem.Severity,
            problem.Message);
    }
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
        bool optedIn = IsTruthy(Environment.GetEnvironmentVariable(DocsOptions.AllowWildcardVariable));
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
