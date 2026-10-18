using System;
using System.Collections.Generic;
using System.Web;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Devops.Site.Infrastructure;

namespace RMS.Devops.Site.Api
{
    /// <summary>
    /// Rebuilds one region of a page as markup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The site's regions refresh by replacing their contents with an answer from here. The answer is markup
    /// and not data, and that is the point: the table that arrives is built by the same method that built the
    /// one already on the page, so a column cannot exist in one and be missing from the other, and a fix to a
    /// rendering cannot be applied in one place only.
    /// </para>
    /// <para>
    /// The view is a name from a fixed list, never a path. This handler reaches the service with the site's
    /// key, so a handler that could be pointed anywhere would be an open proxy standing inside the trust
    /// boundary -- which is the worst place to put one.
    /// </para>
    /// <para>
    /// Every response is one round trip to the service. A page with four regions makes four calls when it
    /// refreshes, which is what keeps one slow region from holding up the other three.
    /// </para>
    /// </remarks>
    public class FragmentHandler : ServiceProxyHandler
    {
        /// <summary>
        /// Builds the response.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        protected override void Handle(HttpContext context)
        {
            switch (ReadText(context, "view", 32))
            {
                case "fleet": Fleet(context); break;
                case "services": Services(context); break;
                case "service": Service(context); break;
                case "service-runs": ServiceRuns(context); break;
                case "runs": Runs(context); break;
                case "artifacts": Artifacts(context); break;
                case "catalogue": Catalogue(context); break;
                default: Refuse(context, 400, "That is not one of the views this site builds."); break;
            }
        }

        /// <summary>
        /// Answers with the machine's summary.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Fleet(HttpContext context)
        {
            ServiceResult<FleetSummary> result = DevopsService.Fleet(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DevopsViews.FleetHeader(result.Value)
                    : DevopsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with the service table.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Services(HttpContext context)
        {
            ServiceResult<List<ServiceSnapshot>> result = DevopsService.Services(Client, recent: 0);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DevopsViews.ServiceTable(
                        result.Value ?? new List<ServiceSnapshot>(),
                        ReadFlag(context, "detailed"))
                    : DevopsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with one service's facts.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Service(HttpContext context)
        {
            string name = ReadText(context, "name", 128);

            if (!IsSafeComponent(name))
            {
                Refuse(context, 400, "That is not a usable service name.");
                return;
            }

            ServiceResult<ServiceSnapshot> result = DevopsService.Service(Client, name);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DevopsViews.ServiceFacts(result.Value)
                    : DevopsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with one service's most recent runs.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void ServiceRuns(HttpContext context)
        {
            string name = ReadText(context, "name", 128);

            if (!IsSafeComponent(name))
            {
                Refuse(context, 400, "That is not a usable service name.");
                return;
            }

            ServiceResult<ServiceSnapshot> result = DevopsService.Service(Client, name);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DevopsViews.RunTable(RecentRunsOf(result.Value), showService: false)
                    : DevopsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with a page of runs and the pager beneath it.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Runs(HttpContext context)
        {
            string service = ReadText(context, "service", 128);

            if (service.Length > 0 && !IsSafeComponent(service))
            {
                Refuse(context, 400, "That is not a usable service name.");
                return;
            }

            int skip = ReadNumber(context, "skip", 0, 0, 10_000_000);
            int take = ReadNumber(context, "take", 50, 1, 500);

            ServiceResult<RunPage> result = DevopsService.Runs(Client, service, skip, take);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DevopsViews.RunList(result.Value, showService: true, basePath: "/History.aspx")
                    : DevopsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with the artefact table.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Artifacts(HttpContext context)
        {
            ServiceResult<ArtifactList> result = DevopsService.Artifacts(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DevopsViews.ArtifactTable(result.Value.Artifacts)
                    : DevopsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Answers with the catalogue summary.
        /// </summary>
        /// <param name="context">The request and its response.</param>
        private void Catalogue(HttpContext context)
        {
            ServiceResult<CatalogSummary> result = DevopsService.Catalogue(Client);

            WriteHtml(
                context,
                result.IsSuccess
                    ? DevopsViews.CatalogPanel(result.Value)
                    : DevopsViews.Failure(result.StatusCode, result.Error));
        }

        /// <summary>
        /// Gets a service's recent runs, or an empty list when it reported none.
        /// </summary>
        /// <param name="service">The service.</param>
        /// <returns>The runs.</returns>
        /// <remarks>
        /// The service's own answer is trusted for the runs it chose to include, and an absent list is treated
        /// as an empty one rather than as a fault. A service that reports no runs is a service that has not run,
        /// which is an ordinary state and not something to raise an alert about.
        /// </remarks>
        private static List<RunSnapshot> RecentRunsOf(ServiceSnapshot service)
        {
            return service == null || service.RecentRuns == null
                ? new List<RunSnapshot>()
                : new List<RunSnapshot>(service.RecentRuns);
        }
    }
}
