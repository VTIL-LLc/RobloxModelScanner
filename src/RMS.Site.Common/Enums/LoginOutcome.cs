namespace RMS.Site.Common.Enums
{
    /// <summary>
    /// How a sign-in attempt ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outcomes are distinguished so that the audit trail can record what actually happened without the
    /// page having to guess. That distinction is deliberately not shown to the person signing in: every
    /// failure is reported as one sentence, because a page that said "no such account" would answer the first
    /// question an attacker asks, and a page that said "wrong password" would confirm the account exists.
    /// </para>
    /// <para>
    /// The store therefore returns the truth and the page withholds it. Keeping the two apart is what makes
    /// it possible to investigate a lockout afterwards without having made the login page a directory of
    /// who has an account.
    /// </para>
    /// </remarks>
    public enum LoginOutcome
    {
        /// <summary>
        /// The credentials were accepted.
        /// </summary>
        Success,

        /// <summary>
        /// No account has that name.
        /// </summary>
        UnknownUser,

        /// <summary>
        /// The account exists and the password was wrong.
        /// </summary>
        WrongPassword,

        /// <summary>
        /// The account exists and has been disabled by an administrator.
        /// </summary>
        Disabled,

        /// <summary>
        /// The account is temporarily locked after too many failures.
        /// </summary>
        LockedOut,

        /// <summary>
        /// No account store is available, so nothing can be verified.
        /// </summary>
        /// <remarks>
        /// This outcome exists so that a misconfigured database reports itself as a misconfiguration. A store
        /// that answered <see cref="UnknownUser"/> when it could not open its file would be a store that
        /// silently refused every legitimate operator and told them their password was wrong.
        /// </remarks>
        Unavailable,
    }
}
