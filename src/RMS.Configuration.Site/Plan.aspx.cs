using System;
using System.Text;

using RMS.Site.Common.Implementation;
using RMS.Site.Common.Models;

using RMS.Configuration.Site.Infrastructure;

namespace RMS.Configuration.Site
{
    /// <summary>
    /// The plan: what the deployer decided the services manifest meant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This page is read-only, and that is not an omission. The plan is written by the deployer every time it
    /// runs, so an edit made here would appear to be accepted and would be gone at the next deployment -- which
    /// is a worse outcome than not being able to make it. The link at the foot of the page goes to the file the
    /// plan is generated from, which is where a change belongs.
    /// </para>
    /// <para>
    /// Its value is that it is read with the installer's own reader. What is shown is therefore not what a
    /// second implementation believes will be installed; it is what the installer will actually act on, with
    /// every placeholder expanded and every platform-qualified value chosen.
    /// </para>
    /// </remarks>
    public class Plan : ConfigurationPage
    {
        /// <summary>
        /// The longest entry name this page will look for.
        /// </summary>
        private const int MaximumEntryNameLength = 128;

        /// <summary>
        /// Gets the title shown in the browser tab.
        /// </summary>
        protected override string PageTitle
        {
            get { return "Plan — " + Settings.Title; }
        }

        /// <summary>
        /// Renders the page.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            WriteDocument(PageTitle, Navigation(), RenderBody(LoadPlan()));
        }

        /// <summary>
        /// Builds the page's body.
        /// </summary>
        /// <param name="plan">The plan, which may have failed.</param>
        /// <returns>The markup.</returns>
        private string RenderBody(ServiceResult<PlanView> plan)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(Html.Heading(
                "The plan",
                "The file the deployer writes and the installer reads, resolved for this machine. Every "
                + "placeholder is expanded here and every platform-qualified value has been chosen, which is "
                + "what makes this the answer to what a service will actually be given."));

            if (!plan.IsSuccess)
            {
                builder.Append(RenderFailure(plan));

                return builder.ToString();
            }

            builder.Append(Live("plan-body", "plan", ConfigurationViews.PlanBody(plan.Value)));

            builder.Append(RenderEntry(plan.Value));

            builder.Append("<p>")
                   .Append(Html.Link("/Services.aspx", "Edit the services manifest this plan is generated from"))
                   .Append("</p>");

            return builder.ToString();
        }

        /// <summary>
        /// Renders the detail of one plan entry, when the address asked for one.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The markup, which is empty when no entry was named.</returns>
        /// <remarks>
        /// The entry is looked for in the plan that has already been read rather than fetched by name. That
        /// keeps the page to one call, and it means an entry that is not in the plan is answered with a sentence
        /// rather than with a route that would have to decide what a missing name means.
        /// </remarks>
        private string RenderEntry(PlanView plan)
        {
            string name = ReadQuery("entry", MaximumEntryNameLength);

            if (name.Length == 0) return string.Empty;

            PlanServiceView service = FindEntry(plan, name);

            if (service == null)
            {
                return Html.Alert(
                    "warning",
                    "There is no entry by that name in this plan.",
                    "The plan was generated from the services manifest as it stood when the deployer last ran, so "
                    + "an entry that was added since then is not in it yet.");
            }

            return Html.Panel("Entry: " + name, ConfigurationViews.PlanServiceDetail(service));
        }

        /// <summary>
        /// Finds one entry in the plan.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="name">The entry's identifier.</param>
        /// <returns>The entry, or <see langword="null"/>.</returns>
        /// <remarks>
        /// Matched without regard to case, because the identifier is a section name an operator typed into a
        /// manifest and will type again into an address, and a section name is not case-sensitive to the
        /// deployer either.
        /// </remarks>
        private static PlanServiceView FindEntry(PlanView plan, string name)
        {
            if (plan == null || plan.Services == null) return null;

            foreach (PlanServiceView service in plan.Services)
            {
                if (string.Equals(service.Id, name, StringComparison.OrdinalIgnoreCase)) return service;
            }

            return null;
        }
    }
}
