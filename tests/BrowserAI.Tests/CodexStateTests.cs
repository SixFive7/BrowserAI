// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Clients;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A Codex thread's first message, read from the <c>threads</c> table of Codex's state
/// database the way a second reader reads one, and never so that a file appears in
/// Codex's home, every database here written by the suite in scratch.
/// </summary>
/// <remarks>
/// <para>
/// <b>1.4 a as the maintainer approved it on 2026-10-10</b> names a thread by its first
/// message when Codex's index does not name it, and the <c>title</c> of the thread's row
/// in <c>state_5.sqlite</c> is that message, 18 of 18 in the scratch homes of the
/// measurement of 2026-10-08, Codex 0.162.0.
/// </para>
/// <para>
/// ⚠️ <b>No arm reads the maintainer's real <c>~\.codex</c></b>; the databases are the
/// suite's own, in the columns of 0.162.0's this reads.
/// </para>
/// </remarks>
internal sealed class CodexStateTests
{
    private const string Thread = "dddddddd-4444-7444-8444-444444444444";

    /// <summary>
    /// A database closed cleanly is read without a lock or a file beside it; one a writer
    /// holds is read through its log, as a second reader reads it; and a log with no
    /// <c>-shm</c> beside it is not opened, because opening it would create one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The premise, measured 2026-10-10 with SQLite 3.50.4: a read-only open of a
    /// database in write-ahead-log mode whose log was checkpointed away creates a
    /// <c>-wal</c> and a <c>-shm</c> and leaves both</b>, and an <c>immutable=1</c> open
    /// reads the database without its log. The first folder's name carries a <c>#</c>, a
    /// <c>%</c>, a space and a letter outside ASCII, the characters a <c>file:</c> URI
    /// would otherwise read as its own.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b>, at SQLite 3.53.4, the version this build vendors and
    /// the suite loads: a reader that opened read-only whatever lay beside the database
    /// left <i>state_5.sqlite, state_5.sqlite-shm, state_5.sqlite-wal</i> in the first
    /// folder, which is the premise seen again; one that always opened it immutable read
    /// no title from the writer's log; one that opened a log with no <c>-shm</c> read the
    /// title out of it; and a URI whose path was not encoded opened nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task CodexsStateIsReadAsASecondReaderReadsItAndNoFileAppearsBesideIt()
    {
        using var scratch = ScratchDirectory.Create("codex-state");

        // Closed cleanly: write-ahead-log mode, its log checkpointed away at the close.
        var closed = Path.Combine(scratch.Path, "home #1 100% Zo\u00EB");
        var closedDatabase = Path.Combine(closed, CodexState.FileName);

        _ = Directory.CreateDirectory(closed);

        using (var codex = SqliteDatabase.OpenForWriting(closedDatabase))
        {
            codex.Execute("PRAGMA journal_mode=WAL");
            Create(codex, "Plan the lemon harvest");
        }

        await Assert.That(FilesIn(closed)).IsEqualTo(CodexState.FileName);
        await Assert.That(CodexState.TitleOf(closedDatabase, Thread)).IsEqualTo("Plan the lemon harvest");
        await Assert.That(FilesIn(closed)).IsEqualTo(CodexState.FileName);

        // Held by a writer, every row still in its log.
        var live = Path.Combine(scratch.Path, "live");
        var liveDatabase = Path.Combine(live, CodexState.FileName);

        _ = Directory.CreateDirectory(live);

        using var writer = SqliteDatabase.OpenForWriting(liveDatabase);

        writer.Execute("PRAGMA journal_mode=WAL");
        Create(writer, "Weigh the lemons");

        var held = FilesIn(live);

        await Assert.That(held).IsEqualTo($"{CodexState.FileName}, {CodexState.FileName}-shm, {CodexState.FileName}-wal");
        await Assert.That(CodexState.TitleOf(liveDatabase, Thread)).IsEqualTo("Weigh the lemons");
        await Assert.That(FilesIn(live)).IsEqualTo(held);

        // A log with no -shm beside it, copied while the writer holds both.
        var orphan = Path.Combine(scratch.Path, "log-without-its-index");
        var orphanDatabase = Path.Combine(orphan, CodexState.FileName);

        _ = Directory.CreateDirectory(orphan);
        CopyShared(liveDatabase, orphanDatabase);
        CopyShared(liveDatabase + "-wal", orphanDatabase + "-wal");

        await Assert.That(CodexState.TitleOf(orphanDatabase, Thread)).IsNull();
        await Assert.That(FilesIn(orphan)).IsEqualTo($"{CodexState.FileName}, {CodexState.FileName}-wal");

        // A thread the table does not have, and a home with no database.
        await Assert.That(CodexState.TitleOf(liveDatabase, "99999999-9999-7999-8999-999999999999")).IsNull();
        await Assert.That(CodexState.TitleOf(Path.Combine(scratch.Path, "no-home", CodexState.FileName), Thread)).IsNull();
    }

    /// <summary>
    /// A path becomes a <c>file:</c> URI with every byte that means something in a URI
    /// encoded, a drive's path under three slashes and a share's under four.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> with the encoding taken out, which the arm above also
    /// sees, as a folder it cannot open.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APathIsAFileUriThatKeepsEveryCharacterOfItsName()
    {
        await Assert.That(CodexState.Immutable(@"C:\Users\Zo" + "\u00EB" + @"\.codex #1\100%\state_5.sqlite"))
            .IsEqualTo("file:///C:/Users/Zo%C3%AB/.codex%20%231/100%25/state_5.sqlite?immutable=1");
        await Assert.That(CodexState.Immutable(@"\\server\share\state_5.sqlite"))
            .IsEqualTo("file:////server/share/state_5.sqlite?immutable=1");
    }

    /// <summary>One thread, as Codex's table holds it.</summary>
    private static void Create(SqliteDatabase database, string title)
    {
        database.Execute("CREATE TABLE threads (id TEXT PRIMARY KEY, rollout_path TEXT NOT NULL DEFAULT '', title TEXT NOT NULL, name TEXT)");

        using var insert = database.Prepare("INSERT INTO threads (id, title) VALUES (?1, ?2)");

        insert.BindText(1, Thread).BindText(2, title).Run();
    }

    /// <summary>The names of a folder's files, in ordinal order, joined.</summary>
    private static string FilesIn(string folder) =>
        string.Join(", ", Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));

    /// <summary>A copy of a file a writer holds, read sharing everything.</summary>
    private static void CopyShared(string from, string to)
    {
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        source.CopyTo(target);
    }
}
