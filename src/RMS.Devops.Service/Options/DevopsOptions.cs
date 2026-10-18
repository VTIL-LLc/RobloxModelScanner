using System.ComponentModel.DataAnnotations;

namespace RMS.Devops.Service.Options;

/// <summary>
/// Everything the DevOps service reads from configuration.
/// </summary>
/// <remarks>
/// <para>
/// The service is a reader: it is given a plan and a set of directories and reports what it finds there. It
/// therefore has no behaviour to configure, only addresses. Each address has an environment-variable fallback,
/// because the deployment that starts this process supplies them that way rather than through a file that
/// would then have to be kept in step with the plan it describes.
/// </para>
/// <para>
/// Nothing here is a secret except <see cref="Token"/>, and that one is never logged: a guard that reported
/// the value it expected would undo the reason for having it.
/// </para>
/// </remarks>
public sealed class DevopsOptions
{
    /// <summary>
    /// The name of the configuration section these values are bound from.
    /// </summary>
    public const string SectionName = "Devops";

    /// <summary>
    /// The environment variable naming the plan file.
    /// </summary>
    public const string PlanFileVariable = "RMS_DEVOPS_PLAN_FILE";

    /// <summary>
    /// The environment variable naming the catalogue index.
    /// </summary>
    public const string IndexPathVariable = "SCANNER_INDEX_PATH";

    /// <summary>
    /// The environment variable holding the shared key.
    /// </summary>
    public const string TokenVariable = "RMS_DEVOPS_TOKEN";

    /// <summary>
    /// The environment variable naming a file the shared key is read from.
    /// </summary>
    /// <remarks>
    /// A file is preferred to the variable where the deployment can arrange it. An environment variable is
    /// visible to anything that can list the process table, and on a shared machine that means the key is
    /// visible to more people than the service it protects. A file can be given an owner and a mode, which the
    /// variable cannot.
    /// </remarks>
    public const string TokenFileVariable = "RMS_DEVOPS_TOKEN_FILE";

    /// <summary>
    /// The environment variable holding the network allow-list.
    /// </summary>
    public const string NetworksVariable = "RMS_DEVOPS_NETWORKS";

    /// <summary>
    /// The environment variable holding the address the service listens on.
    /// </summary>
    public const string BindAddressVariable = "RMS_DEVOPS_BIND_ADDRESS";

    /// <summary>
    /// The environment variable permitting a wildcard bind.
    /// </summary>
    public const string AllowWildcardVariable = "RMS_DEVOPS_ALLOW_WILDCARD";

    /// <summary>
    /// The environment variable holding the port, when no hosting URL is configured.
    /// </summary>
    public const string PortVariable = "RMS_DEVOPS_PORT";

    /// <summary>
    /// The environment variable holding how many runs a page holds.
    /// </summary>
    public const string PageSizeVariable = "RMS_DEVOPS_PAGE_SIZE";

    /// <summary>
    /// The environment variable holding the largest log slice that may be returned at once.
    /// </summary>
    public const string MaximumLogBytesVariable = "RMS_DEVOPS_MAX_LOG_BYTES";

    /// <summary>
    /// The environment variable holding how long a parsed run record is reused before it is re-read.
    /// </summary>
    public const string CacheSecondsVariable = "RMS_DEVOPS_CACHE_SECONDS";

    /// <summary>
    /// Gets or sets the absolute path of the service plan the deployment wrote.
    /// </summary>
    /// <remarks>
    /// The plan is the source of the log root, so without it there is nothing to read and the service reports
    /// that rather than guessing at a directory. That is deliberate: guessing would mean a page showing an
    /// empty history for a machine that is in fact running services under a path nobody told this process
    /// about.
    /// </remarks>
    public string PlanFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the catalogue index.
    /// </summary>
    /// <remarks>
    /// Optional. The catalogue summary is one panel on one page, and a machine that runs the DevOps service
    /// without running the scanner is a perfectly ordinary configuration.
    /// </remarks>
    public string CatalogIndexPath { get; set; } = string.Empty;

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
    /// <remarks>
    /// When this names a readable file its trimmed content is the key and <see cref="Token"/> is ignored. The
    /// file is read once, at start-up: rotating the key means restarting the service, which is the correct
    /// amount of ceremony for a change that is meant to be deliberate.
    /// </remarks>
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
    /// <remarks>
    /// A wildcard is refused unless the operator has explicitly opted in, since this service reports the
    /// layout of the machine it runs on.
    /// </remarks>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets a value indicating whether the service may listen on every interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is refused by default, and the refusal is deliberate rather than cautious. This service describes
    /// the layout of the machine it runs on -- the absolute path of every program, every service and every log,
    /// and the contents of those logs -- so a listener on every interface is a map of a host being offered to
    /// whatever else is on the network.
    /// </para>
    /// <para>
    /// It exists as an opt-in rather than as a ban because a deployment where the site runs on a different host
    /// from the services is a legitimate topology, and in that topology the service must be reachable. The
    /// allow-list and the key still apply; this only decides whether the socket is opened at all.
    /// </para>
    /// </remarks>
    public bool AllowWildcardBind { get; set; }

    /// <summary>
    /// Gets or sets the port the service listens on.
    /// </summary>
    /// <remarks>
    /// Only consulted when the hosting environment has not already been given a URL, which is how the plan
    /// starts this process. The default matches the port the manifest declares, so a manual run and a supervised
    /// run land in the same place.
    /// </remarks>
    [Range(1, 65535)]
    public int Port { get; set; } = 5201;

    /// <summary>
    /// Gets or sets how many runs a page holds.
    /// </summary>
    /// <remarks>
    /// The default matches the deployment history table this site reproduces, which pages twenty-five rows at
    /// a time.
    /// </remarks>
    [Range(1, 500)]
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// Gets or sets the largest slice of a log the service will return in one response.
    /// </summary>
    /// <remarks>
    /// The cap is what stops a request for the history of an extremely talkative service from reading a
    /// multi-megabyte file into memory. A caller that wants more asks for the next slice.
    /// </remarks>
    [Range(4096, 8 * 1024 * 1024)]
    public int MaximumLogBytes { get; set; } = 512 * 1024;

    /// <summary>
    /// Gets or sets how long a parsed run record may be reused.
    /// </summary>
    /// <remarks>
    /// A page lists every run of every service, so without a cache one poll would parse a few hundred small
    /// files. The cache is keyed by the file's identity and is invalidated by a change to its size or its
    /// last-write time, so it can only ever return a record that has not been rewritten.
    /// </remarks>
    [Range(0, 3600)]
    public int CacheSeconds { get; set; } = 5;
}
