using System.Text;

using Microsoft.Data.Sqlite;

using Scanner.MicroApi.Enums;
using Scanner.MicroApi.Interfaces;
using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Implementation;

/// <summary>
/// Serves catalogue searches from the SQLite read model.
/// </summary>
/// <remarks>
/// <para>
/// Every filter, ordering and paging decision is expressed as a parameterized SQL statement, so the
/// database engine does the work against indexed columns and only the requested page is ever
/// materialized. The previous implementation deserialized the entire catalogue document per request
/// and filtered it in memory.
/// </para>
/// <para>
/// The statement text is assembled from compile-time constants only. The one value that varies by
/// column -- the sort key -- is selected by a <see cref="ModelSortField"/> switch, so a
/// caller can never influence which identifier appears in the statement, and every caller-supplied
/// value travels as a bound parameter. Bindings are rebuilt per command rather than shared, because a
/// parameter instance cannot belong to two command collections at once.
/// </para>
/// </remarks>
public sealed class SqliteModelCatalogRepository : IModelCatalogRepository
{
    /// <summary>
    /// The column list selected for a catalogue row. The order matches <see cref="ReadEntry"/>.
    /// </summary>
    private const string SelectColumns =
        "ModelId, Date, Name, Description, CreatorName, CreatorId, HasScripts, Tags, Score, AssetUrl, Thumbnail";

    /// <summary>
    /// Describes one bound value of the filter set. The value is captured as data so that a fresh
    /// <see cref="SqliteParameter"/> can be created for each statement that uses the filter.
    /// </summary>
    /// <param name="Name">The parameter name, including its sigil.</param>
    /// <param name="Type">The SQLite storage class the value is bound as.</param>
    /// <param name="Value">The value to bind.</param>
    private sealed record FilterValue(string Name, SqliteType Type, object Value);

    private readonly IScannerIndexReader _reader;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteModelCatalogRepository"/> class.
    /// </summary>
    /// <param name="reader">The reader that opens the index.</param>
    public SqliteModelCatalogRepository(IScannerIndexReader reader) => _reader = reader;

    /// <inheritdoc />
    public async Task<PagedResult<ModelEntry>> SearchAsync(ModelQuery query, CancellationToken cancellationToken)
    {
        (string filterClause, List<FilterValue> filterValues) = BuildFilters(query);

        using SqliteConnection connection = _reader.OpenConnection();

        int total = await CountAsync(connection, filterClause, filterValues, cancellationToken).ConfigureAwait(false);

        var items = new List<ModelEntry>(Math.Min(query.PageSize, 256));

        await using (SqliteCommand command = connection.CreateCommand())
        {
            // The sort column comes from a closed set of compile-time identifiers; only the direction is
            // interpolated, and it too is constrained to one of two literals.
            command.CommandText =
                $"SELECT {SelectColumns} FROM Models{filterClause} " +
                $"ORDER BY {ResolveSortColumn(query.SortBy)} {(query.Descending ? "DESC" : "ASC")}, ModelId ASC " +
                "LIMIT $limit OFFSET $offset;";

            Bind(command, filterValues);
            command.Parameters.AddWithValue("$limit", query.PageSize);
            command.Parameters.AddWithValue("$offset", (long)(query.Page - 1) * query.PageSize);

            await using SqliteDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadEntry(reader));
            }
        }

        return new PagedResult<ModelEntry>(total, query.Page, query.PageSize, items);
    }

    /// <inheritdoc />
    public async Task<ModelEntry?> GetByIdAsync(long modelId, CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _reader.OpenConnection();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Models WHERE ModelId = $modelId LIMIT 1;";
        command.Parameters.AddWithValue("$modelId", modelId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEntry(reader) : null;
    }

    /// <summary>
    /// Translates a normalized query into a filter clause and its bound values.
    /// </summary>
    /// <param name="query">The normalized query.</param>
    /// <returns>
    /// The <c>WHERE</c> clause (empty when unfiltered) and the values to bind for it.
    /// </returns>
    private static (string Clause, List<FilterValue> Values) BuildFilters(ModelQuery query)
    {
        var clauses = new List<string>(4);
        var values = new List<FilterValue>(4);

        if (query.Search is not null)
        {
            // The pattern is anchored with a leading wildcard, so this predicate scans rather than
            // seeks. That is acceptable here: the catalogue is a few hundred thousand rows spanning four
            // short text columns, which keeps a literal substring search well inside the request budget
            // while matching what operators expect a search box to do.
            clauses.Add(
                "(Name LIKE $search ESCAPE '\\' OR Description LIKE $search ESCAPE '\\' " +
                "OR CreatorName LIKE $search ESCAPE '\\' OR CreatorId LIKE $search ESCAPE '\\' " +
                "OR Tags LIKE $search ESCAPE '\\' OR CAST(ModelId AS TEXT) LIKE $search ESCAPE '\\')");

            values.Add(new FilterValue("$search", SqliteType.Text, BuildLikePattern(query.Search)));
        }

        if (query.HasScripts is { } hasScripts)
        {
            clauses.Add("HasScripts = $hasScripts");
            values.Add(new FilterValue("$hasScripts", SqliteType.Integer, hasScripts ? 1 : 0));
        }

        if (query.HasObfuscation is { } hasObfuscation)
        {
            // "Has detections" is expressed on the aggregate score rather than on the presence of any tag,
            // because trusted-creator watermark hits also produce a tag but contribute no risk.
            clauses.Add(hasObfuscation ? "Score > 0" : "Score = 0");
        }

        if (query.RuleTag is not null)
        {
            clauses.Add("ModelId IN (SELECT ModelId FROM ModelTags WHERE Tag = $tag)");
            values.Add(new FilterValue("$tag", SqliteType.Text, query.RuleTag));
        }

        return clauses.Count == 0 ? (string.Empty, values) : (" WHERE " + string.Join(" AND ", clauses), values);
    }

    /// <summary>
    /// Counts the rows matching the filter set.
    /// </summary>
    /// <param name="connection">An open connection to the index database.</param>
    /// <param name="filterClause">The clause produced by <see cref="BuildFilters"/>.</param>
    /// <param name="filterValues">The values the clause references.</param>
    /// <param name="cancellationToken">A token used to cancel the count.</param>
    /// <returns>The number of rows matching the filter set, before paging.</returns>
    private static async Task<int> CountAsync(
        SqliteConnection connection,
        string filterClause,
        List<FilterValue> filterValues,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM Models{filterClause};";
        Bind(command, filterValues);

        object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is long count ? (int)Math.Min(count, int.MaxValue) : 0;
    }

    /// <summary>
    /// Adds a fresh parameter for each filter value to a command.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="filterValues">The values to bind.</param>
    private static void Bind(SqliteCommand command, List<FilterValue> filterValues)
    {
        foreach (FilterValue value in filterValues)
        {
            command.Parameters.Add(new SqliteParameter(value.Name, value.Type) { Value = value.Value });
        }
    }

    /// <summary>
    /// Maps a validated sort field onto a trusted column identifier.
    /// </summary>
    /// <param name="sortBy">The requested sort field.</param>
    /// <returns>The column identifier to order by.</returns>
    private static string ResolveSortColumn(ModelSortField sortBy) => sortBy switch
    {
        ModelSortField.Date => "Date",
        ModelSortField.Name => "Name",
        ModelSortField.CreatorName => "CreatorName",
        ModelSortField.CreatorId => "CreatorId",
        ModelSortField.HasScripts => "HasScripts",
        ModelSortField.Score => "Score",
        ModelSortField.Tags => "Tags",
        ModelSortField.Description => "Description",
        ModelSortField.Found => "Sequence",
        _ => "ModelId"
    };

    /// <summary>
    /// Builds a <c>LIKE</c> pattern for a free-text term, neutralising wildcard characters.
    /// </summary>
    /// <param name="term">The trimmed search term.</param>
    /// <returns>A pattern that matches the term as a literal substring.</returns>
    /// <remarks>
    /// Without this, a term containing <c>%</c> or <c>_</c> would behave as a wildcard, letting a caller
    /// turn a search into a full-table scan with a pattern that matches everything.
    /// </remarks>
    private static string BuildLikePattern(string term)
    {
        StringBuilder pattern = new(term.Length + 2);
        pattern.Append('%');

        foreach (char character in term)
        {
            if (character is '%' or '_' or '\\') pattern.Append('\\');
            pattern.Append(character);
        }

        return pattern.Append('%').ToString();
    }

    /// <summary>
    /// Materializes a catalogue row from the current reader position.
    /// </summary>
    /// <param name="reader">A reader positioned on a row of the catalogue select.</param>
    /// <returns>The row as a <see cref="ModelEntry"/>.</returns>
    private static ModelEntry ReadEntry(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetInt64(6) != 0,
        reader.GetString(7),
        reader.GetDouble(8),
        reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10));
}
