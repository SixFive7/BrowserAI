// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Storage;

/// <summary>
/// A SQLite call that did not answer <c>SQLITE_OK</c>, carrying the code and
/// whatever the library was able to say about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The code is kept beside the message because callers act on it and readers
/// act on the message.</b> <c>SQLITE_BUSY</c> is a thing to wait out,
/// <c>SQLITE_MISUSE</c> is a defect in this layer, <c>SQLITE_NOTADB</c> is a
/// file that is not ours — three different answers a caller has to be able to
/// tell apart without reading English.
/// </para>
/// <para>
/// <b>Three constructors because the analyzers require them and nothing here
/// uses two of them.</b> That is the framework's exception contract, not a
/// design; the one this layer throws is
/// <see cref="SqliteException(int, string)"/>.
/// </para>
/// </remarks>
internal sealed class SqliteException : Exception
{
    /// <summary>Creates an exception with no result code and no message of its own.</summary>
    public SqliteException()
    {
    }

    /// <summary>Creates an exception with a message and no result code.</summary>
    /// <param name="message">What went wrong.</param>
    public SqliteException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception wrapping another.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What it was wrapping.</param>
    public SqliteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception naming the result code SQLite answered with.</summary>
    /// <param name="result">The <c>SQLITE_*</c> result code.</param>
    /// <param name="message">What went wrong, in a sentence a reader can act on.</param>
    public SqliteException(int result, string message)
        : base(message) => Result = result;

    /// <summary>
    /// The <c>SQLITE_*</c> result code, or <see cref="Sqlite.Ok"/> when this
    /// exception did not come from a result code at all.
    /// </summary>
    public int Result { get; }
}
