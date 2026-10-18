namespace Scanner.IndexContract;

/// <summary>
/// The definition of the SQLite index: its version, its tables and the statements that derive analytics.
/// </summary>
/// <remarks>
/// <para>
/// The index is a contract between two processes that are deployed separately. The indexing service is
/// its only writer; the scanner API opens it read-only and never creates, migrates or repairs anything.
/// A change to a column or to a bookkeeping key is therefore a change to both, so the definition lives
/// in this assembly and both projects reference it. It was previously written out twice -- once by the
/// writer and once by the API's own copy of the same schema -- and the two copies had already drifted,
/// which is the failure this arrangement exists to make impossible.
/// </para>
/// <para>
/// Derived analytics are materialized rather than computed on read. The migration already walks the whole
/// catalogue, so folding the aggregates into the same transaction is close to free, and it turns the
/// analytics endpoint from a set of full-table scans into a handful of keyed reads. The creator
/// leaderboard in particular used to require a grouped scan and a sort that grew with the number of
/// distinct creators.
/// </para>
/// <para>
/// Nothing here is a secret and nothing here is caller input: every statement is a compile-time
/// constant. The class is public because it crosses an assembly boundary, not because it is meant to be
/// varied at run time.
/// </para>
/// </remarks>
public static class SqliteIndexSchema
{
    /// <summary>
    /// The arity of the schema described by this class.
    /// </summary>
    /// <value>
    /// Bump this whenever a table or a bookkeeping key changes shape. Consumers compare it against the
    /// value they find in the database and refuse to read an index they do not understand, which turns a
    /// silent misread into an explicit, reportable mismatch.
    /// </value>
    public const int Version = 1;

    /// <summary>
    /// The bookkeeping key holding the arity of the schema on disk.
    /// </summary>
    public const string MetaSchemaVersion = "index.schemaVersion";

    /// <summary>
    /// The bookkeeping key holding the length of the catalogue revision that was indexed.
    /// </summary>
    public const string MetaSourceLength = "source.length";

    /// <summary>
    /// The bookkeeping key holding the last-write ticks of the catalogue revision that was indexed.
    /// </summary>
    public const string MetaSourceWriteTicks = "source.writeTicks";

    /// <summary>
    /// The bookkeeping key holding the instant the last migration committed.
    /// </summary>
    public const string MetaCompletedUtc = "index.completedUtc";

    /// <summary>
    /// The bookkeeping key holding the number of models in the index.
    /// </summary>
    public const string MetaModelCount = "index.models";

    /// <summary>
    /// The bookkeeping key holding the number of detection rows in the index.
    /// </summary>
    public const string MetaDetectionCount = "index.detections";

    /// <summary>
    /// The statements that create the schema. They are idempotent and are applied on every start-up.
    /// </summary>
    /// <remarks>
    /// <c>journal_mode=WAL</c> is what lets a reader continue against the previous, complete index while a
    /// migration holds its write transaction; without it a reader would block for the whole migration.
    /// The indexes match the query shapes the API actually issues, and the derived tables carry the
    /// aggregates so that no analytics request scans the catalogue.
    /// </remarks>
    public const string CreateSql = """
        PRAGMA journal_mode = WAL;
        PRAGMA busy_timeout = 5000;

        CREATE TABLE IF NOT EXISTS Models
        (
            ModelId     INTEGER NOT NULL PRIMARY KEY,
            Date        TEXT    NOT NULL DEFAULT '',
            Name        TEXT    NOT NULL DEFAULT '',
            Description TEXT    NOT NULL DEFAULT '',
            CreatorName TEXT    NOT NULL DEFAULT '',
            CreatorId   TEXT    NOT NULL DEFAULT '',
            HasScripts  INTEGER NOT NULL DEFAULT 0,
            Tags        TEXT    NOT NULL DEFAULT '',
            Score       REAL    NOT NULL DEFAULT 0,
            AssetUrl    TEXT    NOT NULL DEFAULT '',
            Thumbnail   TEXT    NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Models_Date       ON Models (Date);
        CREATE INDEX IF NOT EXISTS IX_Models_HasScripts ON Models (HasScripts);
        CREATE INDEX IF NOT EXISTS IX_Models_Score      ON Models (Score);
        CREATE INDEX IF NOT EXISTS IX_Models_CreatorId  ON Models (CreatorId);

        CREATE TABLE IF NOT EXISTS ModelTags
        (
            ModelId  INTEGER NOT NULL,
            Tag      TEXT    NOT NULL,
            Category INTEGER NOT NULL DEFAULT 0,
            Severity INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (ModelId, Tag)
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS IX_ModelTags_Tag      ON ModelTags (Tag);
        CREATE INDEX IF NOT EXISTS IX_ModelTags_Category ON ModelTags (Category);

        CREATE TABLE IF NOT EXISTS Meta
        (
            Key   TEXT NOT NULL PRIMARY KEY,
            Value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS CatalogueTotals
        (
            Id                   INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
            TotalModels          INTEGER NOT NULL,
            ModelsWithScripts    INTEGER NOT NULL,
            ModelsWithDetections INTEGER NOT NULL,
            TotalScore           REAL    NOT NULL,
            MaximumScore         REAL    NOT NULL,
            TotalDetections      INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS TagStatistics
        (
            Tag      TEXT    NOT NULL PRIMARY KEY,
            Matches  INTEGER NOT NULL,
            Category INTEGER NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS ScoreStatistics
        (
            Bucket INTEGER NOT NULL PRIMARY KEY,
            Models INTEGER NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS PeriodStatistics
        (
            Period TEXT    NOT NULL PRIMARY KEY,
            Models INTEGER NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS CreatorStatistics
        (
            CreatorId         TEXT    NOT NULL PRIMARY KEY,
            CreatorName       TEXT    NOT NULL,
            Models            INTEGER NOT NULL,
            ModelsWithScripts INTEGER NOT NULL,
            Detections        INTEGER NOT NULL
        ) WITHOUT ROWID;

        /* Serves the leaderboard in descending order straight from the index, without a sort. */
        CREATE INDEX IF NOT EXISTS IX_CreatorStatistics_Models ON CreatorStatistics (Models DESC);
        """;

    /// <summary>
    /// The statements that clear and repopulate the derived analytics tables.
    /// </summary>
    /// <param name="maximumDetections">The greatest number of detection labels the reference table may hold.</param>
    /// <returns>The statements, ready to execute in the migration's own transaction.</returns>
    /// <remarks>
    /// These run inside the same transaction as the catalogue write, so the derived tables can never
    /// disagree with the tables they summarize. The detection list is capped after ordering by frequency,
    /// so a catalogue polluted with thousands of junk labels cannot produce an unbounded reference table.
    /// </remarks>
    public static string BuildDerivedStatisticsSql(int maximumDetections) => $"""
        DELETE FROM CatalogueTotals;
        DELETE FROM TagStatistics;
        DELETE FROM ScoreStatistics;
        DELETE FROM PeriodStatistics;
        DELETE FROM CreatorStatistics;

        INSERT INTO CatalogueTotals
            (Id, TotalModels, ModelsWithScripts, ModelsWithDetections, TotalScore, MaximumScore, TotalDetections)
        SELECT 1,
               COUNT(*),
               COALESCE(SUM(HasScripts), 0),
               COALESCE(SUM(Score > 0), 0),
               COALESCE(SUM(Score), 0),
               COALESCE(MAX(Score), 0),
               (SELECT COUNT(*) FROM ModelTags)
        FROM Models;

        INSERT INTO TagStatistics (Tag, Matches, Category)
        SELECT Tag, COUNT(*) AS Matches, MIN(Category)
        FROM ModelTags
        GROUP BY Tag
        ORDER BY Matches DESC, Tag ASC
        LIMIT {maximumDetections};

        INSERT INTO ScoreStatistics (Bucket, Models)
        SELECT CASE
                   WHEN Score <= 0 THEN 0
                   WHEN Score < 1  THEN 1
                   WHEN Score < 3  THEN 2
                   WHEN Score < 5  THEN 3
                   WHEN Score < 10 THEN 4
                   ELSE 5
               END AS Bucket,
               COUNT(*)
        FROM Models
        GROUP BY Bucket;

        INSERT INTO PeriodStatistics (Period, Models)
        SELECT substr(Date, 1, 7) AS Period, COUNT(*)
        FROM Models
        WHERE Date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]*'
        GROUP BY Period;

        INSERT INTO CreatorStatistics (CreatorId, CreatorName, Models, ModelsWithScripts, Detections)
        SELECT CreatorId,
               MAX(CreatorName),
               COUNT(*),
               COALESCE(SUM(HasScripts), 0),
               COALESCE(SUM(Score > 0), 0)
        FROM Models
        WHERE CreatorId <> ''
        GROUP BY CreatorId;
        """;
}
