using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Options;

namespace RMS.Devops.Site
{
    /// <summary>
    /// The application's own lifecycle: it prepares the database at start-up and reports a fault that escaped a
    /// page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The user this site runs as is unlikely to be able to open a console on the machine, so a failure that
    /// only appears in the event log is a failure nobody reads. Everything written here goes beside the
    /// database, in a file an administrator can open, and the same directory is where a fresh deployment
    /// leaves the password it generated so the site can be entered for the first time.
    /// </para>
    /// <para>
    /// The bootstrap password is written once and only when an account was created. A file that was rewritten
    /// on every start-up would be a file that eventually contained the password of an account somebody had
    /// changed, which is worse than no file at all.
    /// </para>
    /// </remarks>
    public class Global : HttpApplication
    {
        /// <summary>
        /// The name the first administrator is created with.
        /// </summary>
        /// <remarks>
        /// Overridable through the environment so that a deployment can choose it, but it defaults to
        /// something obviously not a person. An account named <c>admin</c> on a machine that is reachable from
        /// anywhere is the account that gets guessed first.
        /// </remarks>
        private const string BootstrapUsernameVariable = "SITE_BOOTSTRAP_USER";

        /// <summary>
        /// The password the first administrator is created with, when the deployment supplies one.
        /// </summary>
        private const string BootstrapPasswordVariable = "SITE_BOOTSTRAP_PASSWORD";

        /// <summary>
        /// The file the generated password is left in.
        /// </summary>
        private const string BootstrapFileName = "initial-admin.txt";

        /// <summary>
        /// The file unhandled errors are appended to.
        /// </summary>
        private const string ErrorLogFileName = "site-errors.log";

        /// <summary>
        /// How large the error log is allowed to grow before it is rolled over.
        /// </summary>
        /// <remarks>
        /// Four megabytes is a few thousand entries, which is more history than anybody has ever asked this
        /// file for. The check runs when the log is about to be written, so a site that is running normally
        /// never touches the file system to make it.
        /// </remarks>
        private const long ErrorLogLimitBytes = 4L * 1024L * 1024L;

        /// <summary>
        /// Prepares the site's database and reports whether the sign-in surface is usable.
        /// </summary>
        /// <param name="sender">The application.</param>
        /// <param name="e">The event data.</param>
        /// <remarks>
        /// A fault here is recorded and swallowed rather than thrown. Throwing would take the whole
        /// application down, and a site that cannot create its database is still the only thing that can
        /// explain why -- which it can do perfectly well on the page, from a process that is still answering.
        /// </remarks>
        protected void Application_Start(object sender, EventArgs e)
        {
            try
            {
                SiteOptions settings = SiteOptions.Load(ConfigurationManager.AppSettings);

                AccountStore store = new AccountStore(settings);

                string generated = store.EnsureCreated(BootstrapUsername(), BootstrapPassword());

                if (generated != null)
                {
                    WriteBootstrapCredential(store.DatabasePath, BootstrapUsername(), generated);
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                || exception is Microsoft.Data.Sqlite.SqliteException
                || exception is IOException
                || exception is UnauthorizedAccessException)
            {
                WriteError("startup", exception);
            }
        }

        /// <summary>
        /// Records a fault that escaped a page and sends the visitor to the error page.
        /// </summary>
        /// <param name="sender">The application.</param>
        /// <param name="e">The event data.</param>
        /// <remarks>
        /// The response is redirected rather than written here, so the error page is one page and not two
        /// spellings of the same page. A fault that already produced a response -- a page halfway through
        /// writing its document -- is not redirected, because appending a redirect to a partial document would
        /// render both.
        /// </remarks>
        protected void Application_Error(object sender, EventArgs e)
        {
            Exception exception = Server.GetLastError();

            if (exception == null) return;

            WriteError("request", exception);

            if (Response.StatusCode != 200 || Response.HeadersWritten) return;

            Server.ClearError();

            Response.Redirect("~/Error.aspx", endResponse: true);
        }

        /// <summary>
        /// Reads the bootstrap administrator's name.
        /// </summary>
        /// <returns>The name.</returns>
        private static string BootstrapUsername()
        {
            string configured = Environment.GetEnvironmentVariable(BootstrapUsernameVariable);

            return string.IsNullOrWhiteSpace(configured) ? "rms-operator" : configured.Trim();
        }

        /// <summary>
        /// Reads the bootstrap administrator's password.
        /// </summary>
        /// <returns>The password, or an empty string to have one generated.</returns>
        private static string BootstrapPassword()
        {
            string configured = Environment.GetEnvironmentVariable(BootstrapPasswordVariable);

            return string.IsNullOrWhiteSpace(configured) ? string.Empty : configured;
        }

        /// <summary>
        /// Writes the generated password beside the database.
        /// </summary>
        /// <param name="databasePath">The path of the site's database.</param>
        /// <param name="username">The account's name.</param>
        /// <param name="password">The generated password.</param>
        private static void WriteBootstrapCredential(string databasePath, string username, string password)
        {
            string directory = Path.GetDirectoryName(databasePath);

            if (string.IsNullOrEmpty(directory)) return;

            string path = Path.Combine(directory, BootstrapFileName);

            string body = string.Format(
                CultureInfo.InvariantCulture,
                "The RMS DevOps console created its first account at {0}.{1}{1}"
                + "    account  {2}{1}"
                + "    password {3}{1}{1}"
                + "Sign in at /Account/Login.aspx, then change this password on the Admin page and delete this "
                + "file. It is written once and is never rewritten, so it describes the state of this site when "
                + "it was first started and not the state of it now.{1}",
                DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture),
                Environment.NewLine,
                username,
                password);

            File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <summary>
        /// Appends a fault to the site's error log.
        /// </summary>
        /// <param name="source">Where the fault came from.</param>
        /// <param name="exception">The fault.</param>
        /// <remarks>
        /// The log is written best-effort. A site whose disk has filled, whose directory has been made
        /// read-only, or whose account lacks the right to create the file must still serve pages; failing to
        /// record that something went wrong is not a reason to fail again.
        /// </remarks>
        private static void WriteError(string source, Exception exception)
        {
            try
            {
                string directory = DiagnosticsDirectory();

                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, ErrorLogFileName);

                RollLog(path);

                string entry = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:u} [{1}] {2}{3}{4}{5}{5}native sqlite: {6}{3}{3}",
                    DateTime.UtcNow,
                    source,
                    exception.GetType().FullName,
                    Environment.NewLine,
                    exception,
                    Environment.NewLine,
                    NativeSqlite.Diagnostic);

                File.AppendAllText(path, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception)
            {
                // Deliberately nothing. See the remarks: recording a fault is a courtesy, and a failure to
                // record one must not become a second fault that replaces the page.
            }
        }

        /// <summary>
        /// Finds the directory the site's diagnostics belong in.
        /// </summary>
        /// <returns>The directory.</returns>
        /// <remarks>
        /// <para>
        /// It is the directory the account database is in. A deployment that moved the database somewhere the
        /// machine backs up moved it because the database is the part worth keeping, and the error log is the
        /// part that says why the thing that writes to it stopped. Which directory that is belongs to
        /// <see cref="SiteDiagnostics"/>, because every site writes an error log and one place should answer
        /// where.
        /// </para>
        /// <para>
        /// Reading the settings again is a few property reads and no file access in the normal case, and this
        /// method is only reached when something has already gone wrong. A configuration that cannot be read is
        /// itself a fault, so the diagnostic falls back to the site's own directory rather than joining it in
        /// the log.
        /// </para>
        /// </remarks>
        private static string DiagnosticsDirectory()
        {
            try
            {
                return SiteDiagnostics.Directory(SiteOptions.Load(ConfigurationManager.AppSettings));
            }
            catch (Exception)
            {
                // Deliberately nothing. Whatever was thrown here is already being reported by the caller, and
                // the fallback is a directory that always exists.
                return SiteDiagnostics.Directory(null);
            }
        }

        /// <summary>
        /// Rotates the error log when it has grown past its limit.
        /// </summary>
        /// <param name="path">The log's path.</param>
        private static void RollLog(string path)
        {
            FileInfo existing = new FileInfo(path);

            if (!existing.Exists || existing.Length < ErrorLogLimitBytes) return;

            // One previous generation is kept. A second copy is enough to see what happened before the thing
            // the reader is looking at, and keeping more would turn a diagnostic into a directory of them.
            string previous = path + ".1";

            if (File.Exists(previous)) File.Delete(previous);

            File.Move(path, previous);
        }
    }
}
