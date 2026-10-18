using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Web;
using System.Web.SessionState;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// Issues and verifies the token that proves a form came from this site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every page that changes something renders one of these into a hidden field, and every handler that
    /// changes something verifies it before it acts. Without it a page on another site can make a signed-in
    /// operator's browser post a change here, which the browser will do happily because the session cookie
    /// travels with the request whatever page asked for it.
    /// </para>
    /// <para>
    /// The token is stored in the session and compared in fixed time. Storage in the session is what ties it
    /// to a particular operator: a value that was simply random would still be one an attacker could read off
    /// their own copy of the page and reuse. Comparison in fixed time is the same reasoning as the shared key
    /// on the services -- a comparison that stops at the first differing byte tells the caller how much of a
    /// guess was right.
    /// </para>
    /// <para>
    /// The token is per session and not per request. A per-request token would break the moment an operator
    /// had two tabs open on the same form, which is an ordinary thing to do, and a security measure that
    /// interrupts ordinary work is one that gets switched off.
    /// </para>
    /// </remarks>
    public static class CsrfToken
    {
        /// <summary>
        /// The name of the hidden field the token travels in.
        /// </summary>
        public const string FieldName = "__rms_token";

        /// <summary>
        /// The session key the token is kept under.
        /// </summary>
        private const string SessionKey = "rms.antiforgery";

        /// <summary>
        /// The token's length, in bytes before encoding.
        /// </summary>
        private const int TokenBytes = 32;

        /// <summary>
        /// Gets the current session's token, creating one when the session has none.
        /// </summary>
        /// <param name="session">The session, which may be null when sessions are not yet available.</param>
        /// <returns>The token, or an empty string when there is no session to hold it in.</returns>
        public static string Issue(HttpSessionState session)
        {
            if (session == null) return string.Empty;

            string existing = session[SessionKey] as string;

            if (!string.IsNullOrEmpty(existing)) return existing;

            byte[] bytes = new byte[TokenBytes];

            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            string token = Convert.ToBase64String(bytes);

            session[SessionKey] = token;

            return token;
        }

        /// <summary>
        /// Determines whether a posted token is the one this session was issued.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="supplied">The token the form carried.</param>
        /// <returns><see langword="true"/> when the token is valid.</returns>
        public static bool Verify(HttpSessionState session, string supplied)
        {
            if (session == null) return false;

            string expected = session[SessionKey] as string;

            // A session with no token cannot verify anything. This is the state a fresh session is in before
            // any page has been rendered, so a post that arrives first is refused rather than credited.
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied)) return false;

            return FixedTimeEquals(expected, supplied);
        }

        /// <summary>
        /// Compares two strings without an early exit.
        /// </summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <returns><see langword="true"/> when the values are equal.</returns>
        private static bool FixedTimeEquals(string left, string right)
        {
            int difference = left.Length ^ right.Length;
            int length = Math.Max(left.Length, right.Length);

            for (int index = 0; index < length; index++)
            {
                char a = index < left.Length ? left[index] : '\0';
                char b = index < right.Length ? right[index] : '\0';

                difference |= a ^ b;
            }

            return difference == 0;
        }

        /// <summary>
        /// Renders the token as a hidden input.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns>The markup.</returns>
        public static string RenderHidden(HttpSessionState session)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "<input type=\"hidden\" name=\"{0}\" value=\"{1}\" />",
                FieldName,
                Html.Encode(Issue(session)));
        }
    }
}
