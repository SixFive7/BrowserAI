// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Sessions;

namespace BrowserAI.Storage;

/// <summary>
/// One session's record: every statement the session has made about itself, and
/// every tool call it has logged.
/// </summary>
/// <remarks>
/// <para>
/// <b>The store is not the guard.</b> Ownership of a session directory is
/// decided by <see cref="LockFile"/> and by the kernel's share modes, and this
/// file has nothing to do with it. That separation is the whole design: a
/// transaction cannot be both the lock and the write path, because a reader
/// only sees committed work and committing ends the transaction — so a store
/// that tried to be the guard would either hide the log from every reader or
/// lock them out of it. What is left for the store to do is be a store.
/// </para>
/// <para>
/// <b>One writer, many readers, and the writer is the lock holder.</b> That is
/// an application-level invariant enforced by the code path that reaches this
/// type, not a defence: BrowserAI's charter names adversarial and
/// hostile-caller defence an explicit non-goal, and a second process that
/// opened this file for writing on purpose would get a SQLite error rather than
/// a refusal from here.
/// </para>
/// <para>
/// <b>Write-ahead logging, so a reader is never refused by a live writer.</b>
/// The reader that matters is <c>browserai_catch_up</c> against a session
/// another BrowserAI is driving — *the case it exists for* — so a journal mode
/// in which a commit blocks readers would fail exactly when it was needed.
/// </para>
/// <para>
/// ⚠️ <b>A CRASHED HOLDER'S WRITE-AHEAD LOG, AND WHAT A READ-ONLY CALLER
/// ACTUALLY GETS — measured 2026-08-26 rather than reasoned about, because the
/// answer is not the one the design note predicted.</b> When a holder dies
/// without closing, the <c>-wal</c> carries committed transactions the
/// <c>.data</c> file does not, and reading them means <i>building</i> the
/// shared-memory wal-index. The prediction was that a read-only connection
/// cannot do that and so reads the session as unreadable until the next
/// acquisition. On Windows, in an ordinary writable session directory, that is
/// <b>false</b>: <c>SQLITE_OPEN_READONLY</c> constrains the main database file
/// and not the <c>-shm</c>, so the connection creates the index, recovers the
/// log, and answers with the crashed session's newest rows.
/// </para>
/// <para>
/// <b>Two consequences, both accepted and both pinned by
/// <c>SqliteStorageTests</c>.</b> First, reading a session directory is not a
/// side-effect-free act — a <c>-shm</c> appears beside the store, in a
/// directory the caller only asked to look at. Second, where the caller may
/// <i>not</i> create files there, the open is refused with
/// <c>SQLITE_CANTOPEN</c> and the session stays unreadable until somebody opens
/// it for writing, which the next acquisition does. That refusal is the right
/// way round: a reader that silently ignored the <c>-wal</c> would answer
/// confidently with a session's history as of its last checkpoint, which is the
/// confident-wrong-answer class this repository has spent two hazard rows
/// closing.
/// </para>
/// <para>
/// <b>No caps, anywhere.</b> Not on a <c>why</c>, not on a purpose, not on the
/// number of log rows, not on a failure payload. That is the maintainer's
/// explicit decision, and the reason it is affordable here and was not before
/// is that an append is an <c>INSERT</c> rather than a durable rewrite of the
/// whole record.
/// </para>
/// </remarks>
internal sealed class SessionStore : IDisposable
{
    /// <summary>The store's file name inside a session directory.</summary>
    public const string DataFileName = "browserai.data";

    /// <summary>
    /// The schema this build writes and the only one it reads.
    /// </summary>
    /// <remarks>
    /// <b>There is no converter and there will not be one</b>, which is this
    /// repository's standing position on a record it cannot act on: the version
    /// is checked in a pass of its own so that a version error reads as a
    /// version error rather than as damage, and the refusal carries the fix.
    /// Making migration cheap is how a record starts carrying fields nobody
    /// decided on.
    /// </remarks>
    public const int SchemaVersion = 1;

    /// <summary>The <c>outcome</c> of a call that has been forwarded and not yet answered.</summary>
    public const string InFlight = "in-flight";

    /// <summary>The <c>outcome</c> of a call that was answered.</summary>
    public const string Successful = "successful";

    /// <summary>The <c>outcome</c> of a call that failed.</summary>
    public const string Failed = "failed";

    private readonly SqliteDatabase _database;

    private SessionStore(SqliteDatabase database, bool writable)
    {
        _database = database;
        IsWritable = writable;
    }

    /// <summary>Whether this connection may write.</summary>
    public bool IsWritable { get; }

    /// <summary>The file this store was opened against.</summary>
    public string Path => _database.Path;

    /// <summary>
    /// How long a contended statement retries before it answers
    /// <see cref="Sqlite.Busy"/>.
    /// </summary>
    /// <remarks>
    /// <b>Derived from <see cref="LockScopes.PerDirectoryGate"/> rather than
    /// chosen.</b> The two answer the same question about the same directory —
    /// *how long may a second caller be made to wait before it is told no* —
    /// and two different numbers would mean a caller admitted by one could be
    /// refused by the other, with nothing in either message naming the
    /// disagreement.
    /// </remarks>
    public static TimeSpan BusyTimeout => LockScopes.PerDirectoryGate;

    /// <summary>
    /// Opens a session's store for writing, creating and initialising it when
    /// it is not there.
    /// </summary>
    /// <remarks>
    /// <b>Only the lock holder may call this</b>, and nothing here checks that:
    /// see the type's own remarks. The order inside is load-bearing — the busy
    /// timeout is set before the first statement that can be refused by
    /// contention, and the journal mode before the schema, so a store is never
    /// created in a mode it will not be used in.
    /// </remarks>
    /// <param name="path">The store file.</param>
    /// <returns>The store.</returns>
    /// <exception cref="SqliteException">SQLite refused, or the schema is not this build's.</exception>
    public static SessionStore OpenForWriting(string path)
    {
        Sqlite.RequireASupportedBuild();

        var database = SqliteDatabase.OpenForWriting(path);

        try
        {
            database.SetBusyTimeout(BusyTimeout);

            // A query rather than an Execute: `PRAGMA journal_mode` answers with
            // the mode it ended up in, and it is allowed to answer with a
            // different one -- a database on a filesystem that cannot do shared
            // memory stays in its old mode and says so, quietly, which would
            // leave every reader blocked by every commit.
            var mode = database.Query("PRAGMA journal_mode=WAL;");

            if (mode.Count is not 1 || !string.Equals(mode[0], "wal", StringComparison.OrdinalIgnoreCase))
            {
                throw new SqliteException(
                    Sqlite.GenericError,
                    $"'{path}' could not be put into write-ahead logging mode; SQLite answered '{string.Join(",", mode)}'. "
                    + "BrowserAI needs WAL because a session's record is read by other processes while its holder is writing to it, "
                    + "and every other journal mode blocks those readers at each commit. A session directory on a network share is the usual cause.");
            }

            var version = SchemaVersionOf(database);

            if (version is 0)
            {
                Create(database);
            }
            else if (version != SchemaVersion)
            {
                throw NotThisSchema(path, version);
            }

            return new SessionStore(database, writable: true);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a session's store for reading, taking nothing and changing
    /// nothing.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Read-only is a constraint on the store file and not on the
    /// directory</b>, which is the half that surprises: against a crashed
    /// holder's hot write-ahead log this open builds a <c>-shm</c> beside the
    /// store and recovers the log, and it is refused only where it may not
    /// create that file. The type's own remarks carry the measurement. What it
    /// genuinely cannot do is create the <i>store</i>, which is what makes
    /// *there is no store here* an answer rather than a directory quietly
    /// gaining one.
    /// </remarks>
    /// <param name="path">The store file.</param>
    /// <returns>The store.</returns>
    /// <exception cref="SqliteException">SQLite refused, or the schema is not this build's.</exception>
    public static SessionStore OpenForReading(string path)
    {
        Sqlite.RequireASupportedBuild();

        var database = SqliteDatabase.OpenForReading(path);

        try
        {
            database.SetBusyTimeout(BusyTimeout);

            var version = SchemaVersionOf(database);

            if (version != SchemaVersion)
            {
                throw NotThisSchema(path, version);
            }

            return new SessionStore(database, writable: false);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Writes the statements one acquisition makes, all of them or none.
    /// </summary>
    /// <remarks>
    /// <b>The one explicit transaction in this store.</b> Everything else
    /// appends in autocommit, because a single <c>INSERT</c> is already atomic
    /// and wrapping it would only add two more statements. Acquisition is
    /// different: the holder row and the statements that say what this session
    /// now is are one fact, and a reader that saw half of it would see a
    /// session held by somebody for no reason, or a reason with no holder.
    /// <c>IMMEDIATE</c> rather than a deferred begin, so the write lock is
    /// taken now and a refusal arrives before any row has been written.
    /// </remarks>
    /// <param name="statements">The statements, in the order they should be stored.</param>
    /// <exception cref="SqliteException">SQLite refused; nothing was written.</exception>
    public void RecordAcquisition(IReadOnlyList<StoredStatement> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        _database.Execute("BEGIN IMMEDIATE;");

        try
        {
            foreach (var statement in statements)
            {
                Insert(statement);
            }

            _database.Execute("COMMIT;");
        }
        catch
        {
            // Best effort, and its failure must not replace the one being
            // reported: a rollback that cannot run means the transaction is
            // already over, which the original exception is about.
            try
            {
                _database.Execute("ROLLBACK;");
            }
            catch (SqliteException)
            {
                // Deliberately swallowed. See above.
            }

            throw;
        }
    }

    /// <summary>Appends one statement about the session.</summary>
    /// <param name="statement">The statement.</param>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public void Append(StoredStatement statement) => Insert(statement);

    /// <summary>
    /// Appends one log row and answers with its id.
    /// </summary>
    /// <remarks>
    /// <b>Written before a call is forwarded, which is why the outcome is a
    /// parameter rather than a return.</b> A call that never comes back still
    /// left a row saying what it was for, and the id is what lets the answer,
    /// when there is one, land on that row rather than beside it.
    /// </remarks>
    /// <param name="at">When the call was made, round-trippable.</param>
    /// <param name="tool">The tool name, verbatim, whatever the caller said.</param>
    /// <param name="why">What the caller said it was for.</param>
    /// <param name="outcome">One of <see cref="InFlight"/>, <see cref="Successful"/> or <see cref="Failed"/>.</param>
    /// <returns>The row's id.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public long AppendLog(string at, string tool, string why, string outcome)
    {
        using (var statement = _database.Prepare(
            "INSERT INTO log (at, tool, why, outcome, settled_at, failure) VALUES (?, ?, ?, ?, NULL, NULL);"))
        {
            statement
                .BindText(1, at)
                .BindText(2, tool)
                .BindText(3, why)
                .BindText(4, outcome)
                .Run();
        }

        return _database.LastInsertRowId;
    }

    /// <summary>
    /// Settles a log row that was written before its call was forwarded.
    /// </summary>
    /// <remarks>
    /// <b>It answers whether it found the row</b>, and the caller is expected to
    /// care. A settle against an id nothing matches is a defect in whoever kept
    /// the id, and an <c>UPDATE</c> that matched nothing is the quietest
    /// possible way for that to happen.
    /// </remarks>
    /// <param name="id">The row from <see cref="AppendLog"/>.</param>
    /// <param name="outcome">The settled outcome.</param>
    /// <param name="settledAt">When it settled, round-trippable.</param>
    /// <param name="failure">The failure payload, or <see langword="null"/> for a call that succeeded.</param>
    /// <returns>Whether a row was updated.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public bool Settle(long id, string outcome, string settledAt, byte[]? failure)
    {
        using (var statement = _database.Prepare(
            "UPDATE log SET outcome = ?, settled_at = ?, failure = ? WHERE id = ?;"))
        {
            statement
                .BindText(1, outcome)
                .BindText(2, settledAt)
                .BindBlob(3, failure)
                .BindInt64(4, id)
                .Run();
        }

        return _database.Changes is not 0;
    }

    /// <summary>Every statement, oldest first.</summary>
    /// <remarks>
    /// <b><c>ORDER BY rowid</c> is stated rather than assumed.</b> The table has
    /// no key of its own — the schema is three text columns — so *the order they
    /// were written in* is the implicit rowid and nothing else. A bare
    /// <c>SELECT</c> happens to return them that way today and is entitled to
    /// stop, and "newest statement wins" is how every field in this record is
    /// read.
    /// </remarks>
    /// <returns>The statements.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public IReadOnlyList<StoredStatement> Statements()
    {
        var rows = new List<StoredStatement>();

        using var statement = _database.Prepare("SELECT field, at, value FROM statements ORDER BY rowid;");

        while (statement.Step())
        {
            rows.Add(new StoredStatement(
                statement.TextAt(0) ?? string.Empty,
                statement.TextAt(1) ?? string.Empty,
                statement.TextAt(2) ?? string.Empty));
        }

        return rows;
    }

    /// <summary>Log rows, oldest first, optionally a page at a time.</summary>
    /// <remarks>
    /// <b>Ordered and paged from the oldest end, which is what makes a page
    /// stable.</b> The log only ever grows at the newest end, so under
    /// oldest-first numbering an append can change the last page and no other;
    /// numbering from the newest end would shift every boundary on every call.
    /// </remarks>
    /// <param name="skip">How many rows to pass over.</param>
    /// <param name="take">How many to return; a negative number means all of them.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public IReadOnlyList<StoredLogEntry> Log(long skip = 0, long take = -1)
    {
        var rows = new List<StoredLogEntry>();

        using var statement = _database.Prepare(
            "SELECT id, at, tool, why, outcome, settled_at, failure FROM log ORDER BY id LIMIT ? OFFSET ?;");

        _ = statement.BindInt64(1, take).BindInt64(2, skip);

        while (statement.Step())
        {
            rows.Add(new StoredLogEntry(
                statement.Int64At(0),
                statement.TextAt(1) ?? string.Empty,
                statement.TextAt(2) ?? string.Empty,
                statement.TextAt(3) ?? string.Empty,
                statement.TextAt(4) ?? string.Empty,
                statement.TextAt(5),
                statement.BlobAt(6)));
        }

        return rows;
    }

    /// <summary>How many log rows there are.</summary>
    /// <returns>The count.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public long LogLength() => _database.QueryInt64("SELECT COUNT(*) FROM log;") ?? 0;

    /// <summary>When the newest call was made, or <see langword="null"/> for a log with nothing in it.</summary>
    /// <remarks>
    /// <b>The <c>at</c> of the newest row rather than the newest <c>at</c>.</b>
    /// Rows are written in call order and <c>id</c> is that order, so ordering
    /// by the timestamp column would sort by a string whose value comes from a
    /// clock the caller can move — and *when did anything last happen here* is
    /// the one field a listing prints for every session.
    /// </remarks>
    /// <returns>The stamp, as it is stored.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public string? NewestLogAt()
    {
        using var statement = _database.Prepare("SELECT at FROM log ORDER BY id DESC LIMIT 1;");

        return statement.Step() ? statement.TextAt(0) : null;
    }

    /// <summary>The journal mode this connection's database is in.</summary>
    /// <returns>The mode, lower case, as SQLite reports it.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public string JournalMode()
    {
        var answer = _database.Query("PRAGMA journal_mode;");

        return answer.Count is 0 ? string.Empty : answer[0];
    }

    /// <summary>The busy timeout this connection is carrying, in milliseconds.</summary>
    /// <returns>The value SQLite reports.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public long BusyTimeoutInMilliseconds() => _database.QueryInt64("PRAGMA busy_timeout;") ?? 0;

    /// <summary>The schema version recorded in the file.</summary>
    /// <returns>The version.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public long RecordedSchemaVersion() => SchemaVersionOf(_database);

    /// <inheritdoc />
    public void Dispose() => _database.Dispose();

    /// <summary>Reads <c>PRAGMA user_version</c>.</summary>
    /// <param name="database">The connection.</param>
    /// <returns>The version, or zero for a database nothing has stamped.</returns>
    private static long SchemaVersionOf(SqliteDatabase database) =>
        database.QueryInt64("PRAGMA user_version;") ?? 0;

    /// <summary>Writes the schema into a database that has none.</summary>
    /// <remarks>
    /// <para>
    /// <b>One <c>Execute</c> and therefore one implicit transaction per
    /// statement, deliberately not wrapped.</b> Every statement here is
    /// <c>IF NOT EXISTS</c> or idempotent, so a half-written schema is
    /// completed by the next open rather than being a state anybody has to
    /// recover from.
    /// </para>
    /// <para>
    /// <b><c>user_version</c> is stamped last</b>, so a database interrupted
    /// mid-creation still reads as version zero and is created again, instead
    /// of reading as this schema and being missing half of it.
    /// </para>
    /// </remarks>
    /// <param name="database">The connection.</param>
    private static void Create(SqliteDatabase database) =>
        database.Execute(
            """
            CREATE TABLE IF NOT EXISTS statements (
                field TEXT NOT NULL,
                at    TEXT NOT NULL,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS log (
                id         INTEGER PRIMARY KEY,
                at         TEXT NOT NULL,
                tool       TEXT NOT NULL,
                why        TEXT NOT NULL,
                outcome    TEXT NOT NULL,
                settled_at TEXT,
                failure    BLOB
            );

            PRAGMA user_version = 1;
            """);

    /// <summary>The refusal for a file this build cannot act on.</summary>
    /// <param name="path">The file.</param>
    /// <param name="version">What it says it is.</param>
    /// <returns>The exception, for the caller to throw.</returns>
    private static SqliteException NotThisSchema(string path, long version) =>
        new(
            Sqlite.NotADatabase,
            $"'{path}' records schema version {version.ToString(CultureInfo.InvariantCulture)} and this build of BrowserAI reads version "
            + $"{SchemaVersion.ToString(CultureInfo.InvariantCulture)}. "
            + (version is 0
                ? "A version of zero means the file is a database that BrowserAI never created. Point BrowserAI at a directory of its own."
                : "There is no converter: use a build of BrowserAI that reads that version, or start a new session directory."));

    /// <summary>Inserts one statement row.</summary>
    /// <param name="statement">The statement.</param>
    private void Insert(StoredStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        using var insert = _database.Prepare("INSERT INTO statements (field, at, value) VALUES (?, ?, ?);");

        insert
            .BindText(1, statement.Field)
            .BindText(2, statement.At)
            .BindText(3, statement.Value)
            .Run();
    }
}

/// <summary>
/// One timestamped statement a session has made about itself.
/// </summary>
/// <param name="Field">Which field it is a statement about.</param>
/// <param name="At">When it was made, round-trippable.</param>
/// <param name="Value">What it says.</param>
/// <remarks>
/// <b>Append-only, and "current" means the newest.</b> Nothing overwrites a
/// statement; a session that moves, or changes its purpose, gains a row. That
/// is what lets a record say how it got here rather than only where it ended
/// up — and it is what kills the string concatenation the old record used to
/// build a purpose out of every purpose before it.
/// </remarks>
internal sealed record StoredStatement(string Field, string At, string Value);

/// <summary>
/// One tool call, as the record holds it.
/// </summary>
/// <param name="Id">The row's own id, which is also its order.</param>
/// <param name="At">When the call was made, before it was forwarded.</param>
/// <param name="Tool">The tool name, verbatim.</param>
/// <param name="Why">What the caller said it was for.</param>
/// <param name="Outcome">In flight, successful, or failed.</param>
/// <param name="SettledAt">When the answer arrived, or <see langword="null"/> while it has not.</param>
/// <param name="Failure">The failure payload, or <see langword="null"/> when there was none.</param>
internal sealed record StoredLogEntry(
    long Id,
    string At,
    string Tool,
    string Why,
    string Outcome,
    string? SettledAt,
    byte[]? Failure);
