using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace Scanner.WebForms.Infrastructure
{
    /// <summary>
    /// The base class for every page of the scanner site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It supplies the small pieces of reading every page needs -- a bounded value out of the address, the
    /// filter the catalogue is being read with -- and the calls the pages make to the API. The calls themselves
    /// live in <see cref="ScannerService"/>, so a page and the handler that refreshes one of its regions cannot
    /// disagree about a route.
    /// </para>
    /// <para>
    /// Every call returns a result rather than throwing. An API that is down, that has not been given a key, or
    /// whose index has not been built yet are all conditions these pages exist to describe, and each page
    /// decides how much to show when one of them happens.
    /// </para>
    /// </remarks>
    public abstract class ScannerPage : ServicePage
    {
        /// <summary>
        /// The longest search term any page will accept out of an address.
        /// </summary>
        protected const int MaximumSearchLength = 128;

        /// <summary>
        /// The longest rule label any page will accept out of an address.
        /// </summary>
        /// <remarks>
        /// Longer than a search term because a label carries a category and a qualifier as well as a name, and
        /// short enough that a label cannot be turned into a way of making the API scan a large column for a very
        /// long string.
        /// </remarks>
        protected const int MaximumTagLength = 160;

        /// <summary>
        /// The largest page number any page will accept.
        /// </summary>
        /// <remarks>
        /// A catalogue of a hundred and twenty thousand rows at two hundred a page is six hundred pages. A
        /// million is a number that no page can reach and that tells the API to skip a slice of the index that
        /// does not exist, which is answered as an empty page rather than as an error -- so the ceiling is here
        /// only to keep the arithmetic in the pager away from the edge of an integer.
        /// </remarks>
        protected const int MaximumPage = 1_000_000;

        /// <summary>
        /// Gets the sections shown in the navigation bar.
        /// </summary>
        /// <returns>The sections.</returns>
        /// <remarks>
        /// <para>
        /// The catalogue is what the site is for and is offered to everybody. The analytics is what the catalogue
        /// adds up to and the rule reference is what its labels mean; both are written for an operator reading
        /// the whole index, and both are withheld from a visitor who has not signed in -- not by hiding a link,
        /// which would leave the address working, but because the sections are not built for a visitor who could
        /// not open them.
        /// </para>
        /// <para>
        /// A link is a promise. This site's other two pages answer a visitor without a session the same way it
        /// answers an address that was never built, so offering the link would be offering a door that leads to a
        /// wall.
        /// </para>
        /// </remarks>
        protected override IEnumerable<KeyValuePair<string, string>> Navigation()
        {
            List<KeyValuePair<string, string>> sections = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Catalogue", "/"),
            };

            if (IsSignedIn)
            {
                sections.Add(new KeyValuePair<string, string>("Analytics", "/Analytics.aspx"));
                sections.Add(new KeyValuePair<string, string>("Rules", "/Rules.aspx"));
            }

            return sections;
        }

        /// <summary>
        /// Gets a value indicating whether this page is readable without signing in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// False here, so every page of this site requires a session until it says otherwise, and the two that do
        /// are the two that are the site's reason for being published: the catalogue and a single model's page.
        /// A page added later that forgets to think about this inherits the requirement rather than inheriting
        /// its absence, which is the direction the mistake has to fall in.
        /// </para>
        /// <para>
        /// What these pages read is the scanner's own catalogue. It is what the site is for -- a visitor arrives
        /// with a model identifier, reads what the rules found in it, and follows the download link -- and none
        /// of it is account data, run history or configuration, which is what the pages that stay behind the
        /// sign-in hold.
        /// </para>
        /// </remarks>
        protected virtual bool IsPublic
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether this site answers its operator pages as though they did not exist.
        /// </summary>
        /// <remarks>
        /// This site is published to strangers, so it does. See <see cref="SitePage.HidesFromAnonymous"/>, which
        /// is what turns the answer into a 404 rather than a sign-in form, and <see cref="ScannerPage.IsPublic"/>,
        /// which is the exception that lets the catalogue through.
        /// </remarks>
        protected override bool HidesFromAnonymous
        {
            get { return true; }
        }

        /// <summary>
        /// Applies this site's access rule.
        /// </summary>
        /// <remarks>
        /// Two questions, in the order that answers the commonest case first: the catalogue and a model's page
        /// are read by anybody, and everything else is read by an operator. A visitor who is neither is answered
        /// by the base as though the address did not exist.
        /// </remarks>
        protected override void EnforceAccess()
        {
            if (IsPublic || IsSignedIn) return;

            base.EnforceAccess();
        }

        /// <summary>
        /// Gets the filter the catalogue is being read with.
        /// </summary>
        /// <remarks>
        /// Read from this page's own address rather than held in the session, so the filter survives a refresh,
        /// can be sent to somebody else, and leaves the back button working.
        /// </remarks>
        protected CatalogueFilter Filter
        {
            get { return CatalogueFilter.FromQuery(Request.QueryString); }
        }

        /// <summary>
        /// Gets the asset identifier the address names, or zero when it names none.
        /// </summary>
        protected long ModelId
        {
            get
            {
                string raw = ReadQuery("id", 20);

                long parsed;

                return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                       && parsed > 0
                    ? parsed
                    : 0;
            }
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
        /// <param name="status">The status the API answered with, or the one this site synthesised.</param>
        /// <param name="error">The sentence the client recorded.</param>
        /// <returns>The markup.</returns>
        protected string RenderFailure(HttpStatusCode status, string error)
        {
            return ScannerViews.Failure(status, error);
        }

        /// <summary>
        /// Renders a failed call as something an operator can act on.
        /// </summary>
        /// <typeparam name="T">The shape the call was read into.</typeparam>
        /// <param name="result">The failed result.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// This overload exists so a page can pass the result it already holds rather than unpacking two
        /// properties at every call site. The generic class cannot be the parameter's type, because the pages
        /// read several different shapes and none of them is an ancestor of the others.
        /// </remarks>
        protected string RenderFailure<T>(ServiceResult<T> result)
            where T : class
        {
            if (result == null) return string.Empty;

            return RenderFailure(result.StatusCode, result.Error);
        }

        /// <summary>
        /// Loads a page of the catalogue.
        /// </summary>
        /// <param name="filter">The filter to read it with.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<ModelPage> LoadModels(CatalogueFilter filter)
        {
            return ScannerService.Models(Client, filter);
        }

        /// <summary>
        /// Loads one model.
        /// </summary>
        /// <param name="model">The asset identifier.</param>
        /// <returns>The result.</returns>
        protected ServiceResult<ModelPage> LoadModel(long model)
        {
            return ScannerService.Model(Client, model);
        }

        /// <summary>
        /// Loads the whole analytics view.
        /// </summary>
        /// <returns>The result.</returns>
        protected ServiceResult<AnalyticsView> LoadAnalytics()
        {
            return ScannerService.Analytics(Client);
        }

        /// <summary>
        /// Loads the rule reference.
        /// </summary>
        /// <returns>The result.</returns>
        protected ServiceResult<List<RuleStatisticView>> LoadRules()
        {
            return ScannerService.Rules(Client);
        }

        /// <summary>
        /// Reads the labels the rule reference names, for reading historical catalogue rows.
        /// </summary>
        /// <param name="rules">The rule reference, which may have failed.</param>
        /// <returns>The labels, in the reference's order.</returns>
        /// <remarks>
        /// A failed reference yields an empty list rather than an exception. The catalogue is still worth showing
        /// when the rule reference cannot be read -- every row's own counts and name are still true -- and the
        /// page says so through the rows it cannot attribute rather than by refusing to render.
        /// </remarks>
        protected static IReadOnlyList<string> Labels(ServiceResult<List<RuleStatisticView>> rules)
        {
            List<string> labels = new List<string>();

            if (rules == null || !rules.IsSuccess || rules.Value == null) return labels;

            foreach (RuleStatisticView rule in rules.Value)
            {
                if (!string.IsNullOrWhiteSpace(rule.Tag)) labels.Add(rule.Tag);
            }

            return labels;
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        /// <remarks>
        /// This forwards to <see cref="ScannerViews.Live(string, string, string)"/> so that a page and the
        /// fragment handler refreshing the same region build the same element. A page that wrapped its regions
        /// its own way would produce a region that the script then refreshed into a slightly different shape
        /// than it started in.
        /// </remarks>
        protected static string Live(string id, string view, string content)
        {
            return ScannerViews.Live(id, view, content);
        }

        /// <summary>
        /// Wraps a region in the element the site's script refreshes, with parameters of its own.
        /// </summary>
        /// <param name="id">The element's identifier.</param>
        /// <param name="view">The fragment view that rebuilds it.</param>
        /// <param name="extraQuery">Additional query parameters for the fragment, not yet encoded.</param>
        /// <param name="content">The content it starts with.</param>
        /// <returns>The markup.</returns>
        protected static string Live(string id, string view, string extraQuery, string content)
        {
            return ScannerViews.Live(id, view, extraQuery, content);
        }
    }
}
