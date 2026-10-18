using Microsoft.Extensions.DependencyInjection;

using RMS.Devops.Service.Implementation;
using RMS.Devops.Service.Interfaces;
using RMS.Devops.Service.Options;

namespace RMS.Devops.Service.Extensions;

/// <summary>
/// Registers the DevOps service's readers.
/// </summary>
/// <remarks>
/// <para>
/// Everything except the plan state is stateless, so the lifetimes are chosen for what each object holds rather
/// than for what it does. The archive holds the plan and the parsed-record cache, so it is a singleton: two
/// instances would keep two copies of one cache and re-read the plan twice for every page. The log reader, the
/// artifact inventory and the catalogue summary hold nothing but the options, so they follow the archive's
/// lifetime to keep the dependency graph trivially correct.
/// </para>
/// <para>
/// The options are validated on start-up rather than on first use. A service that refuses to start because its
/// key is too short is a service an operator notices; a service that starts and then answers every request with
/// a refusal is one they debug.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DevOps service's readers and options.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the options are bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddDevopsServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DevopsOptions>()
            .Bind(configuration.GetSection(DevopsOptions.SectionName))
            .Configure(options =>
            {
                // The environment is applied after the file, so an operator's override wins and a stale
                // appsettings.json left over from a template cannot pin an address the deployment has since
                // moved. An absent or empty variable leaves the bound value alone, and a malformed number falls
                // back to the bound value: a page size has an obvious safe default, whereas a key does not, and
                // only the key is allowed to disable the surface.
                ApplyEnvironment(DevopsOptions.PlanFileVariable, value => options.PlanFile = value);
                ApplyEnvironment(DevopsOptions.IndexPathVariable, value => options.CatalogIndexPath = value);
                ApplyEnvironment(DevopsOptions.TokenVariable, value => options.Token = value);
                ApplyEnvironment(DevopsOptions.TokenFileVariable, value => options.TokenFile = value);
                ApplyEnvironment(DevopsOptions.NetworksVariable, value => options.AllowedNetworks = value);
                ApplyEnvironment(DevopsOptions.BindAddressVariable, value => options.BindAddress = value);
                options.AllowWildcardBind = ReadFlag(DevopsOptions.AllowWildcardVariable, options.AllowWildcardBind);
                options.PageSize = ReadNumber(DevopsOptions.PageSizeVariable, options.PageSize);
                options.MaximumLogBytes = ReadNumber(DevopsOptions.MaximumLogBytesVariable, options.MaximumLogBytes);
                options.CacheSeconds = ReadNumber(DevopsOptions.CacheSecondsVariable, options.CacheSeconds);
                options.Port = ReadNumber(DevopsOptions.PortVariable, options.Port);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IServiceArchive, ServiceArchive>();
        services.AddSingleton<ILogArchive, LogArchive>();
        services.AddSingleton<IArtifactInventory, PublishedArtifactInventory>();
        services.AddSingleton<ICatalogStatistics, SqliteCatalogStatistics>();
        services.AddSingleton<IDevopsKeyGuard, DevopsKeyGuard>();

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
    /// permissive setting must be opted into explicitly.
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
