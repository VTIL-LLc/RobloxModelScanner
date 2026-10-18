namespace RMS.Site.Common.Models
{
    /// <summary>
    /// The identity a signed-in browser carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what travels inside the authentication ticket. It is deliberately a copy of the few facts a
    /// page needs rather than a reference to the account: a ticket that named an account would have to be
    /// re-read from the database on every request, and a ticket that carried the account's mutable state would
    /// keep showing a role that had since been revoked.
    /// </para>
    /// <para>
    /// The session is therefore revalidated against the store on every request, and what travels is only the
    /// identifier of the account. That costs one keyed read per request and buys revocation that takes effect
    /// immediately, which is the only kind that matters when the reason for revoking is that a credential has
    /// been lost.
    /// </para>
    /// </remarks>
    public sealed class UserSession
    {
        /// <summary>
        /// The role that may change what other people see.
        /// </summary>
        public const string AdministratorRole = "Administrator";

        /// <summary>
        /// The role that may only look.
        /// </summary>
        public const string ViewerRole = "Viewer";

        /// <summary>
        /// Gets or sets the identifier of the signed-in account.
        /// </summary>
        public long AccountId { get; set; }

        /// <summary>
        /// Gets or sets the name used to sign in.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Gets or sets the name shown to the operator.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the role, as it stood when the request began.
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Gets a value indicating whether the session may administer the site.
        /// </summary>
        public bool IsAdministrator
        {
            get { return string.Equals(Role, AdministratorRole, System.StringComparison.Ordinal); }
        }
    }
}
