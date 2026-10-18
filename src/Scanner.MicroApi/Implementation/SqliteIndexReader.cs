using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using Scanner.IndexContract;

using Scanner.MicroApi.Enums;
using Scanner.MicroApi.Exceptions;
using Scanner.MicroApi.Interfaces;
using Scanner.MicroApi.Models;
using Scanner.MicroApi.Options;

namespace Scanner.MicroApi.Implementation;

/// <summary>
/// Serves connections to the SQLite index the indexing service writes.
/// </summary>
/// <remarks>
/// <para>
/// The index is opened read-only, and the mode is enforced by SQLite rather than by convention, so no
/// request can alter the file even if a statement were written that tried. The indexing service is the
/// only writer, which is what removes lock contention between the two processes: a reader never waits
/// for a migration to finish, because the migration publishes its work as one transaction and readers
/// continue against the previous, complete revision until it commits.
/// </para>
/// <para>
/// Before a connection is handed out the file is checked twice over. It must exist, which is a stat, and
/// it must record the schema version this assembly was compiled against. The version check runs once per
/// revision of the file rather than once per request, which keeps it off the request path while still
/// catching a deployment that replaced one process and not the other.
/// </para>
/// </remarks>
public sealed class SqliteIndexReader : IScannerIndexReader
{
    private readonly ScannerPaths _paths;
    private readonly ILogger<SqliteIndexReader> _logger;
    private readonly string _connectionString;

    /// <summary>
    /// The length of the index revision whose schema has been verified, or minus one.
    /// </summary>
    private long _verifiedLength = -1;

    /// <summary>
    /// The last-write ticks of the index revision whose schema has been verified, or minus one.
    /// </summary>
    private long _verifiedWriteTicks = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteIndexReader"/> class.
    /// </summary>
    /// <param name="paths">The resolved catalogue and index locations.</param>
    /// <param name="logger">The logger used to report an index that appears, changes or breaks.</param>
    public SqliteIndexReader(ScannerPaths paths, ILogger<SqliteIndexReader> logger)
    {
        _paths = paths;
        _logger = logger;

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.IndexPath,

            // Read-only, and not "read-write but we promise". The distinction matters because it is the
            // operating system and SQLite that enforce it, not this class's own discipline.
            Mode = SqliteOpenMode.ReadOnly,

            // A reader waiting on the writer's transaction is waiting for a bounded amount of work, so a
            // timeout turns a stuck writer into a reportable failure instead of an indefinitely held thread.
            DefaultTimeout = 30
        }.ToString();
    }

    /// <inheritdoc />
    public ScannerPaths Paths => _paths;

    /// <inheritdoc />
    public bool CatalogueExists => File.Exists(_paths.ModelsPath);

    /// <inheritdoc />
    public bool IndexExists => File.Exists(_paths.IndexPath);

    /// <inheritdoc />
    public SqliteConnection OpenConnection()
    {
        EnsureReadable();

        SqliteConnection connection = new(_connectionString);

        try
        {
            connection.Open();
            MarkReadOnly(connection);
        }
        catch (SqliteException exception)
        {
            // The file was removed between the check above and the open, or the process is not allowed
            // to read it. Either way the caller is told what is wrong rather than being handed a
            // database exception that names only an errno.
            connection.Dispose();

            throw new IndexUnavailableException(
                IndexAvailability.Unreadable,
                $"The index at \"{_paths.IndexPath}\" could not be opened for reading. Check that the "
                + "indexing service has run and that this process may read the file and the directory it "
                + "sits in.",
                exception);
        }

        return connection;
    }

    /// <summary>
    /// Refuses the request unless the index exists and was built with a schema this assembly understands.
    /// </summary>
    /// <exception cref="IndexUnavailableException">The index is absent, unreadable or incompatible.</exception>
    /// <remarks>
    /// The verdict is remembered against the length and last-write time of the file, so a request that
    /// arrives after the index has been rebuilt pays for the check again while every other request pays
    /// for one stat.
    /// </remarks>
    private void EnsureReadable()
    {
        FileInfo index = new(_paths.IndexPath);

        if (!index.Exists)
        {
            throw new IndexUnavailableException(
                IndexAvailability.Missing,
                $"No index exists at \"{_paths.IndexPath}\" yet. The catalogue has not been migrated; run "
                + "the indexing service to build it.");
        }

        long length = index.Length;
        long writeTicks = index.LastWriteTimeUtc.Ticks;

        if (length == Interlocked.Read(ref _verifiedLength) &&
            writeTicks == Interlocked.Read(ref _verifiedWriteTicks))
        {
            return;
        }

        Verify(index, length, writeTicks);
    }

    /// <summary>
    /// Reads the schema version recorded in the index and compares it with this assembly's.
    /// </summary>
    /// <param name="index">The file being checked.</param>
    /// <param name="length">Its length, used to remember the verdict.</param>
    /// <param name="writeTicks">Its last-write ticks, used to remember the verdict.</param>
    /// <exception cref="IndexUnavailableException">The index cannot be read or is a different version.</exception>
    private void Verify(FileInfo index, long length, long writeTicks)
    {
        int? version;

        try
        {
            using SqliteConnection connection = new(_connectionString);
            connection.Open();
            version = ReadSchemaVersion(connection);
        }
        catch (SqliteException exception)
        {
            throw new IndexUnavailableException(
                IndexAvailability.Unreadable,
                $"The index at \"{_paths.IndexPath}\" exists but could not be read. A read-only reader needs "
                + "write access to the write-ahead-log shared-memory file beside the database; check the "
                + "permissions on the directory.",
                exception);
        }

        if (version is null)
        {
            throw new IndexUnavailableException(
                IndexAvailability.Incompatible,
                $"The file at \"{_paths.IndexPath}\" records no index schema version, so it was not built by "
                + "this scanner. Point the index path at the database the indexing service writes.");
        }

        if (version != SqliteIndexSchema.Version)
        {
            throw new IndexUnavailableException(
                IndexAvailability.Incompatible,
                $"The index at \"{_paths.IndexPath}\" was built with schema version {version} and this API "
                + $"reads version {SqliteIndexSchema.Version}. Run the indexing service to rebuild it.");
        }

        Interlocked.Exchange(ref _verifiedLength, length);
        Interlocked.Exchange(ref _verifiedWriteTicks, writeTicks);

        _logger.LogInformation(
            "The index at {IndexPath} is readable: schema version {SchemaVersion}, revision {RevisionLength} bytes.",
            _paths.IndexPath,
            version,
            length);
    }

    /// <summary>
    /// Reads the recorded index schema version.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <returns>The version, or <see langword="null"/> when the index does not record one.</returns>
    /// <remarks>
    /// A file that is not an index at all -- an empty file, or another database -- has no
    /// <c>Meta</c> table, and the query fails rather than returning a row. That failure is translated to
    /// <see langword="null"/> here so that "not an index" and "an index of an unknown version" are
    /// reported the same way, because an operator's next step is identical in both cases.
    /// </remarks>
    private static int? ReadSchemaVersion(SqliteConnection connection)
    {
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM Meta WHERE Key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", SqliteIndexSchema.MetaSchemaVersion);

            return command.ExecuteScalar() is string text &&
                   int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
                ? version
                : null;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// <summary>
    /// States on the connection that no statement may write.
    /// </summary>
    /// <param name="connection">A connection that has just been opened.</param>
    /// <remarks>
    /// Redundant against a read-only connection, and kept deliberately. The intent of this class is that
    /// the API cannot alter the index, and a statement of that intent that survives a future change of
    /// access mode is worth one statement on open. Connections are pooled, so this runs when a physical
    /// connection is established rather than on every request.
    /// </remarks>
    private static void MarkReadOnly(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only = ON;";
        command.ExecuteNonQuery();
    }
}
