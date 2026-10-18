using System;
using System.Globalization;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using Scanner.WebForms.Infrastructure;

namespace Scanner.WebForms.Api
{
    /// <summary>
    /// Fetches an asset's content and sends it to the browser under the asset's own name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Roblox content address is signed and time-limited and cannot be fetched without authority, so it cannot
    /// be put into a page when the page is rendered: by the time anybody followed such a link it would have
    /// expired, and the address would have been written into the page's own source where it could be copied out.
    /// It is resolved when the link is followed, which is what makes it current, and it is never rendered
    /// anywhere or kept.
    /// </para>
    /// <para>
    /// The bytes come back through the API rather than the reader being sent to the address, and the reason is
    /// the file's name. A browser following a content address saves the file under the last segment of that
    /// address, which is a hash Roblox generated: the right bytes under a name that identifies nothing, with the
    /// asset's identifier -- the one string that identifies this file everywhere else -- appearing nowhere. The
    /// API is the only place that knows both the identifier and the kind of content Roblox said it was, so the
    /// name is decided there and passed on here unchanged.
    /// </para>
    /// <para>
    /// Reading through the API is also what keeps this handler from being a way to reach Roblox with the site's
    /// authority. The address is one the API resolved and validated against Roblox's own content hosts, and this
    /// side never composes an address, never appends to one, and never follows one a caller supplied.
    /// </para>
    /// </remarks>
    public class DownloadHandler : ServiceProxyHandler
    {
        /// <summary>
        /// Gets a value indicating whether the caller must have signed in.
        /// </summary>
        /// <remarks>
        /// It must not. The catalogue and a model's page are readable by anybody, and this is the link on them:
        /// a visitor who could read that a model exists but not fetch it would have been shown the site's
        /// purpose and refused it.
        /// </remarks>
        protected override bool RequiresSignIn
        {
            get { return false; }
        }

        /// <summary>
        /// Builds the response.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        protected override void Handle(HttpContext context)
        {
            long identifier;

            if (!long.TryParse(
                    ReadText(context, "id", 20),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out identifier)
                || identifier <= 0)
            {
                // The model page explains a missing identifier better than a handler can, so the reader is sent to
                // it rather than told here.
                Back(context, 0, string.Empty);
                return;
            }

            ServiceResult<ServiceFile> fetched = ScannerService.Download(Client, identifier);

            if (!fetched.IsSuccess)
            {
                Back(context, identifier, CodeFor(fetched.StatusCode, fetched.Reason));
                return;
            }

            Send(context, fetched.Value);
        }

        /// <summary>
        /// Writes a fetched asset to the response.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <param name="file">The contents, the name to send them under and the type to send them as.</param>
        /// <remarks>
        /// The disposition is an attachment rather than inline: this address is a download link, and a model sent
        /// inline is a model a browser tries to display. The length is stated because the whole file is already in
        /// memory, and a browser told how large it is can report progress instead of waiting on a response that
        /// looks like it may never end.
        /// </remarks>
        private static void Send(HttpContext context, ServiceFile file)
        {
            context.Response.ContentType = file.ContentType;
            context.Response.AddHeader(
                "Content-Disposition",
                "attachment; filename=\"" + file.FileName + "\"");
            context.Response.AddHeader("Content-Length", file.Content.Length.ToString(CultureInfo.InvariantCulture));

            context.Response.OutputStream.Write(file.Content, 0, file.Content.Length);
            context.Response.Flush();
        }

        /// <summary>
        /// Sends the reader back to the model's page with a short reason.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        /// <param name="identifier">The identifier, or zero when there was not one.</param>
        /// <param name="status">The status word, which the page turns into a sentence.</param>
        /// <remarks>
        /// <para>
        /// A redirect rather than a rendered body, so that the explanation comes from the page that owns it. Two
        /// places that describe a failed download would be two places that could describe it differently, and the
        /// handler would then have to carry markup of its own.
        /// </para>
        /// <para>
        /// The redirect is written without ending the response, so this returns to its caller and the request
        /// finishes normally. Ending the response throws to unwind the request, and the catch that keeps a
        /// handler's exceptions off the reader's screen would have answered a redirect with a server error.
        /// </para>
        /// </remarks>
        private static void Back(HttpContext context, long identifier, string status)
        {
            string address = "/Model.aspx";

            if (identifier > 0)
            {
                address += "?id=" + identifier.ToString(CultureInfo.InvariantCulture)
                           + "&status=" + Uri.EscapeDataString(status);
            }

            context.Response.Redirect(address, endResponse: false);
        }

        /// <summary>
        /// Translates a failed call into the page's own status word.
        /// </summary>
        /// <param name="status">The status the API answered with.</param>
        /// <param name="reason">The word the API used for the failure, when it used one.</param>
        /// <returns>The word.</returns>
        /// <remarks>
        /// <para>
        /// The API's word is read before its status, because the status is the one thing several different
        /// causes have in common. A refusal by Roblox's own delivery service and a request that never left
        /// this machine both arrive as a bad gateway, and the page would then have to guess which happened --
        /// which it did, and guessed wrong: it named a cookie that was missing as the usual cause of a
        /// refusal that a cookie present and out of date produces just as readily. The two are told apart
        /// here, where the answer already knows, rather than there, where it is inventing.
        /// </para>
        /// <para>
        /// A failure this site caused is still its own: a status of 401 or 403 means the API did not accept
        /// this site's key, which is why the words above name the API's upstream and the switch below names
        /// the API itself.
        /// </para>
        /// </remarks>
        private static string CodeFor(System.Net.HttpStatusCode status, string reason)
        {
            switch (reason)
            {
                // The API holds no session at all, so nothing can be resolved until an operator pastes one.
                case "nocookie":
                    return "nocookie";

                // The API holds a session and Roblox would not accept it: it has expired, the account signed
                // out, or the account's password changed.
                case "unauthenticated":
                    return "stale";

                // Roblox has no content address for this asset, which is what removal and moderation look
                // like from here.
                case "removed":
                    return "unavailable";
            }

            switch (status)
            {
                case System.Net.HttpStatusCode.Forbidden:
                case System.Net.HttpStatusCode.Unauthorized:
                    return "refused";

                case System.Net.HttpStatusCode.NotFound:
                    return "unavailable";

                case System.Net.HttpStatusCode.ServiceUnavailable:
                    return "offline";

                // 502 from this API is not a proxy failure: the API is reached over loopback, so there is no
                // proxy in the path. It is the API's own upstream call failing, which for this route means
                // Roblox's delivery service answered in a way that produced no address. That is a different
                // problem from the API being down and has a different fix.
                case System.Net.HttpStatusCode.BadGateway:
                    return "unresolved";

                case System.Net.HttpStatusCode.RequestTimeout:
                    return "slow";

                default:
                    return "unusable";
            }
        }
    }
}
