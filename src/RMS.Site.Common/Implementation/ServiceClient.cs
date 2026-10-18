using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

using RMS.Site.Common.Models;
using RMS.Site.Common.Options;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// Calls the service a site reads, presenting the shared key the browser never sees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only place in a site where the key is attached. Every page and every fragment handler goes
    /// through here, which is what makes it possible to say with certainty that the key is not in a query
    /// string a browser could copy, a page source, or a link an operator might paste into a ticket.
    /// </para>
    /// <para>
    /// One <see cref="HttpClient"/> is shared for the life of the application. A client per request is the
    /// mistake that exhausts the ports on a busy machine: each client owns a connection pool, and disposing
    /// the client does not return the pooled sockets promptly, so a site that creates one per page eventually
    /// runs out of ephemeral ports while reporting that nothing is wrong.
    /// </para>
    /// <para>
    /// A failure is returned as a value rather than thrown. The service this site reads is a separate program
    /// on the same machine, and it being down, slow or unconfigured is an ordinary condition the page has to
    /// describe -- not an exception that replaces the page with an error.
    /// </para>
    /// </remarks>
    public sealed class ServiceClient
    {
        /// <summary>
        /// The largest response body the client will read.
        /// </summary>
        /// <remarks>
        /// A log slice is the largest thing this ever fetches and the service caps those at half a megabyte.
        /// The ceiling here is twice that, so a service that stops honouring its own limit cannot turn one
        /// page into an unbounded allocation.
        /// </remarks>
        private const int MaximumBodyBytes = 1024 * 1024;

        /// <summary>
        /// The largest request body this client will send.
        /// </summary>
        /// <remarks>
        /// The only writable thing in this stack is a manifest, and the largest of those is a dozen kilobytes.
        /// A ceiling a hundred times that is still small enough that a page cannot be talked into posting
        /// something enormous, which is the point of having one at all.
        /// </remarks>
        private const int MaximumRequestBytes = 256 * 1024;

        /// <summary>
        /// The longest sentence accepted from a service's failure document.
        /// </summary>
        /// <remarks>
        /// Every sentence this stack writes is a line or two of prose. The ceiling is generous for those and
        /// short enough that a body which is not one of ours cannot put a page of its own text into a panel:
        /// the document is only trusted when it is small, JSON, and free of markup.
        /// </remarks>
        private const int MaximumFaultLength = 600;

        /// <summary>
        /// The longest word accepted from a service's failure document.
        /// </summary>
        /// <remarks>
        /// A word is an identifier: <c>nocookie</c>, <c>unauthenticated</c>, <c>not_indexed</c>. Anything
        /// longer than a few dozen characters is prose wearing a word's clothes and is read as no word at all.
        /// </remarks>
        private const int MaximumWordLength = 40;

        private static readonly HttpClient Client = CreateClient();

        private readonly SiteOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceClient"/> class.
        /// </summary>
        /// <param name="options">The settings, which name the service and hold the key.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public ServiceClient(SiteOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _options = options;
        }

        /// <summary>
        /// Gets a value indicating whether the site has been given a service to read and a key to read it with.
        /// </summary>
        public bool IsConfigured
        {
            get { return _options.ServiceBaseUrl.Length > 0 && _options.ServiceKey.Length > 0; }
        }

        /// <summary>
        /// Gets the address the site reads, for the diagnostics page.
        /// </summary>
        public string BaseUrl
        {
            get { return _options.ServiceBaseUrl; }
        }

        /// <summary>
        /// Gets the header the shared key is presented in.
        /// </summary>
        private string KeyHeader
        {
            get
            {
                return string.IsNullOrWhiteSpace(_options.ServiceKeyHeader)
                    ? SiteOptions.DefaultServiceKeyHeader
                    : _options.ServiceKeyHeader;
            }
        }

        /// <summary>
        /// Gets the query parameter the shared key is presented in, or an empty string when it travels in a header.
        /// </summary>
        private string KeyQueryParameter
        {
            get { return _options.ServiceKeyQueryParameter ?? string.Empty; }
        }

        /// <summary>
        /// Fetches and deserializes a value from the service.
        /// </summary>
        /// <typeparam name="T">The shape to deserialize into.</typeparam>
        /// <param name="path">The service path, beginning with a slash.</param>
        /// <param name="query">The query parameters, which are encoded here.</param>
        /// <returns>The result.</returns>
        public ServiceResult<T> GetJson<T>(string path, params KeyValuePair<string, string>[] query)
            where T : class
        {
            return Read<T>(Send(path, query, "application/json", null));
        }

        /// <summary>
        /// Posts a value to the service and deserializes its answer.
        /// </summary>
        /// <typeparam name="TRequest">The shape of the body.</typeparam>
        /// <typeparam name="TResult">The shape to deserialize the answer into.</typeparam>
        /// <param name="path">The service path, beginning with a slash.</param>
        /// <param name="body">The value to send.</param>
        /// <returns>The result.</returns>
        /// <remarks>
        /// <para>
        /// This exists for the one kind of call in this stack that changes something: a page that writes a
        /// manifest. Everything else reads.
        /// </para>
        /// <para>
        /// A refusal is answered by the service as a successful response carrying a result that says it was
        /// refused, and only a guard refusal or a clobbered precondition arrives as an HTTP error. That split
        /// is deliberate: "the file is one line wrong" is a thing the page has to render in detail, and a page
        /// that received it as a failure would have to render an error body as though it were a result.
        /// </para>
        /// </remarks>
        public ServiceResult<TResult> PostJson<TRequest, TResult>(string path, TRequest body)
            where TRequest : class
            where TResult : class
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            byte[] payload;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumRequestBytes };

                payload = Encoding.UTF8.GetBytes(serializer.Serialize(body));
            }
            catch (ArgumentException exception)
            {
                return ServiceResult<TResult>.Failure(
                    HttpStatusCode.BadRequest,
                    "The value could not be written: " + exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return ServiceResult<TResult>.Failure(
                    HttpStatusCode.BadRequest,
                    "The value could not be written: " + exception.Message);
            }

            if (payload.Length > MaximumRequestBytes)
            {
                return ServiceResult<TResult>.Failure(
                    HttpStatusCode.RequestEntityTooLarge,
                    "That is larger than this site will send.");
            }

            // The content is disposed here rather than handed to the caller: a request body this client built
            // is this client's to release, and the response is read before the call returns.
            using (ByteArrayContent content = new ByteArrayContent(payload))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

                return Read<TResult>(Send(path, null, "application/json", content));
            }
        }

        /// <summary>
        /// Fetches plain text from the service.
        /// </summary>
        /// <param name="path">The service path, beginning with a slash.</param>
        /// <param name="query">The query parameters, which are encoded here.</param>
        /// <returns>The result.</returns>
        public ServiceResult<string> GetText(string path, params KeyValuePair<string, string>[] query)
        {
            return Send(path, query, "text/plain", null);
        }

        /// <summary>
        /// Builds the address one call is made to.
        /// </summary>
        /// <param name="path">The service path, beginning with a slash.</param>
        /// <param name="query">The query parameters.</param>
        /// <returns>The absolute address.</returns>
        public string BuildUrl(string path, params KeyValuePair<string, string>[] query)
        {
            StringBuilder builder = new StringBuilder(_options.ServiceBaseUrl);

            builder.Append(path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);

            char separator = '?';

            if (query != null)
            {
                foreach (KeyValuePair<string, string> pair in query)
                {
                    if (pair.Value == null) continue;

                    builder.Append(separator)
                           .Append(Uri.EscapeDataString(pair.Key))
                           .Append('=')
                           .Append(Uri.EscapeDataString(pair.Value));

                    separator = '&';
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Appends the shared key to an address as a query parameter.
        /// </summary>
        /// <param name="address">The address, with or without a query string.</param>
        /// <returns>The address carrying the key.</returns>
        /// <remarks>
        /// Both the name and the value are escaped, even though both come from configuration rather than from
        /// a caller. An operator who typed a parameter name containing an ampersand would otherwise build an
        /// address that means something else, and the symptom -- a service that rejects a key it was given --
        /// is worse than the mistake.
        /// </remarks>
        private string AppendKey(string address)
        {
            return address
                   + (address.IndexOf('?') >= 0 ? "&" : "?")
                   + Uri.EscapeDataString(KeyQueryParameter)
                   + "="
                   + Uri.EscapeDataString(_options.ServiceKey);
        }

        /// <summary>
        /// Reads a service answer into the shape a caller asked for.
        /// </summary>
        /// <typeparam name="T">The shape to deserialize into.</typeparam>
        /// <param name="raw">The answer's body, or its failure.</param>
        /// <returns>The result.</returns>
        private static ServiceResult<T> Read<T>(ServiceResult<string> raw)
            where T : class
        {
            if (!raw.IsSuccess) return ServiceResult<T>.Failure(raw.StatusCode, raw.Error, raw.Reason);

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumBodyBytes };

                T value = serializer.Deserialize<T>(raw.Value);

                if (value == null)
                {
                    return ServiceResult<T>.Failure(raw.StatusCode, "The service returned an empty document.");
                }

                return ServiceResult<T>.Success(value);
            }
            catch (ArgumentException exception)
            {
                // A document that cannot be read is reported as a broken service rather than as a broken page,
                // because the page did nothing but ask and the service is the thing that answered wrongly.
                return ServiceResult<T>.Failure(
                    raw.StatusCode,
                    "The service's answer could not be read: " + exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return ServiceResult<T>.Failure(
                    raw.StatusCode,
                    "The service's answer could not be read: " + exception.Message);
            }
        }

        /// <summary>
        /// Reads a body off a response, bounded, answering nothing when it cannot be read.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns>The bytes, or an empty array.</returns>
        /// <remarks>
        /// A body that cannot be read is not this client's failure to report: the status is what the caller
        /// acts on and the sentence is a nicety beside it. Anything larger than this site will read is
        /// discarded for the same reason, and before it is parsed rather than after.
        /// </remarks>
        private static byte[] ReadBody(HttpResponseMessage response)
        {
            try
            {
                byte[] body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

                return body != null && body.Length <= MaximumBodyBytes ? body : Array.Empty<byte>();
            }
            catch (Exception exception) when (exception is HttpRequestException
                                              || exception is IOException
                                              || exception is ObjectDisposedException
                                              || exception is InvalidOperationException)
            {
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Reads the failure document a service answered with, when it answered with one.
        /// </summary>
        /// <param name="body">The body, as it was read.</param>
        /// <returns>The document, or <see langword="null"/> when the body is not one.</returns>
        /// <remarks>
        /// The sentence is going to be rendered on a page, so it is accepted only if it looks like one of
        /// this stack's: short, and free of markup. A proxy, a router, or a server that answered with a page
        /// of HTML must not be able to put that page's text into one of this site's panels.
        /// </remarks>
        private static ServiceFault ReadFault(byte[] body)
        {
            if (body == null || body.Length == 0) return null;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumBodyBytes };

                ServiceFault fault = serializer.Deserialize<ServiceFault>(Encoding.UTF8.GetString(body));

                if (fault == null || string.IsNullOrWhiteSpace(fault.Message)) return null;

                string message = fault.Message.Trim();

                if (message.Length > MaximumFaultLength || message.IndexOf('<') >= 0) return null;

                string word = fault.Error == null ? null : fault.Error.Trim();

                if (word != null && (word.Length == 0 || word.Length > MaximumWordLength || word.IndexOf('<') >= 0))
                {
                    word = null;
                }

                return new ServiceFault { Message = message, Error = word };
            }
            catch (Exception exception) when (exception is ArgumentException
                                              || exception is InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Sends one request and reads its body.
        /// </summary>
        /// <param name="path">The service path, beginning with a slash.</param>
        /// <param name="query">The query parameters.</param>
        /// <param name="accept">The media type the caller wants.</param>
        /// <param name="content">The body to send, or <see langword="null"/> for a read.</param>
        /// <returns>The result.</returns>
        private ServiceResult<string> Send(
            string path,
            KeyValuePair<string, string>[] query,
            string accept,
            HttpContent content)
        {
            if (!IsConfigured)
            {
                /*
                 * The site has nothing to present, and there are two ways to arrive here with different
                 * remedies. Being named a key file the site cannot read is reported by its path, because only
                 * the site knows which file was meant; a site that was named nothing is reported as such. A
                 * single sentence covering both would send an operator looking for a service fault that does
                 * not exist.
                 */
                return ServiceResult<string>.Failure(
                    HttpStatusCode.ServiceUnavailable,
                    _options.ServiceKeyFileProblem.Length > 0
                        ? "This site was given the path of a key file and cannot read it: "
                          + _options.ServiceKeyFileProblem
                          + ". With no key to present it can read nothing."
                        : "This site has not been given a service address and a shared key, so it has nothing "
                          + "to read.");
            }

            string address = BuildUrl(path, query);

            // The key travels in the header or in the query, never in both. Building it here rather than in
            // BuildUrl is deliberate: BuildUrl is public and its result is a string a page is free to render,
            // and the one place the key must never reach is a document.
            if (KeyQueryParameter.Length > 0) address = AppendKey(address);

            HttpRequestMessage request = null;

            // The budget is applied per request rather than on the shared client, because a client's timeout
            // belongs to the client and the shared one has several callers. A cancellation source per call
            // gives every request its own clock without giving every request its own connection pool.
            using (System.Threading.CancellationTokenSource timeout =
                   new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_options.ServiceTimeoutSeconds)))
            {
            try
            {
                request = new HttpRequestMessage(content == null ? HttpMethod.Get : HttpMethod.Post, address);

                if (KeyQueryParameter.Length == 0)
                {
                    request.Headers.TryAddWithoutValidation(KeyHeader, _options.ServiceKey);
                }

                request.Headers.TryAddWithoutValidation("Accept", accept);

                if (content != null) request.Content = content;

                using (HttpResponseMessage response = Client.SendAsync(request, timeout.Token).GetAwaiter().GetResult())
                {
                    // The request message is owned from here on; disposing it with the response keeps the
                    // call from throwing when the same client is used for the next one.
                    request = null;

                    if (!response.IsSuccessStatusCode)
                    {
                        /*
                         * A service in this stack states its own failures: the body is a document of the shape
                         * {"error": "...", "message": "..."}, whose message is written for a reader and whose
                         * error word is written for a caller. Both are kept. The sentence this client used to
                         * substitute said only that something had failed, and a page telling an operator "the
                         * service is not available" about a service that answered perfectly well with a
                         * refusal sends them looking for a fault that does not exist.
                         */
                        ServiceFault fault = ReadFault(ReadBody(response));

                        return fault == null
                            ? ServiceResult<string>.Failure(response.StatusCode, Describe(response.StatusCode))
                            : ServiceResult<string>.Failure(response.StatusCode, fault.Message, fault.Error);
                    }

                    byte[] body = ReadBody(response);

                    if (body.Length > MaximumBodyBytes)
                    {
                        return ServiceResult<string>.Failure(
                            response.StatusCode,
                            "The service's answer was larger than this site will read.");
                    }

                    return ServiceResult<string>.Success(Encoding.UTF8.GetString(body));
                }
            }
            catch (HttpRequestException exception)
            {
                return ServiceResult<string>.Failure(HttpStatusCode.ServiceUnavailable, exception.Message);
            }
            catch (TaskCanceledException)
            {
                // A timeout surfaces as a cancellation rather than as a request exception, and the timeout is
                // what an operator actually needs to hear: the service is up but is not answering in time.
                return ServiceResult<string>.Failure(
                    HttpStatusCode.GatewayTimeout,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The service did not answer within {0} seconds.",
                        _options.ServiceTimeoutSeconds));
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is UriFormatException)
            {
                return ServiceResult<string>.Failure(HttpStatusCode.ServiceUnavailable, exception.Message);
            }
            finally
            {
                if (request != null) request.Dispose();
            }
            }
        }

        /// <summary>
        /// Turns a status code into a sentence an operator can act on.
        /// </summary>
        /// <param name="status">The status the service returned.</param>
        /// <returns>The sentence.</returns>
        /// <remarks>
        /// The refusals are named individually because each one has a different remedy, and a page that said
        /// only "the request failed" would send the operator looking in the wrong place. The shared key is
        /// never named as a value, only as a thing to check, because this text ends up rendered.
        /// </remarks>
        private static string Describe(HttpStatusCode status)
        {
            switch (status)
            {
                case HttpStatusCode.NotFound:
                    return "The service does not have what this page asked for.";

                case HttpStatusCode.Forbidden:
                    return "The service refused this site. Check that the site's key matches the service's, and " +
                           "that the address the site calls from is on the service's allow-list.";

                case HttpStatusCode.Unauthorized:
                    return "The service did not accept this site's credentials.";

                case HttpStatusCode.BadGateway:
                case HttpStatusCode.ServiceUnavailable:
                    return "The service is not available.";

                case HttpStatusCode.GatewayTimeout:
                    return "The service did not answer in time.";

                case HttpStatusCode.Conflict:
                    return "The file has changed since this page read it, so the change was not applied. " +
                           "Reload the page and make the change again.";

                case HttpStatusCode.RequestEntityTooLarge:
                    return "That was larger than this site will send.";

                default:
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "The service answered {0} ({1}).",
                        (int)status,
                        status);
            }
        }

        /// <summary>
        /// Creates the shared client.
        /// </summary>
        /// <returns>The client.</returns>
        private static HttpClient CreateClient()
        {
            // The handler is configured rather than left to its defaults: automatic decompression is off, so
            // the size ceiling above applies to what actually arrives; redirection is off, so a service cannot
            // be made to send this site's key to somewhere else; and cookies are off, because this client has
            // no session and a cookie it never sent is a cookie it cannot leak.
            HttpClientHandler handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                AllowAutoRedirect = false,
                UseCookies = false,
                UseDefaultCredentials = false,
            };

            HttpClient client = new HttpClient(handler, disposeHandler: true);

            // The clock is per request, so the shared client imposes none of its own. Its default would be a
            // hundred seconds applied to every caller, and the budget that matters is the one the site's own
            // configuration names.
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

            return client;
        }

        /// <summary>
        /// The failure document this stack's services answer with.
        /// </summary>
        /// <remarks>
        /// Two fields, named as they appear on the wire. The message is the sentence a reader is shown and the
        /// error is the word a caller decides with; a service that states neither is one this client describes
        /// for itself.
        /// </remarks>
        private sealed class ServiceFault
        {
            /// <summary>
            /// Gets or sets the sentence written for a reader.
            /// </summary>
            public string Message { get; set; }

            /// <summary>
            /// Gets or sets the word written for a caller.
            /// </summary>
            public string Error { get; set; }
        }
    }
}
