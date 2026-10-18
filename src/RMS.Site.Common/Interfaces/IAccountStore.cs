using System.Collections.Generic;

using RMS.Site.Common.Models;

namespace RMS.Site.Common.Interfaces
{
    /// <summary>
    /// The site's own database: who may sign in, what they did, and what the site is saying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One interface covers accounts, the audit trail and the notices because the three are one file and one
    /// schema. Splitting them into three interfaces would suggest three stores, and a deployment that pointed
    /// them at different files would produce a site whose audit trail described a different set of accounts
    /// from the one that signed in.
    /// </para>
    /// <para>
    /// Every method is expected to be safe to call from several requests at once. The implementation opens a
    /// connection per call rather than holding one open, because the alternative is a connection whose lock
    /// outlives the request that took it and a site that hangs while nobody is using it.
    /// </para>
    /// </remarks>
    public interface IAccountStore
    {
        /// <summary>
        /// Gets the path of the database the store is using.
        /// </summary>
        string DatabasePath { get; }

        /// <summary>
        /// Creates the schema and the first administrator when they are absent.
        /// </summary>
        /// <param name="bootstrapUsername">The name of the administrator to create when the store is empty.</param>
        /// <param name="bootstrapPassword">The password to give that administrator.</param>
        /// <returns>
        /// The password the bootstrap administrator was created with, or <see langword="null"/> when no
        /// account was created because one already existed.
        /// </returns>
        /// <remarks>
        /// The generated password is returned so that the caller can write it once to a file the operator can
        /// read, which is how a fresh deployment is entered for the first time. It is never logged and never
        /// rendered, and the account it belongs to is expected to be changed or removed immediately.
        /// </remarks>
        string EnsureCreated(string bootstrapUsername, string bootstrapPassword);

        /// <summary>
        /// Verifies a set of credentials, maintaining the failure count and the lockout.
        /// </summary>
        /// <param name="username">The name supplied.</param>
        /// <param name="password">The password supplied.</param>
        /// <returns>The outcome and, where one was found, the account.</returns>
        LoginResult Authenticate(string username, string password);

        /// <summary>
        /// Finds an account by name.
        /// </summary>
        /// <param name="username">The name to find.</param>
        /// <returns>The account, or <see langword="null"/>.</returns>
        SiteAccount Find(string username);

        /// <summary>
        /// Finds an account by identifier.
        /// </summary>
        /// <param name="accountId">The identifier to find.</param>
        /// <returns>The account, or <see langword="null"/>.</returns>
        SiteAccount FindById(long accountId);

        /// <summary>
        /// Lists the accounts, administrators first and then by name.
        /// </summary>
        /// <returns>The accounts.</returns>
        IReadOnlyList<SiteAccount> List();

        /// <summary>
        /// Creates an account or updates the one with the same name.
        /// </summary>
        /// <param name="username">The name used to sign in.</param>
        /// <param name="displayName">The name shown once signed in.</param>
        /// <param name="role">The role to grant.</param>
        /// <param name="password">The password to set, or <see langword="null"/> to leave the existing one.</param>
        /// <returns>The account as it stands after the call.</returns>
        SiteAccount Save(string username, string displayName, string role, string password);

        /// <summary>
        /// Enables or disables an account and clears its failure count.
        /// </summary>
        /// <param name="username">The account's name.</param>
        /// <param name="isEnabled">Whether the account may sign in.</param>
        /// <returns><see langword="true"/> when an account was changed.</returns>
        bool SetEnabled(string username, bool isEnabled);

        /// <summary>
        /// Removes an account.
        /// </summary>
        /// <param name="username">The account's name.</param>
        /// <returns><see langword="true"/> when an account was removed.</returns>
        bool Remove(string username);

        /// <summary>
        /// Counts the enabled administrators.
        /// </summary>
        /// <returns>The count.</returns>
        /// <remarks>
        /// This exists so that the last administrator cannot be removed or disabled. A site whose only
        /// administrator has been deleted is a site that has to be repaired on disk, and the check that
        /// prevents it belongs beside the write rather than in the page that happens to be the one to try.
        /// </remarks>
        int CountAdministrators();

        /// <summary>
        /// Writes an audit entry.
        /// </summary>
        /// <param name="action">The action, as a short keyword.</param>
        /// <param name="target">What the action was performed on.</param>
        /// <param name="detail">What changed, or why it failed.</param>
        /// <param name="actor">The account that acted, when one is known.</param>
        /// <param name="address">The address the request came from.</param>
        void Audit(string action, string target, string detail, string actor, string address);

        /// <summary>
        /// Lists the most recent audit entries, newest first.
        /// </summary>
        /// <param name="take">How many entries to return.</param>
        /// <returns>The entries.</returns>
        IReadOnlyList<AuditEntry> RecentAudit(int take);

        /// <summary>
        /// Lists the site's notices.
        /// </summary>
        /// <param name="includeInactive">Whether notices that are not shown should be included.</param>
        /// <returns>The notices, in the order they are presented.</returns>
        IReadOnlyList<SiteNotice> ListNotices(bool includeInactive);

        /// <summary>
        /// Creates or updates a notice.
        /// </summary>
        /// <param name="notice">The notice to write.</param>
        /// <param name="updatedBy">The account making the change.</param>
        /// <returns>The notice as it stands after the call.</returns>
        SiteNotice SaveNotice(SiteNotice notice, string updatedBy);

        /// <summary>
        /// Removes a notice.
        /// </summary>
        /// <param name="noticeId">The notice's identifier.</param>
        /// <returns><see langword="true"/> when a notice was removed.</returns>
        bool RemoveNotice(long noticeId);
    }
}
