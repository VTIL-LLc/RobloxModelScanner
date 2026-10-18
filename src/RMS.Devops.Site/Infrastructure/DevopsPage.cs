using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace RMS.Devops.Site.Infrastructure
{
    /// <summary>
    /// The base class for every page of the DevOps console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It supplies the calls the pages make to the service and the small pieces of reading that every page
    /// needs: a bounded value out of the query string, a bounded number, and the way a failed call is
    /// described. The calls themselves live in <see cref="DevopsService"/>, so that a page and the handler
    /// refreshing one of its regions cannot disagree about a route.
    /// </para>
    /// <para>
    /// Every call returns a result rather than throwing. A service that is down, that has not been given a
    /// key, or that has nothing configured is a condition these pages exist to describe, and each page decides
    /// how loudly to say it through <see cref="RenderFailure{T}"/>.
    /// </para>
    /// </remarks>
    public abstract class DevopsPage : ServicePage
    {
        /// <summary>
        /// The largest number of runs any page will ask for in one call.
        /// </summary>
        /// <remarks>
        /// The service caps the page size itself, and this is the same limit stated on this side. A page that
        /// asked for more would get less and would have to notice; asking for exactly what it will show keeps
        /// the arithmetic in the pager honest.
        /// </remarks>
        protected const int PageSize = 50;

        /// <summary>
        /// Gets the sections shown in the navigation bar.
        /// </summary>
        /// <returns>The sections.</returns>
        protected override IEnumerable<KeyValuePair<string, string>> Navigation()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Overview", "/"),
                new KeyValuePair<string, string>("Services", "/Services.aspx"),
                new KeyValuePair<string, string>("History", "/History.aspx"),
                new KeyValuePair<string, string>("Artefacts", "/Artifacts.aspx"),
                new KeyValuePair<string, string>("Catalogue", "/Catalogue.aspx"),
            };
        }

        /// <summary>
        /// Reads a service name from the query string.
        /// </summary>
        /// <returns>The name, or an empty string when the address did not carry a usable one.</returns>
        /// <remarks>
        /// The name is not validated for shape here. The page it feeds reports a missing name rather than
        /// calling the service, and a name that is present but wrong is answered by the service with a refusal
        /// the page reports -- which is a more useful message than "that is not a legal name", because it
        /// distinguishes a typo from a service that has stopped.
        /// </remarks>
        protected string ServiceName
        {
            get { return ReadQuery("name", 128); }
        }

        /// <summary>
        /// Reads a run identifier from the query string.
        /// </summary>
        /// <returns>The identifier, or an empty string.</returns>
        protected string RunId
        {
            get { return ReadQuery("run", 64); }
        }

        /// <summary>
        /// Reads how many rows the current page skips.
        /// </summary>
        /// <returns>The offset, clamped to a range the service can answer.</returns>
        protected int Skip
        {
            get { return ReadNumber("skip", 0, 0, 10_000_000); }
        }

        /// <summary>
        /// Reads how many rows the current page asks for.
        /// </summary>
        /// <returns>The page size, clamped.</returns>
        protected int Take
        {
            get { return ReadNumber("take", PageSize, 1, 500); }
        }

        /// <summary>
        /// Reads a whole number from the query string.
        /// </summary>
        /// <param name="name">The parameter's name.</param>
        /// <param name="fallback">The value used when the parameter is absent or unusable.</param>
        /// <param name="minimum">The smallest accepted value.</param>
        /// <param name="maximum">The largest accepted value.</param>
        /// <returns>The value.</returns>
        protected int ReadNumber(string name, int fallback, int minimum, int maximum)
        {
            string raw = Request.QueryString[name];

            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            int parsed;

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return fallback;

            return parsed < minimum ? minimum : parsed > maximum ? maximum : parsed;
        }

        /// <summary>
        /// Renders a failed call as something an operator can act on.
        /// </summary>
        /// <param name="status">The status the service answered with, or the one this site synthesised.</param>
        /// <param name="error">The sentence the client recorded.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// A refusal and an outage are shown differently, because they have different remedies. A refusal means
        /// the two ends disagree about a secret; an outage means the service is not there. A page that showed
        /// both as "could not load" would send the operator to the wrong machine roughly half the time.
        /// </remarks>
        protected string RenderFailure(HttpStatusCode status, string error)
        {
            return DevopsViews.Failure(status, error);
        }

        /// <summary>
        /// Renders a failed call as something an operator can act on.
        /// </summary>
        /// <typeparam name="T">The shape the call was read into.</typeparam>
        /// <param name="result">The failed result.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// This overload exists so a page can pass the result it already holds rather than unpacking two
        /// properties at every call site. The generic class cannot be the parameter's type because the pages
        /// read several different shapes, and none of them is an ancestor of the others.
        /// </remarks>
        protected string RenderFailure<T>(ServiceResult<T> result)
            where T : class
        {
            if (result == null) return string.Empty;

            return RenderFailure(result.StatusCode, result.Error);
        }

        /// <summary>
        /// Loads the machine's summary.
        /// </summary>
        /// <returns>The result.</returns>
        protected ServiceResult<FleetSummary> LoadFleet()
        {
            return DevopsService.Fleet(Client);
        }

        /// <summary>
        /// Loads the service list.
        /// </summary>
        /// <param name="recent">How many recent runs to include with each service, or zero for none.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<List<ServiceSnapshot>> LoadServices(int recent)
        {
            return DevopsService.Services(Client, recent);
        }

        /// <summary>
        /// Loads one service.
        /// </summary>
        /// <param name="name">The service's name.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<ServiceSnapshot> LoadService(string name)
        {
            return DevopsService.Service(Client, name);
        }

        /// <summary>
        /// Loads a page of runs, across every service or within one.
        /// </summary>
        /// <param name="service">The service to restrict to, or an empty string for all of them.</param>
        /// <param name="skip">How many runs to skip.</param>
        /// <param name="take">How many runs to take.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<RunPage> LoadRuns(string service, int skip, int take)
        {
            return DevopsService.Runs(Client, service, skip, take);
        }

        /// <summary>
        /// Loads one run.
        /// </summary>
        /// <param name="service">The service's name.</param>
        /// <param name="run">The run's identifier.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<RunDetail> LoadRun(string service, string run)
        {
            return DevopsService.Run(Client, service, run);
        }

        /// <summary>
        /// Loads a slice of a run's log.
        /// </summary>
        /// <param name="service">The service's name.</param>
        /// <param name="run">The run's identifier.</param>
        /// <param name="offset">The byte offset to begin at.</param>
        /// <param name="count">The most bytes to read, or zero for the service's own default.</param>
        /// <param name="tail">Whether to read the end of the log instead of the beginning.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<LogSlice> LoadLog(string service, string run, long offset, int count, bool tail)
        {
            return DevopsService.Log(Client, service, run, offset, count, tail);
        }

        /// <summary>
        /// Loads the published artefacts.
        /// </summary>
        /// <returns>The result.</returns>
        protected ServiceResult<ArtifactList> LoadArtifacts()
        {
            return DevopsService.Artifacts(Client);
        }

        /// <summary>
        /// Loads the catalogue summary.
        /// </summary>
        /// <returns>The result.</returns>
        protected ServiceResult<CatalogSummary> LoadCatalogue()
        {
            return DevopsService.Catalogue(Client);
        }

        /// <summary>
        /// Renders an empty state.
        /// </summary>
        /// <param name="message">The sentence explaining what is missing.</param>
        /// <returns>The markup.</returns>
        protected static string Empty(string message)
        {
            return "<p class=\"text-muted empty-note\">" + Html.Encode(message) + "</p>";
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// This forwards to <c>DevopsViews.Live</c> so that a page and the fragment handler refreshing
        /// the same region build the same element. A page that wrapped its regions its own way would produce a
        /// region the script then refreshed into a slightly different shape than it started in.
        /// </remarks>
        protected static string Live(string id, string view, string content)
        {
            return DevopsViews.Live(id, view, content);
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes, with parameters of its own.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="extraQuery">Additional query parameters for the fragment, already encoded.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        protected static string Live(string id, string view, string extraQuery, string content)
        {
            return DevopsViews.Live(id, view, extraQuery, content);
        }
    }
}
