using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Text;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Interfaces;
using RMS.Site.Common.Models;
using RMS.Site.Common.Options;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// The base class for the handlers a page's script calls back into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These handlers exist so that a page's live regions can be refreshed without reloading the page and
    /// without the browser talking to the service. The service's address and key stay on this side of the
    /// boundary, and what comes back is markup, built by the same code that built the page on the way in --
    /// which is what stops a table from being rendered twice, once in C# and once in JavaScript, and drifting.
    /// </para>
    /// <para>
    /// A handler is not a page and does not go through the page lifecycle, so the access rule has to be stated
    /// here as well. It is stated once, in this class, and every handler inherits it. A handler that answered
    /// without checking would be a way to read the whole machine through an address that no page links to.
    /// </para>
    /// <para>
    /// Nothing here accepts a destination. A handler is asked for a named view and a handful of scalar
    /// parameters, and it builds the service's address itself. A handler that took a path and passed it
    /// through would be an open proxy sitting inside the trust boundary, which is the worst possible place
    /// for one.
    /// </para>
    /// </remarks>
    public abstract class ServiceProxyHandler : IHttpHandler
    {
        private SiteOptions _settings;
        private AccountStore _store;
        private ServiceClient _client;

        /// <summary>
        /// Gets a value indicating whether the handler may be reused across requests.
        /// </summary>
        /// <remarks>
        /// Handlers are stateless -- everything a request needs is read from that request -- so one instance is
        /// reused. The settings, the store and the client it caches are the same objects the pages use, and
        /// they are all safe to share.
        /// </remarks>
        public bool IsReusable
        {
            get { return true; }
        }

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
        /// Gets the identity the request carries.
        /// </summary>
        protected UserSession CurrentUser { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the handler may only be called by a signed-in operator.
        /// </summary>
        /// <remarks>
        /// On by default. The sign-in handler is the only one that turns it off, and it does so because it is
        /// the thing that creates the session in the first place.
        /// </remarks>
        protected virtual bool RequiresSignIn
        {
            get { return true; }
        }

        /// <summary>
        /// Handles one request.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        public void ProcessRequest(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            CurrentUser = SessionReader.Read(context, Store);

            context.Response.Headers.Add("X-Content-Type-Options", "nosniff");

            // Uncached through the cache object rather than by adding a header. The framework owns Cache-Control:
            // a value added to the header collection is discarded when the response is written, in favour of the
            // one the cache policy holds, which is how a fragment that asked not to be cached went out cacheable.
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();

            if (RequiresSignIn && CurrentUser == null)
            {
                // The refusal is a status rather than a rendered message. The caller is a script on a page the
                // operator may have left open while their session ended, and the only useful thing that can
                // happen next is that the page notices and sends them to sign in again.
                Refuse(context, 401, "Sign in to read this.");
                return;
            }

            try
            {
                Handle(context);
            }
            catch (Exception exception)
                when (!(exception is HttpException) && !(exception is System.Threading.ThreadAbortException))
            {
                // A handler that let an exception escape would give the reader the framework's error page, which
                // names the assembly, the method and the line. The page's script only needs to know the
                // refresh failed, so it is told that and the detail stays here.
                //
                // ThreadAbortException is excluded because it is how the framework unwinds a request whose
                // response ended itself. Catching it would turn a redirect into a server error.
                Detail = exception.Message;

                Refuse(context, 500, "This view could not be built.");
            }
        }

        /// <summary>
        /// Gets a description of the last failure, for the application log written by the site's global
        /// handler.
        /// </summary>
        protected string Detail { get; private set; }

        /// <summary>
        /// Builds the response for a request that has already been authorised.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        protected abstract void Handle(HttpContext context);

        /// <summary>
        /// Writes a piece of markup.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <param name="markup">The markup.</param>
        protected static void WriteHtml(HttpContext context, string markup)
        {
            context.Response.ContentType = "text/html";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.Write(markup ?? string.Empty);
        }

        /// <summary>
        /// Writes a JSON document.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <param name="json">The document.</param>
        protected static void WriteJson(HttpContext context, string json)
        {
            context.Response.ContentType = "application/json";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.Write(json ?? "{}");
        }

        /// <summary>
        /// Writes a plain-text body.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <param name="text">The body.</param>
        protected static void WriteText(HttpContext context, string text)
        {
            context.Response.ContentType = "text/plain";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.Write(text ?? string.Empty);
        }

        /// <summary>
        /// Refuses a request.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <param name="statusCode">The status to answer with.</param>
        /// <param name="message">The sentence the page's script will show.</param>
        protected static void Refuse(HttpContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;

            WriteJson(
                context,
                "{\"error\":" + Json(message) + ",\"status\":" + statusCode.ToString(CultureInfo.InvariantCulture) + "}");
        }

        /// <summary>
        /// Reads an integer from the query string.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="name">The parameter's name.</param>
        /// <param name="fallback">The value used when the parameter is absent or unusable.</param>
        /// <param name="minimum">The smallest value accepted.</param>
        /// <param name="maximum">The largest value accepted.</param>
        /// <returns>The value.</returns>
        /// <remarks>
        /// Clamping rather than refusing. A page that asked for a page number just past the end of a list is
        /// making an ordinary mistake, and answering the last page is more useful than answering an error.
        /// </remarks>
        protected static int ReadNumber(HttpContext context, string name, int fallback, int minimum, int maximum)
        {
            string raw = context.Request.QueryString[name];

            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            int parsed;

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return fallback;

            return parsed < minimum ? minimum : parsed > maximum ? maximum : parsed;
        }

        /// <summary>
        /// Reads a flag from the query string.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="name">The parameter's name.</param>
        /// <returns><see langword="true"/> when the parameter is present and affirmative.</returns>
        protected static bool ReadFlag(HttpContext context, string name)
        {
            string raw = context.Request.QueryString[name];

            if (string.IsNullOrWhiteSpace(raw)) return false;

            return raw.Equals("1", StringComparison.Ordinal)
                   || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads a bounded text value from the query string.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="name">The parameter's name.</param>
        /// <param name="maximumLength">The longest value accepted.</param>
        /// <returns>The value, or an empty string.</returns>
        protected static string ReadText(HttpContext context, string name, int maximumLength)
        {
            string raw = context.Request.QueryString[name];

            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string value = raw.Trim();

            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        /// <summary>
        /// Serialises a string as a JSON value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The JSON.</returns>
        /// <remarks>
        /// Written by hand rather than through the serializer, because the serializer's output is a document
        /// and this is one field inside one. Doing it here also keeps the escaping in one place, which is what
        /// stops a log line containing a quote from breaking the response apart.
        /// </remarks>
        protected static string Json(string value)
        {
            if (value == null) return "null";

            StringBuilder builder = new StringBuilder(value.Length + 2);

            builder.Append('"');

            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');

            return builder.ToString();
        }

        /// <summary>
        /// Validates a service name before it is put into a service address.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns><see langword="true"/> when the name is usable.</returns>
        /// <remarks>
        /// The service validates too, and that is the check that matters. This one exists so that an obviously
        /// malformed name is refused here rather than becoming part of an address, which keeps this handler
        /// from being a thing that can be pointed at addresses it was not built for.
        /// </remarks>
        protected static bool IsSafeComponent(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128) return false;
            if (name == "." || name == "..") return false;

            foreach (char character in name)
            {
                if (char.IsLetterOrDigit(character)) continue;
                if (character == '-' || character == '_' || character == '.') continue;

                return false;
            }

            return true;
        }

        /// <summary>
        /// Builds the query the service is asked with.
        /// </summary>
        /// <param name="pairs">The name and value pairs, which are encoded by the client.</param>
        /// <returns>The query.</returns>
        protected static KeyValuePair<string, string>[] Query(params KeyValuePair<string, string>[] pairs)
        {
            return pairs ?? new KeyValuePair<string, string>[0];
        }

        /// <summary>
        /// Adds a pair to a query.
        /// </summary>
        /// <param name="query">The existing pairs.</param>
        /// <param name="name">The parameter's name.</param>
        /// <param name="value">The value.</param>
        /// <returns>The query with the pair appended.</returns>
        protected static KeyValuePair<string, string>[] With(
            KeyValuePair<string, string>[] query,
            string name,
            string value)
        {
            List<KeyValuePair<string, string>> combined =
                new List<KeyValuePair<string, string>>(query ?? new KeyValuePair<string, string>[0]);

            combined.Add(new KeyValuePair<string, string>(name, value));

            return combined.ToArray();
        }
    }
}
