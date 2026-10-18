using RMS.Site.Common.Implementation;

namespace RMS.Configuration.Site
{
    /// <summary>
    /// The administration console of the configuration console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The console itself is <see cref="AdminConsolePage"/>, which every site in this stack shares: the accounts,
    /// the notices and the audit trail do not differ between a site that publishes documentation and a site that
    /// reports on scans, so neither does the page that changes them. This class exists because the page's own
    /// directive has to name a type, and because the type it names should belong to this site rather than to the
    /// assembly every site is built on.
    /// </para>
    /// <para>
    /// What this site contributes is its navigation, and that is not written here. The console asks the base page
    /// for the links it should show, and the answer is in <see cref="Infrastructure.ConfigurationPage"/>, which is where this site already
    /// declares the pages it has.
    /// </para>
    /// </remarks>
    public class Admin : AdminConsolePage
    {
    }
}