using System.ComponentModel.DataAnnotations;

namespace RMS.Configuration.Service.Options;

/// <summary>
/// Everything the Configuration service reads from configuration.
/// </summary>
/// <remarks>
/// <para>
/// This service is a console over four files, and its configuration is therefore almost entirely the question
/// of where those four files are. It owns no store of its own and has no setting that is not an address: the
/// whole point of it is that the deployment manifests are the only description of the deployment, and a console
/// that kept a second description would be a console that could disagree with the thing it describes.
/// </para>
/// <para>
/// Nothing here is a secret except <see cref="Token"/>, and that one is never logged.
/// </para>
/// </remarks>
public sealed class ConfigurationOptions
{
    /// <summary>
    /// The name of the configuration section these values are bound from.
    /// </summary>
    public const string SectionName = "Configuration";

    /// <summary>
    /// The environment variable naming the repository root.
    /// </summary>
    public const string RootVariable = "RMS_CONFIGURATION_ROOT";

    /// <summary>
    /// The environment variable naming the directory holding the manifests.
    /// </summary>
    public const string ManifestDirectoryVariable = "RMS_CONFIGURATION_MANIFEST_DIR";

    /// <summary>
    /// The environment variable naming the generated plan file.
    /// </summary>
    public const string PlanFileVariable = "RMS_CONFIGURATION_PLAN_FILE";

    /// <summary>
    /// The environment variable holding the shared key.
    /// </summary>
    public const string TokenVariable = "RMS_CONFIGURATION_TOKEN";

    /// <summary>
    /// The environment variable naming a file the shared key is read from.
    /// </summary>
    public const string TokenFileVariable = "RMS_CONFIGURATION_TOKEN_FILE";

    /// <summary>
    /// The environment variable holding the network allow-list.
    /// </summary>
    public const string NetworksVariable = "RMS_CONFIGURATION_NETWORKS";

    /// <summary>
    /// The environment variable holding the address the service listens on.
    /// </summary>
    public const string BindAddressVariable = "RMS_CONFIGURATION_BIND_ADDRESS";

    /// <summary>
    /// The environment variable permitting a wildcard bind.
    /// </summary>
    public const string AllowWildcardVariable = "RMS_CONFIGURATION_ALLOW_WILDCARD";

    /// <summary>
    /// The environment variable holding the port, when no hosting URL is configured.
    /// </summary>
    public const string PortVariable = "RMS_CONFIGURATION_PORT";

    /// <summary>
    /// The environment variable holding the largest manifest the service will read or accept.
    /// </summary>
    public const string MaximumDocumentBytesVariable = "RMS_CONFIGURATION_MAX_DOCUMENT_BYTES";

    /// <summary>
    /// The file name of the requirements manifest.
    /// </summary>
    public const string RequirementsFileName = "rmsdeploy.requirements";

    /// <summary>
    /// The file name of the build manifest.
    /// </summary>
    public const string BuildFileName = "rmsdeploy.build";

    /// <summary>
    /// The file name of the services manifest.
    /// </summary>
    public const string ServicesFileName = "rmsdeploy.services";

    /// <summary>
    /// The path of the generated plan, relative to the repository root, when none is configured.
    /// </summary>
    public const string DefaultPlanRelativePath = "artifacts/services/services.ini";

    /// <summary>
    /// The placeholders a manifest value may carry, and what they stand for.
    /// </summary>
    /// <remarks>
    /// Only <c>%ROOT%</c> is resolved here. A value naming any other environment variable is left as it is and
    /// reported, because the deployer resolves those against the environment of the machine it is running on
    /// and this service cannot know what that will be. Reporting the unresolved name is more useful than
    /// guessing, and it is what stops the console from claiming a path is valid when it is a promise about a
    /// machine that has not been provisioned yet.
    /// </remarks>
    public const string RootPlaceholder = "%ROOT%";

    /// <summary>
    /// Gets or sets the absolute path of the repository root.
    /// </summary>
    /// <remarks>
    /// The root is what <c>%ROOT%</c> expands to, and what a build step's relative path is resolved against. It
    /// is therefore the one setting that changes the meaning of every value in every manifest, and it defaults
    /// to the parent of the manifest directory when it is not given, which is where the manifests live in this
    /// repository and is therefore right far more often than it is wrong.
    /// </remarks>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the directory holding the manifests.
    /// </summary>
    /// <remarks>
    /// Only the four known file names inside it are ever read. The directory itself is never enumerated and no
    /// manifest name is ever built from caller text, so a request cannot name a path.
    /// </remarks>
    public string ManifestDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the generated plan file.
    /// </summary>
    /// <remarks>
    /// The plan is produced by the deployer and is reported read-only. It is worth showing because it is the
    /// thing the installer actually acts on: an operator who has just edited a service and wants to know what
    /// the edit became has nowhere else to look.
    /// </remarks>
    public string PlanFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shared key callers must present.
    /// </summary>
    /// <remarks>
    /// A value shorter than the minimum length disables the whole surface rather than being accepted.
    /// </remarks>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of a file the shared key is read from.
    /// </summary>
    public string TokenFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the semicolon separated list of CIDR blocks the callers may come from.
    /// </summary>
    /// <remarks>
    /// Empty means loopback only. An unusable list disables the surface, because falling back to a permissive
    /// default would turn a typo into an exposure.
    /// </remarks>
    public string AllowedNetworks { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the address the service listens on.
    /// </summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets a value indicating whether the service may listen on every interface.
    /// </summary>
    /// <remarks>
    /// Refused by default, for the same reason as the DevOps service: this one describes a deployment, and a
    /// description of a deployment is a map of hosts, paths and ports.
    /// </remarks>
    public bool AllowWildcardBind { get; set; }

    /// <summary>
    /// Gets or sets the port the service listens on.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 5202;

    /// <summary>
    /// Gets or sets the largest manifest the service will read from disk or accept for writing.
    /// </summary>
    /// <remarks>
    /// The manifests are between six and eleven kilobytes. The ceiling is large enough to hold a year of growth
    /// and small enough that a request cannot be used to make the service allocate without bound. It is applied
    /// before a document is parsed as well as before one is written, because a parser that ran first would be a
    /// parser an attacker could feed.
    /// </remarks>
    [Range(16 * 1024, 4 * 1024 * 1024)]
    public int MaximumDocumentBytes { get; set; } = 512 * 1024;
}
