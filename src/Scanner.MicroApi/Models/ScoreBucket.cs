namespace Scanner.MicroApi.Models;

/// <summary>
/// One bucket of the aggregate-score histogram.
/// </summary>
/// <remarks>
/// Score buckets let the website show how detection confidence is distributed across the
/// catalogue. Fixed, inclusive-lower bounds are used so the buckets are stable between requests
/// and can be compared over time.
/// </remarks>
/// <param name="Label">The human-readable bucket label.</param>
/// <param name="LowerBound">The inclusive lower bound of the bucket.</param>
/// <param name="UpperBound">
/// The exclusive upper bound of the bucket, or <see langword="null"/> for the open-ended final
/// bucket. The unbounded case is modelled as null rather than as positive infinity because
/// infinity is not a representable JSON number.
/// </param>
/// <param name="Count">The number of models whose score falls inside the bucket.</param>
public sealed record ScoreBucket(string Label, double LowerBound, double? UpperBound, int Count);
