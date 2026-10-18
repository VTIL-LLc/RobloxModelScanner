using System;
using System.Globalization;
using System.Text;

using RMS.Site.Common.Enums;
using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace RMS.Configuration.Site.Account
{
    /// <summary>
    /// The sign-in form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only page in the console that anybody may reach, and it is deliberately uninformative. A
    /// failed attempt is reported as one sentence whatever went wrong -- no such account, wrong password,
    /// disabled, locked -- because the distinctions are exactly what somebody enumerating accounts is looking
    /// for. The truth is written to the audit trail, where an administrator can see it and a visitor cannot.
    /// </para>
    /// <para>
    /// The exception is a store that cannot be read at all. That is a fault on this machine rather than a fact
    /// about the caller, and telling the operator that the database is unavailable is how a misconfiguration
    /// gets found instead of being experienced as everybody's password having stopped working.
    /// </para>
    /// <para>
    /// The form posts to this page rather than to a handler, and the destination it returns to is validated
    /// before it is used. A sign-in page that redirected wherever the query said would be a way to send
    /// somebody to another site immediately after they had typed a password.
    /// </para>
    /// </remarks>
    public class Login : AnonymousPage
    {
        /// <summary>
        /// The name of the field the username is posted in.
        /// </summary>
        private const string UsernameField = "username";

        /// <summary>
        /// The name of the field the password is posted in.
        /// </summary>
        private const string PasswordField = "password";

        /// <summary>
        /// The name of the field the destination is carried in.
        /// </summary>
        private const string ReturnField = "returnUrl";

        /// <summary>
        /// The longest username accepted.
        /// </summary>
        private const int MaximumUsernameLength = 64;

        /// <summary>
        /// The longest password accepted.
        /// </summary>
        private const int MaximumPasswordLength = 512;

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Sign in — " + Settings.Title; }
        }

        /// <summary>
        /// Handles the request.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // A signed-in operator who reaches this page is sent on rather than shown a form they do not
            // need. That is the ordinary consequence of following a bookmark.
            if (IsSignedIn)
            {
                Response.Redirect("/", endResponse: true);
                return;
            }

            if (string.Equals(Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                HandleSignIn();
                return;
            }

            WriteDocument(PageTitle, Navigation(), Render(null, DestinationFromQuery()));
        }

        /// <summary>
        /// Verifies a posted set of credentials.
        /// </summary>
        private void HandleSignIn()
        {
            string destination = Destination(Request.Form[ReturnField]);

            if (!CsrfToken.Verify(Session, Request.Form[CsrfToken.FieldName]))
            {
                // The token is issued with the form and stored in the session. A post without it is either a
                // form that came from elsewhere or a page that sat open until the session lapsed; neither is
                // something a sign-in may proceed on.
                Store.Audit("Refused", "SignIn", "No anti-forgery token.", string.Empty, ClientAddress);

                WriteDocument(PageTitle, Navigation(), Render(Message("The form expired. Try again."), destination));
                return;
            }

            string username = Read(UsernameField, MaximumUsernameLength);
            string password = Read(PasswordField, MaximumPasswordLength);

            if (username.Length == 0 || password.Length == 0)
            {
                WriteDocument(
                    PageTitle,
                    Navigation(),
                    Render(Message("Enter a name and a password."), destination));

                return;
            }

            LoginResult result = Store.Authenticate(username, password);

            if (result.Outcome == LoginOutcome.Unavailable)
            {
                // A store that cannot be opened is this machine's fault. Reporting it as a failed password
                // would send every operator looking for the wrong cause.
                Store.Audit("Failed", "SignIn", "The account store is unavailable.", username, ClientAddress);

                WriteDocument(
                    PageTitle,
                    Navigation(),
                    Render(
                        Message("This console cannot read its account database, so nobody can sign in. The fault "
                                + "is recorded beside the database."),
                        destination));

                return;
            }

            if (!result.IsSuccess)
            {
                Store.Audit("Refused", "SignIn", result.Outcome.ToString(), username, ClientAddress);

                WriteDocument(
                    PageTitle,
                    Navigation(),
                    Render(Message("That name and password were not accepted."), destination));

                return;
            }

            SignIn(result.SiteAccount);

            Store.Audit("SignedIn", "SiteAccount", result.SiteAccount.Username, result.SiteAccount.Username, ClientAddress);

            Response.Redirect(destination, endResponse: true);
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="problem">The message shown above the form, or <see langword="null"/>.</param>
        /// <param name="destination">The destination the form carries forward.</param>
        /// <returns>The markup.</returns>
        private string Render(string problem, string destination)
        {
            StringBuilder builder = new StringBuilder();

            string reason = ReadQuery("reason", 256);

            if (reason.Length > 0)
            {
                builder.Append("<p class=\"text-muted\">").Append(Html.Encode(reason)).Append("</p>");
            }

            if (problem != null) builder.Append(problem);

            builder.Append("<form method=\"post\" action=\"/Account/Login.aspx\" class=\"signin-form\">");
            builder.Append(CsrfToken.RenderHidden(Session));
            builder.Append("<input type=\"hidden\" name=\"").Append(Html.Attribute(ReturnField)).Append("\" value=\"")
                   .Append(Html.Attribute(destination)).Append("\" />");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label for=\"signin-username\" class=\"control-label\">Name</label>");
            builder.Append("<input class=\"form-control\" type=\"text\" id=\"signin-username\" name=\"")
                   .Append(Html.Attribute(UsernameField))
                   .Append("\" maxlength=\"").Append(MaximumUsernameLength.ToString(CultureInfo.InvariantCulture))
                   .Append("\" autocomplete=\"username\" autofocus=\"autofocus\" required=\"required\" />");
            builder.Append("</div>");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label for=\"signin-password\" class=\"control-label\">Password</label>");
            builder.Append("<input class=\"form-control\" type=\"password\" id=\"signin-password\" name=\"")
                   .Append(Html.Attribute(PasswordField))
                   .Append("\" maxlength=\"").Append(MaximumPasswordLength.ToString(CultureInfo.InvariantCulture))
                   .Append("\" autocomplete=\"current-password\" required=\"required\" />");
            builder.Append("</div>");

            builder.Append("<button type=\"submit\" class=\"btn btn-primary btn-block\">Sign in</button>");
            builder.Append("</form>");

            return RenderCard("Sign in", builder.ToString());
        }

        /// <summary>
        /// Renders a problem as an alert.
        /// </summary>
        /// <param name="message">The sentence.</param>
        /// <returns>The markup.</returns>
        private static string Message(string message)
        {
            return Html.Alert("warning", message, string.Empty);
        }

        /// <summary>
        /// Reads the destination from the query string.
        /// </summary>
        /// <returns>The destination.</returns>
        private string DestinationFromQuery()
        {
            return Destination(Request.QueryString[ReturnField]);
        }

        /// <summary>
        /// Validates a destination, falling back to the site's root.
        /// </summary>
        /// <param name="requested">The requested destination.</param>
        /// <returns>A destination inside this application.</returns>
        /// <remarks>
        /// Only a path on this site is accepted. An absolute address, a protocol-relative address, or anything
        /// carrying a colon is refused, because all three are ways to leave this site -- and leaving it
        /// immediately after a password was typed is what makes a sign-in page useful to somebody else.
        /// </remarks>
        private static string Destination(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested)) return "/";

            string value = requested.Trim();

            if (!value.StartsWith("/", StringComparison.Ordinal)) return "/";
            if (value.StartsWith("//", StringComparison.Ordinal)) return "/";
            if (value.IndexOf(':') >= 0) return "/";
            if (value.IndexOf('\\') >= 0) return "/";

            return value.Length <= 1024 ? value : "/";
        }

        /// <summary>
        /// Reads a bounded value from the posted form.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="maximumLength">The longest value accepted.</param>
        /// <returns>The value, or an empty string.</returns>
        private string Read(string name, int maximumLength)
        {
            string raw = Request.Form[name];

            if (string.IsNullOrEmpty(raw)) return string.Empty;

            return raw.Length <= maximumLength ? raw : raw.Substring(0, maximumLength);
        }

        /// <summary>
        /// Reads a bounded value from the query string.
        /// </summary>
        /// <param name="name">The parameter's name.</param>
        /// <param name="maximumLength">The longest value accepted.</param>
        /// <returns>The value, or an empty string.</returns>
        private string ReadQuery(string name, int maximumLength)
        {
            string raw = Request.QueryString[name];

            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string value = raw.Trim();

            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }
    }
}
