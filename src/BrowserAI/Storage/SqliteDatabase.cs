// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Runtime.InteropServices;

namespace BrowserAI.Storage;

/// <summary>
/// One open SQLite connection, and the four things this product asks of one:
/// run a statement, prepare a statement, ask for a list of strings, and say
/// what went wrong.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a connection pool, and not an <c>IDbConnection</c>.</b> The lifetime
/// of the handle on a session's data file is the single most safety-critical
/// resource this product has, and ADO.NET's answer to *when is it closed* is
/// *when the pool decides*. Here it is closed when this object is disposed and
/// at no other time.
/// </para>
/// <para>
/// <b>Thread affinity: none is assumed and none is provided.</b> The library is
/// built serialized — <see cref="Sqlite.RequireASupportedBuild"/> refuses one
/// that is not — so concurrent calls on one connection are safe at the SQLite
/// layer. What this type does not do is serialise a <i>sequence</i>: a
/// prepare-bind-step run interleaved with another caller's is two conversations
/// down one wire, and the caller owns that exclusion. In the design this layer
/// exists for there is exactly one writer per directory, so there is exactly
/// one such caller.
/// </para>
/// </remarks>
internal sealed class SqliteDatabase : IDisposable
{
    private readonly SqliteDatabaseHandle _handle;

    private SqliteDatabase(SqliteDatabaseHandle handle, string path)
    {
        _handle = handle;
        Path = path;
    }

    /// <summary>What this connection was opened against, for messages.</summary>
    public string Path { get; }

    /// <summary>The rowid of the most recent successful insert on this connection.</summary>
    public long LastInsertRowId => Sqlite.LastInsertRowId(_handle);

    /// <summary>How many rows the most recent statement changed.</summary>
    public int Changes => Sqlite.Changes(_handle);

    /// <summary>
    /// Opens a database in memory, for the questions that are about the library
    /// rather than about a file.
    /// </summary>
    /// <returns>The connection.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public static SqliteDatabase OpenInMemory() =>
        Open(":memory:", Sqlite.OpenReadWrite | Sqlite.OpenCreate);

    /// <summary>Opens a database for writing, creating it if it is not there.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public static SqliteDatabase OpenForWriting(string path) =>
        Open(path, Sqlite.OpenReadWrite | Sqlite.OpenCreate);

    /// <summary>
    /// Opens an existing database for reading and nothing else.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Read-only constrains the database file and not the directory it
    /// sits in</b>, so this open can still build the shared-memory index a hot
    /// write-ahead log needs, and is refused only where it may not create that
    /// file — measured, and the consequences are in
    /// <see cref="SessionStore"/>'s own remarks. What it genuinely cannot do is
    /// create the database, which is what makes *this directory has no store*
    /// an answer rather than a side effect.
    /// </remarks>
    /// <param name="path">The file.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public static SqliteDatabase OpenForReading(string path) =>
        Open(path, Sqlite.OpenReadOnly);

    /// <summary>Opens a database with an explicit flag set.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The handle is closed even when the open failed, and the message is
    /// taken off it first.</b> <c>sqlite3_open_v2</c> allocates the connection
    /// before it tries the file, so a failure normally still yields a handle —
    /// and that handle is the only thing <c>sqlite3_errmsg</c> can speak from.
    /// Dropping it on the failure path would leak the connection and reduce
    /// every open failure to a bare number.
    /// </para>
    /// <para>
    /// The one case where it does not yield a handle is a failure to allocate
    /// at all, which is why <c>IsInvalid</c> is asked before the handle is,
    /// and why <see cref="Sqlite.Describe"/> exists as the fallback.
    /// </para>
    /// </remarks>
    /// <param name="path">The file, or <c>:memory:</c>.</param>
    /// <param name="flags">The <c>SQLITE_OPEN_*</c> set.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public static SqliteDatabase Open(string path, int flags)
    {
        Sqlite.EnsureInitialized();

        var result = Sqlite.OpenV2(path, out var handle, flags, vfs: null);

        if (result is Sqlite.Ok && !handle.IsInvalid)
        {
            return new SqliteDatabase(handle, path);
        }

        var message = handle.IsInvalid ? Sqlite.Describe(result) : MessageOn(handle, result);

        handle.Dispose();

        throw new SqliteException(
            result,
            $"SQLite could not open '{path}': {message} (result {result.ToString(CultureInfo.InvariantCulture)}).");
    }

    /// <summary>
    /// Runs statements that produce no rows — schema, pragmas that set,
    /// transaction control.
    /// </summary>
    /// <remarks>
    /// <b>This is the one entry point that takes more than one statement</b>,
    /// because <c>sqlite3_prepare_v2</c> compiles the first and silently
    /// ignores the rest. Anything with a semicolon in the middle belongs here.
    /// </remarks>
    /// <param name="sql">The statements.</param>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public void Execute(string sql)
    {
        var result = Sqlite.Exec(_handle, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (result is not Sqlite.Ok)
        {
            throw Failure(result, sql);
        }
    }

    /// <summary>Compiles one statement.</summary>
    /// <param name="sql">Exactly one statement.</param>
    /// <returns>The prepared statement, which the caller disposes.</returns>
    /// <exception cref="SqliteException">SQLite refused to compile it.</exception>
    public SqliteStatement Prepare(string sql)
    {
        // -1 for the byte count: read to the terminator. The tail pointer is
        // null, so a caller that hands two statements here gets the first one
        // compiled and no warning -- which is why Execute exists and why this
        // parameter is documented as "exactly one".
        var result = Sqlite.PrepareV2(_handle, sql, byteCount: -1, out var statement, tail: IntPtr.Zero);

        if (result is Sqlite.Ok && !statement.IsInvalid)
        {
            return new SqliteStatement(this, statement, sql);
        }

        var failure = Failure(result, sql);

        statement.Dispose();

        throw failure;
    }

    /// <summary>
    /// Asks a statement for its first column, as text, over every row.
    /// </summary>
    /// <remarks>
    /// The shape every pragma this layer reads has — <c>compile_options</c>,
    /// <c>journal_mode</c>, <c>user_version</c>, <c>busy_timeout</c> — and the
    /// reason there is no generic reader beside it: a store with a dozen fixed
    /// statements does not need one, and the one it would need is the one a
    /// future schema will define for itself.
    /// </remarks>
    /// <param name="sql">Exactly one statement.</param>
    /// <returns>The first column of every row, in order.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public IReadOnlyList<string> Query(string sql)
    {
        var answers = new List<string>();

        using var statement = Prepare(sql);

        while (statement.Step())
        {
            answers.Add(statement.TextAt(0) ?? string.Empty);
        }

        return answers;
    }

    /// <summary>
    /// Asks a statement for one integer, which is how every numeric pragma
    /// answers.
    /// </summary>
    /// <param name="sql">Exactly one statement.</param>
    /// <returns>The first column of the first row, or <see langword="null"/> when there is none.</returns>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public long? QueryInt64(string sql)
    {
        using var statement = Prepare(sql);

        return statement.Step() ? statement.Int64At(0) : null;
    }

    /// <summary>
    /// How long a statement blocked by another connection retries before it
    /// answers <see cref="Sqlite.Busy"/>.
    /// </summary>
    /// <remarks>
    /// <b>Through the entry point rather than through <c>PRAGMA
    /// busy_timeout</c>.</b> They set the same thing, and the pragma is a
    /// statement that can itself be refused by the very contention it is being
    /// set to survive.
    /// </remarks>
    /// <param name="patience">The budget.</param>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public void SetBusyTimeout(TimeSpan patience)
    {
        var result = Sqlite.BusyTimeout(_handle, (int)patience.TotalMilliseconds);

        if (result is not Sqlite.Ok)
        {
            throw Failure(result, "sqlite3_busy_timeout");
        }
    }

    /// <summary>Builds the exception for a failed call on this connection.</summary>
    /// <param name="result">The result code.</param>
    /// <param name="sql">What was being run.</param>
    /// <returns>The exception, for the caller to throw.</returns>
    public SqliteException Failure(int result, string sql) =>
        new(
            result,
            $"SQLite refused '{sql}' on '{Path}': {MessageOn(_handle, result)} (result {result.ToString(CultureInfo.InvariantCulture)}).");

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();

    /// <summary>What the connection says about its most recent failure.</summary>
    /// <param name="handle">The connection.</param>
    /// <param name="result">The result code, for the fallback.</param>
    /// <returns>The message.</returns>
    private static string MessageOn(SqliteDatabaseHandle handle, int result) =>
        Marshal.PtrToStringUTF8(Sqlite.ErrorMessage(handle)) ?? Sqlite.Describe(result);
}
