namespace Scanner.MicroApi.Options;

/// <summary>
/// Strongly typed configuration for the scanner HTTP API.
/// </summary>
/// <remarks>
/// <para>
/// The options are bound from the <c>Scanner</c> configuration section. No secret is ever
/// committed to <c>appsettings.json</c>; the API key is expected to arrive through the
/// <c>Scanner:ApiKey</c> setting (user secrets, deployment configuration) or the
/// <c>SCANNER_API_KEY</c> environment variable, and the same GUID must be configured in
/// the website's server-side settings.
/// </para>
/// <para>
/// Paths default to the deployed application directory so a fresh installation works
/// without configuration, while an explicit <c>ModelsPath</c> lets the API read the
/// catalogue produced by the independently deployed scanner process.
/// </para>
/// <para>
/// This class is a reader of the environment, not a store of secrets. Every value here can be supplied
/// by an environment variable of the same name prefixed with <c>SCANNER_</c>, and that is where a
/// credential belongs on a deployed host.
/// </para>
/// </remarks>
public sealed class ScannerOptions
{
    /// <summary>
    /// The configuration section name that these options are bound from.
    /// </summary>
    public const string SectionName = "Scanner";

    /// <summary>
    /// Gets or sets the GUID callers must supply in the <c>apiKey</c> query parameter.
    /// </summary>
    /// <value>An empty or unparsable value disables every authenticated endpoint.</value>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the scanner's <c>models.json</c> catalogue.
    /// </summary>
    /// <value>When empty, <c>Files/models.json</c> beside the application is used.</value>
    public string? ModelsPath { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the SQLite index database.
    /// </summary>
    /// <value>When empty, <c>Files/scanner-index.db</c> beside the catalogue is used.</value>
    public string? IndexPath { get; set; }

    /// <summary>
    /// Gets or sets an optional Discord webhook used for operational notices.
    /// </summary>
    /// <value>Unused by the current endpoints; reserved so deployments can report failures.</value>
    public string? StatusWebhook { get; set; }

    /// <summary>
    /// Gets or sets the addresses of the reverse proxies whose forwarding headers are trusted.
    /// </summary>
    /// <value>
    /// A comma- or semicolon-separated list of addresses or CIDR ranges. Empty means the published
    /// Cloudflare ranges, which is what a deployment behind Cloudflare wants and what leaves a
    /// deployment addressed directly unaffected; the single word <c>none</c> means trust nothing.
    /// </value>
    /// <remarks>
    /// This list is the whole reason a forwarded header can be believed. The header is set by the client
    /// and is trivially forged, so it is honoured only when the peer that sent it is one of these
    /// addresses. Trusting it from anywhere would let a caller choose its own apparent address and so
    /// evade the per-client rate limit, and would put a forged address into every log line.
    /// </remarks>
    public string? TrustedProxies { get; set; }

    /// <summary>
    /// Gets or sets how many requests one client may make in a minute.
    /// </summary>
    /// <value>
    /// Defaults to <c>240</c>. A single page of the administration console fans out to several calls and
    /// refreshes on a timer, so the limit is set well above what one operator's browser produces and well
    /// below what a script intended to exhaust the host would need.
    /// </value>
    public int RequestsPerMinute { get; set; } = 240;

    /// <summary>
    /// Gets or sets the optional Roblox authentication cookie used when resolving asset
    /// download locations for moderated content.
    /// </summary>
    /// <value>
    /// Supplied through the file named by the <c>ROBLOX_COOKIE_FILE</c> environment variable, or, when
    /// that is absent, through <c>ROBLOX_COOKIE</c> itself. A deployment writes the file; the variable
    /// is for a machine where the value is already in the environment. It is required only for the
    /// download-resolution endpoint and must never be exposed to callers.
    /// </value>
    public string? RobloxCookie { get; set; }
}
