// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Storage;

namespace BrowserAI.Clients;

/// <summary>
/// The first message of a Codex thread, from the <c>threads</c> table of Codex's state
/// database, read the way any second reader of a SQLite database reads one and never so
/// that a file appears in Codex's home.
/// </summary>
/// <remarks>
/// <para>
/// <b>1.4 a, as the maintainer approved it on 2026-10-10</b>: a thread Codex's index does
/// not name is called by its first message, and only then <i>Codex in &lt;folder&gt;</i>.
/// In the scratch homes of the measurement of 2026-10-08, Codex 0.162.0,
/// <c>state_5.sqlite</c> has a <c>threads</c> table whose <c>title</c> is the thread's
/// first message: 15 of 15 that day, and 18 of 18 on 2026-10-10 against each rollout's
/// first <c>UserMessage</c>. The desktop app's real home at 0.159 alpha has no such file,
/// which is why the rollout is read next (<see cref="CodexThreads.FirstMessage"/>).
/// </para>
/// <para>
/// ⚠️ <b>A read-only open can still put files in Codex's home</b>, measured 2026-10-10
/// with SQLite 3.50.4 and held by the suite at 3.53.4, the version this build vendors: a
/// database in write-ahead-log mode with no <c>-wal</c> beside it, which is how a
/// database is left when its last connection closed cleanly, gains a <c>-wal</c> and a
/// <c>-shm</c> from a read-only open, and keeps both after it closes. So the files beside
/// the database decide the open, and none of the three creates one:
/// </para>
/// <list type="bullet">
/// <item><b>No <c>-wal</c></b>: nothing waits in a log, so the database is opened with
/// <c>immutable=1</c>, which takes no lock and opens no other file.</item>
/// <item><b>A <c>-wal</c> and a <c>-shm</c></b>: Codex has the database open, or left it
/// so. A read-only open reads the log the way a second reader does, mapping the
/// <c>-shm</c> and marking its place there, and never writes the database or the log.
/// <c>immutable=1</c> would read the database without its log: in three of the six
/// scratch homes every row is still in the log, and the database alone has no
/// <c>threads</c> table.</item>
/// <item><b>A <c>-wal</c> and no <c>-shm</c></b>: an open would create the <c>-shm</c>,
/// so the database is not opened.</item>
/// </list>
/// <para>
/// <b>Never held</b>: one statement, then closed. SQLite's Windows layer opens the file
/// sharing read and write and not delete (<c>winOpen</c> in the amalgamation this build
/// vendors, 3.53.4), so for that one statement Codex could not delete or rename it. A
/// read that fails is tried once more, and a second failure is a thread with no first
/// message here.
/// </para>
/// </remarks>
internal static class CodexState
{
    /// <summary>The state database's name in Codex's home, as 0.162.0 names it.</summary>
    public const string FileName = "state_5.sqlite";

    /// <summary>The thread's first message, when Codex's state database has the thread.</summary>
    /// <param name="database">The database.</param>
    /// <param name="threadId">The thread.</param>
    /// <returns>Its <c>title</c>; <see langword="null"/> when there is no database, no such thread, or no read.</returns>
    public static string? TitleOf(string database, string threadId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(threadId);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!File.Exists(database))
            {
                return null;
            }

            var logged = File.Exists(database + "-wal");

            if (logged && !File.Exists(database + "-shm"))
            {
                return null;
            }

            try
            {
                using var connection = logged
                    ? SqliteDatabase.Open(database, Sqlite.OpenReadOnly)
                    : SqliteDatabase.Open(Immutable(database), Sqlite.OpenReadOnly | Sqlite.OpenUri);
                using var statement = connection.Prepare("SELECT title FROM threads WHERE id = ?1");

                return statement.BindText(1, threadId).Step() ? statement.TextAt(0) : null;
            }
            catch (SqliteException)
            {
                // Busy, torn or not Codex's: the loop tries once more, then gives up.
            }
        }

        return null;
    }

    /// <summary>A path as a <c>file:</c> URI that opens the database immutable.</summary>
    /// <remarks>
    /// Every byte of the path's UTF-8 outside letters, digits and <c>-._~/:</c> is
    /// percent-encoded, so a <c>?</c>, <c>#</c> or <c>%</c> in a folder's name is part of
    /// the path and never the URI's query.
    /// </remarks>
    /// <param name="path">The database.</param>
    /// <returns>The URI.</returns>
    internal static string Immutable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var slashed = path.Replace('\\', '/');
        var uri = new StringBuilder("file://", slashed.Length + 32);

        if (!slashed.StartsWith('/'))
        {
            _ = uri.Append('/');
        }

        foreach (var value in Encoding.UTF8.GetBytes(slashed))
        {
            _ = value is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')
                or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~' or (byte)'/' or (byte)':'
                ? uri.Append((char)value)
                : uri.Append('%').Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return uri.Append("?immutable=1").ToString();
    }
}
