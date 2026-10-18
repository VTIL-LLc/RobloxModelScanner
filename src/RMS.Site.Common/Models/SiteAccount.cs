using System;

namespace RMS.Site.Common.Models
{
    /// <summary>
    /// One operator of one RMS site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape a page may hold. The password material is not part of it and stays inside the store,
    /// which reads it only while it is verifying a password. A type that carried a hash would eventually be
    /// handed to a page, and a page that holds a hash is a page that can render it by mistake.
    /// </para>
    /// <para>
    /// Accounts are per site rather than shared across the stack. The alternative, one database several
    /// sites read, makes a broken deployment of one site an outage of the others' sign-in, and it makes the
    /// blast radius of a stolen account every console rather than one.
    /// </para>
    /// <para>
    /// The name is not <c>Account</c>, though that is what it is. A site keeps its sign-in pages in a folder
    /// called <c>Account</c> because that is where the framework looks for them, and a folder is a namespace, so
    /// the simple name <c>Account</c> inside the site's own namespace means the folder and not this type. One
    /// character's worth of ambiguity there is the difference between a compiler error that names the wrong
    /// thing and code that reads the way it looks.
    /// </para>
    /// </remarks>
    public sealed class SiteAccount
    {
        /// <summary>
        /// Gets or sets the account's identifier.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the name used to sign in.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Gets or sets the name shown once signed in.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the role, which decides what the account may change.
        /// </summary>
        /// <remarks>
        /// A single role rather than a set of permissions. The distinction that matters on these sites is
        /// between somebody who may change what other people see -- notices, rules, accounts -- and somebody
        /// who may only look, and a permission system richer than that would be more code to audit than it is
        /// access to grant.
        /// </remarks>
        public string Role { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the account may sign in.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Gets or sets when the account was created, in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets when the account last signed in, in UTC.
        /// </summary>
        public DateTime? LastLoginUtc { get; set; }

        /// <summary>
        /// Gets or sets how many consecutive failures have been recorded.
        /// </summary>
        public int FailedAttempts { get; set; }

        /// <summary>
        /// Gets or sets the moment the account unlocks, in UTC, when it is locked.
        /// </summary>
        public DateTime? LockedUntilUtc { get; set; }

        /// <summary>
        /// Gets a value indicating whether the account is locked right now.
        /// </summary>
        public bool IsLocked
        {
            get { return LockedUntilUtc.HasValue && LockedUntilUtc.Value > DateTime.UtcNow; }
        }

        /// <summary>
        /// Gets a value indicating whether the account may administer the site.
        /// </summary>
        public bool IsAdministrator
        {
            get { return string.Equals(Role, UserSession.AdministratorRole, StringComparison.Ordinal); }
        }
    }
}
