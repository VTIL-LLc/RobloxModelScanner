namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// The base class for a page that anybody may reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is one such page in each site -- the sign-in form -- and it is a class of its own because it is
    /// the only page whose rules are the opposite of every other page's. Deriving it from the signed-in base
    /// and turning the check off would mean the check could be turned off anywhere; deriving it from here
    /// means the page that skips it is the page that says so in its own declaration.
    /// </para>
    /// <para>
    /// The chrome is not rendered, because the navigation it offers is a set of links that all require the
    /// session this visitor does not yet have. Showing them would be offering four doors that each bounce the
    /// reader back to where they already are.
    /// </para>
    /// </remarks>
    public abstract class AnonymousPage : SitePage
    {
        /// <inheritdoc />
        protected override bool UsesChrome
        {
            get { return false; }
        }

        /// <summary>
        /// Renders the narrow shell a visitor who is not signed in sees.
        /// </summary>
        /// <param name="heading">The heading.</param>
        /// <param name="body">The body markup.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// The shell is deliberately narrow and centred. The form it wraps carries a password, and a password
        /// field that spans a wide monitor is one a passer-by can read from further away than they should be
        /// able to.
        /// </remarks>
        protected string RenderCard(string heading, string body)
        {
            return "<div class=\"container signin-shell\">"
                   + "<div class=\"panel panel-primary\">"
                   + "<div class=\"panel-heading\"><h3 class=\"panel-title\">"
                   + Html.Encode(Settings.Brand) + "</h3></div>"
                   + "<div class=\"panel-body\">"
                   + "<h4>" + Html.Encode(heading) + "</h4>"
                   + body
                   + "</div></div></div>";
        }
    }
}
