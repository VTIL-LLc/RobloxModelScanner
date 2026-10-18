using Microsoft.Data.Sqlite;

using RobloxModelScanner.Features.AssetAnalysis;

using Scanner.MicroApi.Interfaces;
using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Implementation;

/// <summary>
/// Computes the analytics view from the indexed SQLite read model.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is derived by walking the catalogue. Every figure is read from a pre-aggregated table
/// that the indexing service populated in the same transaction that wrote the catalogue, so the cost of
/// this endpoint is a handful of small keyed reads and does not grow with the catalogue. Computing the
/// creator leaderboard live, for example, required a grouped scan and a sort that scaled with the number
/// of distinct creators and dominated the request.
/// </para>
/// <para>
/// The rule table is built from the published rule catalogue rather than from the data alone. A rule
/// that has never matched still appears, with a count of zero, because "what rules exist and what they
/// are for" is a question about the rule set, not about the sample. Conversely, a tag present in
/// historical rows that no longer corresponds to a rule is surfaced as a distinct unknown entry so it
/// can never be silently folded into a real rule's totals.
/// </para>
/// </remarks>
public sealed class SqliteAnalyticsService : IAnalyticsService
{
    /// <summary>
    /// The score histogram edges. Each entry is the exclusive upper bound of its bucket; a null upper
    /// bound marks the open-ended final bucket.
    /// </summary>
    /// <remarks>
    /// The edges were chosen against the observed distribution of aggregate scores so that the "no
    /// detections" mass and the small cluster of heavily flagged assets each get their own bucket
    /// instead of flattening into one.
    /// </remarks>
    private static readonly (string Label, double Lower, double? Upper)[] ScoreBuckets =
    [
        ("No detections", 0, 0),
        ("Under 1", 0, 1),
        ("1 to 3", 1, 3),
        ("3 to 5", 3, 5),
        ("5 to 10", 5, 10),
        ("10 or more", 10, null)
    ];

    /// <summary>
    /// The number of creators published in the leaderboard.
    /// </summary>
    private const int CreatorLeaderboardSize = 12;

    private readonly IScannerIndexReader _reader;
    private readonly IRuleCatalogService _ruleCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteAnalyticsService"/> class.
    /// </summary>
    /// <param name="reader">The reader that opens the index.</param>
    /// <param name="ruleCatalog">The published rule catalogue used to describe each detection.</param>
    public SqliteAnalyticsService(IScannerIndexReader reader, IRuleCatalogService ruleCatalog)
    {
        _reader = reader;
        _ruleCatalog = ruleCatalog;
    }

    /// <inheritdoc />
    public async Task<AnalyticsSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _reader.OpenConnection();

        CatalogueTotals totals = await ReadTotalsAsync(connection, cancellationToken).ConfigureAwait(false);

        List<(string Tag, int Matches, RuleCategory Category)> detectionCounts =
            await ReadDetectionCountsAsync(connection, cancellationToken).ConfigureAwait(false);

        return new AnalyticsSummary(
            TotalModels: totals.TotalModels,
            ModelsWithScripts: totals.ModelsWithScripts,
            ModelsWithDetections: totals.ModelsWithDetections,
            DistinctRulesTriggered: detectionCounts.Count,
            TotalScore: totals.TotalScore,
            AverageScore: totals.TotalModels == 0 ? 0 : Math.Round(totals.TotalScore / totals.TotalModels, 4),
            MaximumScore: totals.MaximumScore,

            // Taken from the stored count rather than summed from the reported rows, so a capped detection
            // list cannot understate the total number of individual matches.
            TotalRuleMatches: totals.TotalDetections,
            Rules: BuildRuleBreakdown(detectionCounts, totals.TotalModels),
            Categories: BuildCategoryRollup(detectionCounts),
            ScoreDistribution: await ReadScoreDistributionAsync(connection, cancellationToken).ConfigureAwait(false),
            DiscoveryTimeline: await ReadTimelineAsync(connection, cancellationToken).ConfigureAwait(false),
            TopCreators: await ReadTopCreatorsAsync(connection, cancellationToken).ConfigureAwait(false),
            GeneratedUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Reads the catalogue-wide totals from the pre-aggregated row.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="cancellationToken">A token used to cancel the query.</param>
    /// <returns>The stored totals, or all zeros when no index has been built yet.</returns>
    private static async Task<CatalogueTotals> ReadTotalsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT TotalModels,
                   ModelsWithScripts,
                   ModelsWithDetections,
                   TotalScore,
                   MaximumScore,
                   TotalDetections
            FROM CatalogueTotals
            WHERE Id = 1;
            """;

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new CatalogueTotals(0, 0, 0, 0, 0, 0);
        }

        return new CatalogueTotals(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetInt32(5));
    }

    /// <summary>
    /// Reads the number of models that triggered each detection label.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="cancellationToken">A token used to cancel the query.</param>
    /// <returns>The per-label match counts, ordered by descending frequency.</returns>
    private static async Task<List<(string Tag, int Matches, RuleCategory Category)>> ReadDetectionCountsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Tag, Matches, Category
            FROM TagStatistics
            ORDER BY Matches DESC, Tag ASC;
            """;

        var results = new List<(string, int, RuleCategory)>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((reader.GetString(0), reader.GetInt32(1), (RuleCategory)reader.GetInt32(2)));
        }

        return results;
    }

    /// <summary>
    /// Combines the published rule catalogue with the observed match counts.
    /// </summary>
    /// <param name="detectionCounts">The observed per-label match counts.</param>
    /// <param name="totalModels">The catalogue size, used to compute each rule's share.</param>
    /// <returns>The per-rule breakdown, ordered by descending match count.</returns>
    private IReadOnlyList<RuleDefinition> BuildRuleBreakdown(
        List<(string Tag, int Matches, RuleCategory Category)> detectionCounts,
        int totalModels)
    {
        var observed = new Dictionary<string, (int Matches, RuleCategory Category)>(detectionCounts.Count, StringComparer.Ordinal);
        foreach ((string tag, int matches, RuleCategory category) in detectionCounts)
        {
            observed[tag] = (matches, category);
        }

        var results = new List<RuleDefinition>(detectionCounts.Count);

        foreach (RuleDefinition definition in _ruleCatalog.GetRules())
        {
            if (observed.Remove(definition.Tag, out (int Matches, RuleCategory Category) observedEntry))
            {
                results.Add(Describe(definition, observedEntry.Matches, totalModels));
                continue;
            }

            results.Add(definition with { Matches = 0, Share = 0 });
        }

        // Anything left over is a label that no current rule claims. Surfacing each one individually
        // keeps the totals honest: an unrecognized historical label must not be attributed to a real rule.
        foreach ((string tag, int matches, RuleCategory category) in detectionCounts)
        {
            if (!observed.ContainsKey(tag)) continue;

            results.Add(new RuleDefinition(
                Tag: tag,
                Score: 0,
                Category: category,
                Severity: RuleSeverity.Informational,
                Purpose: "This label appears in the stored catalogue but is not produced by any current detection rule.",
                Description: "Retained from an earlier revision of the scanner. The tag is reported as-is so the history stays auditable.",
                Matches: matches,
                Share: Share(matches, totalModels)));
        }

        results.Sort(static (left, right) =>
        {
            int byMatches = right.Matches.CompareTo(left.Matches);
            return byMatches != 0 ? byMatches : string.CompareOrdinal(left.Tag, right.Tag);
        });

        return results;
    }

    /// <summary>
    /// Produces the published rule definition with its observed match count and share.
    /// </summary>
    /// <param name="definition">The published definition.</param>
    /// <param name="matches">The number of models that triggered the rule.</param>
    /// <param name="totalModels">The catalogue size.</param>
    /// <returns>The definition augmented with its observed statistics.</returns>
    private static RuleDefinition Describe(RuleDefinition definition, int matches, int totalModels) =>
        definition with { Matches = matches, Share = Share(matches, totalModels) };

    /// <summary>
    /// Computes the proportion of the catalogue a match count represents.
    /// </summary>
    /// <param name="matches">The number of matching models.</param>
    /// <param name="totalModels">The catalogue size.</param>
    /// <returns>The share, rounded to six decimal places, or zero for an empty catalogue.</returns>
    private static double Share(int matches, int totalModels) =>
        totalModels == 0 ? 0 : Math.Round((double)matches / totalModels, 6);

    /// <summary>
    /// Rolls the per-label counts up by rule category.
    /// </summary>
    /// <param name="detectionCounts">The observed per-label match counts.</param>
    /// <returns>The per-category totals, ordered by descending match count.</returns>
    private static IReadOnlyList<CategoryStatistic> BuildCategoryRollup(
        List<(string Tag, int Matches, RuleCategory Category)> detectionCounts)
    {
        var matches = new Dictionary<RuleCategory, int>();
        var distinctRules = new Dictionary<RuleCategory, int>();

        foreach ((_, int count, RuleCategory category) in detectionCounts)
        {
            matches[category] = matches.TryGetValue(category, out int current) ? current + count : count;
            distinctRules[category] = distinctRules.TryGetValue(category, out int rules) ? rules + 1 : 1;
        }

        return matches
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new CategoryStatistic(
                DescribeCategory(pair.Key),
                pair.Value,
                distinctRules[pair.Key]))
            .ToList();
    }

    /// <summary>
    /// Produces the operator-facing name of a rule category.
    /// </summary>
    /// <param name="category">The category to name.</param>
    /// <returns>The display name, with <see cref="RuleCategory.Unknown"/> distinguished from a real category.</returns>
    private static string DescribeCategory(RuleCategory category) =>
        category == RuleCategory.Unknown ? "Unrecognized" : category.ToString();

    /// <summary>
    /// Reads the aggregate-score histogram.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="cancellationToken">A token used to cancel the query.</param>
    /// <returns>One entry per bucket, including buckets with no models.</returns>
    /// <remarks>
    /// The bucket index is decided by SQL when the statistics are built; the labels and bounds stay in
    /// C#, where they are easier to read and to change without touching statement text.
    /// </remarks>
    private static async Task<IReadOnlyList<ScoreBucket>> ReadScoreDistributionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var counts = new int[ScoreBuckets.Length];

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Bucket, Models FROM ScoreStatistics;";

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            int bucket = reader.GetInt32(0);
            if (bucket >= 0 && bucket < counts.Length)
            {
                counts[bucket] = reader.GetInt32(1);
            }
        }

        var distribution = new ScoreBucket[ScoreBuckets.Length];
        for (int index = 0; index < ScoreBuckets.Length; index++)
        {
            (string label, double lower, double? upper) = ScoreBuckets[index];
            distribution[index] = new ScoreBucket(label, lower, upper, counts[index]);
        }

        return distribution;
    }

    /// <summary>
    /// Reads the monthly count of models first recorded.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="cancellationToken">A token used to cancel the query.</param>
    /// <returns>The timeline in ascending period order.</returns>
    private static async Task<IReadOnlyList<TimelinePoint>> ReadTimelineAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Period, Models FROM PeriodStatistics ORDER BY Period ASC;";

        var timeline = new List<TimelinePoint>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            timeline.Add(new TimelinePoint(reader.GetString(0), reader.GetInt32(1)));
        }

        return timeline;
    }

    /// <summary>
    /// Reads the most prolific creators in the catalogue.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="cancellationToken">A token used to cancel the query.</param>
    /// <returns>The creator leaderboard, ordered by descending model count.</returns>
    /// <remarks>
    /// The descending index on the model count lets this be an index-ordered read of the requested rows
    /// rather than a full scan followed by a sort.
    /// </remarks>
    private static async Task<IReadOnlyList<CreatorStatistic>> ReadTopCreatorsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT CreatorId, CreatorName, Models, ModelsWithScripts, Detections
            FROM CreatorStatistics
            ORDER BY Models DESC, CreatorId ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", CreatorLeaderboardSize);

        var creators = new List<CreatorStatistic>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            creators.Add(new CreatorStatistic(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return creators;
    }

    /// <summary>
    /// The stored catalogue-wide totals.
    /// </summary>
    /// <param name="TotalModels">The number of indexed models.</param>
    /// <param name="ModelsWithScripts">The number of indexed models that contain script data.</param>
    /// <param name="ModelsWithDetections">The number of indexed models that triggered at least one rule.</param>
    /// <param name="TotalScore">The sum of every model's aggregate score.</param>
    /// <param name="MaximumScore">The highest aggregate score observed.</param>
    /// <param name="TotalDetections">The total number of individual rule matches.</param>
    private sealed record CatalogueTotals(
        int TotalModels,
        int ModelsWithScripts,
        int ModelsWithDetections,
        double TotalScore,
        double MaximumScore,
        int TotalDetections);
}
