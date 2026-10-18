using System;
using System.Text;

using RMS.Site.Common.Implementation;

namespace RMS.Configuration.Site.Account
{
    /// <summary>
    /// Ends the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Signing out is a change, so it is made by a post carrying the session's anti-forgery token. A sign-out
    /// that happened on any request would be a sign-out that any page on any site could trigger by loading an
    /// image from this one, which is a nuisance for the operator and a way of hiding activity from the audit
    /// trail.
    /// </para>
    /// <para>
    /// The confirmation this page shows is not ceremony: it exists because the alternative is a link in the
    /// navigation that signs the operator out when their cursor passes over it and the mouse button is held.
    /// </para>
    /// </remarks>
    public class Logout : AnonymousPage
    {
        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Sign out — " + Settings.Title; }
        }

        /// <summary>
        /// Handles the request.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (!IsSignedIn)
            {
                Response.Redirect("/Account/Login.aspx", endResponse: true);
                return;
            }

            if (string.Equals(Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
                && CsrfToken.Verify(Session, Request.Form[CsrfToken.FieldName]))
            {
                string who = CurrentUser.Username;

                SignOut();

                Store.Audit("SignedOut", "SiteAccount", who, who, ClientAddress);

                Response.Redirect(
                    "/Account/Login.aspx?reason=" + Uri.EscapeDataString("You have been signed out."),
                    endResponse: true);

                return;
            }

            WriteDocument(PageTitle, Navigation(), RenderCard("Sign out", RenderConfirm()));
        }

        /// <summary>
        /// Renders the confirmation.
        /// </summary>
        /// <returns>The markup.</returns>
        private string RenderConfirm()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<p>You are signed in as <strong>")
                   .Append(Html.Encode(CurrentUser.DisplayName))
                   .Append("</strong>.</p>");

            builder.Append("<form method=\"post\" action=\"/Account/Logout.aspx\" class=\"signin-form\">");
            builder.Append(CsrfToken.RenderHidden(Session));
            builder.Append("<button type=\"submit\" class=\"btn btn-primary btn-block\">Sign out</button>");
            builder.Append("</form>");

            builder.Append("<p><a href=\"/\">Back to the console</a></p>");

            return builder.ToString();
        }
    }
}
