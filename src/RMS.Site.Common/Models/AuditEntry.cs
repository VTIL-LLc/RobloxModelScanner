using System;

namespace RMS.Site.Common.Models
{
    /// <summary>
    /// One thing that happened, recorded for later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The audit trail is written for the events that changed something or that a person would later need to
    /// account for: sign-ins and their failures, account changes, notice changes. It is not a request log --
    /// that is what the HTTP server's own logging is for -- and the difference matters, because a table that
    /// recorded every page view would bury the four rows that matter under a million that do not.
    /// </para>
    /// <para>
    /// The address is recorded exactly as the connection reported it. Where the site runs behind a proxy that
    /// address is the proxy's, and it is recorded that way rather than replaced by a header the caller chose;
    /// a trail that can be written by the caller is not evidence.
    /// </para>
    /// </remarks>
    public sealed class AuditEntry
    {
        /// <summary>
        /// Gets or sets the entry's identifier.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets when the entry was written, in UTC.
        /// </summary>
        public DateTime WhenUtc { get; set; }

        /// <summary>
        /// Gets or sets the name of the account that acted, or an empty string when nobody was signed in.
        /// </summary>
        public string Actor { get; set; }

        /// <summary>
        /// Gets or sets the action, as a short keyword such as <c>SignIn</c> or <c>NoticeChanged</c>.
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// Gets or sets what the action was performed on.
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// Gets or sets the detail: what changed, or why it failed.
        /// </summary>
        public string Detail { get; set; }

        /// <summary>
        /// Gets or sets the address the request came from.
        /// </summary>
        public string Address { get; set; }
    }
}
