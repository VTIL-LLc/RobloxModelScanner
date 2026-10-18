using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Options;

namespace Scanner.Indexer.Implementation;

/// <summary>
/// Decides whether a caller may use the internal control plane.
/// </summary>
/// <remarks>
/// <para>
/// The control plane exists so an operator or the scanner API can ask "is the index current" and
/// "migrate now". It exposes no catalogue data and no file access, but it can trigger a full migration, so
/// it is treated as a privileged surface.
/// </para>
/// <para>
/// Three independent barriers are applied, in this order:
/// </para>
/// <list type="number">
/// <item><description>
/// The socket itself. The listener is bound to a configured address, loopback by default, so the surface
/// is not on the network at all unless an operator deliberately puts it there.
/// </description></item>
/// <item><description>
/// A network allow-list. The peer address of the connection must fall inside one of the configured CIDR
/// blocks. This is evaluated against the socket peer and never against a forwarding header: a forwarded
/// header is attacker-controlled, so trusting it here would make the allow-list decorative.
/// </description></item>
/// <item><description>
/// A shared token, compared over its raw bytes in fixed time so that a guess cannot be narrowed by timing.
/// A token shorter than <see cref="MinimumTokenLength"/> characters disables the surface rather than
/// accepting a weak secret.
/// </description></item>
/// </list>
/// <para>
/// Every construction path fails closed. If anything about the configuration is not usable, the guard
/// reports itself disabled and the endpoints respond as though they do not exist.
/// </para>
/// </remarks>
public sealed class InternalCallGuard : IInternalCallGuard
{
    /// <summary>
    /// The header carrying the control-plane token.
    /// </summary>
    internal const string TokenHeaderName = "X-Indexer-Token";

    /// <summary>
    /// The shortest token this service will accept.
    /// </summary>
    /// <remarks>
    /// A 32-character token is short enough to be typed from a secret manager and long enough that online
    /// guessing is hopeless. Anything shorter disables the control plane, so a copy-paste accident cannot
    /// leave a trivially guessable secret in place.
    /// </remarks>
    private const int MinimumTokenLength = 32;

    private readonly byte[]? _expectedToken;
    private readonly IPNetwork[] _allowedNetworks;
    private readonly ILogger<InternalCallGuard> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalCallGuard"/> class.
    /// </summary>
    /// <param name="options">The bound indexer options.</param>
    /// <param name="logger">The logger used to report why the guard is disabled.</param>
    public InternalCallGuard(IOptions<IndexerOptions> options, ILogger<InternalCallGuard> logger)
    {
        _logger = logger;
        IndexerOptions values = options.Value;

        _expectedToken = BuildToken(values.Token);
        _allowedNetworks = BuildNetworks(values.AllowedNetworks);

        if (_expectedToken is null)
        {
            DisabledReason = string.IsNullOrWhiteSpace(values.Token)
                ? "No control-plane token is configured. Set the SCANNER_INDEXER_TOKEN environment variable, or write the token to the file named by SCANNER_INDEXER_TOKEN_FILE."
                : $"The configured control-plane token is shorter than {MinimumTokenLength} characters.";
        }
        else if (_allowedNetworks.Length == 0)
        {
            DisabledReason = "No usable network allow-list is configured.";
        }

        IsEnabled = DisabledReason is null;
    }

    /// <inheritdoc />
    public bool IsEnabled { get; }

    /// <inheritdoc />
    public string? DisabledReason { get; }

    /// <inheritdoc />
    public bool IsPermitted(IPAddress? remoteAddress, string? suppliedToken, out string? rejectionReason)
    {
        if (!IsEnabled)
        {
            rejectionReason = DisabledReason;
            return false;
        }

        if (remoteAddress is null)
        {
            rejectionReason = "The connection has no peer address.";
            return false;
        }

        // A dual-stack listener reports an IPv4 peer as an IPv4-mapped IPv6 address. Normalizing here means
        // an operator's "127.0.0.1/32" entry matches regardless of how the socket was opened.
        IPAddress peer = remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress;

        if (!IsAllowedNetwork(peer))
        {
            rejectionReason = "The peer address is not in the configured allow-list.";
            _logger.LogWarning("Refused a control-plane call from {Peer}: not in the allow-list.", peer);
            return false;
        }

        if (suppliedToken is null)
        {
            rejectionReason = "The control-plane token header is missing.";
            _logger.LogWarning("Refused a control-plane call from {Peer}: no token supplied.", peer);
            return false;
        }

        if (!IsTokenValid(suppliedToken))
        {
            rejectionReason = "The control-plane token is not valid.";
            _logger.LogWarning("Refused a control-plane call from {Peer}: invalid token.", peer);
            return false;
        }

        rejectionReason = null;
        return true;
    }

    /// <summary>
    /// Determines whether an address falls inside one of the allowed networks.
    /// </summary>
    /// <param name="address">The normalized peer address.</param>
    /// <returns><see langword="true"/> when the address is allowed.</returns>
    private bool IsAllowedNetwork(IPAddress address)
    {
        foreach (IPNetwork network in _allowedNetworks)
        {
            // Contains requires a matching address family; a mismatch is a non-match rather than an error.
            if (network.BaseAddress.AddressFamily != address.AddressFamily) continue;
            if (network.Contains(address)) return true;
        }

        return false;
    }

    /// <summary>
    /// Compares a supplied token with the configured one in fixed time.
    /// </summary>
    /// <param name="supplied">The token supplied by the caller.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    private bool IsTokenValid(string supplied)
    {
        if (_expectedToken is null) return false;

        byte[] candidate = Encoding.UTF8.GetBytes(supplied);

        // FixedTimeEquals short-circuits on a length difference, which reveals only the length of the
        // configured secret, never any of its content.
        return CryptographicOperations.FixedTimeEquals(candidate, _expectedToken);
    }

    /// <summary>
    /// Validates and encodes the configured token.
    /// </summary>
    /// <param name="configured">The configured token, which may be absent.</param>
    /// <returns>The token's raw bytes, or <see langword="null"/> when the token is absent or too short.</returns>
    private static byte[]? BuildToken(string? configured) =>
        string.IsNullOrWhiteSpace(configured) || configured.Length < MinimumTokenLength
            ? null
            : Encoding.UTF8.GetBytes(configured);

    /// <summary>
    /// Parses the configured network allow-list.
    /// </summary>
    /// <param name="configured">The semi-colon separated CIDR list, which may be absent.</param>
    /// <returns>
    /// The parsed networks, defaulting to loopback when nothing is configured, or an empty array when the
    /// configuration is present but unusable.
    /// </returns>
    /// <remarks>
    /// An empty result disables the control plane. That is the correct response to a malformed allow-list:
    /// silently falling back to a permissive default would turn a typo into an exposure.
    /// </remarks>
    private static IPNetwork[] BuildNetworks(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return [IPNetwork.Parse("127.0.0.1/32"), IPNetwork.Parse("::1/128")];
        }

        var networks = new List<IPNetwork>();

        foreach (string entry in configured.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPNetwork.TryParse(entry, out IPNetwork network)) networks.Add(network);
        }

        return networks.Count == 0 ? [] : networks.ToArray();
    }
}
