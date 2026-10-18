using System;

namespace RMS.Site.Common.Models
{
    /// <summary>
    /// A message the site shows to whoever is looking at it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Notices are held in the database rather than written into a page so that the person who knows about the
    /// misclassification or the maintenance window can say so without a deployment. That is the whole reason
    /// the table exists: an announcement that requires a build is an announcement that arrives after the
    /// incident.
    /// </para>
    /// <para>
    /// A notice has a severity because the alert class matters. A warning that a rule is imprecise and a
    /// warning that a service is down are not the same message, and rendering them identically trains the
    /// reader to ignore both.
    /// </para>
    /// </remarks>
    public sealed class SiteNotice
    {
        /// <summary>
        /// Gets or sets the notice's identifier.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the severity, which selects the alert class: <c>info</c>, <c>warning</c> or
        /// <c>danger</c>.
        /// </summary>
        public string Severity { get; set; }

        /// <summary>
        /// Gets or sets the short heading, rendered in bold.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the body, rendered as plain text.
        /// </summary>
        /// <remarks>
        /// Plain text, not markup. A notice that could carry markup would be a notice that could carry script,
        /// and the administrator who may write one is not necessarily the person who would be held responsible
        /// for what it did.
        /// </remarks>
        public string Body { get; set; }

        /// <summary>
        /// Gets or sets the order the notices are shown in.
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the notice is shown.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets when the notice was created, in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets when the notice was last changed, in UTC.
        /// </summary>
        public DateTime UpdatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the name of the account that last changed the notice.
        /// </summary>
        public string UpdatedBy { get; set; }
    }
}
