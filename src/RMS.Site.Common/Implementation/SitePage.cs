using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Security;
using System.Web.UI;

using RMS.Site.Common.Enums;
using RMS.Site.Common.Implementation;
using RMS.Site.Common.Interfaces;
using RMS.Site.Common.Models;
using RMS.Site.Common.Options;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// The base class every page of every RMS site derives from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It supplies the four things each page would otherwise have to get right on its own: the settings, the
    /// site's database, the client that talks to the service, and the chrome. Getting any of those wrong in
    /// one page is the kind of mistake that is invisible until it matters -- a page that built its own client
    /// would hold the key somewhere else, and a page that resolved its own identity would have its own idea of
    /// what an empty session means.
    /// </para>
    /// <para>
    /// The pages themselves carry no server controls and no view state. Everything is rendered from
    /// expressions in the markup, which means the rendered page is exactly the markup that was written, no
    /// postback script is injected, and the content-security policy can be strict enough to be worth having.
    /// The cost is that a page cannot raise a postback and handle it; the shape this stack uses instead is a
    /// plain HTML form posting to the same page, read through <see cref="System.Web.HttpRequest.Form"/>.
    /// </para>
    /// <para>
    /// Identity is carried in an encrypted forms-authentication ticket whose payload is the account
    /// identifier, and the account is re-read from the database on every request. The ticket therefore says
    /// who signed in and nothing about what they may do, so disabling an account or taking away its role
    /// takes effect on the next click rather than whenever a cookie happens to expire.
    /// </para>
    /// <para>
    /// A page is never declared <c>sealed</c>, however finished it looks. A page file is compiled into a class
    /// that derives from the type it names in its own directive, so a sealed page fails to compile the first
    /// time it is requested, with an error that names neither the page nor the directive that caused it. The
    /// same applies to a handler.
    /// </para>
    /// </remarks>
    public abstract class SitePage : Page
    {
        /// <summary>
        /// The key the notices are cached under.
        /// </summary>
        private const string NoticeCacheKey = "rms.notices";

        /// <summary>
        /// How long the notices are cached for, in seconds.
        /// </summary>
        /// <remarks>
        /// Long enough that a burst of requests reads the database once, short enough that an administrator
        /// who saves a notice sees it on the next refresh rather than wondering whether the save worked.
        /// </remarks>
        private const int NoticeCacheSeconds = 10;

        private SiteOptions _settings;
        private AccountStore _store;
        private ServiceClient _client;

        /// <summary>
        /// Gets the settings in force.
        /// </summary>
        protected SiteOptions Settings
        {
            get { return _settings ?? (_settings = SiteOptions.Load(ConfigurationManager.AppSettings)); }
        }

        /// <summary>
        /// Gets the site's own database.
        /// </summary>
        /// <remarks>
        /// The store is created on first use because the classes that derive from this one are constructed
        /// before the application has finished starting, and a page that is only ever rendered does not need
        /// the database at all.
        /// </remarks>
        protected IAccountStore Store
        {
            get { return _store ?? (_store = new AccountStore(Settings)); }
        }

        /// <summary>
        /// Gets the client that reads the service.
        /// </summary>
        protected ServiceClient Client
        {
            get { return _client ?? (_client = new ServiceClient(Settings)); }
        }

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected virtual string PageTitle
        {
            get { return Settings.Title; }
        }

        /// <summary>
        /// Gets the navigation entry to highlight.
        /// </summary>
        /// <remarks>
        /// An empty value highlights nothing, which is what a page that is not one of the main destinations --
        /// a run's detail, a sign-in -- wants. Highlighting the section a detail page belongs to would be
        /// wrong here because these pages are reached from several sections.
        /// </remarks>
        protected virtual string ActiveSection
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the site's chrome is rendered around this page.
        /// </summary>
        /// <remarks>
        /// The sign-in page turns it off, because it is shown to somebody who is not yet an operator and
        /// offering them the navigation would be offering doors that do not open.
        /// </remarks>
        protected virtual bool UsesChrome
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether an address that needs a session is answered as though it did not
        /// exist.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Off by default, and it is the base of every console that reads it. A console sits on an internal
        /// network and is reached by operators who know it is there: a visitor who is bounced to the sign-in form
        /// has lost nothing and can sign in from the page they were sent to, which is a better answer than being
        /// told the address is not there.
        /// </para>
        /// <para>
        /// A site that is published to strangers turns it on. Its operator pages are then indistinguishable from
        /// addresses that were never built, so a visitor cannot map the machine by guessing names, and the
        /// catalogue remains the only thing the site says to somebody who has not signed in. What is turned off
        /// is the statement that the address exists, never the check: the session and the role are still required
        /// by the code that renders these pages, and a caller who sends a valid ticket gains nothing they did not
        /// already have.
        /// </para>
        /// </remarks>
        protected virtual bool HidesFromAnonymous
        {
            get { return false; }
        }

        /// <summary>
        /// Gets the identity signed in to this request.
        /// </summary>
        protected UserSession CurrentUser { get; private set; }

        /// <summary>
        /// Gets a value indicating whether a signed-in identity has been resolved.
        /// </summary>
        protected bool IsSignedIn
        {
            get { return CurrentUser != null; }
        }

        /// <summary>
        /// Gets a value indicating whether the viewer may change what other people see.
        /// </summary>
        protected bool IsAdministrator
        {
            get { return CurrentUser != null && CurrentUser.IsAdministrator; }
        }

        /// <summary>
        /// Resolves the identity and enforces whatever the derived page requires.
        /// </summary>
        /// <param name="e">The event data.</param>
        /// <remarks>
        /// The work happens here rather than in a load handler so that it precedes the rendering of anything.
        /// A page that resolved its identity after it had written its heading would have already decided what
        /// to draw, and the decision is the part that depends on who is asking.
        /// </remarks>
        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);

            Response.Headers.Add("X-Content-Type-Options", "nosniff");
            Response.Headers.Add("Referrer-Policy", "same-origin");
            Response.Headers.Add("X-Frame-Options", "DENY");

            // Uncached through the cache object rather than by adding a header: the framework owns Cache-Control
            // and writes the value its own policy holds. A page of this console carries account names, an audit
            // trail and a notice board, so an intermediary or the browser's back button must not be able to
            // produce it again from a store.
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();

            ResolveIdentity();

            EnforceAccess();
        }

        /// <summary>
        /// Applies the page's own access rule.
        /// </summary>
        /// <remarks>
        /// The base does nothing. A page that must not be reached without a session overrides it and says so,
        /// which keeps the rule beside the page rather than in a list somewhere that has to be kept in step
        /// with the files on disk.
        /// </remarks>
        protected virtual void EnforceAccess()
        {
        }

        /// <summary>
        /// Reads the identity from the authentication ticket.
        /// </summary>
        /// <remarks>
        /// The rules live in <see cref="SessionReader"/> so that the fragment handlers cannot apply a slightly
        /// different set. All this does is hold the result.
        /// </remarks>
        private void ResolveIdentity()
        {
            CurrentUser = SessionReader.Read(Context, Store);
        }

        /// <summary>
        /// Issues the authentication cookie for an account.
        /// </summary>
        /// <param name="account">The account that has just signed in.</param>
        /// <remarks>
        /// The ticket carries the account's identifier as its payload and the account's name as its name, and
        /// nothing else. A ticket that carried the role would keep granting it after it had been taken away,
        /// and unlike a session timer there is no moment at which that becomes acceptable.
        /// </remarks>
        protected void SignIn(SiteAccount account)
        {
            SessionReader.Issue(Context, account, Settings.SessionMinutes, Settings.RequireHttps);

            CurrentUser = new UserSession
            {
                AccountId = account.Id,
                Username = account.Username,
                DisplayName = account.DisplayName,
                Role = account.Role,
            };
        }

        /// <summary>
        /// Ends the current session.
        /// </summary>
        protected void SignOut()
        {
            SessionReader.Clear(Context, Settings.RequireHttps);

            Session.Abandon();

            CurrentUser = null;
        }

        /// <summary>
        /// Gets the address a request came from, as the connection reported it.
        /// </summary>
        /// <returns>The address, or an empty string when it cannot be read.</returns>
        protected string ClientAddress
        {
            get { return Request.UserHostAddress ?? string.Empty; }
        }

        /// <summary>
        /// Redirects an unauthenticated visitor to the sign-in page, remembering where they were going.
        /// </summary>
        /// <param name="reason">Why they are being sent there.</param>
        protected void RedirectToSignIn(string reason)
        {
            // The destination is carried as a path and a query only. A full URL would make this a redirector
            // that an outsider could aim anywhere by sending a link, which is the classic way a login page
            // becomes a phishing step.
            string returnUrl = Request.Url == null
                ? "/"
                : Request.Url.PathAndQuery;

            Response.Redirect(
                "/Account/Login.aspx?reason=" + HttpUtility.UrlEncode(reason ?? string.Empty)
                + "&returnUrl=" + HttpUtility.UrlEncode(returnUrl),
                endResponse: true);
        }

        /// <summary>
        /// Answers as though the address did not exist.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The status is the one a request for a file that was never here gets, and the body is the same for
        /// every address that reaches this method. That is the whole of it: a page that said "you may not read
        /// this" would have confirmed that there is something to read, which is the one thing
        /// <see cref="HidesFromAnonymous"/> exists to avoid saying.
        /// </para>
        /// <para>
        /// The chrome is still rendered around it, because the visitor who sees this may be an operator whose
        /// session has just ended, and the navigation is where the way back in is. It names only the sections
        /// that visitor may already reach.
        /// </para>
        /// <para>
        /// The response is ended here rather than returned from. <c>OnInit</c> runs before the page builds
        /// anything, so nothing of it has been written yet, and a method that returned would let the page's own
        /// load handler append the document it was about to refuse to the document that has just gone out.
        /// Ending the response is what the sign-in redirect does as well, for the same reason.
        /// </para>
        /// </remarks>
        protected void RespondNotFound()
        {
            Response.StatusCode = 404;

            // The framework's own error handling is told not to replace this body. Without it, a request that
            // arrives through IIS has its answer rewritten -- the status stays, the page becomes IIS's -- and the
            // navigation rendered below would be a navigation nobody ever sees.
            Response.TrySkipIisCustomErrors = true;

            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "That page is not here",
                "This address is not one this site serves."));

            builder.Append("<p>If you were looking for the catalogue, it is the first link in the navigation "
                           + "above.</p>");

            WriteDocument("Not found", Navigation(), builder.ToString());

            Response.End();
        }

        /// <summary>
        /// Reads the notices shown at the top of every page.
        /// </summary>
        /// <returns>The active notices.</returns>
        protected IReadOnlyList<SiteNotice> LoadNotices()
        {
            object cached = Cache[NoticeCacheKey];

            if (cached is IReadOnlyList<SiteNotice>) return (IReadOnlyList<SiteNotice>)cached;

            IReadOnlyList<SiteNotice> notices;

            try
            {
                notices = Store.ListNotices(includeInactive: false);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is Microsoft.Data.Sqlite.SqliteException || exception is System.IO.IOException)
            {
                // A site whose database cannot be read still has to render something. Reporting the notices as
                // absent is the honest answer at this level: the page that needs to explain the fault is the
                // diagnostics page, and it will say so itself.
                return new List<SiteNotice>();
            }

            Cache.Insert(
                NoticeCacheKey,
                notices,
                null,
                DateTime.UtcNow.AddSeconds(NoticeCacheSeconds),
                System.Web.Caching.Cache.NoSlidingExpiration);

            return notices;
        }

        /// <summary>
        /// Discards the cached notices so the next request reads them afresh.
        /// </summary>
        protected void InvalidateNotices()
        {
            Cache.Remove(NoticeCacheKey);
        }

        /// <summary>
        /// Renders the notices as alerts.
        /// </summary>
        /// <returns>The markup, which is empty when there is nothing to say.</returns>
        protected string RenderNotices()
        {
            StringBuilder builder = new StringBuilder();

            foreach (SiteNotice notice in LoadNotices())
            {
                builder.Append(Html.Alert(notice.Severity, notice.Title, notice.Body, dismissible: true));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders the document head.
        /// </summary>
        /// <param name="title">The page's title.</param>
        /// <returns>The markup.</returns>
        protected string RenderHead(string title)
        {
            // The policy is written here rather than only in the response headers so that a deployment which
            // strips headers -- a proxy that rewrites them, a host that overrides them -- still serves a page
            // that names its own limits. The two agree, and the header is the one that has to be trusted.
            const string Policy =
                "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                "connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";

            return string.Format(
                CultureInfo.InvariantCulture,
                "<meta charset=\"utf-8\" />"
                + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />"
                + "<meta http-equiv=\"Content-Security-Policy\" content=\"{0}\" />"
                + "<meta name=\"rms-refresh\" content=\"{2}\" />"
                + "<title>{1}</title>"
                + "<link rel=\"shortcut icon\" href=\"/favicon.ico\" type=\"image/x-icon\" />"
                + "<link rel=\"icon\" type=\"image/x-icon\" href=\"/favicon.ico\" />"
                + "<link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"/favicon-32.png\" />"
                + "<link rel=\"stylesheet\" href=\"/Content/css7af7.css\" />"
                + "<link rel=\"stylesheet\" href=\"/Content/site.css\" />",
                Html.Attribute(Policy),
                Html.Encode(title),
                Settings.RefreshSeconds.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Renders the navigation bar.
        /// </summary>
        /// <param name="sections">The sections, as label and path pairs.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The sections are passed in rather than discovered, because the order they are presented in is a
        /// decision about what an operator looks at first and not a property of the files on disk.
        /// </remarks>
        protected string RenderNavbar(IEnumerable<KeyValuePair<string, string>> sections)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<nav class=\"navbar navbar-default navbar-static-top\">");
            builder.Append("<div class=\"container-fluid\">");
            builder.Append("<div class=\"navbar-header\">");
            builder.Append("<a class=\"navbar-brand\" href=\"/\">")
                   .Append(Html.Encode(Settings.Brand))
                   .Append("</a>");
            builder.Append("</div>");

            builder.Append("<ul class=\"nav navbar-nav\">");

            if (sections != null)
            {
                foreach (KeyValuePair<string, string> section in sections)
                {
                    bool active = !string.IsNullOrEmpty(ActiveSection)
                                  && string.Equals(ActiveSection, section.Key, StringComparison.OrdinalIgnoreCase);

                    builder.Append("<li")
                           .Append(active ? " class=\"active\"" : string.Empty)
                           .Append("><a href=\"").Append(Html.Attribute(section.Value)).Append("\">")
                           .Append(Html.Encode(section.Key))
                           .Append("</a></li>");
                }
            }

            builder.Append("</ul>");

            builder.Append("<ul class=\"nav navbar-nav navbar-right\">");

            if (IsSignedIn)
            {
                builder.Append("<li><p class=\"navbar-text\">")
                       .Append(Html.Encode(CurrentUser.DisplayName))
                       .Append(" <span class=\"text-muted\">")
                       .Append(Html.Encode(CurrentUser.Role))
                       .Append("</span></p></li>");

                if (IsAdministrator)
                {
                    builder.Append("<li><a href=\"/Admin.aspx\">Admin</a></li>");
                }

                builder.Append("<li><a href=\"/Account/Logout.aspx\">Sign out</a></li>");
            }
            else
            {
                builder.Append("<li><a href=\"/Account/Login.aspx\">Sign in</a></li>");
            }

            builder.Append("</ul>");
            builder.Append("</div></nav>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the page's footer.
        /// </summary>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The address of the service is in the footer, and only for an operator. It is the first thing an
        /// operator looks at when a page says the API is not answering, because a site reading the wrong port is
        /// the commonest reason for it -- and it is a description of the inside of the deployment that a visitor
        /// who has not signed in has no use for. The refresh interval is harmless and is shown to everybody,
        /// because it explains why the numbers move on their own.
        /// </remarks>
        protected string RenderFooter()
        {
            string opening = "<footer class=\"container-fluid body-content text-muted footer-note\"><p>";

            string reading = IsSignedIn
                ? "Reading <span class=\"code\">"
                  + Html.Encode(Client.IsConfigured ? Client.BaseUrl : "not configured")
                  + "</span>. "
                : string.Empty;

            return opening
                   + reading
                   + "This page refreshes itself every " + Settings.RefreshSeconds.ToString(CultureInfo.InvariantCulture)
                   + " seconds.</p></footer>";
        }

        /// <summary>
        /// Renders a whole page around a body.
        /// </summary>
        /// <param name="title">The page's title.</param>
        /// <param name="sections">The navigation sections.</param>
        /// <param name="body">The body markup.</param>
        /// <returns>The document.</returns>
        protected string RenderDocument(
            string title,
            IEnumerable<KeyValuePair<string, string>> sections,
            string body)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<!DOCTYPE html><html lang=\"en\">");
            builder.Append("<head>").Append(RenderHead(title)).Append("</head>");
            builder.Append("<body>");

            if (UsesChrome)
            {
                builder.Append(RenderNavbar(sections));
            }

            builder.Append("<div class=\"container-fluid body-content\">");
            builder.Append(RenderNotices());
            builder.Append(body);
            builder.Append("</div>");

            if (UsesChrome)
            {
                builder.Append(RenderFooter());
            }

            builder.Append("<script src=\"/Scripts/site.js").Append(StaticVersion("~/Scripts/site.js")).Append("\"></script>");
            builder.Append("</body></html>");

            return builder.ToString();
        }

        /// <summary>
        /// The query string that makes a static file's address change when the file does.
        /// </summary>
        /// <param name="applicationRelativePath">The file, relative to the application root.</param>
        /// <returns><c>?v=...</c> for a file that exists, or an empty string.</returns>
        /// <remarks>
        /// <para>
        /// Static content on these sites is served with a thirty-day cache. That is what makes the stylesheets
        /// and the icons cost one request a month -- and it is also what would make a rebuilt bundle invisible
        /// for the rest of that month. To a cache, the address of a file is its identity, so a file whose
        /// content changed under an unchanged address is answered from the browser's copy, and from the kernel's
        /// copy, until the month is up. A rebuild that did not change the address of the bundle would be a
        /// rebuild nobody saw.
        /// </para>
        /// <para>
        /// The stamp is the file's write time rather than a hash of its content, because this runs once per
        /// page and a hash would be a read of the whole file per page. A timestamp already says what a hash
        /// would say here: a file this site publishes is written when it changes, and at no other time.
        /// </para>
        /// </remarks>
        private static string StaticVersion(string applicationRelativePath)
        {
            HttpContext context = HttpContext.Current;
            if (context == null)
            {
                return string.Empty;
            }

            try
            {
                string path = context.Server.MapPath(applicationRelativePath);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return "?v=" + File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (HttpException)
            {
                // The file is not under this application. A stamp is a nicety, not a requirement.
            }
            catch (ArgumentException)
            {
                // The path was not something MapPath could resolve, for the same reason.
            }

            return string.Empty;
        }

        /// <summary>
        /// Writes a rendered document as the response.
        /// </summary>
        /// <param name="title">The page's title.</param>
        /// <param name="sections">The navigation sections.</param>
        /// <param name="body">The body markup.</param>
        /// <remarks>
        /// The response is written whole rather than through a literal control, which is what keeps the page
        /// free of view state. The content type and encoding are set explicitly because the default would be
        /// the code page of the server, and every one of these pages carries text that came from a file.
        /// </remarks>
        protected void WriteDocument(string title, IEnumerable<KeyValuePair<string, string>> sections, string body)
        {
            Response.ContentType = "text/html";
            Response.ContentEncoding = Encoding.UTF8;
            Response.Write(RenderDocument(title, sections, body));
        }

        /// <summary>
        /// Builds the site's standard navigation.
        /// </summary>
        /// <returns>The sections, in the order they are presented.</returns>
        protected virtual IEnumerable<KeyValuePair<string, string>> Navigation()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Overview", "/"),
                new KeyValuePair<string, string>("Services", "/Services.aspx"),
                new KeyValuePair<string, string>("History", "/History.aspx"),
                new KeyValuePair<string, string>("Artefacts", "/Artifacts.aspx"),
                new KeyValuePair<string, string>("Catalogue", "/Catalogue.aspx"),
            };
        }
    }
}
