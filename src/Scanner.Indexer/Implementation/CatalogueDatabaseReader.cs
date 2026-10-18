using System.Globalization;

using Microsoft.Data.Sqlite;

using Scanner.IndexContract;
using Scanner.Indexer.Interfaces;
using Scanner.Indexer.Models;

namespace Scanner.Indexer.Implementation;

/// <summary>
/// Streams the scanner's catalogue database into catalogue rows.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is opened read-only, one connection per read, and rows are yielded as the statement walks
/// the table, so peak memory is one row rather than the whole catalogue. This is the same discipline the
/// reader kept when the catalogue was a JSON document, and it survived the change of format because the
/// reason for it did: the migration is a streaming pass and there is no point in its holding a second copy
/// of what it is writing.
/// </para>
/// <para>
/// The table and its columns are named from the schema the scanner writes against, so a column cannot be
/// renamed on one side of this boundary and left behind on the other. The scan is ordered by the found
/// order, which is the order the catalogue discovered the rows in and the order the index stores them in.
/// </para>
/// <para>
/// The read is written as an iterator so that the connection is owned by the enumeration rather than by the
/// call that starts it. Returning an enumerable from a method that had already closed the connection would
/// hand the caller a sequence that fails on first use, which is a failure mode that appears only once the
/// caller actually reads the first row.
/// </para>
/// </remarks>
internal sealed class CatalogueDatabaseReader : ICatalogueReader
{
    /// <inheritdoc />
    public long? ReadRevision(string path)
    {
        if (!File.Exists(path)) return null;

        using SqliteConnection connection = OpenReadOnly(path);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT Value FROM Meta WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", SqliteCatalogueSchema.MetaRevision);

        // A catalogue that has never been written to holds no revision. Reporting zero rather than nothing
        // is deliberate: the database is there, it is a catalogue, and it is at revision zero -- which is a
        // different statement from "there is no catalogue", and one the caller acts on differently.
        return long.TryParse(
            command.ExecuteScalar() as string,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long revision)
            ? revision
            : 0;
    }

    /// <inheritdoc />
    public IEnumerable<CatalogueRow> Read(string path, CancellationToken cancellationToken)
    {
        using SqliteConnection connection = OpenReadOnly(path);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"SELECT {SqliteCatalogueSchema.ModelColumns} FROM {SqliteCatalogueSchema.ModelsTable} ORDER BY Sequence;";

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new CatalogueRow(
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
        }
    }

    /// <summary>
    /// Opens a read-only connection to a catalogue.
    /// </summary>
    /// <param name="path">The absolute path to the catalogue database.</param>
    /// <returns>An open connection that the caller is responsible for disposing.</returns>
    /// <remarks>
    /// The scanner may be committing while this connection is open. Its writes are applied in one
    /// transaction and the file is in write-ahead mode, so this read sees a complete revision -- the one
    /// before the commit or the one after it, never a half-applied one. The timeout covers the moment when
    /// the two overlap, which is a wait rather than an error.
    /// </remarks>
    private static SqliteConnection OpenReadOnly(string path)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 30
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();

        return connection;
    }
}
