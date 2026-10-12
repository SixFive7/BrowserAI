// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Runtime;
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
/// only sees committed work and committing ends the transaction -- so a store
/// that tried to be the guard would either hide the log from every reader or
/// lock them out of it. What is left for the store to do is be a store.
/// </para>
/// <para>
/// <b>One writer, many readers, and the writer is the lock holder.</b> That is
/// an application-level invariant enforced by the code path that reaches this
/// type, not a defence: BrowserAI's charter names adversarial and
/// hostile-caller defence an explicit non-goal, and a second process that
/// opened this file for writing on purpose would get a SQLite error and not
/// a refusal from here.
/// </para>
/// <para>
/// <b>Write-ahead logging, so a reader is never refused by a live writer.</b>
/// The reader that matters is <c>browserai_catch_up</c> against a session
/// another BrowserAI is driving -- *the case it exists for* -- so a journal mode
/// in which a commit blocks readers would fail exactly when it was needed.
/// </para>
/// <para>
/// ⚠️ <b>A CRASHED HOLDER'S WRITE-AHEAD LOG, AND WHAT A READ-ONLY CALLER
/// ACTUALLY GETS -- measured 2026-08-26, not reasoned about, because the
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
/// side-effect-free act -- a <c>-shm</c> appears beside the store, in a
/// directory the caller only asked to look at. ⚠️ <b>And the reader is not
/// always a caller: starting the server does this to EVERY session on the
/// machine</b> (added 2026-08-26 -- this paragraph said <i>the caller</i> and
/// meant it). <c>Program.Main</c> starts the stray sweep, one pass calls
/// <c>SessionIndex.Sweep</c>, and that follows every entry in the machine-wide
/// index through <see cref="OpenForReadingAsync"/> -- so one process start is one
/// store open, and one <c>-shm</c> and <c>-wal</c>, per registered session on
/// the host. Measured that day through the published binary against a
/// cleanly-closed session, with a second BrowserAI that sent nothing but
/// <c>initialize</c>. Second, where the caller may
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
/// is that an append is an <c>INSERT</c> and not a durable rewrite of the
/// whole record.
/// </para>
/// <para>
/// ⚠️ <b>Every statement runs on the store's own thread since 2026-10-12</b>, decided by
/// the maintainer, 82 b, in his words verbatim: <i>"78-82 your recommendation"</i>. SQLite
/// has no asynchronous form: Microsoft's own page on <c>Microsoft.Data.Sqlite</c> says
/// <i>"SQLite doesn't support asynchronous I/O"</i>, and a statement waits in the disk, in
/// a sync of the write-ahead log, or in the busy handler for as long as
/// <see cref="BusyTimeout"/> allows. So each store starts a thread of its own when it is
/// opened, the open, every statement and the close run there, and a caller awaits them.
/// The statements are on <see cref="SessionStoreConnection"/>, which the store hands only
/// to work running on that thread, and each of them refuses a caller on any other.
/// <i>Corrected that day (previously every member here ran on its caller's thread, and the
/// session's lock was held across each statement).</i>
/// </para>
/// </remarks>
internal sealed class SessionStore : IAsyncDisposable
{
    /// <summary>The store's file name inside a session directory.</summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-09-15 (previously the literal itself).</b> The
    /// literal is <see cref="Sessions.SessionLayout.DataFileName"/> and this is
    /// an alias for it, for the reason
    /// <see cref="LockFile.FileName"/> gives: the layout moved into
    /// <c>BrowserAI.Core</c> and this layer did not, so the one spelling had to
    /// move to the side that is linked and not the side that links.
    /// </remarks>
    public const string DataFileName = Sessions.SessionLayout.DataFileName;

    /// <summary>
    /// The schema this build writes and the only one it reads.
    /// </summary>
    /// <remarks>
    /// <b>There is no converter and there will not be one</b>, which is this
    /// repository's standing position on a record it cannot act on: the version
    /// is checked in a pass of its own so that a version error reads as a
    /// version error and not as damage, and the refusal carries the fix.
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

    /// <summary>The <c>outcome</c> of a call the hold-back held once: a valid call, and no failure.</summary>
    /// <remarks>
    /// <b>The maintainer's 23.2 c, 2026-10-10</b>: a resume held back by the hold-back stays
    /// an error to the client, and the session's own record and <c>browserai_catch_up</c>
    /// list it as held back. <i>Added that day (previously such a row was settled
    /// <see cref="Failed"/>).</i> A build older than this one reads the value as no outcome
    /// it knows, which its <c>browserai_catch_up</c> lists as no answer recorded.
    /// </remarks>
    public const string HeldBack = "held-back";

    private readonly DedicatedThread _thread;
    private readonly SessionStoreConnection _connection;
    private readonly Lock _closing = new();
    private Task? _closed;

    private SessionStore(DedicatedThread thread, SessionStoreConnection connection)
    {
        _thread = thread;
        _connection = connection;
    }

    /// <summary>Whether this store may write.</summary>
    public bool IsWritable => _connection.IsWritable;

    /// <summary>The file this store was opened against.</summary>
    public string Path => _connection.Path;

    /// <summary>
    /// How long a contended statement retries before it answers
    /// <see cref="Sqlite.Busy"/>.
    /// </summary>
    /// <remarks>
    /// <b>Derived from <see cref="LockScopes.PerDirectoryGate"/> and not
    /// chosen.</b> The two answer the same question about the same directory --
    /// *how long may a second caller be made to wait before it is told no* --
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
    /// see the type's own remarks. The order inside is load-bearing -- the busy
    /// timeout is set before the first statement that can be refused by
    /// contention, and the journal mode before the schema, so a store is never
    /// created in a mode it will not be used in.
    /// </remarks>
    /// <param name="path">The store file.</param>
    /// <returns>The store, its thread started.</returns>
    /// <exception cref="SqliteException">SQLite refused, or the schema is not this build's.</exception>
    public static Task<SessionStore> OpenForWritingAsync(string path) => OpenAsync(path, writable: true, first: null);

    /// <summary>
    /// Opens a session's store for writing and makes its first statements in the same item
    /// on the store's thread, so nothing runs between the open and them.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-12 for the acquisition</b>, with the maintainer's 82 b. The guard is
    /// held from before the open, and a peer that finds it held reads the record for the
    /// holder's purpose without the gate; so the time in which a peer finds the guard held
    /// and the record not yet saying by whom is the open and the acquisition's statements, as
    /// it was before the store had a thread. Handed over as two items, each awaited, that
    /// time took two waits for a pool worker as well, and the gate of that day met it:
    /// three of fifteen contenders of <c>SessionLockTests</c>' race were told the session
    /// records no purpose.
    /// </remarks>
    /// <param name="path">The store file.</param>
    /// <param name="first">
    /// The first statements, made right after the open on the store's thread. If they throw,
    /// the store is closed and the exception is the caller's.
    /// </param>
    /// <returns>The store, its thread started and its first statements made.</returns>
    /// <exception cref="SqliteException">SQLite refused the open, the schema or the first statements.</exception>
    public static Task<SessionStore> OpenForWritingAsync(string path, Action<SessionStoreConnection> first)
    {
        ArgumentNullException.ThrowIfNull(first);

        return OpenAsync(path, writable: true, first);
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
    /// *there is no store here* an answer and not a directory quietly
    /// gaining one.
    /// </remarks>
    /// <param name="path">The store file.</param>
    /// <returns>The store, its thread started.</returns>
    /// <exception cref="SqliteException">SQLite refused, or the schema is not this build's.</exception>
    public static Task<SessionStore> OpenForReadingAsync(string path) => OpenAsync(path, writable: false, first: null);

    /// <summary>Runs <paramref name="work"/> on this store's thread, after everything handed over before it.</summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The statements, made through the connection it is handed.</param>
    /// <returns>Its result, or its exception.</returns>
    /// <exception cref="ObjectDisposedException">The store is closed.</exception>
    public Task<T> RunAsync<T>(Func<SessionStoreConnection, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return _thread.RunAsync(() => work(_connection));
    }

    /// <summary>Runs <paramref name="work"/> on this store's thread, after everything handed over before it.</summary>
    /// <param name="work">The statements, made through the connection it is handed.</param>
    /// <returns>Its completion, or its exception.</returns>
    /// <exception cref="ObjectDisposedException">The store is closed.</exception>
    public Task RunAsync(Action<SessionStoreConnection> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return _thread.RunAsync(() => work(_connection));
    }

    /// <summary>
    /// Closes the connection on this store's thread, after every statement handed over
    /// before it, and ends the thread. Calling it again answers with the same close.
    /// </summary>
    /// <remarks>
    /// <b>The close is the store's last item</b>: work handed over after it is refused
    /// with <see cref="ObjectDisposedException"/>, so nothing reaches the file once the
    /// close has been asked for. Closing the last connection checkpoints the write-ahead
    /// log and removes it, which is why the close is I/O and runs where a statement does.
    /// </remarks>
    /// <returns>The close.</returns>
    public Task CloseAsync()
    {
        lock (_closing)
        {
            if (_closed is null)
            {
                _closed = _thread.RunAsync(_connection.Close);
                _thread.Complete();
            }

            return _closed;
        }
    }

    /// <summary>The same as <see cref="CloseAsync"/>.</summary>
    /// <returns>The close.</returns>
    public ValueTask DisposeAsync() => new(CloseAsync());

    /// <summary>Starts the store's thread and opens the connection on it.</summary>
    /// <param name="path">The store file.</param>
    /// <param name="writable">Whether the connection may write.</param>
    /// <param name="first">Statements made in the open's own item, or <see langword="null"/>.</param>
    /// <returns>The store.</returns>
    private static async Task<SessionStore> OpenAsync(string path, bool writable, Action<SessionStoreConnection>? first)
    {
        ArgumentNullException.ThrowIfNull(path);

        var thread = new DedicatedThread("BrowserAI session store");

        try
        {
            var connection = await thread.RunAsync(() =>
            {
                var opened = SessionStoreConnection.Open(path, writable, thread);

                if (first is not null)
                {
                    try
                    {
                        first(opened);
                    }
                    catch
                    {
                        opened.Close();
                        throw;
                    }
                }

                return opened;
            }).ConfigureAwait(false);

            return new SessionStore(thread, connection);
        }
        catch
        {
            thread.Complete();
            throw;
        }
    }
}

/// <summary>
/// One session store's connection, and every statement the store makes. It is handed only
/// to work running on its store's thread, and each member refuses a caller on any other.
/// </summary>
/// <remarks>
/// <b>Added 2026-10-12 for the maintainer's 82 b</b>, in his words verbatim:
/// <i>"78-82 your recommendation"</i>, with the members <see cref="SessionStore"/> carried
/// until that day, unchanged but for the check. One thread runs them, in the order they
/// were handed over, so <see cref="AppendLog"/>'s <c>INSERT</c> and the
/// <c>last_insert_rowid</c> after it are one item and nothing interleaves between them.
/// </remarks>
internal sealed class SessionStoreConnection
{
    private readonly SqliteDatabase _database;
    private readonly DedicatedThread _thread;

    private SessionStoreConnection(SqliteDatabase database, bool writable, DedicatedThread thread)
    {
        _database = database;
        _thread = thread;
        IsWritable = writable;
    }

    /// <summary>Whether this connection may write.</summary>
    public bool IsWritable { get; }

    /// <summary>The file this connection was opened against.</summary>
    public string Path => _database.Path;

    /// <summary>Opens a connection on its store's thread.</summary>
    /// <param name="path">The store file.</param>
    /// <param name="writable">Whether to open it for writing, creating and initialising it when it is not there.</param>
    /// <param name="thread">The store's thread, which is the calling thread.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="SqliteException">SQLite refused, or the schema is not this build's.</exception>
    internal static SessionStoreConnection Open(string path, bool writable, DedicatedThread thread)
    {
        if (!thread.IsCurrent)
        {
            throw OffItsThread();
        }

        Sqlite.RequireASupportedBuild();

        return writable ? OpenForWriting(path, thread) : OpenForReading(path, thread);
    }

    /// <summary>The open for writing, as <see cref="SessionStore.OpenForWritingAsync(string)"/> describes it.</summary>
    /// <param name="path">The store file.</param>
    /// <param name="thread">The store's thread.</param>
    /// <returns>The connection.</returns>
    private static SessionStoreConnection OpenForWriting(string path, DedicatedThread thread)
    {
        var database = SqliteDatabase.OpenForWriting(path);

        try
        {
            database.SetBusyTimeout(SessionStore.BusyTimeout);

            // A query and not an Execute: `PRAGMA journal_mode` answers with
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
            else if (version != SessionStore.SchemaVersion)
            {
                throw NotThisSchema(path, version);
            }

            return new SessionStoreConnection(database, writable: true, thread);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>The open for reading, as <see cref="SessionStore.OpenForReadingAsync"/> describes it.</summary>
    /// <param name="path">The store file.</param>
    /// <param name="thread">The store's thread.</param>
    /// <returns>The connection.</returns>
    private static SessionStoreConnection OpenForReading(string path, DedicatedThread thread)
    {
        var database = SqliteDatabase.OpenForReading(path);

        try
        {
            database.SetBusyTimeout(SessionStore.BusyTimeout);

            var version = SchemaVersionOf(database);

            if (version != SessionStore.SchemaVersion)
            {
                throw NotThisSchema(path, version);
            }

            return new SessionStoreConnection(database, writable: false, thread);
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
    /// <c>IMMEDIATE</c> and not a deferred begin, so the write lock is
    /// taken now and a refusal arrives before any row has been written.
    /// </remarks>
    /// <param name="statements">The statements, in the order they should be stored.</param>
    /// <exception cref="SqliteException">SQLite refused; nothing was written.</exception>
    public void RecordAcquisition(IReadOnlyList<StoredStatement> statements)
    {
        Require();
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
    public void Append(StoredStatement statement)
    {
        Require();
        Insert(statement);
    }

    /// <summary>
    /// Appends one log row and answers with its id.
    /// </summary>
    /// <remarks>
    /// <b>Written before a call is forwarded, which is why the outcome is a
    /// parameter and not a return.</b> A call that never comes back still
    /// left a row saying what it was for, and the id is what lets the answer,
    /// when there is one, land on that row and not beside it.
    /// </remarks>
    /// <param name="at">When the call was made, round-trippable.</param>
    /// <param name="tool">The tool name, verbatim, whatever the caller said.</param>
    /// <param name="why">What the caller said it was for.</param>
    /// <param name="outcome">One of <see cref="SessionStore.InFlight"/>, <see cref="SessionStore.Successful"/> or <see cref="SessionStore.Failed"/>.</param>
    /// <returns>The row's id.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public long AppendLog(string at, string tool, string why, string outcome)
    {
        Require();

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
        Require();

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
    /// <b><c>ORDER BY rowid</c> is stated, not assumed.</b> The table has
    /// no key of its own -- the schema is three text columns -- so *the order they
    /// were written in* is the implicit rowid and nothing else. A bare
    /// <c>SELECT</c> happens to return them that way today and is entitled to
    /// stop, and "newest statement wins" is how every field in this record is
    /// read.
    /// </remarks>
    /// <returns>The statements.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public IReadOnlyList<StoredStatement> Statements()
    {
        Require();

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
        Require();

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
    public long LogLength()
    {
        Require();

        return _database.QueryInt64("SELECT COUNT(*) FROM log;") ?? 0;
    }

    /// <summary>When the newest call was made, or <see langword="null"/> for a log with nothing in it.</summary>
    /// <remarks>
    /// <b>The <c>at</c> of the newest row and not the newest <c>at</c>.</b>
    /// Rows are written in call order and <c>id</c> is that order, so ordering
    /// by the timestamp column would sort by a string whose value comes from a
    /// clock the caller can move -- and *when did anything last happen here* is
    /// the one field a listing prints for every session.
    /// </remarks>
    /// <returns>The stamp, as it is stored.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public string? NewestLogAt()
    {
        Require();

        using var statement = _database.Prepare("SELECT at FROM log ORDER BY id DESC LIMIT 1;");

        return statement.Step() ? statement.TextAt(0) : null;
    }

    /// <summary>The journal mode this connection's database is in.</summary>
    /// <returns>The mode, lower case, as SQLite reports it.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public string JournalMode()
    {
        Require();

        var answer = _database.Query("PRAGMA journal_mode;");

        return answer.Count is 0 ? string.Empty : answer[0];
    }

    /// <summary>The busy timeout this connection is carrying, in milliseconds.</summary>
    /// <returns>The value SQLite reports.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public long BusyTimeoutInMilliseconds()
    {
        Require();

        return _database.QueryInt64("PRAGMA busy_timeout;") ?? 0;
    }

    /// <summary>The schema version recorded in the file.</summary>
    /// <returns>The version.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public long RecordedSchemaVersion()
    {
        Require();

        return SchemaVersionOf(_database);
    }

    /// <summary>Closes the connection. The store's thread ends after it.</summary>
    internal void Close()
    {
        Require();
        _database.Dispose();
    }

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
    /// completed by the next open and is not a state anybody has to
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
            + $"{SessionStore.SchemaVersion.ToString(CultureInfo.InvariantCulture)}. "
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

    /// <summary>The refusal for a statement made off the store's thread.</summary>
    /// <returns>The exception, for the caller to throw.</returns>
    private static InvalidOperationException OffItsThread() =>
        new("A statement on a session's store was made off that store's own thread, the one thread its statements run on. "
            + "Hand it to SessionStore.RunAsync (the maintainer's 82 b).");

    /// <summary>Refuses a caller on any thread but the store's own.</summary>
    private void Require()
    {
        if (!_thread.IsCurrent)
        {
            throw OffItsThread();
        }
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
/// is what lets a record say how it got here and not only where it ended
/// up -- and it is what kills the string concatenation the old record used to
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
/// <param name="Outcome">In flight, successful, failed, or held back since 2026-10-10.</param>
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
