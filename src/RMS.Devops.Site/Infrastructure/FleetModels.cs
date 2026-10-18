using System;
using System.Collections.Generic;

namespace RMS.Devops.Site.Infrastructure
{
    /// <summary>
    /// The shapes the DevOps service answers in, as this site reads them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These types mirror the service's own models and are deliberately not shared with them. The service is a
    /// separate program that is deployed separately, so the two ends have to be able to change on their own
    /// schedules; a shared type would turn any field the service added into a compile error here, and any
    /// field it renamed into a deployment that has to be done in a particular order. Mirroring the contract
    /// means the failure is instead a page that shows a dash where a value would have been -- which is a
    /// visible, harmless and self-explaining outcome.
    /// </para>
    /// <para>
    /// Every member is settable, because the deserializer needs to set them and has no constructor to call.
    /// Nothing here validates: a value the service sent is reported as it arrived, and the layer that decides
    /// how to present it is the one that knows what a usable value looks like.
    /// </para>
    /// <para>
    /// Timestamps are read as <see cref="DateTime"/> rather than as an offset-bearing type. The deserializer
    /// this site uses under the .NET Framework is predictable about one and not about the other, and a page
    /// that renders times wrongly is worse than a page that renders them in a fixed zone. Every value is
    /// converted to UTC before it is shown.
    /// </para>
    /// </remarks>
    public sealed class FleetSummary
    {
        /// <summary>Gets or sets the name of the machine the service is running on.</summary>
        public string Machine { get; set; }

        /// <summary>Gets or sets the moment the answer was built.</summary>
        public DateTime? ReportedUtc { get; set; }

        /// <summary>Gets or sets the plan file the service read.</summary>
        public string PlanFile { get; set; }

        /// <summary>Gets or sets a value indicating whether the plan was read.</summary>
        public bool PlanLoaded { get; set; }

        /// <summary>Gets or sets the reason the plan could not be read.</summary>
        public string PlanDetail { get; set; }

        /// <summary>Gets or sets the directory every service's history lives under.</summary>
        public string LogRoot { get; set; }

        /// <summary>Gets or sets how many services the machine knows about.</summary>
        public int Services { get; set; }

        /// <summary>Gets or sets how many services are running right now.</summary>
        public int Running { get; set; }

        /// <summary>Gets or sets how many services last finished well.</summary>
        public int Healthy { get; set; }

        /// <summary>Gets or sets how many services last finished badly.</summary>
        public int Failed { get; set; }

        /// <summary>Gets or sets how many services were stopped deliberately.</summary>
        public int Stopped { get; set; }

        /// <summary>Gets or sets how many runs were abandoned without an ending.</summary>
        public int Abandoned { get; set; }

        /// <summary>Gets or sets how many services have never run.</summary>
        public int Idle { get; set; }

        /// <summary>Gets or sets the most recent moment anything happened.</summary>
        public DateTime? LastActivity { get; set; }

        /// <summary>Gets or sets how many artefacts have been published.</summary>
        public int Artifacts { get; set; }

        /// <summary>Gets or sets the service that was active most recently.</summary>
        public string LastActiveService { get; set; }

        /// <summary>Gets or sets what the scanner's index says about the catalogue.</summary>
        public CatalogSummary Catalog { get; set; }
    }

    /// <summary>
    /// The catalogue, as the service summarized it.
    /// </summary>
    /// <remarks>
    /// This is a summary and not the catalogue. The service holds the totals and the most frequent detections
    /// because that is what a machine-wide overview needs; the detailed analytics stay with the API that owns
    /// the index, and a copy of them here would be a second answer to the same question that could disagree
    /// with the first.
    /// </remarks>
    public sealed class CatalogSummary
    {
        /// <summary>Gets or sets a value indicating whether the index could be read.</summary>
        public bool Available { get; set; }

        /// <summary>Gets or sets the reason the index could not be read.</summary>
        public string Detail { get; set; }

        /// <summary>Gets or sets the path of the index.</summary>
        public string Path { get; set; }

        /// <summary>Gets or sets the schema version the index reports.</summary>
        public int SchemaVersion { get; set; }

        /// <summary>Gets or sets how many rows the catalogue holds.</summary>
        public long Models { get; set; }

        /// <summary>Gets or sets how many rows contain scripts.</summary>
        public long ModelsWithScripts { get; set; }

        /// <summary>Gets or sets how many rows carry at least one detection.</summary>
        public long ModelsWithDetections { get; set; }

        /// <summary>Gets or sets the sum of every score.</summary>
        public double TotalScore { get; set; }

        /// <summary>Gets or sets the highest score.</summary>
        public double MaximumScore { get; set; }

        /// <summary>Gets or sets how many detection rows exist.</summary>
        public long Detections { get; set; }

        /// <summary>Gets or sets the moment the index was built.</summary>
        public DateTime? IndexedUtc { get; set; }

        /// <summary>Gets or sets the revision of the catalogue the index was built from.</summary>
        public long SourceRevision { get; set; }

        /// <summary>Gets or sets the most frequent detections.</summary>
        public List<TagStatistic> TopDetections { get; set; }

        /// <summary>Gets or sets how the catalogue is distributed over time.</summary>
        public List<PeriodStatistic> Periods { get; set; }
    }

    /// <summary>
    /// How often one detection matched.
    /// </summary>
    public sealed class TagStatistic
    {
        /// <summary>Gets or sets the detection's name, as the rule spells it.</summary>
        public string Tag { get; set; }

        /// <summary>Gets or sets how many rows matched.</summary>
        public long Matches { get; set; }

        /// <summary>Gets or sets the rule category, as an integer.</summary>
        public int Category { get; set; }
    }

    /// <summary>
    /// How many rows were published in one period.
    /// </summary>
    public sealed class PeriodStatistic
    {
        /// <summary>Gets or sets the period, formatted by the indexer.</summary>
        public string Period { get; set; }

        /// <summary>Gets or sets how many rows it holds.</summary>
        public long Models { get; set; }
    }
}
