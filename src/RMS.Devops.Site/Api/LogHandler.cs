using System;
using System.Globalization;
using System.Text;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site.Api
{
    /// <summary>
    /// Answers with the next piece of a running program's log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only handler that answers with data rather than with markup, and the reason is that a log
    /// is text. Rendering it as markup would mean deciding what a line of a log looks like -- encoding it,
    /// wrapping it, colouring it -- and every one of those decisions is wrong for some program's output. The
    /// page puts the text into the element as text, so nothing in a log can be interpreted as anything.
    /// </para>
    /// <para>
    /// The offset travels with the answer so that the caller can ask for what follows without having to know
    /// how much it received. A caller that computed the next offset itself would be right until the day the
    /// service truncated its slice, and then it would silently skip the middle of the log.
    /// </para>
    /// <para>
    /// Nothing here reads a file. Bounds, existence and identity are all checked by the service, which is the
    /// only program that knows where a service's history is kept.
    /// </para>
    /// </remarks>
    public class LogHandler : ServiceProxyHandler
    {
        /// <summary>
        /// The most bytes a single answer carries.
        /// </summary>
        /// <remarks>
        /// Half a megabyte, which is a few thousand lines. The limit is enforced here as well as at the service
        /// so that this handler cannot be made to hold a large allocation even if the service stopped applying
        /// its own ceiling.
        /// </remarks>
        private const int MaximumCount = 512 * 1024;

        /// <summary>
        /// Builds the response.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        protected override void Handle(HttpContext context)
        {
            string name = ReadText(context, "name", 128);
            string run = ReadText(context, "run", 64);

            if (!IsSafeComponent(name) || !IsSafeComponent(run))
            {
                Refuse(context, 400, "That is not a usable service or run identifier.");
                return;
            }

            long offset;

            if (!long.TryParse(
                    context.Request.QueryString["offset"],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out offset))
            {
                offset = 0;
            }

            if (offset < 0) offset = 0;

            int count = ReadNumber(context, "count", MaximumCount, 1, MaximumCount);
            bool tail = ReadFlag(context, "tail");

            ServiceResult<LogSlice> result = DevopsService.Log(Client, name, run, offset, count, tail);

            if (!result.IsSuccess)
            {
                // The failure keeps the shape the caller expects, so the script does not have to branch on
                // whether it is reading a slice or an error. The status is passed through, which is what makes
                // a lapse of the session distinguishable from a fault of the service.
                Refuse(context, (int)result.StatusCode, result.Error);
                return;
            }

            LogSlice slice = result.Value;

            WriteJson(
                context,
                "{\"name\":" + Json(name)
                + ",\"run\":" + Json(run)
                + ",\"text\":" + Json(slice.Text)
                + ",\"offset\":" + slice.Offset.ToString(CultureInfo.InvariantCulture)
                + ",\"length\":" + slice.Length.ToString(CultureInfo.InvariantCulture)
                + ",\"total\":" + slice.TotalBytes.ToString(CultureInfo.InvariantCulture)
                + ",\"complete\":" + (slice.Complete ? "true" : "false")
                + ",\"limitReached\":" + (slice.LimitReached ? "true" : "false")
                + "}");
        }
    }
}
