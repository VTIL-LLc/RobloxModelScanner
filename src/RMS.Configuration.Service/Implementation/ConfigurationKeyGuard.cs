using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RMS.Configuration.Service.Interfaces;
using RMS.Configuration.Service.Options;

namespace RMS.Configuration.Service.Implementation;

/// <summary>
/// Decides whether a caller may read or write the deployment's manifests.
/// </summary>
/// <remarks>
/// <para>
/// Three independent barriers, in this order:
/// </para>
/// <list type="number">
/// <item><description>
/// The socket. The listener is bound to a configured address, loopback by default, so the surface is not on the
/// network at all unless an operator deliberately puts it there.
/// </description></item>
/// <item><description>
/// A network allow-list, evaluated against the peer address of the connection and never against a forwarding
/// header. A forwarded header is written by the caller, so an allow-list that trusted one would be satisfied by
/// anyone who could type the right string.
/// </description></item>
/// <item><description>
/// A shared key, compared over its raw bytes in fixed time so that guessing cannot be narrowed by timing. A key
/// shorter than <see cref="MinimumKeyLength"/> characters disables the surface instead of being accepted.
/// </description></item>
/// </list>
/// <para>
/// The order matters. The allow-list is checked before the key, so a caller from an unexpected network is refused
/// without this service having looked at the secret it presented -- which means a wrong-network probe cannot be
/// used to confirm that a guessed key was right.
/// </para>
/// <para>
/// Nothing here logs the configured key, and no refusal reason contains it. A guard that reported the value it
/// expected would undo the reason for having one.
/// </para>
/// </remarks>
public sealed class ConfigurationKeyGuard : IConfigurationKeyGuard
{
    /// <summary>
    /// The header carrying the shared key.
    /// </summary>
    /// <remarks>
    /// A different header from the DevOps service's on purpose. Two services that accepted the same header would
    /// be two services that could be reached with one secret, and this one can write the deployment's description
    /// while that one can only read the deployment's state.
    /// </remarks>
    internal const string KeyHeaderName = "X-Configuration-Key";

    /// <summary>
    /// The shortest key this service will accept.
    /// </summary>
    /// <remarks>
    /// Thirty-two characters is short enough to be pasted from a secret manager and long enough that online
    /// guessing is hopeless. Anything shorter disables the surface, so a copy-paste accident cannot leave a
    /// trivially guessable secret in place.
    /// </remarks>
    private const int MinimumKeyLength = 32;

    private readonly byte[]? _expectedKey;
    private readonly IPNetwork[] _allowedNetworks;
    private readonly ILogger<ConfigurationKeyGuard> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationKeyGuard"/> class.
    /// </summary>
    /// <param name="options">The bound Configuration options.</param>
    /// <param name="logger">The logger used to report refusals and why the guard is disabled.</param>
    public ConfigurationKeyGuard(
        IOptions<ConfigurationOptions> options,
        ILogger<ConfigurationKeyGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        ConfigurationOptions values = options.Value;

        string? configuredKey = ResolveKey(values);

        _expectedKey = BuildKey(configuredKey);
        _allowedNetworks = BuildNetworks(values.AllowedNetworks);

        if (_expectedKey is null)
        {
            DisabledReason = string.IsNullOrWhiteSpace(configuredKey)
                ? $"No shared key is configured. Set the {ConfigurationOptions.TokenFileVariable} or "
                  + $"{ConfigurationOptions.TokenVariable} environment variable."
                : $"The configured key is shorter than {MinimumKeyLength} characters.";
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
    public bool IsPermitted(IPAddress? remoteAddress, string? suppliedKey, out string? rejectionReason)
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

        // A dual-stack listener reports an IPv4 peer as an IPv4-mapped IPv6 address. Normalizing here means an
        // operator's "127.0.0.1/32" entry matches regardless of how the socket was opened.
        IPAddress peer = remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress;

        if (!IsAllowedNetwork(peer))
        {
            rejectionReason = "The peer address is not in the configured allow-list.";
            _logger.LogWarning("Refused a Configuration call from {Peer}: not in the allow-list.", peer);
            return false;
        }

        if (suppliedKey is null)
        {
            rejectionReason = "The key header is missing.";
            _logger.LogWarning("Refused a Configuration call from {Peer}: no key supplied.", peer);
            return false;
        }

        if (!IsKeyValid(suppliedKey))
        {
            rejectionReason = "The supplied key is not valid.";
            _logger.LogWarning("Refused a Configuration call from {Peer}: invalid key.", peer);
            return false;
        }

        rejectionReason = null;
        return true;
    }

    /// <summary>
    /// Resolves the shared key from the file or the setting.
    /// </summary>
    /// <param name="values">The bound options.</param>
    /// <returns>The configured key, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// A configured file that cannot be read disables the surface rather than falling back to the setting. The
    /// fallback would be the wrong answer twice over: it would silently admit a weaker secret than the deployment
    /// intended, and it would hide the real fault -- a file the service cannot see -- behind a surface that
    /// appears to work.
    /// </remarks>
    private string? ResolveKey(ConfigurationOptions values)
    {
        if (!string.IsNullOrWhiteSpace(values.TokenFile))
        {
            try
            {
                string key = File.ReadAllText(values.TokenFile).Trim();

                _logger.LogInformation("The shared key was read from {File}.", values.TokenFile);

                return key;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(
                    exception,
                    "The shared key file {File} could not be read, so the surface stays disabled.",
                    values.TokenFile);

                return null;
            }
        }

        return values.Token;
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
    /// Compares a supplied key with the configured one in fixed time.
    /// </summary>
    /// <param name="supplied">The key the caller presented.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    private bool IsKeyValid(string supplied)
    {
        if (_expectedKey is null) return false;

        byte[] candidate = Encoding.UTF8.GetBytes(supplied);

        // FixedTimeEquals short-circuits on a length difference, which reveals only the length of the configured
        // secret, never any of its content.
        return CryptographicOperations.FixedTimeEquals(candidate, _expectedKey);
    }

    /// <summary>
    /// Validates and encodes the configured key.
    /// </summary>
    /// <param name="configured">The configured key, which may be absent.</param>
    /// <returns>The key's raw bytes, or <see langword="null"/> when the key is absent or too short.</returns>
    private static byte[]? BuildKey(string? configured) =>
        string.IsNullOrWhiteSpace(configured) || configured.Length < MinimumKeyLength
            ? null
            : Encoding.UTF8.GetBytes(configured);

    /// <summary>
    /// Parses the configured network allow-list.
    /// </summary>
    /// <param name="configured">The semicolon separated CIDR list, which may be absent.</param>
    /// <returns>
    /// The parsed networks, defaulting to loopback when nothing is configured, or an empty array when the
    /// configuration is present but unusable.
    /// </returns>
    /// <remarks>
    /// An empty result disables the surface. That is the correct response to a malformed allow-list: silently
    /// falling back to a permissive default would turn a typo into an exposure.
    /// </remarks>
    private static IPNetwork[] BuildNetworks(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return [IPNetwork.Parse("127.0.0.1/32"), IPNetwork.Parse("::1/128")];
        }

        List<IPNetwork> networks = [];

        foreach (string entry in configured.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPNetwork.TryParse(entry, out IPNetwork network)) networks.Add(network);
        }

        return networks.Count == 0 ? [] : [.. networks];
    }
}
