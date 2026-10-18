using System.ComponentModel.DataAnnotations;

namespace RMS.Docs.Service.Options;

/// <summary>
/// Everything the documentation service reads from configuration.
/// </summary>
/// <remarks>
/// <para>
/// The service owns no store. The documentation is a directory of files in the repository, and every setting
/// here is either an address -- where that directory is, where this service listens, which networks may reach
/// it -- or a ceiling that keeps a request from making it read more than the documentation directory could
/// reasonably contain.
/// </para>
/// <para>
/// That is deliberate. A documentation service with a database would be a second copy of the documentation,
/// and a second copy is a copy that can disagree with the files the repository actually holds. The files are
/// the source; this service is a reader of them.
/// </para>
/// <para>
/// Nothing here is a secret except <see cref="Token"/>, and that one is never logged.
/// </para>
/// </remarks>
public sealed class DocsOptions
{
    /// <summary>
    /// The name of the configuration section these values are bound from.
    /// </summary>
    public const string SectionName = "Docs";

    /// <summary>
    /// The environment variable naming the repository root.
    /// </summary>
    /// <remarks>
    /// Reported but not used to resolve anything: the document directory is an absolute path in its own right,
    /// and guessing that the root is its parent would be wrong for a deployment that keeps the documents
    /// somewhere else. It is kept because the start-up line that names the root is what tells an operator which
    /// checkout this process is reading.
    /// </remarks>
    public const string RootVariable = "RMS_DOCS_ROOT";

    /// <summary>
    /// The environment variable naming the directory the documents are read from.
    /// </summary>
    public const string DocumentDirectoryVariable = "RMS_DOCS_DOCUMENT_DIR";

    /// <summary>
    /// The environment variable holding the shared key.
    /// </summary>
    public const string TokenVariable = "RMS_DOCS_TOKEN";

    /// <summary>
    /// The environment variable naming a file the shared key is read from.
    /// </summary>
    public const string TokenFileVariable = "RMS_DOCS_TOKEN_FILE";

    /// <summary>
    /// The environment variable holding the network allow-list.
    /// </summary>
    public const string NetworksVariable = "RMS_DOCS_NETWORKS";

    /// <summary>
    /// The environment variable holding the address the service listens on.
    /// </summary>
    public const string BindAddressVariable = "RMS_DOCS_BIND_ADDRESS";

    /// <summary>
    /// The environment variable permitting a wildcard bind.
    /// </summary>
    public const string AllowWildcardVariable = "RMS_DOCS_ALLOW_WILDCARD";

    /// <summary>
    /// The environment variable holding the port, when no hosting URL is configured.
    /// </summary>
    public const string PortVariable = "RMS_DOCS_PORT";

    /// <summary>
    /// The environment variable holding the largest document the service will read.
    /// </summary>
    public const string MaximumDocumentBytesVariable = "RMS_DOCS_MAXIMUM_DOCUMENT_BYTES";

    /// <summary>
    /// The name of the directory this repository keeps its documentation in.
    /// </summary>
    public const string DocumentDirectoryName = "docs";

    /// <summary>
    /// The file whose presence identifies a directory as the documentation directory.
    /// </summary>
    /// <remarks>
    /// An empty directory called <c>docs</c> is a directory somebody made for some other reason. The landing
    /// document is what makes it this deployment's documentation, and it is required anyway: a documentation
    /// site whose front page is missing is not finished.
    /// </remarks>
    public const string MarkerFileName = "index.md";

    /// <summary>
    /// Gets or sets the absolute path of the repository root.
    /// </summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the directory the documents are read from.
    /// </summary>
    /// <remarks>
    /// The directory is enumerated once per request and a document is only ever read from a path that came out
    /// of that enumeration. No path is built from caller text, so a request cannot name a file -- it can only
    /// name one of the names the directory itself offered.
    /// </remarks>
    public string DocumentDirectory { get; set; } = string.Empty;

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
    /// Refused by default. The documentation itself is not secret, but this surface is reachable by the site's
    /// proxies alone, and a documentation reader that is reachable from every network is a service that has to
    /// be kept patched for no benefit: public documentation belongs on the public site, not on an internal
    /// port.
    /// </remarks>
    public bool AllowWildcardBind { get; set; }

    /// <summary>
    /// Gets or sets the port the service listens on.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 5203;

    /// <summary>
    /// Gets or sets the largest document the service will read.
    /// </summary>
    /// <remarks>
    /// This is a ceiling on one file, not on the directory. A document over it is reported as a problem and not
    /// served, rather than truncated: half a document served as though it were whole is worse than a document
    /// that is visibly absent, because the reader has no way to tell that something is missing.
    /// </remarks>
    [Range(4 * 1024, 4 * 1024 * 1024)]
    public int MaximumDocumentBytes { get; set; } = 512 * 1024;
}
