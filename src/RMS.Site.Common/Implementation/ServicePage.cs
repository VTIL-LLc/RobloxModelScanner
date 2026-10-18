using System;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// The base class for a page that may only be read by a signed-in operator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every page of a console derives from this class, so the rule that the site is not readable without a
    /// session is stated once and applies everywhere. The alternative -- checking in each page -- is the
    /// arrangement in which a page added six months later is the one that forgot.
    /// </para>
    /// <para>
    /// The check happens before the page renders anything, and it redirects rather than reporting a refusal.
    /// A page that rendered "you are not signed in" would leave the reader looking at a form-less page with
    /// nothing to do, and would confirm to an outsider that the address exists.
    /// </para>
    /// </remarks>
    public abstract class ServicePage : SitePage
    {
        /// <summary>
        /// Gets or sets the sentence shown on the sign-in page explaining why the visitor was sent there.
        /// </summary>
        protected string SignInReason { get; set; }

        /// <summary>
        /// Reads a bounded text value from the query string.
        /// </summary>
        /// <param name="name">The parameter's name.</param>
        /// <param name="maximumLength">The longest value accepted.</param>
        /// <returns>The value, or an empty string.</returns>
        /// <remarks>
        /// <para>
        /// A value over the bound is shortened rather than refused, because every value read this way is a
        /// question rather than a document: a page number past the end of a list and a search phrase that ran long
        /// are both ordinary, and answering them with the shortened question is more useful than answering them
        /// with a fault.
        /// </para>
        /// <para>
        /// It is here rather than in each console's own base page because the administration console, which every
        /// console shares, is built on this class and reads the values it is given the same way every other page
        /// does. Four copies of a bound is four chances for one of them to be changed and the other three to keep
        /// the old number.
        /// </para>
        /// </remarks>
        protected string ReadQuery(string name, int maximumLength)
        {
            string raw = Request.QueryString[name];

            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string value = raw.Trim();

            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        /// <summary>
        /// Requires a signed-in operator.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The reason is written for the operator rather than for the log: it is what they read immediately
        /// after being bounced to the sign-in form, and it is the only thing telling them that their session
        /// ended rather than that they mistyped an address.
        /// </para>
        /// <para>
        /// A site that hides its operator pages from strangers answers instead as though the address were not
        /// here, and does so for every address it protects, so that the answer says nothing about which of them
        /// exist. See <see cref="SitePage.HidesFromAnonymous"/> for which sites do that and why.
        /// </para>
        /// </remarks>
        protected override void EnforceAccess()
        {
            if (IsSignedIn) return;

            if (HidesFromAnonymous)
            {
                RespondNotFound();
                return;
            }

            RedirectToSignIn("Sign in to read this.");
        }

        /// <summary>
        /// Requires an operator who may change what other people see.
        /// </summary>
        /// <param name="explanation">The sentence shown when the operator may not.</param>
        /// <remarks>
        /// This is called from the page's own load rather than from <see cref="EnforceAccess"/> because not
        /// every action of an administrative page needs the higher role: the pages that both show and change
        /// call it once they know which they are doing.
        /// </remarks>
        protected void RequireAdministrator(string explanation)
        {
            if (IsAdministrator) return;

            Store.Audit(
                "Refused",
                Request.Url == null ? string.Empty : Request.Url.AbsolutePath,
                "Administrator role required.",
                IsSignedIn ? CurrentUser.Username : string.Empty,
                ClientAddress);

            Response.Redirect("/?denied=" + Uri.EscapeDataString(explanation ?? string.Empty), endResponse: true);
        }
    }
}
