namespace Scanner.IndexContract;

/// <summary>
/// The definition of the catalogue database: its table, its bookkeeping keys and the statements that create it.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is the scanner's own file. It is written by the scanner and read directly by the API, so the
/// table's shape is the whole contract between the two processes. It lives here, in its own assembly, so that
/// neither side has to hold a private copy of a name: a column renamed in one process and not the other is a
/// failure at run time, in production, on the reader.
/// </para>
/// <para>
/// The catalogue was a JSON document until it became this. A JSON array can only be written by rewriting it,
/// so every flush was a full serialisation of the whole catalogue. A database is appended to, so the scanner
/// publishes a revision number it bumps when it actually changes something, and a reader can ask for that one
/// number to decide whether its cached view is still current.
/// </para>
/// <para>
/// There is no separate index any more. The API reads these rows directly, which removes a whole service and
/// the window in which the index and the catalogue could disagree. Analytics are derived from the catalogue
/// rows by the API and cached against the revision below.
/// </para>
/// <para>
/// Nothing here is a secret and nothing here is caller input: every statement is a compile-time constant.
/// The class is public because it crosses an assembly boundary, not because it is meant to be varied at
/// run time.
/// </para>
/// </remarks>
public static class SqliteCatalogueSchema
{
    /// <summary>
    /// The arity of the catalogue schema described by this class.
    /// </summary>
    /// <remarks>
    /// Bump this whenever a table or a bookkeeping key changes shape. Readers compare it against the value they
    /// find in the database and refuse a catalogue they do not understand, which turns a silent misread of a
    /// renamed column into an explicit, reportable mismatch.
    /// </remarks>
    public const int Version = 1;

    /// <summary>
    /// The name of the table that holds one row per discovered asset.
    /// </summary>
    public const string ModelsTable = "Models";

    /// <summary>
    /// The bookkeeping key holding the catalogue's schema version.
    /// </summary>
    public const string MetaSchemaVersion = "catalogue.schemaVersion";

    /// <summary>
    /// The bookkeeping key holding the catalogue's revision number.
    /// </summary>
    /// <remarks>
    /// Bumped inside the same transaction as the rows it describes, so a reader can never observe a
    /// revision that its rows have not reached. Analytics readers compare it against the revision their
    /// cached snapshot was built from: equal means there is nothing to rebuild.
    /// </remarks>
    public const string MetaRevision = "catalogue.revision";

    /// <summary>
    /// The bookkeeping key holding the instant the catalogue last changed.
    /// </summary>
    /// <remarks>
    /// Reported to an operator and used for nothing else. It is deliberately not the cache trigger:
    /// a clock can be wrong, can move backwards, and on a machine with a coarse timer can fail to move at
    /// all, whereas the revision cannot do any of those things.
    /// </remarks>
    public const string MetaUpdatedUtc = "catalogue.updatedUtc";

    /// <summary>
    /// The columns of <see cref="ModelsTable"/>, in the order the scanner writes them.
    /// </summary>
    /// <remarks>
    /// The API selects by name rather than by position, so this list is documentation for a reader of this
    /// file and the constraint both sides are written against.
    /// </remarks>
    public const string ModelColumns =
        "Model, Sequence, Date, Name, Description, CreatorName, CreatorId, HasScripts, Tags, Score, AssetUrl, Thumbnail";

    /// <summary>
    /// The statements that create the catalogue schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They are idempotent and are applied on every scanner start-up, so a catalogue directory that arrives
    /// empty -- a fresh installation, or a deployment whose data volume has been replaced -- becomes a working
    /// catalogue rather than a scanner that refuses to start.
    /// </para>
    /// <para>
    /// <c>journal_mode=WAL</c> is what lets the API read the catalogue while the scanner is writing it. Without
    /// it a reader would block for the duration of a flush, and the flush is the largest piece of work the
    /// scanner does.
    /// </para>
    /// <para>
    /// <c>Sequence</c> is the order in which the catalogue found the row, assigned on first insert and
    /// never reassigned, including when a repair replaces every other column of the row. It exists because
    /// an asset identifier answers when Roblox created the asset, which is not the question "what has this
    /// scanner found lately": a sweep that reaches a high identifier range and a sweep that repairs an old
    /// one are not distinguishable by identifier, and the operator watching the catalogue wants the second
    /// one.
    /// </para>
    /// <para>
    /// The indexes match the shapes the API actually issues: the default order, each sortable column and the
    /// two filters that can be answered from an index.
    /// </para>
    /// </remarks>
    public const string CreateSql = """
        PRAGMA journal_mode = WAL;
        PRAGMA busy_timeout = 5000;

        CREATE TABLE IF NOT EXISTS Models
        (
            Model       INTEGER NOT NULL PRIMARY KEY,
            Sequence    INTEGER NOT NULL DEFAULT 0,
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

        /* The order rows were found in, which is the default order the catalogue is read in. */
        CREATE INDEX IF NOT EXISTS IX_Models_Sequence   ON Models (Sequence DESC);
        CREATE INDEX IF NOT EXISTS IX_Models_Date       ON Models (Date);
        CREATE INDEX IF NOT EXISTS IX_Models_HasScripts ON Models (HasScripts);
        CREATE INDEX IF NOT EXISTS IX_Models_Score      ON Models (Score);
        CREATE INDEX IF NOT EXISTS IX_Models_CreatorId  ON Models (CreatorId);

        CREATE TABLE IF NOT EXISTS Meta
        (
            Key   TEXT NOT NULL PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """;
}
