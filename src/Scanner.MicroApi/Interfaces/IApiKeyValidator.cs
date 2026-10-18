namespace Scanner.MicroApi.Interfaces;

/// <summary>
/// Validates the <c>apiKey</c> query parameter supplied by callers.
/// </summary>
/// <remarks>
/// The comparison is performed on the raw 16 bytes of the parsed GUID using a fixed-time routine,
/// so the validator never leaks how many leading characters of a guess were correct. Because the
/// contract requires the key in the query string, deployments must still use HTTPS and must
/// configure their infrastructure not to log the parameter.
/// </remarks>
public interface IApiKeyValidator
{
    /// <summary>
    /// Gets a value indicating whether a valid key has been configured.
    /// </summary>
    /// <value>
    /// When <see langword="false"/> every authenticated endpoint rejects requests, which is the
    /// safe default for a deployment that started without a key.
    /// </value>
    bool IsConfigured { get; }

    /// <summary>
    /// Determines whether a supplied key matches the configured key.
    /// </summary>
    /// <param name="suppliedKey">The raw value of the <c>apiKey</c> query parameter.</param>
    /// <returns><see langword="true"/> only when the key is configured and the values are equal.</returns>
    bool IsAuthorized(string? suppliedKey);
}
