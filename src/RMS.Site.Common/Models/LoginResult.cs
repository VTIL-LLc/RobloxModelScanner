using RMS.Site.Common.Enums;

namespace RMS.Site.Common.Models
{
    /// <summary>
    /// The result of verifying a set of credentials.
    /// </summary>
    /// <remarks>
    /// The account is carried alongside the outcome rather than instead of it, because a successful
    /// verification needs the account and a failed one still needs to know which account it was about, so that
    /// the failure count is incremented on the right row. Returning the account only on success would make the
    /// lockout counter impossible to maintain without a second lookup.
    /// </remarks>
    public sealed class LoginResult
    {
        /// <summary>
        /// Gets or sets how the attempt ended.
        /// </summary>
        public LoginOutcome Outcome { get; set; }

        /// <summary>
        /// Gets or sets the account the attempt concerned, when one was found.
        /// </summary>
        public SiteAccount SiteAccount { get; set; }

        /// <summary>
        /// Gets a value indicating whether the attempt succeeded.
        /// </summary>
        public bool IsSuccess
        {
            get { return Outcome == LoginOutcome.Success; }
        }

        /// <summary>
        /// Builds a result.
        /// </summary>
        /// <param name="outcome">How the attempt ended.</param>
        /// <param name="account">The account the attempt concerned, when one was found.</param>
        /// <returns>The result.</returns>
        public static LoginResult Of(LoginOutcome outcome, SiteAccount account = null)
        {
            return new LoginResult { Outcome = outcome, SiteAccount = account };
        }
    }
}
