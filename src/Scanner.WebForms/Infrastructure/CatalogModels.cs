using System;
using System.Collections.Generic;

namespace Scanner.WebForms.Infrastructure
{
    /// <summary>
    /// One page of the model catalogue, as the API answered it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These types are the site's reading of the API's document, not a second contract. Every property name
    /// here matches the JSON the API writes, and the reader this site uses matches case-insensitively, so the
    /// shapes stay together by name rather than by a mapping table that could drift out of step with the
    /// service it describes.
    /// </para>
    /// <para>
    /// Every property is settable and every type has a parameterless constructor, because that is what the
    /// framework's serializer requires. Every string that comes from the service is treated as possibly
    /// absent: a JSON value the service did not send leaves the property at its default, which for a string
    /// means <see langword="null"/> rather than an empty one.
    /// </para>
    /// </remarks>
    public sealed class ModelPage
    {
        /// <summary>
        /// Gets or sets the number of models matching the filter, before paging.
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// Gets or sets the one-based page number that was returned.
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// Gets or sets the number of models per page.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Gets or sets the models on the page.
        /// </summary>
        public List<ModelRow> Items { get; set; }
    }

    /// <summary>
    /// One model as the catalogue holds it.
    /// </summary>
    /// <remarks>
    /// Identifier and date are always present; everything else is optional and older rows carry none of it.
    /// A page therefore has to be readable when every model on it has no name, no creator and no description,
    /// which is the ordinary state of the earliest part of the catalogue rather than a fault.
    /// </remarks>
    public sealed class ModelRow
    {
        /// <summary>
        /// Gets or sets the Roblox asset identifier.
        /// </summary>
        public long Model { get; set; }

        /// <summary>
        /// Gets or sets the ISO-8601 instant at which the scanner first recorded the asset.
        /// </summary>
        public string Date { get; set; }

        /// <summary>
        /// Gets or sets the asset's name, which is empty for the rows the scanner captured before it read
        /// names at all.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the asset's description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the creator's display name.
        /// </summary>
        public string CreatorName { get; set; }

        /// <summary>
        /// Gets or sets the creator's numeric identifier, as text.
        /// </summary>
        public string CreatorId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the asset contains script data.
        /// </summary>
        public bool HasScripts { get; set; }

        /// <summary>
        /// Gets or sets the stored labels of every detection rule that matched, joined by a pipe-delimited
        /// separator on current rows and by single spaces on rows written by earlier revisions.
        /// </summary>
        public string Tags { get; set; }

        /// <summary>
        /// Gets or sets the sum of the scores of the rules that matched.
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Gets or sets the canonical store address of the asset.
        /// </summary>
        public string AssetUrl { get; set; }

        /// <summary>
        /// Gets or sets the asset thumbnail address, when the scanner captured one.
        /// </summary>
        public string Thumbnail { get; set; }
    }

    /// <summary>
    /// The whole analytics view of the indexed catalogue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the answer to what the site is for: how many models were indexed, how many of them carry
    /// script, how many triggered a rule, which rules fired and what they mean, and how the confidence
    /// distributes across the catalogue.
    /// </para>
    /// <para>
    /// The lists are read defensively. The service always sends them, but a site that dereferenced one
    /// without checking would turn a service that changed shape into a page that does not render at all.
    /// </para>
    /// </remarks>
    public sealed class AnalyticsView
    {
        /// <summary>
        /// Gets or sets the number of indexed models.
        /// </summary>
        public int TotalModels { get; set; }

        /// <summary>
        /// Gets or sets the number of indexed models that contain script data.
        /// </summary>
        public int ModelsWithScripts { get; set; }

        /// <summary>
        /// Gets or sets the number of indexed models that triggered at least one rule.
        /// </summary>
        public int ModelsWithDetections { get; set; }

        /// <summary>
        /// Gets or sets the number of distinct rules that matched at least one model.
        /// </summary>
        public int DistinctRulesTriggered { get; set; }

        /// <summary>
        /// Gets or sets the sum of every model's aggregate score.
        /// </summary>
        public double TotalScore { get; set; }

        /// <summary>
        /// Gets or sets the mean aggregate score across the catalogue.
        /// </summary>
        public double AverageScore { get; set; }

        /// <summary>
        /// Gets or sets the highest aggregate score observed.
        /// </summary>
        public double MaximumScore { get; set; }

        /// <summary>
        /// Gets or sets the total number of individual rule matches, counting repeats across models.
        /// </summary>
        public int TotalRuleMatches { get; set; }

        /// <summary>
        /// Gets or sets the per-rule breakdown, ordered by match count.
        /// </summary>
        public List<RuleStatisticView> Rules { get; set; }

        /// <summary>
        /// Gets or sets the per-category roll-up, ordered by match count.
        /// </summary>
        public List<CategoryView> Categories { get; set; }

        /// <summary>
        /// Gets or sets the aggregate-score histogram.
        /// </summary>
        public List<ScoreBucketView> ScoreDistribution { get; set; }

        /// <summary>
        /// Gets or sets the monthly count of models first recorded.
        /// </summary>
        public List<TimelineView> DiscoveryTimeline { get; set; }

        /// <summary>
        /// Gets or sets the most prolific creators in the catalogue.
        /// </summary>
        public List<CreatorView> TopCreators { get; set; }

        /// <summary>
        /// Gets or sets the instant at which the summary was computed, as the service wrote it.
        /// </summary>
        /// <remarks>
        /// Read as the raw text the service sent and converted by <see cref="ScannerViews.ToOffset(string)"/>
        /// rather than bound straight to a <see cref="DateTime"/>. The service sends an offset, the framework's
        /// serializer resolves it into local time, and one conversion applied in one place is easier to be sure
        /// of than the serializer's behaviour on each platform.
        /// </remarks>
        public string GeneratedUtc { get; set; }
    }

    /// <summary>
    /// One rule as the analytics view counts it.
    /// </summary>
    /// <remarks>
    /// <see cref="Category"/> and <see cref="Severity"/> arrive as the API's own enumeration names, because
    /// the API publishes its enumerations as strings. They are read as text for that reason and spaced for
    /// display by <see cref="ScannerViews.CategoryName"/> and <see cref="ScannerViews.SeverityName"/>, so the
    /// names exist in exactly one place on this side and nothing here has to hold the scanner's numbering.
    /// </remarks>
    public sealed class RuleStatisticView
    {
        /// <summary>
        /// Gets or sets the label written into catalogue rows.
        /// </summary>
        public string Tag { get; set; }

        /// <summary>
        /// Gets or sets the score a match contributes.
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Gets or sets the behaviour group the rule belongs to.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Gets or sets the relative risk of a match.
        /// </summary>
        public string Severity { get; set; }

        /// <summary>
        /// Gets or sets the operator-facing explanation of why the rule exists.
        /// </summary>
        public string Purpose { get; set; }

        /// <summary>
        /// Gets or sets the technical description of the signature.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the number of indexed models that triggered the rule.
        /// </summary>
        public int Matches { get; set; }

        /// <summary>
        /// Gets or sets the proportion of indexed models that triggered the rule.
        /// </summary>
        public double Share { get; set; }
    }

    /// <summary>
    /// The count of matches in one rule category.
    /// </summary>
    public sealed class CategoryView
    {
        /// <summary>
        /// Gets or sets the category name.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Gets or sets the number of model and rule matches in the category.
        /// </summary>
        public int Matches { get; set; }

        /// <summary>
        /// Gets or sets the number of distinct rules in the category that matched at least once.
        /// </summary>
        public int DistinctRules { get; set; }
    }

    /// <summary>
    /// One bucket of the aggregate-score histogram.
    /// </summary>
    public sealed class ScoreBucketView
    {
        /// <summary>
        /// Gets or sets the human-readable label.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Gets or sets the inclusive lower bound.
        /// </summary>
        public double LowerBound { get; set; }

        /// <summary>
        /// Gets or sets the exclusive upper bound, or nothing for the open-ended final bucket.
        /// </summary>
        /// <remarks>
        /// The service sends <c>null</c> for the last bucket's upper bound, which arrives here as
        /// <see langword="null"/> rather than as a very large number. A bucket with no upper bound is a bucket
        /// whose description says so; treating it as a bound would describe the catalogue as ending somewhere.
        /// </remarks>
        public double? UpperBound { get; set; }

        /// <summary>
        /// Gets or sets the number of models in the bucket.
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// The number of models first recorded in one calendar month.
    /// </summary>
    public sealed class TimelineView
    {
        /// <summary>
        /// Gets or sets the month, formatted as <c>yyyy-MM</c>.
        /// </summary>
        public string Period { get; set; }

        /// <summary>
        /// Gets or sets the number of models recorded in it.
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// One entry of the creator leaderboard.
    /// </summary>
    public sealed class CreatorView
    {
        /// <summary>
        /// Gets or sets the creator's numeric identifier as text.
        /// </summary>
        public string CreatorId { get; set; }

        /// <summary>
        /// Gets or sets the creator's display name.
        /// </summary>
        public string CreatorName { get; set; }

        /// <summary>
        /// Gets or sets the number of indexed models attributed to the creator.
        /// </summary>
        public int Models { get; set; }

        /// <summary>
        /// Gets or sets the number of those models that contain script data.
        /// </summary>
        public int ModelsWithScripts { get; set; }

        /// <summary>
        /// Gets or sets the number of those models that triggered at least one rule.
        /// </summary>
        public int Detections { get; set; }
    }

    /// <summary>
    /// The API's answer to a request for an asset's signed download location.
    /// </summary>
    /// <remarks>
    /// The location is a time-limited signed address that Roblox issues and that nobody may cache, so it is
    /// resolved, handed to the browser, and forgotten. The site never stores one and never renders one into a
    /// page.
    /// </remarks>
    public sealed class DownloadAddress
    {
        /// <summary>
        /// Gets or sets the asset identifier the address was issued for.
        /// </summary>
        public long Model { get; set; }

        /// <summary>
        /// Gets or sets the signed content address.
        /// </summary>
        public string Location { get; set; }
    }
}
