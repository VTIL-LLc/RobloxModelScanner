using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RobloxModelScanner.Features.AssetAnalysis;

using Scanner.IndexContract;
using Scanner.Indexer.Enums;
using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Models;
using Scanner.Indexer.Options;

namespace Scanner.Indexer.Implementation;

/// <summary>
/// Migrates the scanner's catalogue into the SQLite index and is the only writer of that database.
/// </summary>
/// <remarks>
/// <para>
/// The scanner discovers assets at a rate that makes reading its catalogue per request impossible: the
/// catalogue holds six figures of rows and keeps growing. It is projected into indexed tables instead, and
/// keeping that projection in its own service means the API can open the database read-only, which removes
/// lock contention entirely and makes it impossible for a request to corrupt the index or trigger an
/// unbounded migration.
/// </para>
/// <para>
/// Migration is driven by the catalogue's revision number and an inspect interval. A pass that finds the
/// revision unchanged does one keyed lookup and stops. When it has moved, the whole catalogue is streamed
/// into a fresh set of rows inside a single transaction: readers continue to see the previous, complete
/// index until the commit, and a failed pass rolls the transaction back and leaves that previous index
/// intact.
/// </para>
/// <para>
/// Peak memory is one catalogue row rather than the whole table, because the reader yields rows as the
/// statement walks them. Nothing here holds the catalogue open: each read is its own short-lived
/// connection, so the scanner's own writes are never queued behind a migration that is merely slow.
/// </para>
/// </remarks>
public sealed class SqliteIndexWriter : IIndexWriter
{
    /// <summary>
    /// The arity of the index schema this writer produces.
    /// </summary>
    private const int SchemaVersion = SqliteIndexSchema.Version;

    /// <summary>
    /// Ensures the schema exists exactly once per process.
    /// </summary>
    private readonly SemaphoreSlim _schemaGate = new(1, 1);

    /// <summary>
    /// Serializes migrations, so at most one write transaction is ever open against the index.
    /// </summary>
    private readonly SemaphoreSlim _migrationGate = new(1, 1);

    private readonly IndexerPaths _paths;
    private readonly ICatalogueReader _reader;
    private readonly ILogger<SqliteIndexWriter> _logger;
    private readonly string _connectionString;
    private readonly long _inspectIntervalMilliseconds;
    private readonly int _maximumDetections;
    private readonly string _derivedStatisticsSql;

    private IndexFingerprint _indexedFingerprint = IndexFingerprint.Unknown;
    private int _indexedModels;
    private int _indexedDetections;
    private DateTimeOffset? _indexedUtc;
    private DateTimeOffset? _lastAttemptUtc;
    private string? _lastFailure;
    private long _lastInspectTick;
    private bool _schemaReady;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteIndexWriter"/> class.
    /// </summary>
    /// <param name="paths">The resolved catalogue and index locations.</param>
    /// <param name="reader">The catalogue reader used to stream the source document.</param>
    /// <param name="options">The bound indexer options.</param>
    /// <param name="logger">The logger used to report migration outcomes.</param>
    public SqliteIndexWriter(
        IndexerPaths paths,
        ICatalogueReader reader,
        IOptions<IndexerOptions> options,
        ILogger<SqliteIndexWriter> logger)
    {
        IndexerOptions values = options.Value;

        _paths = paths;
        _reader = reader;
        _logger = logger;
        _inspectIntervalMilliseconds = Math.Max(1, values.RefreshSeconds) * 1000L;
        _maximumDetections = Math.Max(1, values.MaximumDetectionsPerBuild);
        _derivedStatisticsSql = SqliteIndexSchema.BuildDerivedStatisticsSql(_maximumDetections);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.IndexPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Bounds contention with readers that hold a snapshot open while a migration commits.
            DefaultTimeout = 30
        }.ToString();
    }

    /// <inheritdoc />
    public async Task<IndexBuildOutcome> EnsureCurrentAsync(bool ignoreThrottle, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        long? catalogueRevision = ReadCatalogueRevision();

        if (catalogueRevision is null)
        {
            _lastAttemptUtc = DateTimeOffset.UtcNow;
            return IndexBuildOutcome.CatalogueMissing;
        }

        IndexFingerprint current = new(catalogueRevision.Value);

        if (current == _indexedFingerprint) return IndexBuildOutcome.UpToDate;

        if (!ignoreThrottle && Environment.TickCount64 - _lastInspectTick < _inspectIntervalMilliseconds)
        {
            return IndexBuildOutcome.UpToDate;
        }

        if (!await _migrationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // Another migration is running. Its completion will publish a fingerprint that either matches
            // the revision this caller observed or supersedes it, so reporting Busy is honest and useful.
            return IndexBuildOutcome.Busy;
        }

        try
        {
            // Re-inspect under the gate: the revision may already have been migrated while we waited.
            catalogueRevision = ReadCatalogueRevision();

            if (catalogueRevision is null) return IndexBuildOutcome.CatalogueMissing;

            current = new IndexFingerprint(catalogueRevision.Value);
            if (current == _indexedFingerprint) return IndexBuildOutcome.UpToDate;

            _lastInspectTick = Environment.TickCount64;
            _lastAttemptUtc = DateTimeOffset.UtcNow;

            // The migration is disk and CPU bound and uses the synchronous SQLite API. Running it on a pool
            // thread keeps the control-plane request path responsive while it completes.
            return await Task.Run(() => Migrate(current, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _migrationGate.Release();
        }
    }

    /// <summary>
    /// Reads the revision the catalogue is at.
    /// </summary>
    /// <returns>The revision, or <see langword="null"/> when there is no catalogue to read.</returns>
    /// <remarks>
    /// The revision is recorded before the rows are walked rather than after. If the scanner commits while
    /// the pass is running, the index records the older revision and contains the newer rows, and the next
    /// pass rebuilds against a number that has moved. Recording the later revision instead would let the
    /// index claim to reflect rows it had not yet read, which is the only direction of error that matters.
    /// </remarks>
    private long? ReadCatalogueRevision() => _reader.ReadRevision(_paths.CataloguePath);

    /// <inheritdoc />
    public IndexFingerprint GetIndexedFingerprint() => _indexedFingerprint;

    /// <inheritdoc />
    public (int Models, int Detections) GetIndexedCounts() => (_indexedModels, _indexedDetections);

    /// <inheritdoc />
    public DateTimeOffset? GetIndexedUtc() => _indexedUtc;

    /// <inheritdoc />
    public int GetSchemaVersion() => _schemaReady ? SchemaVersion : 0;

    /// <summary>
    /// Gets the instant the last migration attempt started.
    /// </summary>
    /// <returns>The attempt instant, or <see langword="null"/> when no attempt has been made.</returns>
    internal DateTimeOffset? GetLastAttemptUtc() => _lastAttemptUtc;

    /// <summary>
    /// Gets the last migration failure, if any.
    /// </summary>
    /// <returns>A short failure description, or <see langword="null"/> when the last pass was clean.</returns>
    internal string? GetLastFailure() => _lastFailure;

    /// <summary>
    /// Creates the schema, reuses the existing index's bookkeeping and verifies its arity.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes once the writer is ready to migrate.</returns>
    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady) return;

        await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaReady) return;

            string? directory = Path.GetDirectoryName(_paths.IndexPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using SqliteConnection connection = OpenConnection();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = SqliteIndexSchema.CreateSql;
                command.ExecuteNonQuery();
            }

            int existingVersion = ReadSchemaVersion(connection);

            if (existingVersion != 0 && existingVersion != SchemaVersion)
            {
                // A reader that expects this arity would misread the tables. Rebuilding is the only safe
                // response, so the previous bookkeeping is discarded and the next pass rewrites everything.
                _logger.LogWarning(
                    "The index schema on disk is version {Existing} but this service writes version {Current}. " +
                    "The index will be rebuilt from the catalogue.",
                    existingVersion,
                    SchemaVersion);

                // Creating the current schema over the old tables is not enough. Every statement in it is
                // guarded by IF NOT EXISTS, so an old table survives with the old columns, and the migration
                // then fails on the first column it names that the old table does not have -- which is
                // exactly what happened when the found order was added. Dropping first is what makes the
                // rebuild real, and it costs only the work the rebuild was about to do anyway.
                using (SqliteCommand drop = connection.CreateCommand())
                {
                    drop.CommandText = SqliteIndexSchema.DropSql;
                    drop.ExecuteNonQuery();
                }

                using (SqliteCommand create = connection.CreateCommand())
                {
                    create.CommandText = SqliteIndexSchema.CreateSql;
                    create.ExecuteNonQuery();
                }

                _indexedFingerprint = IndexFingerprint.Unknown;
            }
            else
            {
                // Adopting the stored fingerprint is what makes an index built by a previous run
                // authoritative immediately after a restart, instead of forcing a needless full migration.
                _indexedFingerprint = ReadFingerprint(connection);
                (_indexedModels, _indexedDetections) = ReadCounts(connection);
                _indexedUtc = ReadCompletedUtc(connection);
            }

            _schemaReady = true;

            _logger.LogInformation(
                "Index ready at {IndexPath}. Schema version {SchemaVersion}, {ModelCount} models indexed.",
                _paths.IndexPath,
                SchemaVersion,
                _indexedModels);
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    /// <summary>
    /// Rebuilds the index from the catalogue document inside a single transaction.
    /// </summary>
    /// <param name="fingerprint">The catalogue revision being migrated.</param>
    /// <param name="cancellationToken">A token used to cancel the migration.</param>
    /// <returns>How the migration ended.</returns>
    private IndexBuildOutcome Migrate(IndexFingerprint fingerprint, CancellationToken cancellationToken)
    {
        try
        {
            using SqliteConnection connection = OpenConnection();

            using (SqliteCommand pragma = connection.CreateCommand())
            {
                // Durability is relaxed for the index: it is derived data that can always be rebuilt, and the
                // trade removes a per-commit disk flush from the migration.
                pragma.CommandText = "PRAGMA synchronous = NORMAL;";
                pragma.ExecuteNonQuery();
            }

            using SqliteTransaction transaction = connection.BeginTransaction();

            ExecuteNonQuery(connection, transaction, "DELETE FROM Models;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM ModelTags;");

            using SqliteCommand insertModel = CreateModelInsert(connection, transaction);
            using SqliteCommand insertTag = CreateTagInsert(connection, transaction);

            IDetectionRuleRegistry registry = AssetAnalyzer.Registry;

            int models = 0;
            int detections = 0;

            foreach (CatalogueRow row in _reader.Read(_paths.CataloguePath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                BindModel(insertModel, row);
                insertModel.ExecuteNonQuery();
                models++;

                foreach (string tag in TagParser.Parse(row.Tags))
                {
                    IDetectionRule? rule = registry.FindByTag(tag);

                    BindTag(
                        insertTag,
                        row.Model,
                        tag,
                        rule is null ? RuleCategory.Unknown : rule.Category,
                        rule is null ? RuleSeverity.Informational : rule.Severity);

                    insertTag.ExecuteNonQuery();
                    detections++;
                }
            }

            ExecuteNonQuery(connection, transaction, _derivedStatisticsSql);

            DateTimeOffset completedUtc = DateTimeOffset.UtcNow;

            WriteMeta(connection, transaction, SqliteIndexSchema.MetaSchemaVersion, SchemaVersion.ToString(CultureInfo.InvariantCulture));
            WriteMeta(connection, transaction, SqliteIndexSchema.MetaSourceRevision, fingerprint.Revision.ToString(CultureInfo.InvariantCulture));
            WriteMeta(connection, transaction, SqliteIndexSchema.MetaModelCount, models.ToString(CultureInfo.InvariantCulture));
            WriteMeta(connection, transaction, SqliteIndexSchema.MetaDetectionCount, detections.ToString(CultureInfo.InvariantCulture));
            WriteMeta(connection, transaction, SqliteIndexSchema.MetaCompletedUtc, completedUtc.ToString("O", CultureInfo.InvariantCulture));

            transaction.Commit();

            _indexedFingerprint = fingerprint;
            _indexedModels = models;
            _indexedDetections = detections;
            _indexedUtc = completedUtc;
            _lastFailure = null;

            _logger.LogInformation(
                "Catalogue migrated: {ModelCount} models, {DetectionCount} detections.",
                models,
                detections);

            return IndexBuildOutcome.Rebuilt;
        }
        catch (IOException exception)
        {
            return RecordFailure(IndexBuildOutcome.StorageFailed, exception, "The catalogue or index could not be read or written.");
        }
        catch (SqliteException exception)
        {
            // Two databases are in play here: the catalogue is being read and the index is being written,
            // and a refusal from either arrives as this one exception type. Separating them would mean
            // guessing from the message, and the distinction that matters to an operator survives anyway --
            // the previous index is still in place, the pass failed, and the text below says which layer
            // refused it.
            return RecordFailure(IndexBuildOutcome.StorageFailed, exception, "The catalogue or index database rejected the migration.");
        }
        catch (OperationCanceledException)
        {
            // A cancelled migration is a shutdown, not a fault. The transaction rolls back on dispose and the
            // fingerprint is left unchanged, so the next start re-reads the document.
            throw;
        }
        catch (Exception exception)
        {
            // Anything else is a bug. It is recorded so the status endpoint reports an unhealthy index
            // instead of the failure being visible only in a log, and the good index is left in place.
            return RecordFailure(IndexBuildOutcome.StorageFailed, exception, "The migration failed unexpectedly.");
        }
    }

    /// <summary>
    /// Records a failed migration and reports the outcome to the caller.
    /// </summary>
    /// <param name="outcome">The outcome to report.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="message">A caller-safe description of the failure.</param>
    /// <returns>The supplied outcome.</returns>
    private IndexBuildOutcome RecordFailure(IndexBuildOutcome outcome, Exception exception, string message)
    {
        _lastFailure = message;
        _logger.LogWarning(exception, "{Message} The previous index is retained.", message);
        return outcome;
    }

    /// <summary>
    /// Opens a connection to the index database.
    /// </summary>
    /// <returns>An open connection that the caller is responsible for disposing.</returns>
    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Creates the prepared statement used to insert catalogue rows.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="transaction">The transaction the command participates in.</param>
    /// <returns>A prepared command whose parameters are bound per row by <see cref="BindModel"/>.</returns>
    private static SqliteCommand CreateModelInsert(SqliteConnection connection, SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO Models
                (ModelId, Sequence, Date, Name, Description, CreatorName, CreatorId, HasScripts, Tags, Score, AssetUrl, Thumbnail)
            VALUES
                ($modelId, $sequence, $date, $name, $description, $creatorName, $creatorId, $hasScripts, $tags, $score, $assetUrl, $thumbnail);
            """;

        command.Parameters.Add("$modelId", SqliteType.Integer);
        command.Parameters.Add("$sequence", SqliteType.Integer);
        command.Parameters.Add("$date", SqliteType.Text);
        command.Parameters.Add("$name", SqliteType.Text);
        command.Parameters.Add("$description", SqliteType.Text);
        command.Parameters.Add("$creatorName", SqliteType.Text);
        command.Parameters.Add("$creatorId", SqliteType.Text);
        command.Parameters.Add("$hasScripts", SqliteType.Integer);
        command.Parameters.Add("$tags", SqliteType.Text);
        command.Parameters.Add("$score", SqliteType.Real);
        command.Parameters.Add("$assetUrl", SqliteType.Text);
        command.Parameters.Add("$thumbnail", SqliteType.Text);
        command.Prepare();

        return command;
    }

    /// <summary>
    /// Binds one catalogue row to the insert command.
    /// </summary>
    /// <param name="command">The prepared model insert command.</param>
    /// <param name="row">The row to bind.</param>
    /// <remarks>
    /// Text members are coerced rather than trusted: a catalogue row may carry an empty string for a name or
    /// a description, and the columns are declared <c>NOT NULL</c>.
    /// </remarks>
    private static void BindModel(SqliteCommand command, CatalogueRow row)
    {
        command.Parameters["$modelId"].Value = row.Model;
        command.Parameters["$sequence"].Value = row.Sequence;
        command.Parameters["$date"].Value = row.Date ?? string.Empty;
        command.Parameters["$name"].Value = row.Name ?? string.Empty;
        command.Parameters["$description"].Value = row.Description ?? string.Empty;
        command.Parameters["$creatorName"].Value = row.CreatorName ?? string.Empty;
        command.Parameters["$creatorId"].Value = row.CreatorId ?? string.Empty;
        command.Parameters["$hasScripts"].Value = row.HasScripts ? 1 : 0;
        command.Parameters["$tags"].Value = row.Tags ?? string.Empty;
        command.Parameters["$score"].Value = row.Score;
        command.Parameters["$assetUrl"].Value = row.AssetUrl ?? string.Empty;
        command.Parameters["$thumbnail"].Value = row.Thumbnail is null ? DBNull.Value : row.Thumbnail;
    }

    /// <summary>
    /// Creates the prepared statement used to insert normalized detection rows.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="transaction">The transaction the command participates in.</param>
    /// <returns>A prepared command whose parameters are bound per detection by <see cref="BindTag"/>.</returns>
    private static SqliteCommand CreateTagInsert(SqliteConnection connection, SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        // A duplicate tag cannot occur for one model because the primary key collapses it, but OR IGNORE
        // keeps the statement correct even if a source row repeats a label.
        command.CommandText = """
            INSERT OR IGNORE INTO ModelTags (ModelId, Tag, Category, Severity)
            VALUES ($modelId, $tag, $category, $severity);
            """;

        command.Parameters.Add("$modelId", SqliteType.Integer);
        command.Parameters.Add("$tag", SqliteType.Text);
        command.Parameters.Add("$category", SqliteType.Integer);
        command.Parameters.Add("$severity", SqliteType.Integer);
        command.Prepare();

        return command;
    }

    /// <summary>
    /// Binds one normalized detection row to the insert command.
    /// </summary>
    /// <param name="command">The prepared tag insert command.</param>
    /// <param name="modelId">The asset identifier the detection belongs to.</param>
    /// <param name="tag">The canonical rule tag.</param>
    /// <param name="category">The rule category, or <see cref="RuleCategory.Unknown"/> for a legacy label.</param>
    /// <param name="severity">The rule severity, or informational for a legacy label.</param>
    private static void BindTag(
        SqliteCommand command,
        long modelId,
        string tag,
        RuleCategory category,
        RuleSeverity severity)
    {
        command.Parameters["$modelId"].Value = modelId;
        command.Parameters["$tag"].Value = tag;
        command.Parameters["$category"].Value = (int)category;
        command.Parameters["$severity"].Value = (int)severity;
    }

    /// <summary>
    /// Executes a non-query statement inside a transaction.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="transaction">The transaction the statement participates in.</param>
    /// <param name="sql">The statement text. It is a compile-time constant, never caller input.</param>
    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes an index bookkeeping value.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="transaction">The transaction the statement participates in.</param>
    /// <param name="key">The bookkeeping key.</param>
    /// <param name="value">The value to store.</param>
    private static void WriteMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Meta (Key, Value) VALUES ($key, $value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads an index bookkeeping value.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="key">The bookkeeping key.</param>
    /// <returns>The stored value, or <see langword="null"/> when the key has never been written.</returns>
    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Meta WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Reads the arity of the schema recorded in the database.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <returns>The recorded schema version, or zero when the index has never been written.</returns>
    private static int ReadSchemaVersion(SqliteConnection connection) =>
        int.TryParse(ReadMeta(connection, SqliteIndexSchema.MetaSchemaVersion), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
            ? version
            : 0;

    /// <summary>
    /// Reads the catalogue revision the index reflects.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <returns>The recorded fingerprint, or <see cref="IndexFingerprint.Unknown"/> when none is recorded.</returns>
    private static IndexFingerprint ReadFingerprint(SqliteConnection connection)
    {
        bool hasRevision = long.TryParse(
            ReadMeta(connection, SqliteIndexSchema.MetaSourceRevision),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long revision);

        return hasRevision ? new IndexFingerprint(revision) : IndexFingerprint.Unknown;
    }

    /// <summary>
    /// Reads the model and detection counts recorded by the last migration.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <returns>The recorded model and detection counts, or zeros when none are recorded.</returns>
    private static (int Models, int Detections) ReadCounts(SqliteConnection connection)
    {
        int models = int.TryParse(
            ReadMeta(connection, SqliteIndexSchema.MetaModelCount),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int modelCount)
            ? modelCount
            : 0;

        int detections = int.TryParse(
            ReadMeta(connection, SqliteIndexSchema.MetaDetectionCount),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int detectionCount)
            ? detectionCount
            : 0;

        return (models, detections);
    }

    /// <summary>
    /// Reads the instant the last migration committed.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <returns>The recorded completion instant, or <see langword="null"/> when none is recorded.</returns>
    private static DateTimeOffset? ReadCompletedUtc(SqliteConnection connection) =>
        DateTimeOffset.TryParse(
            ReadMeta(connection, SqliteIndexSchema.MetaCompletedUtc),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset completedUtc)
            ? completedUtc
            : null;
}
