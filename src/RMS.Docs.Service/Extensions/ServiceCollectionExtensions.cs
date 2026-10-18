using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RMS.Docs.Service.Implementation;
using RMS.Docs.Service.Interfaces;
using RMS.Docs.Service.Options;

namespace RMS.Docs.Service.Extensions;

/// <summary>
/// Registers the documentation service's catalogue and guard.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is stateless except the options, so every registration is a singleton. The catalogue holds the
/// directory and the ceiling; the guard holds the key and the allow-list. A scoped lifetime would allocate two
/// objects per request to do what two fields can do.
/// </para>
/// <para>
/// The options are validated on start-up rather than on first use. A service that refuses to start because its
/// port is not a port is a service an operator notices; a service that starts and then answers every request with
/// a refusal is one they debug.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the documentation service's catalogue, guard and options.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the options are bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddDocsServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DocsOptions>()
            .Bind(configuration.GetSection(DocsOptions.SectionName))
            .Configure(options =>
            {
                // The environment is applied after the file, so an operator's override wins and a stale
                // appsettings.json left over from a template cannot pin an address the deployment has since
                // moved. An absent or empty variable leaves the bound value alone, and a malformed number falls
                // back to the bound value: a port has an obvious default, whereas a key does not, and only the
                // key is allowed to disable the surface.
                ApplyEnvironment(DocsOptions.RootVariable, value => options.RepositoryRoot = value);
                ApplyEnvironment(DocsOptions.DocumentDirectoryVariable, value => options.DocumentDirectory = value);
                ApplyEnvironment(DocsOptions.TokenVariable, value => options.Token = value);
                ApplyEnvironment(DocsOptions.TokenFileVariable, value => options.TokenFile = value);
                ApplyEnvironment(DocsOptions.NetworksVariable, value => options.AllowedNetworks = value);
                ApplyEnvironment(DocsOptions.BindAddressVariable, value => options.BindAddress = value);
                options.AllowWildcardBind = ReadFlag(DocsOptions.AllowWildcardVariable, options.AllowWildcardBind);

                options.Port = ReadNumber(DocsOptions.PortVariable, options.Port);
                options.MaximumDocumentBytes = ReadNumber(
                    DocsOptions.MaximumDocumentBytesVariable,
                    options.MaximumDocumentBytes);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The document directory defaults to the one this repository keeps the documentation in, and the root to
        // that directory's parent. Both are guesses, but they are guesses that are right for a checkout of this
        // repository and obviously wrong -- a directory that does not exist -- for anything else, which is a
        // better failure than a plausible path that happens to hold somebody else's files.
        services.AddSingleton<IConfigureOptions<DocsOptions>, DocsAddressDefaults>();

        services.AddSingleton<IDocumentCatalog, DocumentCatalog>();
        services.AddSingleton<IDocsKeyGuard, DocsKeyGuard>();

        return services;
    }

    /// <summary>
    /// Applies an environment variable to the options when it carries a value.
    /// </summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="apply">The action that assigns the value.</param>
    private static void ApplyEnvironment(string name, Action<string> apply)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value)) apply(value);
    }

    /// <summary>
    /// Reads a counted environment variable, keeping the configured value when it is absent or unusable.
    /// </summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="current">The value in force before the variable is applied.</param>
    /// <returns>The value to use.</returns>
    private static int ReadNumber(string name, int current)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        return int.TryParse(value, out int parsed) ? parsed : current;
    }

    /// <summary>
    /// Reads a boolean environment variable, keeping the configured value when it is absent or unusable.
    /// </summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="current">The value in force before the variable is applied.</param>
    /// <returns>The value to use.</returns>
    /// <remarks>
    /// A flag is compared case-insensitively against the spellings an operator actually types. Anything else --
    /// including <c>0</c>, <c>no</c> and <c>false</c> -- turns the flag off, which is the safe direction: a
    /// permissive setting has to be opted into explicitly.
    /// </remarks>
    private static bool ReadFlag(string name, bool current)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return current;

        return value.Equals("1", StringComparison.Ordinal)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
