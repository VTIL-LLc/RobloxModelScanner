using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;
using System.Text;
using System.Web.Hosting;

namespace RMS.Site.Common.Options
{
    /// <summary>
    /// Everything an RMS site reads from its own configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every value has an environment-variable fallback, and the environment wins whenever it carries
    /// anything. That ordering is not a convenience: the committed <c>Web.config</c> must not hold a secret,
    /// so the service key and the connection string are supplied by the deployment, and an operator who
    /// rotates a key restarts a service rather than editing a file that is under version control.
    /// </para>
    /// <para>
    /// The settings are read once per application, because a site whose behaviour changed halfway through a
    /// request would be a site whose failures could not be reproduced. An IIS application recycle is the
    /// mechanism for applying a change, and that is the correct amount of ceremony for a deployment.
    /// </para>
    /// </remarks>
    public sealed class SiteOptions
    {
        /// <summary>
        /// The environment variable naming the service the site reads.
        /// </summary>
        public const string ServiceBaseUrlVariable = "SITE_SERVICE_BASE_URL";

        /// <summary>
        /// The environment variable holding the shared service key.
        /// </summary>
        public const string ServiceKeyVariable = "SITE_SERVICE_KEY";

        /// <summary>
        /// The environment variable naming a file the shared service key is read from.
        /// </summary>
        public const string ServiceKeyFileVariable = "SITE_SERVICE_KEY_FILE";

        /// <summary>
        /// The environment variable naming the query parameter the shared key travels in.
        /// </summary>
        public const string ServiceKeyQueryParameterVariable = "SITE_SERVICE_KEY_QUERY_PARAMETER";

        /// <summary>
        /// The environment variable naming the account database.
        /// </summary>
        public const string AuthDatabaseVariable = "SITE_AUTH_DATABASE";

        /// <summary>
        /// The header the shared service key travels in when no other is configured.
        /// </summary>
        public const string DefaultServiceKeyHeader = "X-Devops-Key";

        /// <summary>
        /// Gets the name shown in the browser tab.
        /// </summary>
        public string Title { get; private set; }

        /// <summary>
        /// Gets the name shown in the navbar.
        /// </summary>
        public string Brand { get; private set; }

        /// <summary>
        /// Gets the absolute URL of the service this site reads.
        /// </summary>
        public string ServiceBaseUrl { get; private set; }

        /// <summary>
        /// Gets the key presented to the service.
        /// </summary>
        /// <remarks>
        /// The key never leaves the server. The browser talks only to this application, and this application
        /// attaches the key on the way out, so a page source, a developer console and a proxy log all show
        /// the caller's cookie and nothing else.
        /// </remarks>
        public string ServiceKey { get; private set; }

        /// <summary>
        /// Gets the path of the key file this site was named and could not read, or an empty string.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It is recorded rather than discarded because the site's own pages are where the fault is seen, and
        /// "the service is not answering" sends an operator to the service. On a deployment that has just been
        /// installed the usual cause is the other end: the deployment writes the path of a credential file into
        /// this site's configuration, and the file itself is created afterwards, by hand, by the operator who is
        /// allowed to know the secret. A site that starts before that step has no key, and naming the file turns
        /// that into a one-line fix rather than a hunt.
        /// </para>
        /// <para>
        /// A named file that cannot be read deliberately does not fall back to a key written somewhere else. A
        /// site that quietly used a different key would authenticate against a service the deployment did not
        /// intend and would look, from every page, like it was working.
        /// </para>
        /// </remarks>
        public string ServiceKeyFileProblem { get; private set; }

        /// <summary>
        /// Gets the header the key is presented in.
        /// </summary>
        /// <remarks>
        /// Each service in this stack names its own header, so that a key copied from one deployment's
        /// configuration into another's reveals nothing: the value would be presented in a header the other
        /// service does not read. The default is the DevOps service's header, which is the one every site
        /// written before this setting existed already used, so a site that does not name one keeps working.
        /// </remarks>
        public string ServiceKeyHeader { get; private set; }

        /// <summary>
        /// Gets the query parameter the key is presented in, or an empty string when it travels in a header.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A query parameter is the weaker of the two places to put a key: it is written into the service's
        /// access log, into any reverse proxy's log in front of it, and into a <c>Referer</c> header if the
        /// answer ever links somewhere. A header is visible to none of those. So a header is the default and a
        /// query parameter is something a site asks for by name.
        /// </para>
        /// <para>
        /// It is asked for here because one service in this stack reads the key from the query and declares
        /// that as its published contract: the scanner's API has always taken <c>apiKey</c> as a query
        /// parameter, and every existing caller sends it there. Changing the service would break those callers;
        /// naming the parameter here changes nothing but this site. When this is set the header is not sent,
        /// because sending one key two ways is sending it twice.
        /// </para>
        /// </remarks>
        public string ServiceKeyQueryParameter { get; private set; }

        /// <summary>
        /// Gets the number of seconds a single service call may take.
        /// </summary>
        public int ServiceTimeoutSeconds { get; private set; }

        /// <summary>
        /// Gets the absolute path of the account database.
        /// </summary>
        public string AuthDatabasePath { get; private set; }

        /// <summary>
        /// Gets the number of PBKDF2 iterations used to derive a password hash.
        /// </summary>
        public int PasswordIterations { get; private set; }

        /// <summary>
        /// Gets how many consecutive password failures lock an account.
        /// </summary>
        public int MaximumFailedAttempts { get; private set; }

        /// <summary>
        /// Gets how long a locked account stays locked, in minutes.
        /// </summary>
        public int LockoutMinutes { get; private set; }

        /// <summary>
        /// Gets how long a sign-in lasts, in minutes.
        /// </summary>
        public int SessionMinutes { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the authentication cookie requires a secure channel.
        /// </summary>
        /// <remarks>
        /// Off by default so the site also runs on a local HTTP bind, which is how it is developed and how it
        /// is reached over a private network. A deployment behind TLS turns it on, and the deployment in front
        /// of Cloudflare always is.
        /// </remarks>
        public bool RequireHttps { get; private set; }

        /// <summary>
        /// Gets how often a page's live regions refresh themselves, in seconds.
        /// </summary>
        public int RefreshSeconds { get; private set; }

        /// <summary>
        /// Loads the settings from the site's configuration and the environment.
        /// </summary>
        /// <param name="settings">The site's application settings.</param>
        /// <returns>The settings in force.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        public static SiteOptions Load(NameValueCollection settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            string brand = Read(settings, "SiteBrand", "RMS");

            /*
             * The key and the reason it is missing are read together, because the second is only expressible
             * while the first is being looked for: a file that was named and could not be read is, afterwards,
             * indistinguishable from a site that was given no key at all -- and the two have different
             * remedies. One is a file that has not been created or that the service account cannot read; the
             * other is a deployment that named nothing.
             */
            string keyProblem;
            string key = ReadKey(settings, out keyProblem);

            return new SiteOptions
            {
                Title = Read(settings, "SiteTitle", brand),
                Brand = brand,
                ServiceBaseUrl = Read(settings, "SiteServiceBaseUrl", "http://127.0.0.1:5201").TrimEnd('/'),
                ServiceKey = key,
                ServiceKeyFileProblem = keyProblem,
                ServiceKeyHeader = Read(settings, "SiteServiceKeyHeader", DefaultServiceKeyHeader),
                ServiceKeyQueryParameter = Read(settings, "SiteServiceKeyQueryParameter", string.Empty),
                ServiceTimeoutSeconds = ReadNumber(settings, "SiteServiceTimeoutSeconds", 20, 1, 300),
                AuthDatabasePath = ResolveDatabase(Read(settings, "SiteAuthDatabase", "~/App_Data/site-auth.db")),
                PasswordIterations = ReadNumber(settings, "SitePasswordIterations", 210_000, 50_000, 4_000_000),
                MaximumFailedAttempts = ReadNumber(settings, "SiteMaximumFailedAttempts", 8, 1, 1000),
                LockoutMinutes = ReadNumber(settings, "SiteLockoutMinutes", 15, 1, 10_080),
                SessionMinutes = ReadNumber(settings, "SiteSessionMinutes", 480, 5, 43_200),
                RequireHttps = ReadFlag(settings, "SiteRequireHttps", false),
                RefreshSeconds = ReadNumber(settings, "SiteRefreshSeconds", 15, 2, 3600),
            };
        }

        /// <summary>
        /// Reads a setting, preferring the environment.
        /// </summary>
        /// <param name="settings">The site's application settings.</param>
        /// <param name="key">The setting's name.</param>
        /// <param name="fallback">The value used when neither source carries one.</param>
        /// <returns>The value in force.</returns>
        private static string Read(NameValueCollection settings, string key, string fallback)
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentName(key));

            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();

            string configured = settings[key];

            return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        }

        /// <summary>
        /// Derives the environment variable that overrides a setting.
        /// </summary>
        /// <param name="key">The setting's name, which begins with <c>Site</c>.</param>
        /// <returns>The environment variable's name.</returns>
        /// <remarks>
        /// <para>
        /// A setting named <c>SiteServiceBaseUrl</c> is overridden by <c>SITE_SERVICE_BASE_URL</c>. The name is
        /// derived rather than listed so that adding a setting cannot forget to add its override, which is the
        /// mistake that produces a deployment where half the values come from the environment and half from a
        /// file that nobody edited.
        /// </para>
        /// <para>
        /// Underscores are inserted at the boundaries between words, because that is how every other name in
        /// this stack is written -- and because an operator who has just been handed
        /// <c>SCANNER_INDEX_PATH</c> will guess <c>SITE_SERVICE_KEY</c> rather than <c>SITE_SERVICEKEY</c>.
        /// Getting this wrong is silent: the override would simply never be seen, and the site would keep
        /// using a value from a file while reporting that it had read the environment.
        /// </para>
        /// </remarks>
        private static string EnvironmentName(string key)
        {
            string bare = key.StartsWith("Site", StringComparison.Ordinal) ? key.Substring(4) : key;

            StringBuilder builder = new StringBuilder("SITE_");

            for (int index = 0; index < bare.Length; index++)
            {
                char character = bare[index];

                if (index > 0 && char.IsUpper(character) && !char.IsUpper(bare[index - 1]))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToUpperInvariant(character));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Resolves the shared key from a file, from the environment, or from the configuration.
        /// </summary>
        /// <param name="settings">The site's application settings.</param>
        /// <returns>The key, or an empty string when none is configured.</returns>
        /// <remarks>
        /// <para>
        /// A key file is preferred where the deployment can arrange it, because an environment variable is
        /// visible to anything that can enumerate a process and a file can be given an owner and a mode. The
        /// file is read once, at start-up.
        /// </para>
        /// <para>
        /// The file can be named twice over, because the two hosts a site runs under are told where it is in
        /// different ways. Under systemd or launchd the deployer passes <c>SITE_SERVICE_KEY_FILE</c> in the
        /// unit's environment. A worker process under IIS does not inherit an environment a deployment can
        /// set, so there the deployer writes the path into the published <c>Web.config</c> as
        /// <c>SiteServiceKeyFile</c>. A file named either way is preferred to a key written either way, so a
        /// value left behind in a settings file cannot override the file a deployment supplied.
        /// </para>
        /// </remarks>
        /// <param name="problem">Set to the path of a key file that was named and could not be read, or to an
        /// empty string when no file was named or the file named could be read.</param>
        private static string ReadKey(NameValueCollection settings, out string problem)
        {
            problem = string.Empty;

            string keyFile = Environment.GetEnvironmentVariable(ServiceKeyFileVariable);

            if (string.IsNullOrWhiteSpace(keyFile))
            {
                keyFile = settings["SiteServiceKeyFile"];
            }

            if (!string.IsNullOrWhiteSpace(keyFile))
            {
                try
                {
                    return System.IO.File.ReadAllText(keyFile.Trim()).Trim();
                }
                catch (Exception exception) when (exception is System.IO.IOException
                                                  || exception is UnauthorizedAccessException
                                                  || exception is ArgumentException
                                                  || exception is NotSupportedException)
                {
                    /*
                     * A key file the site cannot read leaves the site unauthenticated against its service, which
                     * is a visible, diagnosable fault. Falling back to the configuration here would hide it behind
                     * a key that appears to work, and a site quietly reading a service its deployment never named
                     * is a fault that reaches production.
                     *
                     * The path leaves here with the empty key, because it is the one fact that turns this failure
                     * into a fix, and this is the only moment at which it is known.
                     */
                    problem = keyFile.Trim();
                    return string.Empty;
                }
            }

            string fromEnvironment = Environment.GetEnvironmentVariable(ServiceKeyVariable);

            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();

            string configured = settings["SiteServiceKey"];

            return string.IsNullOrWhiteSpace(configured) ? string.Empty : configured.Trim();
        }

        /// <summary>
        /// Turns a configured database location into an absolute path.
        /// </summary>
        /// <param name="configured">The configured value, which may be a virtual path.</param>
        /// <returns>The absolute path.</returns>
        private static string ResolveDatabase(string configured)
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(AuthDatabaseVariable);

            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return System.IO.Path.GetFullPath(fromEnvironment.Trim());
            }

            if (configured.StartsWith("~", StringComparison.Ordinal))
            {
                return MapPath(configured);
            }

            return System.IO.Path.IsPathRooted(configured)
                ? System.IO.Path.GetFullPath(configured)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory(), configured));
        }

        /// <summary>
        /// Maps a virtual path onto the file system.
        /// </summary>
        /// <param name="virtualPath">The virtual path, beginning with a tilde.</param>
        /// <returns>The absolute path.</returns>
        /// <remarks>
        /// The hosting environment is consulted first because it is what knows the application root when the
        /// site is running inside IIS. The directory is derived from the assembly's location when there is no
        /// hosting environment, which is the case in a unit test that exercises this class outside a request.
        /// </remarks>
        private static string MapPath(string virtualPath)
        {
            string mapped = HostingEnvironment.MapPath(virtualPath);

            return string.IsNullOrEmpty(mapped)
                ? System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Directory(), virtualPath.TrimStart('~', '/', '\\').Replace('/', '\\')))
                : mapped;
        }

        /// <summary>
        /// Gets the directory the application is loaded from.
        /// </summary>
        /// <returns>The absolute path.</returns>
        private static string Directory()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            return string.IsNullOrEmpty(baseDirectory)
                ? System.IO.Directory.GetCurrentDirectory()
                : baseDirectory;
        }

        /// <summary>
        /// Reads a whole-number setting, clamping it into a usable range.
        /// </summary>
        /// <param name="settings">The site's application settings.</param>
        /// <param name="key">The setting's name.</param>
        /// <param name="fallback">The value used when neither source carries a usable number.</param>
        /// <param name="minimum">The smallest accepted value.</param>
        /// <param name="maximum">The largest accepted value.</param>
        /// <returns>The value in force.</returns>
        /// <remarks>
        /// A malformed number falls back rather than failing the site. Refusing to serve a page because a
        /// refresh interval was typed wrongly would turn a cosmetic mistake into an outage, and every setting
        /// here has an obvious safe default.
        /// </remarks>
        private static int ReadNumber(
            NameValueCollection settings,
            string key,
            int fallback,
            int minimum,
            int maximum)
        {
            string raw = Read(settings, key, fallback.ToString(CultureInfo.InvariantCulture));

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return fallback;
            }

            return parsed < minimum ? minimum : parsed > maximum ? maximum : parsed;
        }

        /// <summary>
        /// Reads a boolean setting.
        /// </summary>
        /// <param name="settings">The site's application settings.</param>
        /// <param name="key">The setting's name.</param>
        /// <param name="fallback">The value used when neither source carries a usable flag.</param>
        /// <returns>The value in force.</returns>
        private static bool ReadFlag(NameValueCollection settings, string key, bool fallback)
        {
            string raw = Read(settings, key, fallback ? "true" : "false");

            if (raw.Equals("1", StringComparison.Ordinal)) return true;
            if (raw.Equals("0", StringComparison.Ordinal)) return false;

            return bool.TryParse(raw, out bool parsed) ? parsed : fallback;
        }
    }
}
