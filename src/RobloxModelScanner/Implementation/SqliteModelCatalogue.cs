using System.Globalization;

using Microsoft.Data.Sqlite;

using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

using Scanner.IndexContract;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// The scanner's catalogue, held in the SQLite database it owns.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue was a single JSON array until it became this, and that format had one dominating
/// property: saving any part of it meant rewriting all of it. Most of what the previous implementation
/// did existed to hide that -- a whole-document snapshot for every write, a forty-megabyte serialisation
/// on a timer, and a flush throttle whose only job was to keep the cost of the format off the scan path.
/// A database does not have the property, so the work is gone, but the batching is not: a commit per
/// discovered asset would put an fsync on the path of every inspection, and an inspection is the part
/// that costs real time.
/// </para>
/// <para>
/// The in-memory catalogue is a map from identifier to the order the row was found in. That is all the
/// interface has to answer without touching the disk -- whether an identifier is known, and how many rows
/// there are -- and the rows themselves stay in the database. It replaces a resident list of a hundred and
/// twenty thousand records with a hundred and twenty thousand integers, which is the difference between a
/// working set the scanner notices and one it does not.
/// </para>
/// <para>
/// The found order is assigned when a row is first inserted and is never reassigned, including by the
/// repair pass that rewrites everything else about it. "Found lately" and "created lately" are different
/// questions, and the catalogue is asked the first one.
/// </para>
/// <para>
/// Writes are atomic in the sense that matters: rows and the revision that describes them are committed
/// together, so the indexing service either sees the whole batch or none of it, and it can tell which by
/// asking for one number.
/// </para>
/// </remarks>
/// <seealso cref="IModelCatalogue"/>
public sealed class SqliteModelCatalogue : IModelCatalogue
{
    /// <summary>
    /// Guards the found-order map, the pending batch and the bookkeeping.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The absolute path of the database, which may not exist yet.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// How long a change may wait before it forces a write.
    /// </summary>
    private readonly TimeSpan _flushInterval;

    /// <summary>
    /// How many changes force a write before the interval elapses.
    /// </summary>
    private readonly int _flushRows;

    /// <summary>
    /// The connection string, built once from the path.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Every known identifier, mapped to the order the catalogue found it in.
    /// </summary>
    private readonly Dictionary<long, long> _found = [];

    /// <summary>
    /// Changes made since the last write began, keyed by identifier.
    /// </summary>
    /// <remarks>
    /// Keyed rather than listed, because a row can change twice between two flushes and only its final
    /// state is worth writing. A list would write the same row twice and the earlier write would be lost.
    /// </remarks>
    private readonly Dictionary<long, CatalogueRecord> _pending = [];

    /// <summary>
    /// The highest found order handed out so far.
    /// </summary>
    private long _foundHigh;

    /// <summary>
    /// The number of changes that have not yet been written.
    /// </summary>
    private int _unsaved;

    /// <summary>
    /// Whether the database differs from the pending batch and the rows in memory.
    /// </summary>
    private bool _dirty;

    /// <summary>
    /// When the last write completed.
    /// </summary>
    private DateTime _lastWriteUtc = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteModelCatalogue"/> class.
    /// </summary>
    /// <param name="path">The path of the database, which need not exist yet.</param>
    /// <param name="flushInterval">How long a change may wait before it forces a write.</param>
    /// <param name="flushRows">How many changes force a write before the interval elapses.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    public SqliteModelCatalogue(string path, TimeSpan flushInterval, int flushRows)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        _path = System.IO.Path.GetFullPath(path);
        _flushInterval = flushInterval;
        _flushRows = Math.Max(1, flushRows);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // A flush and the indexing service's read of the same file each take a moment, and the indexing
            // service reads it on a timer of its own. The timeout is what keeps an unlucky overlap from
            // being reported as a failure to write the catalogue.
            DefaultTimeout = 30
        }.ToString();
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_gate) return _found.Count;
        }
    }

    /// <inheritdoc />
    public int UnsavedCount
    {
        get
        {
            lock (_gate) return _unsaved;
        }
    }

    /// <inheritdoc />
    /// <exception cref="SqliteException">Thrown when the database exists but is not a catalogue.</exception>
    /// <remarks>
    /// The provider's asynchronous API is a thread-pool hop around a synchronous call, so the work is done
    /// on this thread and only the return type is asynchronous. A load is one indexed statement, it happens
    /// once, and it happens before the sweep starts, which is exactly the point at which a stall is free.
    /// </remarks>
    public Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using SqliteConnection connection = OpenConnection();

        using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText = SqliteCatalogueSchema.CreateSql;
            create.ExecuteNonQuery();
        }

        Dictionary<long, long> found = [];
        long high = 0;

        using (SqliteCommand read = connection.CreateCommand())
        {
            read.CommandText = "SELECT Model, Sequence FROM Models ORDER BY Sequence;";

            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                long model = reader.GetInt64(0);
                long sequence = reader.GetInt64(1);

                found[model] = sequence;
                if (sequence > high) high = sequence;
            }
        }

        lock (_gate)
        {
            _found.Clear();
            foreach (KeyValuePair<long, long> row in found) _found[row.Key] = row.Value;

            _foundHigh = high;
            _pending.Clear();
            _unsaved = 0;
            _dirty = false;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool Contains(long modelId)
    {
        lock (_gate) return _found.ContainsKey(modelId);
    }

    /// <inheritdoc />
    public bool Append(CatalogueRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            if (_found.ContainsKey(record.Model)) return false;

            long sequence = ++_foundHigh;

            _found[record.Model] = sequence;
            _pending[record.Model] = record with { Sequence = sequence };
            MarkChanged();

            return true;
        }
    }

    /// <inheritdoc />
    public bool Replace(CatalogueRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            if (!_found.TryGetValue(record.Model, out long sequence)) return false;

            _pending[record.Model] = record with { Sequence = sequence };
            MarkChanged();

            return true;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The whole catalogue is read here, which is the one thing the rest of this class is built to avoid.
    /// The alternative is expressing "nothing descriptive is known about this row" as a SQL predicate, and
    /// that would put the rule in two places: the record owns the definition of the damage a repair is
    /// looking for, and a query that disagreed with it would either skip damaged rows or chase rows that
    /// have none. This is called twice per repair pass and never on the sweep's path.
    /// </remarks>
    public IReadOnlyList<CatalogueRecord> RowsWithoutMetadata()
    {
        Dictionary<long, CatalogueRecord> rows = [];

        using (SqliteConnection connection = OpenConnection())
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.CommandText = $"SELECT {SqliteCatalogueSchema.ModelColumns} FROM Models ORDER BY Sequence;";

            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                CatalogueRecord record = ReadRecord(reader);
                rows[record.Model] = record;
            }
        }

        lock (_gate)
        {
            // A row changed since the last write is not in the database yet, and where it is, the pending
            // copy is the newer one.
            foreach (KeyValuePair<long, CatalogueRecord> change in _pending) rows[change.Key] = change.Value;
        }

        List<CatalogueRecord> damaged = [];
        foreach (CatalogueRecord record in rows.Values)
        {
            if (record.HasNoMetadata()) damaged.Add(record);
        }

        damaged.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

        return damaged;
    }

    /// <inheritdoc />
    public Task FlushAsync(bool force, CancellationToken cancellationToken)
    {
        CatalogueRecord[] batch;

        lock (_gate)
        {
            // Both halves matter. Without the first, an idle scanner would commit an empty transaction every
            // time the timer fired and the index would keep rebuilding for nothing. Without the second,
            // every change would be written immediately and the throttle would be decorative.
            bool due = force || _unsaved >= _flushRows || DateTime.UtcNow - _lastWriteUtc >= _flushInterval;
            if (!_dirty || !due) return Task.CompletedTask;

            batch = [.. _pending.Values];
            _pending.Clear();
            _unsaved = 0;
        }

        // Written outside the lock. Holding it across a commit would stall every inspecting worker for the
        // duration of the disk write, which is the opposite of the point.
        Write(batch, cancellationToken);

        lock (_gate)
        {
            _lastWriteUtc = DateTime.UtcNow;

            // Cleared only when nothing arrived while the write was running. A change made during the write
            // is not in this batch, and leaving the flag set is what guarantees the next flush writes it.
            if (_pending.Count == 0) _dirty = false;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records that the in-memory state no longer matches the database.
    /// </summary>
    /// <remarks>
    /// Called with the lock held.
    /// </remarks>
    private void MarkChanged()
    {
        _unsaved++;
        _dirty = true;
    }

    /// <summary>
    /// Writes a batch of rows and the revision that describes them, in one transaction.
    /// </summary>
    /// <param name="batch">The rows to write.</param>
    /// <param name="cancellationToken">A token used to abandon the write.</param>
    /// <exception cref="SqliteException">Thrown when the write fails.</exception>
    /// <remarks>
    /// The revision is bumped inside the transaction rather than after it. That ordering is the whole
    /// reason a reader can trust the number: it never observes a revision whose rows have not landed, so
    /// "the index is current" is decidable from one integer.
    /// </remarks>
    private void Write(CatalogueRecord[] batch, CancellationToken cancellationToken)
    {
        using SqliteConnection connection = OpenConnection();

        using (SqliteCommand pragma = connection.CreateCommand())
        {
            // Durability is left at its default here, unlike the index's. The index can be rebuilt from the
            // catalogue, and the catalogue cannot be rebuilt from anything.
            pragma.CommandText = "PRAGMA busy_timeout = 30000;";
            pragma.ExecuteNonQuery();
        }

        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand upsert = CreateUpsert(connection, transaction))
        {
            foreach (CatalogueRecord record in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Bind(upsert, record);
                upsert.ExecuteNonQuery();
            }
        }

        long revision = ReadRevision(connection, transaction) + 1;

        WriteMeta(
            connection,
            transaction,
            SqliteCatalogueSchema.MetaRevision,
            revision.ToString(CultureInfo.InvariantCulture));

        WriteMeta(
            connection,
            transaction,
            SqliteCatalogueSchema.MetaUpdatedUtc,
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        transaction.Commit();
    }

    /// <summary>
    /// Opens a connection to the catalogue database.
    /// </summary>
    /// <returns>An open connection that the caller is responsible for disposing.</returns>
    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Creates the prepared statement used to insert a row or update the one already there.
    /// </summary>
    /// <param name="connection">An open connection to the catalogue database.</param>
    /// <param name="transaction">The transaction the command participates in.</param>
    /// <returns>A prepared command whose parameters are bound per row by <see cref="Bind"/>.</returns>
    /// <remarks>
    /// The found order is absent from the update list on purpose. A row that is written a second time --
    /// because a repair corrected it, or because a later inspection learned more about it -- keeps the
    /// place it was given when it was first found. Moving it would make the catalogue's default order a
    /// statement about editing rather than about discovery, and every repair would reshuffle the pages.
    /// </remarks>
    private static SqliteCommand CreateUpsert(SqliteConnection connection, SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Models
                (Model, Sequence, Date, Name, Description, CreatorName, CreatorId, HasScripts, Tags, Score, AssetUrl, Thumbnail)
            VALUES
                ($model, $sequence, $date, $name, $description, $creatorName, $creatorId, $hasScripts, $tags, $score, $assetUrl, $thumbnail)
            ON CONFLICT (Model) DO UPDATE SET
                Date        = excluded.Date,
                Name        = excluded.Name,
                Description = excluded.Description,
                CreatorName = excluded.CreatorName,
                CreatorId   = excluded.CreatorId,
                HasScripts  = excluded.HasScripts,
                Tags        = excluded.Tags,
                Score       = excluded.Score,
                AssetUrl    = excluded.AssetUrl,
                Thumbnail   = excluded.Thumbnail;
            """;

        command.Parameters.Add("$model", SqliteType.Integer);
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
    /// Binds one row to the upsert command.
    /// </summary>
    /// <param name="command">The prepared upsert command.</param>
    /// <param name="record">The row to bind.</param>
    /// <remarks>
    /// Text members are coerced rather than trusted: the columns are declared <c>NOT NULL</c>, and a row
    /// written by an earlier revision of the scanner can carry a null in any of them.
    /// </remarks>
    private static void Bind(SqliteCommand command, CatalogueRecord record)
    {
        command.Parameters["$model"].Value = record.Model;
        command.Parameters["$sequence"].Value = record.Sequence;
        command.Parameters["$date"].Value = record.Date ?? string.Empty;
        command.Parameters["$name"].Value = record.Name ?? string.Empty;
        command.Parameters["$description"].Value = record.Description ?? string.Empty;
        command.Parameters["$creatorName"].Value = record.CreatorName ?? string.Empty;
        command.Parameters["$creatorId"].Value = record.CreatorId ?? string.Empty;
        command.Parameters["$hasScripts"].Value = record.HasScripts ? 1 : 0;
        command.Parameters["$tags"].Value = record.Tags ?? string.Empty;
        command.Parameters["$score"].Value = record.Score;
        command.Parameters["$assetUrl"].Value = record.AssetUrl ?? string.Empty;
        command.Parameters["$thumbnail"].Value = record.Thumbnail is null ? DBNull.Value : record.Thumbnail;
    }

    /// <summary>
    /// Reads one row from the column order declared by the schema.
    /// </summary>
    /// <param name="reader">A reader positioned on a row.</param>
    /// <returns>The row.</returns>
    private static CatalogueRecord ReadRecord(SqliteDataReader reader) => new(
        Model: reader.GetInt64(0),
        Sequence: reader.GetInt64(1),
        Date: reader.GetString(2),
        Name: reader.GetString(3),
        Description: reader.GetString(4),
        CreatorName: reader.GetString(5),
        CreatorId: reader.GetString(6),
        HasScripts: reader.GetInt64(7) != 0,
        Tags: reader.GetString(8),
        Score: reader.GetDouble(9),
        AssetUrl: reader.GetString(10),
        Thumbnail: reader.IsDBNull(11) ? null : reader.GetString(11));

    /// <summary>
    /// Reads the catalogue's current revision inside a write transaction.
    /// </summary>
    /// <param name="connection">An open connection to the catalogue database.</param>
    /// <param name="transaction">The transaction the read participates in.</param>
    /// <returns>The stored revision, or zero when the catalogue has never been written.</returns>
    private static long ReadRevision(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Value FROM Meta WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", SqliteCatalogueSchema.MetaRevision);

        return long.TryParse(
            command.ExecuteScalar() as string,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long revision)
            ? revision
            : 0;
    }

    /// <summary>
    /// Writes a bookkeeping value.
    /// </summary>
    /// <param name="connection">An open connection to the catalogue database.</param>
    /// <param name="transaction">The transaction the statement participates in.</param>
    /// <param name="key">The bookkeeping key.</param>
    /// <param name="value">The value to store.</param>
    private static void WriteMeta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value)
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
}
