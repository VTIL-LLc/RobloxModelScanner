namespace RMS.Devops.Service.Models;

/// <summary>
/// What the scanner's index says about the catalogue, as one panel shows it.
/// </summary>
/// <remarks>
/// <para>
/// This is a summary, not the analytics surface. The publishing API owns the detailed questions -- which
/// creators, which score buckets, which rows -- and it is the right owner, because it shares the detection
/// assembly that gives the numbers their names. What the DevOps service needs is the one thing an operator
/// asks when they open the dashboard: is the index current, how big is the catalogue, and what did the last
/// scan find. That is the whole of this type.
/// </para>
/// <para>
/// <see cref="Available"/> is separate from an empty result on purpose. A machine that has never indexed
/// anything and a machine whose index cannot be opened are different, and only the second is a problem worth
/// an alert.
/// </para>
/// </remarks>
public sealed class CatalogSummary
{
    /// <summary>
    /// Gets a value indicating whether the index could be read.
    /// </summary>
    public bool Available { get; init; }

    /// <summary>
    /// Gets the reason the index could not be read, when it could not.
    /// </summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>
    /// Gets the path of the index that was read.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the schema version the index reports.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets how many catalogue rows the index holds.
    /// </summary>
    public long Models { get; init; }

    /// <summary>
    /// Gets how many of those rows contain scripts.
    /// </summary>
    public long ModelsWithScripts { get; init; }

    /// <summary>
    /// Gets how many of those rows carry at least one detection.
    /// </summary>
    public long ModelsWithDetections { get; init; }

    /// <summary>
    /// Gets the sum of every row's score.
    /// </summary>
    public double TotalScore { get; init; }

    /// <summary>
    /// Gets the highest score in the catalogue.
    /// </summary>
    public double MaximumScore { get; init; }

    /// <summary>
    /// Gets the number of individual detection rows.
    /// </summary>
    public long Detections { get; init; }

    /// <summary>
    /// Gets when the last migration committed.
    /// </summary>
    public DateTimeOffset? IndexedUtc { get; init; }

    /// <summary>
    /// Gets the length of the catalogue revision the index was built from.
    /// </summary>
    /// <remarks>
    /// The length is what makes the staleness question answerable without reading the catalogue: a deployment
    /// that reports a different length from the one recorded here has a catalogue that has changed since it was
    /// indexed.
    /// </remarks>
    public long SourceLength { get; init; }

    /// <summary>
    /// Gets the most frequent detections, most frequent first.
    /// </summary>
    public IReadOnlyList<TagStatistic> TopDetections { get; init; } = [];

    /// <summary>
    /// Gets the publication timeline, oldest period first.
    /// </summary>
    public IReadOnlyList<PeriodStatistic> Periods { get; init; } = [];

    /// <summary>
    /// Returns a summary describing an index that could not be read.
    /// </summary>
    /// <param name="path">The index path that was attempted.</param>
    /// <param name="detail">The reason.</param>
    /// <returns>The summary.</returns>
    public static CatalogSummary Unavailable(string path, string detail)
    {
        return new CatalogSummary
        {
            Available = false,
            Path = path,
            Detail = detail,
        };
    }
}
