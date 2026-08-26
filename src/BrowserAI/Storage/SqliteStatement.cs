// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using System.Text;

namespace BrowserAI.Storage;

/// <summary>
/// One compiled statement: bind, step, read, finalize.
/// </summary>
/// <remarks>
/// <para>
/// <b>Prepared, used once, and finalized.</b> There is no <c>reset</c> and no
/// <c>clear_bindings</c> here, and their absence is a decision rather than an
/// omission: this store runs a dozen statements a session, so re-using a
/// compiled one buys microseconds and costs the invariant that makes the rest
/// of this file simple — that a statement's bindings are exactly what this
/// caller put there.
/// </para>
/// <para>
/// <b>Every bind is checked and every checked failure names the statement.</b>
/// A bind that silently did nothing produces a row of nulls, which is the
/// failure shape this repository exists to eliminate: a record that was
/// written, that is readable, and that says something other than what happened.
/// </para>
/// </remarks>
internal sealed class SqliteStatement : IDisposable
{
    private readonly SqliteDatabase _database;
    private readonly SqliteStatementHandle _handle;
    private readonly string _sql;

    /// <summary>Wraps a compiled statement.</summary>
    /// <param name="database">The connection it belongs to, for its error messages.</param>
    /// <param name="handle">The compiled statement.</param>
    /// <param name="sql">What it was compiled from, for messages.</param>
    internal SqliteStatement(SqliteDatabase database, SqliteStatementHandle handle, string sql)
    {
        _database = database;
        _handle = handle;
        _sql = sql;
    }

    /// <summary>Binds text to a parameter.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The text is encoded here and its length is passed explicitly, and
    /// that is the difference between storing a caller's value and storing a
    /// prefix of it.</b> The obvious spelling — <c>StringMarshalling.Utf8</c>
    /// and a byte count of <c>-1</c> — tells SQLite to read to the first zero
    /// byte, and U+0000 encodes as exactly that. A <c>why</c> carrying one
    /// would be truncated at it, silently, in the field whose whole job is to
    /// say what a call was for.
    /// </para>
    /// <para>
    /// <b>The buffer always carries a terminator it does not count</b>, so it
    /// is never zero-length — and a zero-length array marshals to a pointer
    /// SQLite would read as SQL <c>NULL</c> rather than as the empty string.
    /// </para>
    /// </remarks>
    /// <param name="index">The one-based parameter index.</param>
    /// <param name="value">The text.</param>
    /// <returns>This statement, so binds can be chained.</returns>
    /// <exception cref="SqliteException">SQLite refused the bind.</exception>
    public SqliteStatement BindText(int index, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var byteCount = Encoding.UTF8.GetByteCount(value);
        var buffer = new byte[byteCount + 1];

        _ = Encoding.UTF8.GetBytes(value, buffer);

        return Checked(Sqlite.BindText(_handle, index, buffer, byteCount, Sqlite.Transient), index);
    }

    /// <summary>Binds an integer to a parameter.</summary>
    /// <param name="index">The one-based parameter index.</param>
    /// <param name="value">The value.</param>
    /// <returns>This statement, so binds can be chained.</returns>
    /// <exception cref="SqliteException">SQLite refused the bind.</exception>
    public SqliteStatement BindInt64(int index, long value) =>
        Checked(Sqlite.BindInt64(_handle, index, value), index);

    /// <summary>Binds bytes, or SQL <c>NULL</c>, to a parameter.</summary>
    /// <remarks>
    /// <b>A null reference is SQL <c>NULL</c> and an empty array is a
    /// zero-length blob</b>, and the two are different answers to *was there a
    /// payload*. The one-byte buffer behind an empty array exists so the
    /// pointer is not null, because <c>sqlite3_bind_blob</c> reads a null
    /// pointer as SQL <c>NULL</c> whatever the length says.
    /// </remarks>
    /// <param name="index">The one-based parameter index.</param>
    /// <param name="value">The bytes, or <see langword="null"/>.</param>
    /// <returns>This statement, so binds can be chained.</returns>
    /// <exception cref="SqliteException">SQLite refused the bind.</exception>
    public SqliteStatement BindBlob(int index, byte[]? value)
    {
        if (value is null)
        {
            return Checked(Sqlite.BindNull(_handle, index), index);
        }

        var buffer = value.Length is 0 ? new byte[1] : value;

        return Checked(Sqlite.BindBlob(_handle, index, buffer, value.Length, Sqlite.Transient), index);
    }

    /// <summary>Runs the statement one row further.</summary>
    /// <returns><see langword="true"/> when a row arrived, <see langword="false"/> when the statement finished.</returns>
    /// <exception cref="SqliteException">SQLite refused, which includes <c>SQLITE_BUSY</c> after the timeout.</exception>
    public bool Step()
    {
        var result = Sqlite.Step(_handle);

        return result switch
        {
            Sqlite.Row => true,
            Sqlite.Done => false,
            _ => throw _database.Failure(result, _sql),
        };
    }

    /// <summary>Runs a statement that is not expected to produce rows.</summary>
    /// <remarks>
    /// A row here is not an error and is not swallowed either: it is stepped
    /// past, because <c>INSERT ... RETURNING</c> and the pragmas that both set
    /// and report are legitimate shapes, and refusing them would be this layer
    /// having an opinion about SQL.
    /// </remarks>
    /// <exception cref="SqliteException">SQLite refused.</exception>
    public void Run()
    {
        while (Step())
        {
            // Every row, discarded. The statement is not finished until it says
            // it is, and stopping at the first row would leave a write half
            // done.
        }
    }

    /// <summary>Whether a column of the current row is SQL <c>NULL</c>.</summary>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>Whether it is null.</returns>
    public bool IsNullAt(int index) => Sqlite.ColumnType(_handle, index) is Sqlite.TypeNull;

    /// <summary>Reads a column of the current row as text.</summary>
    /// <remarks>
    /// ⚠️ <b>The length is taken from <c>sqlite3_column_bytes</c> rather than
    /// from the terminator</b>, for the same reason the bind passes one: a
    /// stored value carrying U+0000 is a value, and reading to the first zero
    /// byte would hand back a prefix of it. The accessor is called before the
    /// length, because SQLite converts in place on the first typed accessor and
    /// the length is the length after that conversion.
    /// </remarks>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The text, or <see langword="null"/> for SQL <c>NULL</c>.</returns>
    public string? TextAt(int index)
    {
        if (IsNullAt(index))
        {
            return null;
        }

        var pointer = Sqlite.ColumnText(_handle, index);
        var length = Sqlite.ColumnBytes(_handle, index);

        if (pointer == IntPtr.Zero || length <= 0)
        {
            return string.Empty;
        }

        var bytes = new byte[length];

        Marshal.Copy(pointer, bytes, 0, length);

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Reads a column of the current row as an integer.</summary>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The value; SQL <c>NULL</c> reads as zero, so ask <see cref="IsNullAt"/> first when that matters.</returns>
    public long Int64At(int index) => Sqlite.ColumnInt64(_handle, index);

    /// <summary>Reads a column of the current row as bytes.</summary>
    /// <remarks>
    /// <b>A zero-length blob and SQL <c>NULL</c> both answer with a null
    /// pointer</b>, so the type is asked first and the pointer is never the
    /// discriminator.
    /// </remarks>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The bytes, or <see langword="null"/> for SQL <c>NULL</c>.</returns>
    public byte[]? BlobAt(int index)
    {
        if (IsNullAt(index))
        {
            return null;
        }

        var pointer = Sqlite.ColumnBlob(_handle, index);
        var length = Sqlite.ColumnBytes(_handle, index);

        if (pointer == IntPtr.Zero || length <= 0)
        {
            return [];
        }

        var bytes = new byte[length];

        Marshal.Copy(pointer, bytes, 0, length);

        return bytes;
    }

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();

    /// <summary>Turns a bind result into either this statement or an exception.</summary>
    /// <param name="result">What the bind answered.</param>
    /// <param name="index">Which parameter it was, for the message.</param>
    /// <returns>This statement.</returns>
    /// <exception cref="SqliteException">The bind failed.</exception>
    private SqliteStatement Checked(int result, int index)
    {
        if (result is not Sqlite.Ok)
        {
            throw _database.Failure(result, $"binding parameter {index} of \"{_sql}\"");
        }

        return this;
    }
}
