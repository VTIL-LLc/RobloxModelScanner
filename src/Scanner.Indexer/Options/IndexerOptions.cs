namespace Scanner.Indexer.Options;

/// <summary>
/// Strongly typed configuration for the catalogue indexing service.
/// </summary>
/// <remarks>
/// <para>
/// The service has two responsibilities with very different exposure. The migration loop reads the
/// scanner's JSON catalogue and writes the SQLite index; the control plane answers internal status and
/// rebuild requests. Only the second is reachable over a socket, and its binding, its network allow-list
/// and its token are all configured here so that an operator can tighten them without touching code.
/// </para>
/// <para>
/// No secret is committed. The control token is read from the <c>SCANNER_INDEXER_TOKEN</c> environment
/// variable, and the control plane refuses to start when the token is absent rather than serving
/// unauthenticated state to anything that can reach the port.
/// </para>
/// </remarks>
public sealed class IndexerOptions
{
    /// <summary>
    /// The configuration section name that these options are bound from.
    /// </summary>
    public const string SectionName = "Indexer";

    /// <summary>
    /// Gets or sets the absolute path to the scanner's <c>models.json</c> catalogue.
    /// </summary>
    /// <value>When empty, <c>Files/models.json</c> beside the application is used.</value>
    public string? ModelsPath { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the SQLite index this service owns.
    /// </summary>
    /// <value>When empty, <c>Files/scanner-index.db</c> beside the catalogue is used.</value>
    public string? IndexPath { get; set; }

    /// <summary>
    /// Gets or sets the minimum number of seconds between catalogue inspect passes.
    /// </summary>
    /// <value>
    /// Defaults to <c>5</c>. The migration itself only runs when the catalogue's fingerprint changes;
    /// this interval governs how promptly a change is noticed, and inspecting the file costs one
    /// stat call.
    /// </value>
    public int RefreshSeconds { get; set; } = 5;

    /// <summary>
    /// Gets or sets the address the internal control plane binds to.
    /// </summary>
    /// <value>
    /// Defaults to the IPv4 loopback address. Binding a wildcard address would expose the control plane
    /// to every network the host is attached to, so a non-loopback value is only appropriate when the
    /// operator has constrained it with a host firewall.
    /// </value>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the port the internal control plane listens on.
    /// </summary>
    public int ControlPort { get; set; } = 5081;

    /// <summary>
    /// Gets or sets the shared secret required on every control-plane request.
    /// </summary>
    /// <value>
    /// Supplied through the <c>SCANNER_INDEXER_TOKEN</c> environment variable. An empty value disables
    /// the control plane entirely: the migration loop still runs, but nothing is exposed.
    /// </value>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the semi-colon separated list of networks permitted to reach the control plane.
    /// </summary>
    /// <value>
    /// Defaults to the loopback addresses. Entries are CIDR blocks, for example
    /// <c>127.0.0.1/32;10.20.0.0/16</c>. A request from any other address is refused before routing, so
    /// this is the first line of defence and the token is the second.
    /// </value>
    public string? AllowedNetworks { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of distinct detection labels written to the statistics table.
    /// </summary>
    /// <value>
    /// Defaults to <c>512</c>. The rule set is finite, but a corrupted catalogue could carry thousands of
    /// junk labels; the cap is applied after ordering by frequency so the most significant labels survive.
    /// </value>
    public int MaximumDetectionsPerBuild { get; set; } = 512;
}
