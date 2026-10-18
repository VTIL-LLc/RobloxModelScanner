using Scanner.MicroApi.Enums;

namespace Scanner.MicroApi.Exceptions;

/// <summary>
/// Raised when the SQLite index cannot be read.
/// </summary>
/// <remarks>
/// <para>
/// This is an operational condition rather than a fault: the index is a separate artefact, produced by a
/// separate service, and a deployment can legitimately ask for catalogue data before that service has
/// run. The API answers it with a service-unavailable response naming the reason, which is why the
/// condition travels as its own type rather than as a bare <c>SqliteException</c> the endpoints would
/// have to distinguish by message text.
/// </para>
/// <para>
/// The message is written for an operator and contains no file contents, no SQL and no connection
/// string. It is safe to return to a caller; nothing else about the failure is.
/// </para>
/// </remarks>
/// <seealso cref="IndexAvailability"/>
public sealed class IndexUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IndexUnavailableException"/> class.
    /// </summary>
    /// <param name="availability">Why the index cannot be read.</param>
    /// <param name="message">The operator-facing explanation.</param>
    public IndexUnavailableException(IndexAvailability availability, string message)
        : base(message)
    {
        Availability = availability;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexUnavailableException"/> class.
    /// </summary>
    /// <param name="availability">Why the index cannot be read.</param>
    /// <param name="message">The operator-facing explanation.</param>
    /// <param name="innerException">The underlying failure.</param>
    public IndexUnavailableException(IndexAvailability availability, string message, Exception innerException)
        : base(message, innerException)
    {
        Availability = availability;
    }

    /// <summary>
    /// Gets the reason the index cannot be read.
    /// </summary>
    public IndexAvailability Availability { get; }
}
