using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Data.Sqlite;

using RMS.Site.Common.Enums;
using RMS.Site.Common.Interfaces;
using RMS.Site.Common.Models;
using RMS.Site.Common.Options;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// The site's own database, held in one SQLite file under <c>App_Data</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Accounts, the audit trail and the site's notices share a file because they share a lifetime and a
    /// deployment. Keeping them together means a site is backed up, moved or thrown away as one thing, which
    /// is the property that matters when the site has to be restored at three in the morning.
    /// </para>
    /// <para>
    /// A connection is opened per call and not held. The alternative -- a connection cached for the life of
    /// the application -- is faster and buys a lock that outlives the request which took it, so a page that
    /// half-failed while writing leaves every later page waiting. The file is small and the calls are few, and
    /// a connection to a local file costs far less than the failure mode it removes.
    /// </para>
    /// <para>
    /// The schema is created on first use rather than by a script. A site deployed to a machine that has never
    /// seen it must be able to come up unattended, and a script that has to be run in the right order on the
    /// right host is a script that will be run wrongly on one of them. Creation is idempotent, so it is
    /// harmless that it also runs on every subsequent start.
    /// </para>
    /// <para>
    /// The write-ahead log is enabled for the same reason it is enabled on the scanner's index: a reader must
    /// not wait for a writer. Its consequence here is that the <c>App_Data</c> folder holds three files rather
    /// than one, so a backup taken with a file copy must take all of them or none.
    /// </para>
    /// </remarks>
    public sealed class AccountStore : IAccountStore
    {
        /// <summary>
        /// The version of the schema this class creates.
        /// </summary>
        private const int SchemaVersion = 1;

        /// <summary>
        /// The shortest password the site accepts.
        /// </summary>
        public const int MinimumPasswordLength = 12;

        /// <summary>
        /// The name given to the administrator created for a fresh deployment when none is supplied.
        /// </summary>
        public const string DefaultAdministratorName = "admin";

        /// <summary>
        /// A salt used to derive a value for an account that does not exist.
        /// </summary>
        /// <remarks>
        /// Asking about an account that is not there would otherwise return without doing the derivation, and
        /// the difference between that and a real check is large enough to be measured from outside. The
        /// constant is not a secret and is not used to store anything: it exists so that both paths do the
        /// same amount of work.
        /// </remarks>
        private const string DecoySalt = "AAAAAAAAAAAAAAAAAAAAAA==";

        private readonly SiteOptions _options;
        private readonly string _connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="AccountStore"/> class.
        /// </summary>
        /// <param name="options">The settings, which name the database and the lockout policy.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public AccountStore(SiteOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _options = options;
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = options.AuthDatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                DefaultTimeout = 30,
            }.ToString();
        }

        /// <inheritdoc />
        public string DatabasePath
        {
            get { return _options.AuthDatabasePath; }
        }

        /// <inheritdoc />
        public string EnsureCreated(string bootstrapUsername, string bootstrapPassword)
        {
            NativeSqlite.EnsureLoaded();

            string directory = Path.GetDirectoryName(_options.AuthDatabasePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (SqliteConnection connection = Open())
            {
                Execute(connection, SchemaSql);
                WriteSchemaVersion(connection);

                using (SqliteCommand count = connection.CreateCommand())
                {
                    count.CommandText = "SELECT COUNT(*) FROM Accounts;";

                    if (Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    {
                        return null;
                    }
                }

                string name = string.IsNullOrWhiteSpace(bootstrapUsername)
                    ? DefaultAdministratorName
                    : bootstrapUsername.Trim();

                // The generated password is returned to the caller, written once to a file only the operator
                // can read, and never written here. A password that reaches a log is a password that has to be
                // changed before the deployment is finished.
                string password = string.IsNullOrWhiteSpace(bootstrapPassword)
                    ? PasswordHasher.GeneratePassword(28)
                    : bootstrapPassword;

                Save(name, "Site administrator", UserSession.AdministratorRole, password);

                Audit(
                    "Bootstrap",
                    name,
                    "The first administrator was created for a fresh deployment.",
                    name,
                    string.Empty);

                return password;
            }
        }

        /// <inheritdoc />
        public LoginResult Authenticate(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || password == null)
            {
                return LoginResult.Of(LoginOutcome.UnknownUser);
            }

            NativeSqlite.EnsureLoaded();

            string name = username.Trim();

            using (SqliteConnection connection = Open())
            {
                SiteAccount account;
                string hash;
                string salt;
                int iterations;

                if (!TryReadCredential(connection, name, out account, out hash, out salt, out iterations))
                {
                    // The same derivation is performed for a name that does not exist, so that the time taken
                    // to refuse an unknown account matches the time taken to refuse a wrong password.
                    PasswordHasher.Verify(password, hash, DecoySalt, _options.PasswordIterations);

                    return LoginResult.Of(LoginOutcome.UnknownUser);
                }

                if (!account.IsEnabled)
                {
                    return LoginResult.Of(LoginOutcome.Disabled, account);
                }

                if (account.IsLocked)
                {
                    return LoginResult.Of(LoginOutcome.LockedOut, account);
                }

                if (!PasswordHasher.Verify(password, hash, salt, iterations))
                {
                    return LoginResult.Of(RecordFailure(connection, account), account);
                }

                ClearFailures(connection, account.Id);

                account.FailedAttempts = 0;
                account.LockedUntilUtc = null;
                account.LastLoginUtc = DateTime.UtcNow;

                return LoginResult.Of(LoginOutcome.Success, account);
            }
        }

        /// <inheritdoc />
        public SiteAccount Find(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            NativeSqlite.EnsureLoaded();

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = SelectAccount + " WHERE Username = $name LIMIT 1;";
                command.Parameters.AddWithValue("$name", username.Trim());

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadAccount(reader) : null;
                }
            }
        }

        /// <inheritdoc />
        public SiteAccount FindById(long accountId)
        {
            NativeSqlite.EnsureLoaded();

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = SelectAccount + " WHERE Id = $id LIMIT 1;";
                command.Parameters.AddWithValue("$id", accountId);

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadAccount(reader) : null;
                }
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<SiteAccount> List()
        {
            NativeSqlite.EnsureLoaded();

            List<SiteAccount> accounts = new List<SiteAccount>();

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                // Administrators first, then by name. The reader is looking for the account that can fix the
                // thing they came here about, and that account is at the top.
                command.CommandText = SelectAccount + " ORDER BY (Role = 'Administrator') DESC, Username ASC;";

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read()) accounts.Add(ReadAccount(reader));
                }
            }

            return accounts;
        }

        /// <inheritdoc />
        public SiteAccount Save(string username, string displayName, string role, string password)
        {
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("A name is required.", nameof(username));

            NativeSqlite.EnsureLoaded();

            string name = username.Trim();
            string effectiveRole = NormaliseRole(role);
            string effectiveDisplay = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
            DateTime now = DateTime.UtcNow;

            using (SqliteConnection connection = Open())
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                bool exists;

                using (SqliteCommand probe = connection.CreateCommand())
                {
                    probe.Transaction = transaction;
                    probe.CommandText = "SELECT COUNT(*) FROM Accounts WHERE Username = $name;";
                    probe.Parameters.AddWithValue("$name", name);

                    exists = Convert.ToInt64(probe.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
                }

                if (exists)
                {
                    using (SqliteCommand update = connection.CreateCommand())
                    {
                        update.Transaction = transaction;
                        update.CommandText =
                            "UPDATE Accounts SET DisplayName = $display, Role = $role " +
                            (password == null
                                ? string.Empty
                                : ", PasswordHash = $hash, PasswordSalt = $salt, PasswordIterations = $iterations, " +
                                  "FailedAttempts = 0, LockedUntilUtc = NULL ") +
                            " WHERE Username = $name;";

                        update.Parameters.AddWithValue("$display", effectiveDisplay);
                        update.Parameters.AddWithValue("$role", effectiveRole);
                        update.Parameters.AddWithValue("$name", name);
                        AddPasswordParameters(update, password);

                        update.ExecuteNonQuery();
                    }
                }
                else
                {
                    string hash;
                    string salt;

                    PasswordHasher.Derive(password ?? PasswordHasher.GeneratePassword(28), _options.PasswordIterations, out hash, out salt);

                    using (SqliteCommand insert = connection.CreateCommand())
                    {
                        insert.Transaction = transaction;
                        insert.CommandText =
                            "INSERT INTO Accounts (Username, DisplayName, Role, PasswordHash, PasswordSalt, " +
                            "PasswordIterations, IsEnabled, CreatedUtc, FailedAttempts) " +
                            "VALUES ($name, $display, $role, $hash, $salt, $iterations, 1, $created, 0);";

                        insert.Parameters.AddWithValue("$name", name);
                        insert.Parameters.AddWithValue("$display", effectiveDisplay);
                        insert.Parameters.AddWithValue("$role", effectiveRole);
                        insert.Parameters.AddWithValue("$hash", hash);
                        insert.Parameters.AddWithValue("$salt", salt);
                        insert.Parameters.AddWithValue("$iterations", _options.PasswordIterations);
                        insert.Parameters.AddWithValue("$created", Format(now));

                        insert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return Find(name);
        }

        /// <inheritdoc />
        public bool SetEnabled(string username, bool isEnabled)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            NativeSqlite.EnsureLoaded();

            string name = username.Trim();

            using (SqliteConnection connection = Open())
            {
                SiteAccount account = ReadNamed(connection, name);

                if (account == null) return false;

                // Disabling the last administrator would leave a site nobody can administer, and repairing
                // that means editing the database by hand on the server. The check is made here, under the
                // write, so no page can skip it.
                if (!isEnabled && account.IsAdministrator && account.IsEnabled && CountAdministrators() <= 1)
                {
                    throw new InvalidOperationException(
                        "This is the last enabled administrator, so it cannot be disabled.");
                }

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "UPDATE Accounts SET IsEnabled = $enabled, FailedAttempts = 0, LockedUntilUtc = NULL " +
                        "WHERE Username = $name;";

                    command.Parameters.AddWithValue("$enabled", isEnabled ? 1 : 0);
                    command.Parameters.AddWithValue("$name", name);

                    return command.ExecuteNonQuery() > 0;
                }
            }
        }

        /// <inheritdoc />
        public bool Remove(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            NativeSqlite.EnsureLoaded();

            string name = username.Trim();

            using (SqliteConnection connection = Open())
            {
                SiteAccount account = ReadNamed(connection, name);

                if (account == null) return false;

                if (account.IsAdministrator && account.IsEnabled && CountAdministrators() <= 1)
                {
                    throw new InvalidOperationException(
                        "This is the last enabled administrator, so it cannot be removed.");
                }

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM Accounts WHERE Username = $name;";
                    command.Parameters.AddWithValue("$name", name);

                    return command.ExecuteNonQuery() > 0;
                }
            }
        }

        /// <inheritdoc />
        public int CountAdministrators()
        {
            NativeSqlite.EnsureLoaded();

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT COUNT(*) FROM Accounts WHERE Role = $role AND IsEnabled = 1;";

                command.Parameters.AddWithValue("$role", UserSession.AdministratorRole);

                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        /// <inheritdoc />
        public void Audit(string action, string target, string detail, string actor, string address)
        {
            NativeSqlite.EnsureLoaded();

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO Audit (WhenUtc, Actor, Action, Target, Detail, Address) " +
                    "VALUES ($when, $actor, $action, $target, $detail, $address);";

                command.Parameters.AddWithValue("$when", Format(DateTime.UtcNow));
                command.Parameters.AddWithValue("$actor", Truncate(actor, 128));
                command.Parameters.AddWithValue("$action", Truncate(action, 64));
                command.Parameters.AddWithValue("$target", Truncate(target, 256));
                command.Parameters.AddWithValue("$detail", Truncate(detail, 1024));
                command.Parameters.AddWithValue("$address", Truncate(address, 64));

                command.ExecuteNonQuery();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<AuditEntry> RecentAudit(int take)
        {
            NativeSqlite.EnsureLoaded();

            if (take < 1) take = 50;
            if (take > 1000) take = 1000;

            List<AuditEntry> entries = new List<AuditEntry>();

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, WhenUtc, Actor, Action, Target, Detail, Address FROM Audit " +
                    "ORDER BY Id DESC LIMIT $take;";

                command.Parameters.AddWithValue("$take", take);

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new AuditEntry
                        {
                            Id = reader.GetInt64(0),
                            WhenUtc = Parse(reader.GetString(1)),
                            Actor = reader.GetString(2),
                            Action = reader.GetString(3),
                            Target = reader.GetString(4),
                            Detail = reader.GetString(5),
                            Address = reader.GetString(6),
                        });
                    }
                }
            }

            return entries;
        }

        /// <inheritdoc />
        public IReadOnlyList<SiteNotice> ListNotices(bool includeInactive)
        {
            NativeSqlite.EnsureLoaded();

            List<SiteNotice> notices = new List<SiteNotice>();

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = SelectNotice
                                      + (includeInactive ? string.Empty : " WHERE IsActive = 1")
                                      + " ORDER BY SortOrder ASC, Id ASC;";

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read()) notices.Add(ReadNotice(reader));
                }
            }

            return notices;
        }

        /// <inheritdoc />
        public SiteNotice SaveNotice(SiteNotice notice, string updatedBy)
        {
            if (notice == null) throw new ArgumentNullException(nameof(notice));

            NativeSqlite.EnsureLoaded();

            DateTime now = DateTime.UtcNow;
            string severity = NormaliseSeverity(notice.Severity);

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                if (notice.Id > 0)
                {
                    command.CommandText =
                        "UPDATE Notices SET Severity = $severity, Title = $title, Body = $body, " +
                        "SortOrder = $sort, IsActive = $active, UpdatedUtc = $updated, UpdatedBy = $by " +
                        "WHERE Id = $id;";

                    command.Parameters.AddWithValue("$id", notice.Id);
                }
                else
                {
                    command.CommandText =
                        "INSERT INTO Notices (Severity, Title, Body, SortOrder, IsActive, CreatedUtc, UpdatedUtc, UpdatedBy) " +
                        "VALUES ($severity, $title, $body, $sort, $active, $created, $updated, $by);";

                    command.Parameters.AddWithValue("$created", Format(now));
                    notice.CreatedUtc = now;
                }

                command.Parameters.AddWithValue("$severity", severity);
                command.Parameters.AddWithValue("$title", Truncate(notice.Title, 256));
                command.Parameters.AddWithValue("$body", Truncate(notice.Body, 4096));
                command.Parameters.AddWithValue("$sort", notice.SortOrder);
                command.Parameters.AddWithValue("$active", notice.IsActive ? 1 : 0);
                command.Parameters.AddWithValue("$updated", Format(now));
                command.Parameters.AddWithValue("$by", Truncate(updatedBy, 128));

                command.ExecuteNonQuery();

                if (notice.Id <= 0)
                {
                    notice.Id = LastInsertedId(connection);
                }
            }

            return notice;
        }

        /// <inheritdoc />
        public bool RemoveNotice(long noticeId)
        {
            NativeSqlite.EnsureLoaded();

            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM Notices WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", noticeId);

                return command.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>
        /// The account columns every read selects.
        /// </summary>
        /// <remarks>
        /// The column list is written once and the statements are built from it. The alternative -- a whole
        /// statement per read, each repeating the list -- is what allowed a read that needed three more columns
        /// to append them to a statement that already ended in <c>FROM Accounts</c>, producing a query that no
        /// parser accepts and that fails only on the sign-in path, where it is least likely to be noticed.
        /// </remarks>
        private const string AccountColumns =
            "Id, Username, DisplayName, Role, IsEnabled, CreatedUtc, LastLoginUtc, FailedAttempts, LockedUntilUtc";

        /// <summary>
        /// The statement that reads an account without its password material.
        /// </summary>
        private const string SelectAccount = "SELECT " + AccountColumns + " FROM Accounts";

        /// <summary>
        /// The statement that reads an account together with its password material.
        /// </summary>
        /// <remarks>
        /// Every caller that holds the result of this one is verifying a password, so the extra columns are
        /// selected only here. Keeping them out of <see cref="SelectAccount"/> is what makes it reasonable to
        /// say that a page never holds a hash: the only statement that fetches one is urgent, and its result
        /// does not leave the store.
        /// </remarks>
        private const string SelectCredential =
            "SELECT " + AccountColumns + ", PasswordHash, PasswordSalt, PasswordIterations FROM Accounts";

        /// <summary>
        /// The column list every notice read selects.
        /// </summary>
        private const string SelectNotice =
            "SELECT Id, Severity, Title, Body, SortOrder, IsActive, CreatedUtc, UpdatedUtc, UpdatedBy FROM Notices";

        /// <summary>
        /// The statements that create the schema.
        /// </summary>
        /// <remarks>
        /// The name column is compared without regard to case, so that an operator who signs in as
        /// <c>Administrator</c> one day and <c>administrator</c> the next is the same person. Making the
        /// comparison insensitive in the column rather than in every query is what guarantees no query can
        /// forget it.
        /// </remarks>
        private const string SchemaSql = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS Accounts
            (
                Id                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Username           TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                DisplayName        TEXT    NOT NULL DEFAULT '',
                Role               TEXT    NOT NULL DEFAULT 'Viewer',
                PasswordHash       TEXT    NOT NULL DEFAULT '',
                PasswordSalt       TEXT    NOT NULL DEFAULT '',
                PasswordIterations INTEGER NOT NULL DEFAULT 0,
                IsEnabled          INTEGER NOT NULL DEFAULT 1,
                CreatedUtc         TEXT    NOT NULL,
                LastLoginUtc       TEXT    NULL,
                FailedAttempts     INTEGER NOT NULL DEFAULT 0,
                LockedUntilUtc     TEXT    NULL
            );

            CREATE TABLE IF NOT EXISTS Audit
            (
                Id      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                WhenUtc TEXT    NOT NULL,
                Actor   TEXT    NOT NULL DEFAULT '',
                Action  TEXT    NOT NULL,
                Target  TEXT    NOT NULL DEFAULT '',
                Detail  TEXT    NOT NULL DEFAULT '',
                Address TEXT    NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_Audit_Id ON Audit (Id DESC);

            CREATE TABLE IF NOT EXISTS Notices
            (
                Id         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Severity   TEXT    NOT NULL DEFAULT 'info',
                Title      TEXT    NOT NULL DEFAULT '',
                Body       TEXT    NOT NULL DEFAULT '',
                SortOrder  INTEGER NOT NULL DEFAULT 0,
                IsActive   INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT    NOT NULL,
                UpdatedUtc TEXT    NOT NULL,
                UpdatedBy  TEXT    NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_Notices_Order ON Notices (SortOrder, Id);
            ";

        /// <summary>
        /// Opens a connection to the site's database.
        /// </summary>
        /// <returns>The open connection.</returns>
        /// <remarks>
        /// The native library is loaded before the provider is asked to open anything. The provider registers
        /// its own probe when it is first used, and that probe looks beside the assemblies -- which, inside a
        /// web application, is not where the native library is. Loading it here, by its full path, means the
        /// provider's later attempt by name resolves to the module already in the process, so it succeeds for
        /// the same reason it succeeds on a console.
        /// </remarks>
        private SqliteConnection Open()
        {
            NativeSqlite.EnsureLoaded();

            SqliteConnection connection = new SqliteConnection(_connectionString);
            connection.Open();

            return connection;
        }

        /// <summary>
        /// Runs a batch of statements.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <param name="sql">The statements.</param>
        private static void Execute(SqliteConnection connection, string sql)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Records the schema version in the database.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <remarks>
        /// The version is not used to migrate yet -- there is one version -- but it is written from the first
        /// release so that a future change can tell an old file from a new one without having to infer it from
        /// which columns happen to exist.
        /// </remarks>
        private static void WriteSchemaVersion(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS SchemaInfo (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL);" +
                    "INSERT INTO SchemaInfo (Key, Value) VALUES ('schema.version', $version) " +
                    "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";

                command.Parameters.AddWithValue("$version", SchemaVersion.ToString(CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Reads the account a name refers to.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <param name="name">The account's name.</param>
        /// <returns>The account, or <see langword="null"/>.</returns>
        private static SiteAccount ReadNamed(SqliteConnection connection, string name)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = SelectAccount + " WHERE Username = $name LIMIT 1;";
                command.Parameters.AddWithValue("$name", name);

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadAccount(reader) : null;
                }
            }
        }

        /// <summary>
        /// Reads an account and its stored password material.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <param name="name">The account's name.</param>
        /// <param name="account">Receives the account.</param>
        /// <param name="hash">Receives the stored hash.</param>
        /// <param name="salt">Receives the stored salt.</param>
        /// <param name="iterations">Receives the iteration count the hash was created with.</param>
        /// <returns><see langword="true"/> when the account exists.</returns>
        private static bool TryReadCredential(
            SqliteConnection connection,
            string name,
            out SiteAccount account,
            out string hash,
            out string salt,
            out int iterations)
        {
            account = null;
            hash = null;
            salt = null;
            iterations = 0;

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = SelectCredential + " WHERE Username = $name LIMIT 1;";
                command.Parameters.AddWithValue("$name", name);

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return false;

                    account = ReadAccount(reader);

                    // The password columns are located by name rather than by position. The position is known
                    // today and would be wrong the day a column is added to the shared list, and the failure
                    // that would produce is a sign-in that rejects every password.
                    hash = reader.GetString(reader.GetOrdinal("PasswordHash"));
                    salt = reader.GetString(reader.GetOrdinal("PasswordSalt"));
                    iterations = reader.GetInt32(reader.GetOrdinal("PasswordIterations"));

                    return true;
                }
            }
        }

        /// <summary>
        /// Increments an account's failure count and locks it when the limit is reached.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <param name="account">The account that failed.</param>
        /// <returns>The outcome to report.</returns>
        private LoginOutcome RecordFailure(SqliteConnection connection, SiteAccount account)
        {
            int failures = account.FailedAttempts + 1;
            DateTime? lockedUntil = failures >= _options.MaximumFailedAttempts
                ? DateTime.UtcNow.AddMinutes(_options.LockoutMinutes)
                : (DateTime?)null;

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "UPDATE Accounts SET FailedAttempts = $failures, LockedUntilUtc = $locked WHERE Id = $id;";

                command.Parameters.AddWithValue("$failures", failures);
                command.Parameters.AddWithValue("$locked", lockedUntil.HasValue ? (object)Format(lockedUntil.Value) : DBNull.Value);
                command.Parameters.AddWithValue("$id", account.Id);

                command.ExecuteNonQuery();
            }

            return lockedUntil.HasValue ? LoginOutcome.LockedOut : LoginOutcome.WrongPassword;
        }

        /// <summary>
        /// Clears an account's failure count after a successful sign-in.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <param name="accountId">The account's identifier.</param>
        private static void ClearFailures(SqliteConnection connection, long accountId)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "UPDATE Accounts SET FailedAttempts = 0, LockedUntilUtc = NULL, LastLoginUtc = $when " +
                    "WHERE Id = $id;";

                command.Parameters.AddWithValue("$when", Format(DateTime.UtcNow));
                command.Parameters.AddWithValue("$id", accountId);

                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Reads an account from a row of the account select.
        /// </summary>
        /// <param name="reader">A reader positioned on a row.</param>
        /// <returns>The account.</returns>
        private static SiteAccount ReadAccount(SqliteDataReader reader)
        {
            return new SiteAccount
            {
                Id = reader.GetInt64(0),
                Username = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Role = reader.GetString(3),
                IsEnabled = reader.GetInt32(4) != 0,
                CreatedUtc = Parse(reader.GetString(5)),
                LastLoginUtc = reader.IsDBNull(6) ? (DateTime?)null : Parse(reader.GetString(6)),
                FailedAttempts = reader.GetInt32(7),
                LockedUntilUtc = reader.IsDBNull(8) ? (DateTime?)null : Parse(reader.GetString(8)),
            };
        }

        /// <summary>
        /// Reads a notice from a row of the notice select.
        /// </summary>
        /// <param name="reader">A reader positioned on a row.</param>
        /// <returns>The notice.</returns>
        private static SiteNotice ReadNotice(SqliteDataReader reader)
        {
            return new SiteNotice
            {
                Id = reader.GetInt64(0),
                Severity = reader.GetString(1),
                Title = reader.GetString(2),
                Body = reader.GetString(3),
                SortOrder = reader.GetInt32(4),
                IsActive = reader.GetInt32(5) != 0,
                CreatedUtc = Parse(reader.GetString(6)),
                UpdatedUtc = Parse(reader.GetString(7)),
                UpdatedBy = reader.GetString(8),
            };
        }

        /// <summary>
        /// Reads the identifier SQLite assigned to the row just inserted.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <returns>The identifier.</returns>
        private static long LastInsertedId(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT last_insert_rowid();";

                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Adds the password parameters a statement declares.
        /// </summary>
        /// <param name="command">The command to add to.</param>
        /// <param name="password">The password to set, or <see langword="null"/>.</param>
        private void AddPasswordParameters(SqliteCommand command, string password)
        {
            string hash = null;
            string salt = null;

            if (password != null)
            {
                PasswordHasher.Derive(password, _options.PasswordIterations, out hash, out salt);
            }

            command.Parameters.AddWithValue("$hash", (object)hash ?? DBNull.Value);
            command.Parameters.AddWithValue("$salt", (object)salt ?? DBNull.Value);
            command.Parameters.AddWithValue("$iterations", hash == null ? 0 : _options.PasswordIterations);
        }

        /// <summary>
        /// Constrains a role to one the site understands.
        /// </summary>
        /// <param name="role">The requested role.</param>
        /// <returns>The role in force.</returns>
        /// <remarks>
        /// An unrecognised role becomes the least privileged one. A role that reached the database unvalidated
        /// would be a role that no page recognises, and a page that does not recognise a role must decide
        /// whether to treat it as privileged or not -- which is a decision that should never have to be made.
        /// </remarks>
        private static string NormaliseRole(string role)
        {
            return string.Equals(role, UserSession.AdministratorRole, StringComparison.OrdinalIgnoreCase)
                ? UserSession.AdministratorRole
                : UserSession.ViewerRole;
        }

        /// <summary>
        /// Constrains a notice's severity to one the stylesheet defines.
        /// </summary>
        /// <param name="severity">The requested severity.</param>
        /// <returns>The severity in force.</returns>
        private static string NormaliseSeverity(string severity)
        {
            if (string.IsNullOrWhiteSpace(severity)) return "info";

            string value = severity.Trim().ToLowerInvariant();

            return value == "warning" || value == "danger" ? value : "info";
        }

        /// <summary>
        /// Formats an instant for storage.
        /// </summary>
        /// <param name="value">The instant.</param>
        /// <returns>The round-trippable text.</returns>
        private static string Format(DateTime value)
        {
            return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses a stored instant.
        /// </summary>
        /// <param name="value">The stored text.</param>
        /// <returns>The instant, in UTC.</returns>
        private static DateTime Parse(string value)
        {
            DateTime parsed;

            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out parsed))
            {
                return parsed;
            }

            // A row that cannot be parsed is treated as the beginning of time rather than as a fault. A
            // corrupted timestamp must not be able to stop an operator from signing in and fixing it.
            return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        }

        /// <summary>
        /// Shortens a value to the width its column declares.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="maximum">The greatest accepted length.</param>
        /// <returns>The shortened value, never null.</returns>
        private static string Truncate(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }
    }
}
