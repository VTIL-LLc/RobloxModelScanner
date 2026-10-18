namespace Scanner.MicroApi.Models;

/// <summary>
/// Detection counts rolled up by rule category.
/// </summary>
/// <remarks>
/// Category totals answer "how many of each kind of signal" without forcing the client to
/// aggregate the per-rule table itself, which keeps the website rendering logic trivial and fast.
/// </remarks>
/// <param name="Category">The category name.</param>
/// <param name="Matches">The number of model/rule matches in the category.</param>
/// <param name="DistinctRules">The number of distinct rules in the category that matched at least once.</param>
public sealed record CategoryStatistic(string Category, int Matches, int DistinctRules);
