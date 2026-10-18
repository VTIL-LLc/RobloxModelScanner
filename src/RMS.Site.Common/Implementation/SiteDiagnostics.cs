using System;
using System.IO;
using System.Web;

using RMS.Site.Common.Options;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// Decides where a site's own diagnostics are written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the directory the account database is in, not the directory the site is in. A deployment that
    /// moved the database somewhere the machine backs up moved it because the database is the part worth
    /// keeping, and the error log is the part that says why the thing that writes to it stopped. The two
    /// belong together, and the file carrying the first administrator's password is written here for the same
    /// reason.
    /// </para>
    /// <para>
    /// Every site leaves its own <c>App_Data</c> behind as the fallback, for the case where the settings cannot
    /// be read at all -- in which case the failure being reported may well be that failure. Nothing here reads
    /// the disk; the settings are property reads over the configuration collection the application already
    /// holds.
    /// </para>
    /// <para>
    /// This is one of the few places in the shared library that names a directory rather than using one it was
    /// given, which is why it is here and not in the signing-in code: every site writes an error log and every
    /// site should say where, and a second answer to that question is a second place to look for a fault.
    /// </para>
    /// </remarks>
    public static class SiteDiagnostics
    {
        /// <summary>
        /// The directory inside the site that diagnostics fall back to.
        /// </summary>
        /// <remarks>
        /// <c>App_Data</c> is a hidden segment under the request filtering every supported server applies, so a
        /// file written here is a file no visitor can ask for.
        /// </remarks>
        public const string FallbackDirectoryName = "App_Data";

        /// <summary>
        /// Finds the directory a site's diagnostics belong in.
        /// </summary>
        /// <param name="settings">The site's settings.</param>
        /// <returns>The directory. It is not created; the caller is about to write into it.</returns>
        public static string Directory(SiteOptions settings)
        {
            if (settings != null)
            {
                string database = settings.AuthDatabasePath;

                if (!string.IsNullOrWhiteSpace(database))
                {
                    string directory = Path.GetDirectoryName(database);

                    if (!string.IsNullOrEmpty(directory)) return directory;
                }
            }

            return Path.Combine(HttpRuntime.AppDomainAppPath ?? ".", FallbackDirectoryName);
        }
    }
}
