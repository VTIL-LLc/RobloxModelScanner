using System.Net;

namespace RMS.Site.Common.Models
{
    /// <summary>
    /// The outcome of one call to a service.
    /// </summary>
    /// <typeparam name="T">The shape the answer was read into.</typeparam>
    /// <remarks>
    /// <para>
    /// A result rather than an exception, because a service being unreachable, refusing this site, or having
    /// nothing to say are three different things an operator needs told apart, and all three are ordinary.
    /// An exception collapses them into one failure path, which is how a page ends up asserting that the
    /// service is down when the truth is that its key does not match.
    /// </para>
    /// <para>
    /// The status is carried so that a page can choose how loudly to say it. A refusal is worth an alert at
    /// the top of the page; an empty list is worth nothing at all.
    /// </para>
    /// </remarks>
    public sealed class ServiceResult<T>
        where T : class
    {
        /// <summary>
        /// Gets the value, when the call succeeded.
        /// </summary>
        public T Value { get; private set; }

        /// <summary>
        /// Gets the status the service answered with, or the status this site synthesised.
        /// </summary>
        public HttpStatusCode StatusCode { get; private set; }

        /// <summary>
        /// Gets the sentence explaining a failure.
        /// </summary>
        public string Error { get; private set; }

        /// <summary>
        /// Gets the word the service used for the failure, when it gave one, otherwise <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// A sentence is for a reader and a word is for the caller. Pages that only need to say what went
        /// wrong use the sentence; a caller that has to choose between several answers -- the download route
        /// is the only one today -- needs the word, because matching on the sentence would break the first
        /// time somebody improved the wording.
        /// </remarks>
        public string Reason { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the call succeeded.
        /// </summary>
        public bool IsSuccess { get; private set; }

        /// <summary>
        /// Builds a successful result.
        /// </summary>
        /// <param name="value">The value read.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<T> Success(T value)
        {
            return new ServiceResult<T>
            {
                Value = value,
                StatusCode = HttpStatusCode.OK,
                Error = null,
                IsSuccess = true,
            };
        }

        /// <summary>
        /// Builds a failed result.
        /// </summary>
        /// <param name="status">The status to report.</param>
        /// <param name="error">The sentence explaining the failure.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<T> Failure(HttpStatusCode status, string error)
        {
            return Failure(status, error, null);
        }

        /// <summary>
        /// Builds a failed result that carries the word the service used for the failure.
        /// </summary>
        /// <param name="status">The status to report.</param>
        /// <param name="error">The sentence explaining the failure.</param>
        /// <param name="reason">The word the service used, or <see langword="null"/> when it gave none.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<T> Failure(HttpStatusCode status, string error, string reason)
        {
            return new ServiceResult<T>
            {
                Value = null,
                StatusCode = status,
                Error = error ?? string.Empty,
                Reason = reason,
                IsSuccess = false,
            };
        }
    }
}
