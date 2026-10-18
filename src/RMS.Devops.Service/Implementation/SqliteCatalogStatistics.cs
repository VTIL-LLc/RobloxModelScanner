using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RMS.Devops.Service.Interfaces;
using RMS.Devops.Service.Models;
using RMS.Devops.Service.Options;

namespace RMS.Devops.Service.Implementation;

/// <summary>
/// Summarizes the scanner's catalogue index by reading the derived statistics tables.
/// </summary>
/// <remarks>
/// <para>
/// The index belongs to the indexing service and this class only ever reads it. Every statement below touches
/// one of the tables the migration derives inside its own write transaction, so a summary is a handful of
/// keyed reads rather than a scan of the catalogue, and the numbers returned cannot disagree with each other:
/// they were committed together or not at all.
/// </para>
/// <para>
/// The connection is opened read-only. That is not a precaution against this code -- it is the mechanism by
/// which a query here can never block a migration, and by which a mistake in a statement here can never damage
/// the file the rest of the stack depends on. A read-only connection to a write-ahead-logged database still
/// needs the log's shared-memory file to exist, which it will, because the indexing service created it.
/// </para>
/// <para>
/// A failure to read is reported, not thrown. The catalogue is one panel on one page of a dashboard whose
/// purpose is to say what is running on a machine, and a machine that runs the DevOps service without running
/// the scanner is an ordinary machine.
/// </para>
/// </remarks>
public sealed class SqliteCatalogStatistics : ICatalogStatistics
{
    /// <summary>The bookkeeping key holding the schema version.</summary>
    private const string SchemaVersionKey = "index.schemaVersion";

    /// <summary>The bookkeeping key holding the moment the last migration committed.</summary>
    private const string CompletedKey = "index.completedUtc";

    /// <summary>The bookkeeping key holding the revision of the indexed catalogue.</summary>
    private const string SourceRevisionKey = "source.revision";

    /// <summary>How many detections the summary lists.</summary>
    /// <remarks>
    /// The panel is a top-ten list with room for a title. Asking for the whole derived table would make a
    /// summary of the catalogue cost the same as the catalogue, which is the opposite of its purpose.
    /// </remarks>
    private const int DetectionLimit = 15;

    private readonly DevopsOptions _options;
    private readonly ILogger<SqliteCatalogStatistics> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteCatalogStatistics"/> class.
    /// </summary>
    /// <param name="options">The bound DevOps options, which name the index.</param>
    /// <param name="logger">The logger used to report an index that could not be read.</param>
    public SqliteCatalogStatistics(IOptions<DevopsOptions> options, ILogger<SqliteCatalogStatistics> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public CatalogSummary Get()
    {
        string path = _options.CatalogIndexPath;

        if (string.IsNullOrWhiteSpace(path))
            return CatalogSummary.Unavailable(path, "No catalogue index is configured.");

        if (!File.Exists(path))
            return CatalogSummary.Unavailable(path, $"The catalogue index '{path}' does not exist yet.");

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 30,
        };

        try
        {
            using SqliteConnection connection = new(builder.ToString());
            connection.Open();

            CatalogueTotals totals = ReadTotals(connection);

            return new CatalogSummary
            {
                Available = true,
                Path = path,
                SchemaVersion = ReadSchemaVersion(connection),
                Models = totals.Models,
                ModelsWithScripts = totals.ModelsWithScripts,
                ModelsWithDetections = totals.ModelsWithDetections,
                TotalScore = totals.TotalScore,
                MaximumScore = totals.MaximumScore,
                Detections = totals.Detections,
                IndexedUtc = ReadTimestamp(connection),
                SourceRevision = ReadSourceRevision(connection),
                TopDetections = ReadDetections(connection),
                Periods = ReadPeriods(connection),
            };
        }
        catch (SqliteException exception)
        {
            _logger.LogWarning(exception, "The catalogue index {Index} could not be read.", path);
            return CatalogSummary.Unavailable(path, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The catalogue index {Index} could not be opened.", path);
            return CatalogSummary.Unavailable(path, exception.Message);
        }
    }

    /// <summary>
    /// Reads the catalogue's totals.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <returns>The totals, or zeroes when the derived table has not been written yet.</returns>
    private static CatalogueTotals ReadTotals(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT TotalModels,
                   ModelsWithScripts,
                   ModelsWithDetections,
                   TotalScore,
                   MaximumScore,
                   TotalDetections
            FROM CatalogueTotals
            WHERE Id = 1
            LIMIT 1;
            """;

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new CatalogueTotals(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetInt64(5))
            : CatalogueTotals.Empty;
    }

    /// <summary>
    /// Reads the schema version the index reports.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <returns>The version, or zero when it is not recorded.</returns>
    private static int ReadSchemaVersion(SqliteConnection connection) =>
        int.TryParse(ReadMeta(connection, SchemaVersionKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
            ? version
            : 0;

    /// <summary>
    /// Reads when the last migration committed.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <returns>The moment, or <see langword="null"/> when it is not recorded.</returns>
    private static DateTimeOffset? ReadTimestamp(SqliteConnection connection) =>
        DateTimeOffset.TryParse(
            ReadMeta(connection, CompletedKey),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out DateTimeOffset moment)
            ? moment
            : null;

    /// <summary>
    /// Reads the revision of the catalogue the index was built from.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <returns>The revision, or zero when it is not recorded.</returns>
    private static long ReadSourceRevision(SqliteConnection connection) =>
        long.TryParse(ReadMeta(connection, SourceRevisionKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out long revision)
            ? revision
            : 0;

    /// <summary>
    /// Reads a bookkeeping value.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <param name="key">The key to read.</param>
    /// <returns>The value, or <see langword="null"/> when the key is absent.</returns>
    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Meta WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Reads the most frequent detections.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <returns>The detections, most frequent first.</returns>
    private static IReadOnlyList<TagStatistic> ReadDetections(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Tag, Matches, Category
            FROM TagStatistics
            ORDER BY Matches DESC, Tag ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", DetectionLimit);

        List<TagStatistic> detections = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            detections.Add(new TagStatistic
            {
                Tag = reader.GetString(0),
                Matches = reader.GetInt64(1),
                Category = reader.GetInt32(2),
            });
        }

        return detections;
    }

    /// <summary>
    /// Reads the publication timeline.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <returns>The periods, oldest first.</returns>
    private static IReadOnlyList<PeriodStatistic> ReadPeriods(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Period, Models FROM PeriodStatistics ORDER BY Period ASC;";

        List<PeriodStatistic> periods = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            periods.Add(new PeriodStatistic
            {
                Period = reader.GetString(0),
                Models = reader.GetInt64(1),
            });
        }

        return periods;
    }

    /// <summary>
    /// The catalogue's totals, as one row of the derived table.
    /// </summary>
    /// <param name="Models">How many rows the catalogue holds.</param>
    /// <param name="ModelsWithScripts">How many rows contain scripts.</param>
    /// <param name="ModelsWithDetections">How many rows carry a detection.</param>
    /// <param name="TotalScore">The sum of every score.</param>
    /// <param name="MaximumScore">The highest score.</param>
    /// <param name="Detections">How many detection rows exist.</param>
    private readonly record struct CatalogueTotals(
        long Models,
        long ModelsWithScripts,
        long ModelsWithDetections,
        double TotalScore,
        double MaximumScore,
        long Detections)
    {
        /// <summary>Totals for an index that has not been populated yet.</summary>
        public static CatalogueTotals Empty { get; } = new(0, 0, 0, 0, 0, 0);
    }
}
