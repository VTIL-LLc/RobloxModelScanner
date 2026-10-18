namespace RMS.Configuration.Service.Exceptions;

/// <summary>
/// Thrown when a write's precondition does not hold: the file changed since it was read.
/// </summary>
/// <remarks>
/// <para>
/// This is the one refusal that is answered as a transport failure rather than as a result object, because there
/// is nothing to render: the text the caller sent is not in question, the state of the file is. The exception
/// carries the hash the file now has so that the answer can say which value would work, which turns "conflict"
/// into "reload and try again" without a second round trip.
/// </para>
/// <para>
/// It deliberately does not carry the file's contents. The site already has a way to read a manifest, and a
/// refusal body that echoed a manifest would put the same text in a log, in a browser's network tab and in
/// whatever the error path does with it -- three places a deployment description should not be.
/// </para>
/// </remarks>
public sealed class ManifestPreconditionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestPreconditionException"/> class.
    /// </summary>
    /// <param name="message">The sentence describing the refusal.</param>
    /// <param name="currentSha256">The SHA-256 the file now has.</param>
    public ManifestPreconditionException(string message, string currentSha256)
        : base(message)
    {
        CurrentSha256 = currentSha256;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestPreconditionException"/> class.
    /// </summary>
    /// <param name="message">The sentence describing the refusal.</param>
    /// <param name="currentSha256">The SHA-256 the file now has.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public ManifestPreconditionException(string message, string currentSha256, Exception innerException)
        : base(message, innerException)
    {
        CurrentSha256 = currentSha256;
    }

    /// <summary>
    /// Gets the SHA-256 of the file as it stands, which is the value the caller should send next time.
    /// </summary>
    public string CurrentSha256 { get; }
}
