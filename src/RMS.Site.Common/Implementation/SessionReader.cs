using System;
using System.Globalization;
using System.Web;
using System.Web.Security;

using RMS.Site.Common.Interfaces;
using RMS.Site.Common.Models;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// Reads, issues and clears the ticket that says who is signed in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pages and fragment handlers both have to answer the same question before they do anything, and this is
    /// the answer. It is one class rather than a method on each because the rule it applies is a security
    /// rule: a ticket naming an account that has since been disabled must stop working, and a handler whose
    /// copy of that rule had drifted from the page's copy would be the way an authorization check is lost.
    /// </para>
    /// <para>
    /// The ticket is encrypted with the machine key, so a browser cannot read the account identifier out of it
    /// and cannot mint one. It is not signed with anything this application holds, which is deliberate: the
    /// key belongs to the host, so a ticket issued by one application cannot be presented to another, and
    /// rotating it is a deployment operation rather than a code change.
    /// </para>
    /// </remarks>
    public static class SessionReader
    {
        /// <summary>
        /// Reads the identity a request carries.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="store">The store the account is re-read from.</param>
        /// <returns>The identity, or <see langword="null"/> when the request is anonymous.</returns>
        public static UserSession Read(HttpContext context, IAccountStore store)
        {
            if (context == null || store == null) return null;

            HttpCookie cookie = context.Request.Cookies[FormsAuthentication.FormsCookieName];

            if (cookie == null || string.IsNullOrEmpty(cookie.Value)) return null;

            FormsAuthenticationTicket ticket;

            try
            {
                ticket = FormsAuthentication.Decrypt(cookie.Value);
            }
            catch (ArgumentException)
            {
                // A cookie that does not decrypt is a cookie that was written by something else, or was
                // truncated, or was tampered with. None of those is worth distinguishing to the caller.
                return null;
            }

            if (ticket == null || ticket.Expired || string.IsNullOrWhiteSpace(ticket.UserData)) return null;

            long accountId;

            if (!long.TryParse(ticket.UserData, NumberStyles.Integer, CultureInfo.InvariantCulture, out accountId))
            {
                return null;
            }

            // The account is read on every request rather than trusted from the ticket. That is one keyed
            // read against a local file, and it is what makes disabling an account take effect on the next
            // click instead of whenever the cookie happens to lapse.
            SiteAccount account = store.FindById(accountId);

            if (account == null || !account.IsEnabled) return null;

            return new UserSession
            {
                AccountId = account.Id,
                Username = account.Username,
                DisplayName = account.DisplayName,
                Role = account.Role,
            };
        }

        /// <summary>
        /// Issues the ticket for an account.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="account">The account that has just signed in.</param>
        /// <param name="sessionMinutes">How long the session lasts.</param>
        /// <param name="secure">Whether the cookie requires a secure channel.</param>
        public static void Issue(HttpContext context, SiteAccount account, int sessionMinutes, bool secure)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (account == null) throw new ArgumentNullException(nameof(account));

            DateTime issued = DateTime.UtcNow;
            DateTime expires = issued.AddMinutes(sessionMinutes);

            FormsAuthenticationTicket ticket = new FormsAuthenticationTicket(
                2,
                account.Username,
                issued,
                expires,
                isPersistent: false,
                userData: account.Id.ToString(CultureInfo.InvariantCulture),
                cookiePath: FormsAuthentication.FormsCookiePath);

            HttpCookie cookie = new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket))
            {
                HttpOnly = true,
                Secure = secure,
                Path = FormsAuthentication.FormsCookiePath,
                Expires = expires,
                SameSite = SameSiteMode.Lax,
            };

            context.Response.Cookies.Add(cookie);
        }

        /// <summary>
        /// Expires the ticket.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="secure">Whether the cookie requires a secure channel.</param>
        public static void Clear(HttpContext context, bool secure)
        {
            if (context == null) return;

            HttpCookie expired = new HttpCookie(FormsAuthentication.FormsCookieName, string.Empty)
            {
                HttpOnly = true,
                Secure = secure,
                Path = FormsAuthentication.FormsCookiePath,
                Expires = DateTime.UtcNow.AddYears(-1),
            };

            context.Response.Cookies.Add(expired);
        }
    }
}
