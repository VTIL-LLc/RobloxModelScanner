using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RMS.Configuration.Service.Implementation;
using RMS.Configuration.Service.Interfaces;
using RMS.Configuration.Service.Options;

namespace RMS.Configuration.Service.Extensions;

/// <summary>
/// Registers the Configuration service's readers and writers.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is stateless except the options, so every registration is a singleton. The reader and the
/// catalog hold nothing but the ceilings and the addresses they were configured with; the writer holds the same
/// plus a logger. A scoped lifetime would allocate four objects per request to do what four fields can do.
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
    /// Registers the Configuration service's readers, writers and options.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the options are bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddConfigurationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<ConfigurationOptions>()
            .Bind(configuration.GetSection(ConfigurationOptions.SectionName))
            .Configure(options =>
            {
                // The environment is applied after the file, so an operator's override wins and a stale
                // appsettings.json left over from a template cannot pin an address the deployment has since
                // moved. An absent or empty variable leaves the bound value alone, and a malformed number falls
                // back to the bound value: a port has an obvious default, whereas a key does not, and only the
                // key is allowed to disable the surface.
                ApplyEnvironment(ConfigurationOptions.RootVariable, value => options.RepositoryRoot = value);
                ApplyEnvironment(ConfigurationOptions.ManifestDirectoryVariable, value => options.ManifestDirectory = value);
                ApplyEnvironment(ConfigurationOptions.PlanFileVariable, value => options.PlanFile = value);
                ApplyEnvironment(ConfigurationOptions.TokenVariable, value => options.Token = value);
                ApplyEnvironment(ConfigurationOptions.TokenFileVariable, value => options.TokenFile = value);
                ApplyEnvironment(ConfigurationOptions.NetworksVariable, value => options.AllowedNetworks = value);
                ApplyEnvironment(ConfigurationOptions.BindAddressVariable, value => options.BindAddress = value);
                options.AllowWildcardBind = ReadFlag(
                    ConfigurationOptions.AllowWildcardVariable,
                    options.AllowWildcardBind);

                options.Port = ReadNumber(ConfigurationOptions.PortVariable, options.Port);
                options.MaximumDocumentBytes = ReadNumber(
                    ConfigurationOptions.MaximumDocumentBytesVariable,
                    options.MaximumDocumentBytes);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The manifest directory defaults to the one this repository keeps the manifests in, and the root to that
        // directory's parent. Both are guesses, but they are guesses that are right for a checkout of this
        // repository and obviously wrong -- a directory that does not exist -- for anything else, which is a
        // better failure than a plausible path that happens to hold somebody else's deployment.
        services.AddSingleton<IConfigureOptions<ConfigurationOptions>, ManifestAddressDefaults>();

        services.AddSingleton<ManifestDocumentReader>();
        services.AddSingleton<IManifestValidator, ManifestValidator>();
        services.AddSingleton<IManifestCatalog, ManifestCatalog>();
        services.AddSingleton<ManifestWriter>();
        services.AddSingleton<IPlanReader, PlanReader>();
        services.AddSingleton<IConfigurationKeyGuard, ConfigurationKeyGuard>();

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
