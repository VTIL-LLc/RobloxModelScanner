namespace Scanner.MicroApi.Models;

/// <summary>
/// The complete analytics view of the indexed catalogue.
/// </summary>
/// <remarks>
/// The summary is assembled from indexed SQL aggregates, never from a scan of the JSON document,
/// so it stays fast as the catalogue grows. It is the single payload behind the website's
/// analytics page: totals, rule breakdown with operator-facing purpose, category roll-ups, a
/// score histogram, a discovery timeline and a creator leaderboard.
/// </remarks>
/// <param name="TotalModels">The number of indexed models.</param>
/// <param name="ModelsWithScripts">The number of indexed models that contain script data.</param>
/// <param name="ModelsWithDetections">The number of indexed models that triggered at least one rule.</param>
/// <param name="DistinctRulesTriggered">The number of distinct rules that matched at least one model.</param>
/// <param name="TotalScore">The sum of every model's aggregate score.</param>
/// <param name="AverageScore">The mean aggregate score across the catalogue.</param>
/// <param name="MaximumScore">The highest aggregate score observed.</param>
/// <param name="TotalRuleMatches">The total number of individual rule matches, counting repeats across models.</param>
/// <param name="Rules">The per-rule breakdown, ordered by match count.</param>
/// <param name="Categories">The per-category roll-up, ordered by match count.</param>
/// <param name="ScoreDistribution">The aggregate-score histogram.</param>
/// <param name="DiscoveryTimeline">The monthly count of models first recorded.</param>
/// <param name="TopCreators">The most prolific creators in the catalogue.</param>
/// <param name="GeneratedUtc">The UTC instant at which the summary was computed.</param>
public sealed record AnalyticsSummary(
    int TotalModels,
    int ModelsWithScripts,
    int ModelsWithDetections,
    int DistinctRulesTriggered,
    double TotalScore,
    double AverageScore,
    double MaximumScore,
    int TotalRuleMatches,
    IReadOnlyList<RuleDefinition> Rules,
    IReadOnlyList<CategoryStatistic> Categories,
    IReadOnlyList<ScoreBucket> ScoreDistribution,
    IReadOnlyList<TimelinePoint> DiscoveryTimeline,
    IReadOnlyList<CreatorStatistic> TopCreators,
    DateTimeOffset GeneratedUtc);
