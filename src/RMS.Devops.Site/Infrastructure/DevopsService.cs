using System;
using System.Collections.Generic;
using System.Globalization;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

namespace RMS.Devops.Site.Infrastructure
{
    /// <summary>
    /// Every call this site makes to the DevOps service, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page and the fragment handler that refreshes one of its regions need the same data, and they need it
    /// from the same route. Writing those routes twice is how the two ends drift: a parameter named one way in
    /// the page and another in the handler produces a refresh that quietly returns a different list from the
    /// one the page rendered, and the symptom is a table that changes shape when it updates.
    /// </para>
    /// <para>
    /// Every method takes the client rather than holding one, so nothing here decides which site is being
    /// served. The client carries the address and the key; this class only knows the shape of the surface.
    /// </para>
    /// </remarks>
    public static class DevopsService
    {
        /// <summary>
        /// Reads the machine's summary.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<FleetSummary> Fleet(ServiceClient client)
        {
            return client.GetJson<FleetSummary>("/api/fleet");
        }

        /// <summary>
        /// Reads the service list.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="recent">How many recent runs to include with each service, or zero for none.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<List<ServiceSnapshot>> Services(ServiceClient client, int recent)
        {
            return recent > 0
                ? client.GetJson<List<ServiceSnapshot>>(
                    "/api/services",
                    new KeyValuePair<string, string>("recent", recent.ToString(CultureInfo.InvariantCulture)))
                : client.GetJson<List<ServiceSnapshot>>("/api/services");
        }

        /// <summary>
        /// Reads one service.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="name">The service's name.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<ServiceSnapshot> Service(ServiceClient client, string name)
        {
            return client.GetJson<ServiceSnapshot>("/api/services/" + Uri.EscapeDataString(name));
        }

        /// <summary>
        /// Reads a page of runs, either across every service or within one.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="service">The service to restrict to, or an empty string for all of them.</param>
        /// <param name="skip">How many runs to skip.</param>
        /// <param name="take">How many runs to take.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<RunPage> Runs(ServiceClient client, string service, int skip, int take)
        {
            List<KeyValuePair<string, string>> query = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("skip", skip.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("take", take.ToString(CultureInfo.InvariantCulture)),
            };

            string path = "/api/runs";

            if (!string.IsNullOrEmpty(service))
            {
                query.Add(new KeyValuePair<string, string>("service", service));
            }

            return client.GetJson<RunPage>(path, query.ToArray());
        }

        /// <summary>
        /// Reads one run.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="service">The service's name.</param>
        /// <param name="run">The run's identifier.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<RunDetail> Run(ServiceClient client, string service, string run)
        {
            return client.GetJson<RunDetail>(
                "/api/services/" + Uri.EscapeDataString(service) + "/runs/" + Uri.EscapeDataString(run));
        }

        /// <summary>
        /// Reads a slice of a run's log.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="service">The service's name.</param>
        /// <param name="run">The run's identifier.</param>
        /// <param name="offset">The byte offset to begin at.</param>
        /// <param name="count">The most bytes to read, or zero for the service's own default.</param>
        /// <param name="tail">Whether to read the end of the log rather than the beginning.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<LogSlice> Log(
            ServiceClient client,
            string service,
            string run,
            long offset,
            int count,
            bool tail)
        {
            List<KeyValuePair<string, string>> query = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("offset", offset.ToString(CultureInfo.InvariantCulture)),
            };

            if (count > 0) query.Add(new KeyValuePair<string, string>("count", count.ToString(CultureInfo.InvariantCulture)));
            if (tail) query.Add(new KeyValuePair<string, string>("tail", "true"));

            return client.GetJson<LogSlice>(
                "/api/services/" + Uri.EscapeDataString(service) + "/runs/" + Uri.EscapeDataString(run) + "/log",
                query.ToArray());
        }

        /// <summary>
        /// Reads the published artefacts.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<ArtifactList> Artifacts(ServiceClient client)
        {
            return client.GetJson<ArtifactList>("/api/artifacts");
        }

        /// <summary>
        /// Reads the catalogue summary.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>The result.</returns>
        public static ServiceResult<CatalogSummary> Catalogue(ServiceClient client)
        {
            return client.GetJson<CatalogSummary>("/api/catalogue");
        }
    }
}
