using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using Scanner.MicroApi.Interfaces;
using Scanner.MicroApi.Options;

namespace Scanner.MicroApi.Implementation;

/// <summary>
/// Validates the caller-supplied API key against the configured GUID.
/// </summary>
/// <remarks>
/// <para>
/// The configured key is parsed into its sixteen raw bytes once, at construction. Each request then
/// parses the supplied value the same way and compares the two byte spans with
/// <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// </para>
/// <para>
/// Comparing the normalized binary form rather than the text means an attacker cannot exploit
/// differences in GUID formatting (braces, casing, the <c>D</c> versus <c>B</c> forms) to shorten a
/// guess, and the fixed-time comparison removes the timing signal that a naive string equality
/// would expose.
/// </para>
/// </remarks>
public sealed class ApiKeyValidator : IApiKeyValidator
{
    /// <summary>
    /// The raw bytes of the configured key, or <see langword="null"/> when no valid key is configured.
    /// </summary>
    private readonly byte[]? _expectedKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyValidator"/> class.
    /// </summary>
    /// <param name="options">The bound scanner options holding the configured key.</param>
    public ApiKeyValidator(IOptions<ScannerOptions> options)
    {
        string? configured = options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(configured) && Guid.TryParse(configured, out Guid expected))
        {
            _expectedKey = expected.ToByteArray();
        }
    }

    /// <inheritdoc />
    public bool IsConfigured => _expectedKey is not null;

    /// <inheritdoc />
    public bool IsAuthorized(string? suppliedKey)
    {
        // A deployment without a key rejects everything rather than allowing anonymous access.
        if (_expectedKey is null) return false;
        if (!Guid.TryParse(suppliedKey, out Guid supplied)) return false;

        Span<byte> candidate = stackalloc byte[16];
        supplied.TryWriteBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(candidate, _expectedKey);
    }
}
