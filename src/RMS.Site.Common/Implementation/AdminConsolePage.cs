using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web;

using RMS.Site.Common.Models;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// The administration console, shared by every console in the stack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things are administrable here: who may sign in, and what the site is saying. Both are held in a
    /// site's own database, which is why neither of them requires a deployment.
    /// </para>
    /// <para>
    /// Every change is a form post that ends in a redirect. The redirect is not decoration: without it, a
    /// refresh would post the change again, and an operator who reloaded the page after adding an account
    /// would add it twice -- or, worse, would be told that it already existed and would believe the first
    /// attempt had failed.
    /// </para>
    /// <para>
    /// Every form carries the session's anti-forgery token, and every action is checked for the administrator
    /// role before it runs. The role check is here rather than only in the navigation, because a hidden link
    /// is not a permission.
    /// </para>
    /// <para>
    /// It is one class rather than four because it is the same console on every site. What a site is for does
    /// not change who may sign in to it, what it is announcing to its readers, or the audits it keeps, and four
    /// copies of those several hundred lines are four chances for one of them to be corrected and the other
    /// three to keep the fault. What does differ between the sites is their navigation, and this class does not
    /// name it: it asks the base page, and each site's own page class is where the answer is written.
    /// </para>
    /// <para>
    /// A site reaches this through a subclass that declares nothing but its base class. The subclass is not
    /// redundant: it is what the page's own directive names, what the site's folder owns, and the place a site
    /// would put a difference if it ever had one.
    /// </para>
    /// </remarks>
    public abstract class AdminConsolePage : ServicePage
    {
        /// <summary>
        /// The longest password accepted.
        /// </summary>
        /// <remarks>
        /// A ceiling rather than a rule about what a password must contain. Length is the property that matters
        /// and the only one this site can measure; the ceiling exists so that a pasted megabyte cannot be used
        /// to make the key derivation expensive for everybody else.
        /// </remarks>
        private const int MaximumPasswordLength = 512;

        /// <summary>
        /// The shortest password accepted.
        /// </summary>
        private const int MinimumPasswordLength = 12;

        /// <summary>
        /// The longest display name accepted.
        /// </summary>
        private const int MaximumDisplayNameLength = 64;

        /// <summary>
        /// The longest notice title accepted.
        /// </summary>
        private const int MaximumTitleLength = 120;

        /// <summary>
        /// The longest notice body accepted.
        /// </summary>
        private const int MaximumBodyLength = 4000;

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Administration — " + Settings.Title; }
        }

        /// <summary>
        /// Handles the request.
        /// </summary>
        /// <param name="e">The event data.</param>
        /// <remarks>
        /// The role is required for the whole page and not only for the posts that change something. A viewer
        /// who could read this page would be reading the account list, the audit trail and the addresses that
        /// have failed to sign in, which is more than the role grants.
        /// </remarks>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            RequireAdministrator("Administration is only available to an administrator.");

            if (string.Equals(Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                HandlePost();
                return;
            }

            WriteDocument(PageTitle, Navigation(), RenderPage(RenderStatus()));
        }

        /// <summary>
        /// Applies a posted change and redirects back to the page.
        /// </summary>
        private void HandlePost()
        {
            if (!CsrfToken.Verify(Session, Request.Form[CsrfToken.FieldName]))
            {
                // A post without the token is either a form that came from somewhere else or a session that
                // has lapsed between rendering the page and pressing the button. Both are refusals, and the
                // difference is not one this page can act on.
                Store.Audit(
                    "Refused",
                    "/Admin.aspx",
                    "A change was posted without a valid anti-forgery token.",
                    IsSignedIn ? CurrentUser.Username : string.Empty,
                    ClientAddress);

                Redirect("token");
            }

            string action = ReadPost("action", 32);

            switch (action)
            {
                case "notice.save": Redirect(SaveNotice()); break;
                case "notice.remove": Redirect(RemoveNotice()); break;
                case "account.save": Redirect(SaveAccount()); break;
                case "account.enable": Redirect(SetAccountEnabled()); break;
                case "account.remove": Redirect(RemoveAccount()); break;
                default: Redirect("unknown"); break;
            }
        }

        /// <summary>
        /// Writes or updates a notice.
        /// </summary>
        /// <returns>The status code the redirect carries.</returns>
        private string SaveNotice()
        {
            long id;

            if (!long.TryParse(ReadPost("id", 20), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) id = 0;

            string severity = ReadPost("severity", 16);
            string title = ReadPost("title", MaximumTitleLength);
            string body = ReadPost("body", MaximumBodyLength);

            if (title.Length == 0)
            {
                return "notice-notitle";
            }

            if (severity != "warning" && severity != "danger") severity = "info";

            int sortOrder;

            if (!int.TryParse(ReadPost("sort", 6), NumberStyles.Integer, CultureInfo.InvariantCulture, out sortOrder))
            {
                sortOrder = 0;
            }

            SiteNotice existing = id > 0 ? FindNotice(id) : null;

            SiteNotice notice = new SiteNotice
            {
                Id = id,
                Severity = severity,
                Title = title,
                Body = body,
                SortOrder = sortOrder,
                IsActive = ReadPost("active", 8).Length > 0,
                CreatedUtc = existing == null ? DateTime.UtcNow : existing.CreatedUtc,
                UpdatedUtc = DateTime.UtcNow,
                UpdatedBy = CurrentUser.Username,
            };

            Store.SaveNotice(notice, CurrentUser.Username);

            InvalidateNotices();

            Store.Audit("Saved", "Notice", title, CurrentUser.Username, ClientAddress);

            return "notice-saved";
        }

        /// <summary>
        /// Removes a notice.
        /// </summary>
        /// <returns>The status code the redirect carries.</returns>
        private string RemoveNotice()
        {
            long id;

            if (!long.TryParse(ReadPost("id", 20), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return "notice-missing";
            }

            SiteNotice existing = FindNotice(id);

            if (existing == null) return "notice-missing";

            Store.RemoveNotice(id);

            InvalidateNotices();

            Store.Audit("Removed", "Notice", existing.Title, CurrentUser.Username, ClientAddress);

            return "notice-removed";
        }

        /// <summary>
        /// Creates an account or changes the one with the same name.
        /// </summary>
        /// <returns>The status code the redirect carries.</returns>
        private string SaveAccount()
        {
            string username = ReadPost("username", 64);
            string displayName = ReadPost("displayname", MaximumDisplayNameLength);
            string role = ReadPost("role", 32);
            string password = ReadPost("password", MaximumPasswordLength);

            if (!IsUsername(username)) return "account-name";

            if (role != UserSession.AdministratorRole) role = UserSession.ViewerRole;

            bool creating = Store.Find(username) == null;

            if (creating && password.Length == 0) return "account-password";

            if (password.Length > 0 && (password.Length < MinimumPasswordLength || password.Length > MaximumPasswordLength))
            {
                return "account-password";
            }

            if (displayName.Length == 0) displayName = username;

            try
            {
                Store.Save(username, displayName, role, password.Length == 0 ? null : password);
            }
            catch (InvalidOperationException)
            {
                // The store refuses a change that would leave the site with no enabled administrator. That is
                // the one rule it enforces itself, because it is the rule whose violation cannot be repaired
                // from inside the application.
                return "account-lastadmin";
            }

            Store.Audit(
                creating ? "Created" : "Saved",
                "SiteAccount",
                username + " (" + role + ")",
                CurrentUser.Username,
                ClientAddress);

            return creating ? "account-created" : "account-saved";
        }

        /// <summary>
        /// Enables or disables an account.
        /// </summary>
        /// <returns>The status code the redirect carries.</returns>
        private string SetAccountEnabled()
        {
            string username = ReadPost("username", 64);

            if (!IsUsername(username)) return "account-name";

            bool enable = ReadPost("enabled", 8).Length > 0;

            try
            {
                if (!Store.SetEnabled(username, enable)) return "account-missing";
            }
            catch (InvalidOperationException)
            {
                return "account-lastadmin";
            }

            Store.Audit(
                enable ? "Enabled" : "Disabled",
                "SiteAccount",
                username,
                CurrentUser.Username,
                ClientAddress);

            return enable ? "account-enabled" : "account-disabled";
        }

        /// <summary>
        /// Removes an account.
        /// </summary>
        /// <returns>The status code the redirect carries.</returns>
        private string RemoveAccount()
        {
            string username = ReadPost("username", 64);

            if (!IsUsername(username)) return "account-name";

            // An operator removing their own account would end their session at the next click, which looks
            // like the site having broken. The refusal explains itself rather than doing it and being
            // confusing.
            if (string.Equals(username, CurrentUser.Username, StringComparison.OrdinalIgnoreCase))
            {
                return "account-self";
            }

            try
            {
                if (!Store.Remove(username)) return "account-missing";
            }
            catch (InvalidOperationException)
            {
                return "account-lastadmin";
            }

            Store.Audit("Removed", "SiteAccount", username, CurrentUser.Username, ClientAddress);

            return "account-removed";
        }

        /// <summary>
        /// Builds the page.
        /// </summary>
        /// <param name="status">The notice shown at the top, which is empty when there is nothing to say.</param>
        /// <returns>The markup.</returns>
        private string RenderPage(string status)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "Administration",
                "Accounts for this console, and the notices it shows at the top of every page."));

            builder.Append(status);

            builder.Append(Html.Panel("Accounts", RenderAccounts()));
            builder.Append(Html.Panel("Notices", RenderNoticeList()));
            builder.Append(Html.Panel("Recent activity", RenderAudit()));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the account list and the form that adds one.
        /// </summary>
        /// <returns>The markup.</returns>
        private string RenderAccounts()
        {
            IReadOnlyList<SiteAccount> accounts = Store.List();

            StringBuilder builder = new StringBuilder();

            builder.Append("<p class=\"text-muted\">Each account belongs to this console alone. A password must be "
                           + "at least " + MinimumPasswordLength.ToString(CultureInfo.InvariantCulture)
                           + " characters; there are no other rules about what it may contain, because a rule "
                           + "about composition makes passwords easier to guess rather than harder.</p>");

            foreach (SiteAccount account in accounts)
            {
                builder.Append(RenderAccount(account));
            }

            builder.Append("<h4>Add an account</h4>");
            builder.Append(AccountForm(
                username: string.Empty,
                displayName: string.Empty,
                role: UserSession.ViewerRole,
                isNew: true,
                isEnabled: true,
                isSelf: false));

            return builder.ToString();
        }

        /// <summary>
        /// Renders one account as a form.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <returns>The markup.</returns>
        private string RenderAccount(SiteAccount account)
        {
            bool isSelf = string.Equals(account.Username, CurrentUser.Username, StringComparison.OrdinalIgnoreCase);

            StringBuilder builder = new StringBuilder();

            builder.Append("<h4>").Append(Html.Encode(account.Username));

            if (isSelf) builder.Append(" <span class=\"text-muted\">(this is you)</span>");
            if (!account.IsEnabled) builder.Append(" ").Append(Html.Badge("disabled", "default", null));
            if (account.IsLocked) builder.Append(" ").Append(Html.Badge("locked", "warning", "Too many failed sign-ins."));

            builder.Append("</h4>");

            builder.Append("<p class=\"text-muted\">Created ")
                   .Append(Html.Encode(Html.Timestamp(new DateTimeOffset(DateTime.SpecifyKind(account.CreatedUtc, DateTimeKind.Utc)))))
                   .Append(". Last signed in ")
                   .Append(account.LastLoginUtc.HasValue
                       ? Html.Encode(Html.Timestamp(new DateTimeOffset(DateTime.SpecifyKind(account.LastLoginUtc.Value, DateTimeKind.Utc))))
                       : "never")
                   .Append("</p>");

            builder.Append(AccountForm(
                account.Username,
                account.DisplayName,
                account.Role,
                isNew: false,
                isEnabled: account.IsEnabled,
                isSelf: isSelf));

            return builder.ToString();
        }

        /// <summary>
        /// Renders the form that creates or changes one account.
        /// </summary>
        /// <param name="username">The account's name, or an empty string when a new one is being added.</param>
        /// <param name="displayName">The name shown to the operator.</param>
        /// <param name="role">The role.</param>
        /// <param name="isNew">Whether the form creates an account rather than changing one.</param>
        /// <param name="isEnabled">Whether the account may sign in.</param>
        /// <param name="isSelf">Whether this is the account making the change.</param>
        /// <returns>The markup.</returns>
        private string AccountForm(
            string username,
            string displayName,
            string role,
            bool isNew,
            bool isEnabled,
            bool isSelf)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<form method=\"post\" action=\"/Admin.aspx\" class=\"form-horizontal account-form\">");
            builder.Append(CsrfToken.RenderHidden(Session));
            builder.Append("<input type=\"hidden\" name=\"action\" value=\"account.save\" />");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label class=\"col-sm-2 control-label\" for=\"username-").Append(Html.Attribute(Key(username)))
                   .Append("\">Name</label><div class=\"col-sm-4\">");

            if (isNew)
            {
                builder.Append("<input class=\"form-control input-sm\" type=\"text\" id=\"username-")
                       .Append(Html.Attribute(Key(username)))
                       .Append("\" name=\"username\" required=\"required\" maxlength=\"64\" autocomplete=\"off\" />");
            }
            else
            {
                builder.Append("<p class=\"form-control-static\"><span class=\"code\">")
                       .Append(Html.Encode(username))
                       .Append("</span></p><input type=\"hidden\" name=\"username\" value=\"")
                       .Append(Html.Attribute(username))
                       .Append("\" />");
            }

            builder.Append("</div>");

            builder.Append("<label class=\"col-sm-2 control-label\" for=\"displayname-")
                   .Append(Html.Attribute(Key(username)))
                   .Append("\">Display name</label><div class=\"col-sm-4\">");

            builder.Append("<input class=\"form-control input-sm\" type=\"text\" id=\"displayname-")
                   .Append(Html.Attribute(Key(username)))
                   .Append("\" name=\"displayname\" maxlength=\"")
                   .Append(MaximumDisplayNameLength.ToString(CultureInfo.InvariantCulture))
                   .Append("\" value=\"").Append(Html.Attribute(displayName ?? string.Empty))
                   .Append("\" autocomplete=\"off\" />");

            builder.Append("</div></div>");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label class=\"col-sm-2 control-label\" for=\"role-").Append(Html.Attribute(Key(username)))
                   .Append("\">Role</label><div class=\"col-sm-4\">");
            builder.Append("<select class=\"form-control input-sm\" id=\"role-").Append(Html.Attribute(Key(username)))
                   .Append("\" name=\"role\">");
            builder.Append(RoleOption(UserSession.ViewerRole, "Viewer — may look at everything", role));
            builder.Append(RoleOption(UserSession.AdministratorRole, "Administrator — may change what others see", role));
            builder.Append("</select></div>");

            builder.Append("<label class=\"col-sm-2 control-label\" for=\"password-")
                   .Append(Html.Attribute(Key(username)))
                   .Append("\">Password</label><div class=\"col-sm-4\">");
            builder.Append("<input class=\"form-control input-sm\" type=\"password\" id=\"password-")
                   .Append(Html.Attribute(Key(username)))
                   .Append("\" name=\"password\" autocomplete=\"new-password\" maxlength=\"")
                   .Append(MaximumPasswordLength.ToString(CultureInfo.InvariantCulture))
                   .Append("\" placeholder=\"")
                   .Append(isNew ? "required" : "leave empty to keep the current one")
                   .Append("\" />");
            builder.Append("</div></div>");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<div class=\"col-sm-offset-2 col-sm-10\">");
            builder.Append("<button type=\"submit\" class=\"btn btn-primary btn-sm\">")
                   .Append(isNew ? "Add the account" : "Save")
                   .Append("</button>");
            builder.Append("</div></div>");

            builder.Append("</form>");

            // The enable control and the removal are separate forms rather than more buttons on the one above,
            // because they carry different consequences: disabling ends somebody's access, and removing deletes
            // the row. Keeping them apart is what makes the button that was pressed the one that was meant.
            if (!isNew)
            {
                builder.Append(EnableForm(username, isEnabled, isSelf));
                builder.Append(RemoveForm(username, isSelf));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders the form that enables or disables an account.
        /// </summary>
        /// <param name="username">The account's name.</param>
        /// <param name="isEnabled">Whether the account may sign in.</param>
        /// <param name="isSelf">Whether this is the account making the change.</param>
        /// <returns>The markup.</returns>
        private string EnableForm(string username, bool isEnabled, bool isSelf)
        {
            return PostForm(
                "account.enable",
                "<input type=\"hidden\" name=\"username\" value=\"" + Html.Attribute(username) + "\" />"
                + "<input type=\"hidden\" name=\"enabled\" value=\"" + (isEnabled ? string.Empty : "1") + "\" />"
                + "<button type=\"submit\" class=\"btn btn-default btn-sm\""
                + (isSelf && isEnabled
                    ? " title=\"Disabling the account you are signed in with ends this session at the next click.\""
                    : string.Empty)
                + ">" + (isEnabled ? "Disable" : "Enable") + "</button>");
        }

        /// <summary>
        /// Renders the form that removes an account.
        /// </summary>
        /// <param name="username">The account's name.</param>
        /// <param name="isSelf">Whether this is the account making the change.</param>
        /// <returns>The markup.</returns>
        private string RemoveForm(string username, bool isSelf)
        {
            if (isSelf) return string.Empty;

            return PostForm(
                "account.remove",
                "<input type=\"hidden\" name=\"username\" value=\"" + Html.Attribute(username) + "\" />"
                + "<button type=\"submit\" class=\"btn btn-danger btn-sm\">Remove</button>");
        }

        /// <summary>
        /// Renders the notice list and the form that adds one.
        /// </summary>
        /// <returns>The markup.</returns>
        private string RenderNoticeList()
        {
            IReadOnlyList<SiteNotice> notices = Store.ListNotices(includeInactive: true);

            StringBuilder builder = new StringBuilder();

            builder.Append("<p class=\"text-muted\">A notice appears at the top of every page of this site, to "
                           + "everybody who can read it. It is the way to say that a rule is imprecise or that "
                           + "something is being changed, without a deployment.</p>");

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("Notice"))
                   .Append(Html.Head("Severity"))
                   .Append(Html.Head("Order", "col-number"))
                   .Append(Html.Head("Shown"))
                   .Append(Html.Head("Changed"))
                   .Append(Html.Head(string.Empty))
                   .Append("</tr></thead><tbody>");

            if (notices.Count == 0)
            {
                builder.Append("<tr><td colspan=\"6\" class=\"muted\">Nothing is being announced.</td></tr>");
            }

            foreach (SiteNotice notice in notices)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column(
                    "<strong>" + Html.Encode(notice.Title) + "</strong>"
                    + (notice.Body.Length == 0
                        ? string.Empty
                        : "<br /><span class=\"text-muted wrap-anywhere\">" + Html.Encode(notice.Body) + "</span>")));

                builder.Append(Html.Column(Html.Badge(notice.Severity, SeverityClass(notice.Severity), null)));
                builder.Append(Html.Column(Html.Encode(notice.SortOrder.ToString(CultureInfo.InvariantCulture)), "col-number"));
                builder.Append(Html.Column(notice.IsActive
                    ? Html.Badge("yes", "success", null)
                    : Html.Badge("no", "default", null)));
                builder.Append(Html.Column(
                    Html.Encode(Html.Timestamp(new DateTimeOffset(DateTime.SpecifyKind(notice.UpdatedUtc, DateTimeKind.Utc))))
                    + " <span class=\"text-muted\">" + Html.Encode(notice.UpdatedBy) + "</span>"));
                builder.Append(Html.Column(PostForm(
                    "notice.remove",
                    "<input type=\"hidden\" name=\"id\" value=\""
                    + notice.Id.ToString(CultureInfo.InvariantCulture)
                    + "\" /><button type=\"submit\" class=\"btn btn-danger btn-xs\">Remove</button>")));
                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            builder.Append("<h4>Add a notice</h4>");
            builder.Append(NoticeForm());

            return builder.ToString();
        }

        /// <summary>
        /// Renders the form that creates a notice.
        /// </summary>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// There is no form that edits a notice in place. A notice is three lines of text; removing it and
        /// writing it again is faster than an editing form, and it means there is one form here rather than
        /// one per row and one to add.
        /// </remarks>
        private string NoticeForm()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<form method=\"post\" action=\"/Admin.aspx\" class=\"form-horizontal\">");
            builder.Append(CsrfToken.RenderHidden(Session));
            builder.Append("<input type=\"hidden\" name=\"action\" value=\"notice.save\" />");
            builder.Append("<input type=\"hidden\" name=\"id\" value=\"0\" />");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label class=\"col-sm-2 control-label\" for=\"notice-title\">Title</label>");
            builder.Append("<div class=\"col-sm-10\"><input class=\"form-control input-sm\" type=\"text\" "
                           + "id=\"notice-title\" name=\"title\" required=\"required\" maxlength=\""
                           + MaximumTitleLength.ToString(CultureInfo.InvariantCulture) + "\" /></div></div>");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label class=\"col-sm-2 control-label\" for=\"notice-body\">Body</label>");
            builder.Append("<div class=\"col-sm-10\"><textarea class=\"form-control input-sm\" id=\"notice-body\" "
                           + "name=\"body\" rows=\"3\" maxlength=\""
                           + MaximumBodyLength.ToString(CultureInfo.InvariantCulture) + "\"></textarea></div></div>");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<label class=\"col-sm-2 control-label\" for=\"notice-severity\">Severity</label>");
            builder.Append("<div class=\"col-sm-4\"><select class=\"form-control input-sm\" id=\"notice-severity\" "
                           + "name=\"severity\">");
            builder.Append("<option value=\"info\">Information</option>");
            builder.Append("<option value=\"warning\">Warning</option>");
            builder.Append("<option value=\"danger\">Serious</option>");
            builder.Append("</select></div>");

            builder.Append("<label class=\"col-sm-2 control-label\" for=\"notice-sort\">Order</label>");
            builder.Append("<div class=\"col-sm-4\"><input class=\"form-control input-sm\" type=\"number\" "
                           + "id=\"notice-sort\" name=\"sort\" value=\"0\" step=\"10\" /></div></div>");

            builder.Append("<div class=\"form-group form-group-sm\">");
            builder.Append("<div class=\"col-sm-offset-2 col-sm-10\">");
            builder.Append("<div class=\"checkbox\"><label><input type=\"checkbox\" name=\"active\" value=\"1\" "
                           + "checked=\"checked\" /> Show this notice</label></div>");
            builder.Append("<button type=\"submit\" class=\"btn btn-primary btn-sm\">Add the notice</button>");
            builder.Append("</div></div>");

            builder.Append("</form>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the recent audit entries.
        /// </summary>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The trail is here rather than on a page of its own because it is read in the same sitting as the
        /// thing that produced it: an operator changes a role, and the next thing they want is to see it
        /// recorded. It is bounded for the same reason.
        /// </remarks>
        private string RenderAudit()
        {
            IReadOnlyList<AuditEntry> entries = Store.RecentAudit(50);

            StringBuilder builder = new StringBuilder();

            builder.Append("<p class=\"text-muted\">The last "
                           + entries.Count.ToString(CultureInfo.InvariantCulture)
                           + " recorded actions, newest first. Sign-in attempts, changes and refusals are all "
                           + "recorded here, including the ones that failed.</p>");

            builder.Append(Html.TableOpen());
            builder.Append("<thead><tr>")
                   .Append(Html.Head("When", "col-time"))
                   .Append(Html.Head("Actor"))
                   .Append(Html.Head("Action"))
                   .Append(Html.Head("Target"))
                   .Append(Html.Head("Detail"))
                   .Append(Html.Head("From"))
                   .Append("</tr></thead><tbody>");

            if (entries.Count == 0)
            {
                builder.Append("<tr><td colspan=\"6\" class=\"muted\">Nothing has been recorded yet.</td></tr>");
            }

            foreach (AuditEntry entry in entries)
            {
                builder.Append("<tr>");
                builder.Append(Html.Column(
                    Html.Encode(Html.Timestamp(new DateTimeOffset(DateTime.SpecifyKind(entry.WhenUtc, DateTimeKind.Utc)))),
                    "col-time"));
                builder.Append(Html.Column(Html.Cell(entry.Actor)));
                builder.Append(Html.Column(Html.Cell(entry.Action)));
                builder.Append(Html.Column(Html.Cell(entry.Target)));
                builder.Append(Html.Column("<span class=\"wrap-anywhere\">" + Html.Cell(entry.Detail) + "</span>"));
                builder.Append(Html.Column(Html.Cell(entry.Address)));
                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders a small standalone form.
        /// </summary>
        /// <param name="action">The action the form carries.</param>
        /// <param name="body">The fields and the button.</param>
        /// <returns>The markup.</returns>
        private string PostForm(string action, string body)
        {
            return "<form method=\"post\" action=\"/Admin.aspx\" class=\"inline-form\">"
                   + CsrfToken.RenderHidden(Session)
                   + "<input type=\"hidden\" name=\"action\" value=\"" + Html.Attribute(action) + "\" />"
                   + body
                   + "</form>";
        }

        /// <summary>
        /// Renders one option of the role selector.
        /// </summary>
        /// <param name="value">The role.</param>
        /// <param name="label">The text shown.</param>
        /// <param name="current">The role currently held.</param>
        /// <returns>The markup.</returns>
        private static string RoleOption(string value, string label, string current)
        {
            bool selected = string.Equals(value, current, StringComparison.Ordinal);

            return "<option value=\"" + Html.Attribute(value) + "\"" + (selected ? " selected=\"selected\"" : string.Empty)
                   + ">" + Html.Encode(label) + "</option>";
        }

        /// <summary>
        /// Turns a status code into the notice shown at the top of the page.
        /// </summary>
        /// <returns>The markup.</returns>
        private string RenderStatus()
        {
            string code = ReadQuery("status", 32);

            switch (code)
            {
                case "notice-saved": return Html.Alert("info", "The notice was saved.", string.Empty);
                case "notice-removed": return Html.Alert("info", "The notice was removed.", string.Empty);
                case "notice-notitle": return Html.Alert("warning", "A notice needs a title.", string.Empty);
                case "notice-missing": return Html.Alert("warning", "That notice no longer exists.", string.Empty);
                case "account-created": return Html.Alert("info", "The account was created.", string.Empty);
                case "account-saved": return Html.Alert("info", "The account was saved.", string.Empty);
                case "account-removed": return Html.Alert("info", "The account was removed.", string.Empty);
                case "account-enabled": return Html.Alert("info", "The account may sign in again.", string.Empty);
                case "account-disabled": return Html.Alert("info", "The account may no longer sign in.", string.Empty);

                case "account-name":
                    return Html.Alert(
                        "warning",
                        "That is not a usable account name.",
                        "A name is between one and sixty-four characters, may contain letters, digits, dots, "
                        + "hyphens and underscores, and may not be a relative path.");

                case "account-password":
                    return Html.Alert(
                        "warning",
                        "That password was not accepted.",
                        "A new account needs a password, and every password must be at least "
                        + MinimumPasswordLength.ToString(CultureInfo.InvariantCulture)
                        + " characters and no more than "
                        + MaximumPasswordLength.ToString(CultureInfo.InvariantCulture) + ".");

                case "account-lastadmin":
                    return Html.Alert(
                        "danger",
                        "That would leave the console without an administrator.",
                        "This site refuses to remove or disable the last account that may administer it, because "
                        + "there would then be no way back in from the interface.");

                case "account-missing": return Html.Alert("warning", "That account no longer exists.", string.Empty);
                case "account-self":
                    return Html.Alert(
                        "warning",
                        "You cannot remove your own account.",
                        "Sign in as another administrator and remove it from there, so that the change cannot "
                        + "end the session that is making it.");

                case "token":
                    return Html.Alert(
                        "warning",
                        "That change was not applied.",
                        "The form arrived without a valid token for this session, which happens when a page has "
                        + "been left open long enough for the session to lapse. Reload the page and try again.");

                case "unknown":
                    return Html.Alert("warning", "That change was not recognised.", string.Empty);

                default: return string.Empty;
            }
        }

        /// <summary>
        /// Finds a notice by identifier.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>The notice, or <see langword="null"/>.</returns>
        private SiteNotice FindNotice(long id)
        {
            foreach (SiteNotice notice in Store.ListNotices(includeInactive: true))
            {
                if (notice.Id == id) return notice;
            }

            return null;
        }

        /// <summary>
        /// Reads a value from the posted form.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="maximumLength">The longest value accepted.</param>
        /// <returns>The value, or an empty string.</returns>
        /// <remarks>
        /// Trimmed and truncated rather than refused. A field that is too long is a field somebody pasted into,
        /// and truncating it produces an account with a slightly shorter display name, where refusing produces
        /// a form they have to fill in again.
        /// </remarks>
        private string ReadPost(string name, int maximumLength)
        {
            string raw = Request.Form[name];

            if (string.IsNullOrEmpty(raw)) return string.Empty;

            string value = raw.Trim();

            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        /// <summary>
        /// Determines whether a name may be used for an account.
        /// </summary>
        /// <param name="username">The name.</param>
        /// <returns><see langword="true"/> when it may.</returns>
        /// <remarks>
        /// The same shape the service names are held to: letters, digits, dots, hyphens and underscores. The
        /// name reaches a URL on the sign-in form and a row in a table, and a name that could be neither is a
        /// name that will fail somewhere later, in a way that is harder to explain.
        /// </remarks>
        private static bool IsUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length > 64) return false;
            if (username == "." || username == "..") return false;

            foreach (char character in username)
            {
                if (char.IsLetterOrDigit(character)) continue;
                if (character == '-' || character == '_' || character == '.') continue;

                return false;
            }

            return true;
        }

        /// <summary>
        /// Builds the fragment of an element identifier for an account.
        /// </summary>
        /// <param name="username">The account's name.</param>
        /// <returns>The fragment.</returns>
        /// <remarks>
        /// Identifiers have to be unique across the page and have to be usable in a label's <c>for</c>
        /// attribute. A name is not usable as one, so it is reduced to something that is.
        /// </remarks>
        private static string Key(string username)
        {
            if (string.IsNullOrEmpty(username)) return "new";

            StringBuilder builder = new StringBuilder(username.Length);

            foreach (char character in username)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : '-');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Gets the label class a notice severity is drawn with.
        /// </summary>
        /// <param name="severity">The severity.</param>
        /// <returns>The class.</returns>
        private static string SeverityClass(string severity)
        {
            if (severity == "danger") return "danger";
            if (severity == "warning") return "warning";

            return "info";
        }

        /// <summary>
        /// Sends the browser back to the page with a status in the query.
        /// </summary>
        /// <param name="status">The status code.</param>
        private void Redirect(string status)
        {
            Response.Redirect("/Admin.aspx?status=" + Uri.EscapeDataString(status), endResponse: true);
        }
    }
}
