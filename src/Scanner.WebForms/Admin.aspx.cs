using RMS.Site.Common.Implementation;

namespace Scanner.WebForms
{
    /// <summary>
    /// The administration console of the analytics console.
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
    /// for the links it should show, and the answer is in <see cref="Infrastructure.ScannerPage"/>, which is where this site already
    /// declares the pages it has.
    /// </para>
    /// </remarks>
    public class Admin : AdminConsolePage
    {
        /// <summary>
        /// Gets a value indicating whether this address is answered as though it did not exist.
        /// </summary>
        /// <remarks>
        /// This is the site's own addition to the console every site shares. The console sits on an internal
        /// network everywhere else in this stack; here it is on the same host as a catalogue that strangers are
        /// invited to read, so a visitor who is not an administrator is told nothing about whether the address is
        /// here at all. The role is still what decides, and it is still checked by the console itself -- this
        /// only decides what a visitor who has no session is told.
        /// </remarks>
        protected override bool HidesFromAnonymous
        {
            get { return true; }
        }
    }
}