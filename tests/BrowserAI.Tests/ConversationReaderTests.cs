// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Clients;
using BrowserAI.Relay;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// Which conversation a relay serves and what the person sees it called, read by the
/// background from a client's own records, every one of them written by the suite in
/// scratch.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all your
/// recommendations"</i></b>: for Claude Code, its file per process when its
/// <c>procStart</c> is the client's, then <c>CLAUDE_CODE_SESSION_ID</c>, then a session
/// on the command line, then <i>Claude Code in &lt;folder&gt;</i>; the name by the
/// extension's title rule, or <i>new conversation in &lt;folder&gt;</i> with no record
/// yet; for Codex, its first call's thread named from its index, else <i>Codex in
/// &lt;folder&gt;</i>. Each file read is opened sharing read, write and delete, and a
/// parse that fails is tried once more.
/// </para>
/// <para>
/// ⚠️ <b>No arm reads the maintainer's real <c>~\.claude</c> or <c>~\.codex</c></b>: every
/// configuration folder and home here is a scratch directory, and the files in them are
/// written in the shapes the measurement of 2026-10-08 read.
/// </para>
/// </remarks>
internal sealed class ConversationReaderTests
{
    /// <summary>The client every arm's Claude Code process stands for.</summary>
    private const int ClientPid = 4242;

    /// <summary>Its creation time, as Claude Code writes <c>procStart</c>.</summary>
    private const long ClientStarted = 134360637732277608;

    private const string Live = "aaaaaaaa-1111-4111-8111-111111111111";
    private const string Stale = "bbbbbbbb-2222-4222-8222-222222222222";
    private const string Resumed = "cccccccc-3333-4333-8333-333333333333";
    private const string Thread = "dddddddd-4444-7444-8444-444444444444";

    /// <summary>
    /// Claude Code's file for the client's process names the live conversation, past a
    /// stale environment; a file for another process is passed over for the
    /// environment, then the command line, then the client and its folder; and a session
    /// with no record yet is a new conversation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.1 c, in its order</b>: after <c>/clear</c> the variable still names the
    /// previous conversation, 6 of 6 in the measurement, and the file names the new one,
    /// which is why the file comes first. The record here is longer than two windows, its
    /// AI title only in its last 64 KB, so the title is found the way the extension finds
    /// it and nowhere else.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reader that tried the variable before the
    /// file, and against one that took the file without its <c>procStart</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClientsOwnFileComesFirstThenTheEnvironmentThenTheCommandLineThenTheFolder()
    {
        using var scratch = ScratchDirectory.Create("conversation-claude");
        var config = Path.Combine(scratch.Path, "config");
        var folder = Path.Combine(scratch.Path, "projects", "Apples");

        WriteProcessFile(config, ClientPid, ClientStarted, Live, folder);
        WriteRecord(config, folder, Live, padding: 3 * ClaudeCodeTitle.Window, Ai("Apples tab"));
        WriteRecord(config, folder, Stale, padding: 0, Prompt("the conversation before the clear"), Ai("Before the clear"));

        var reader = ConversationReader.Files;
        var memo = new ConversationMemo();

        var live = reader.Read(KnownClaudeCode, Facts(config, sessionId: Stale), ClientPid, threadId: null, folder, memo);

        await Assert.That(live.Conversation).IsEqualTo(Live);
        await Assert.That(live.Name).IsEqualTo(new ConversationName("Apples tab", IsTitle: true));
        await Assert.That(live.Source).IsEqualTo(ConversationSource.ProcessFile);
        await Assert.That(live.NameSource).IsEqualTo(NameSource.AiTitle);

        // Another process's file, by its procStart: the environment answers.
        WriteProcessFile(config, ClientPid, ClientStarted + 1, Live, folder);

        var environment = reader.Read(KnownClaudeCode, Facts(config, sessionId: Stale), ClientPid, threadId: null, folder, new ConversationMemo());

        await Assert.That(environment.Conversation).IsEqualTo(Stale);
        await Assert.That(environment.Name).IsEqualTo(new ConversationName("Before the clear", IsTitle: true));
        await Assert.That(environment.Source).IsEqualTo(ConversationSource.Environment);

        // No variable: the command line; and a session with no record is new.
        var commandLine = reader.Read(KnownClaudeCode, Facts(config, commandLineSessionId: Resumed), ClientPid, threadId: null, folder, new ConversationMemo());

        await Assert.That(commandLine.Conversation).IsEqualTo(Resumed);
        await Assert.That(commandLine.Name).IsEqualTo(new ConversationName("new conversation in Apples", IsTitle: false));
        await Assert.That(commandLine.Source).IsEqualTo(ConversationSource.CommandLine);
        await Assert.That(commandLine.NameSource).IsEqualTo(NameSource.NoRecordYet);

        // Nothing names a session: the client and its folder.
        var nothing = reader.Read(KnownClaudeCode, Facts(config), ClientPid, threadId: null, folder, new ConversationMemo());

        await Assert.That(nothing.Conversation).IsNull();
        await Assert.That(nothing.Name).IsEqualTo(new ConversationName("Claude Code in Apples", IsTitle: false));
        await Assert.That(nothing.Source).IsEqualTo(ConversationSource.None);

        // A client whose records BrowserAI does not read is named nothing at all.
        await Assert.That(reader.Read("some-other-client", Facts(config, sessionId: Stale), ClientPid, threadId: null, folder, new ConversationMemo()))
            .IsEqualTo(ConversationReading.Unread);
    }

    /// <summary>
    /// A conversation is read again at every draw: a rename and a <c>/clear</c> are seen
    /// at the next read, with no message from the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.3 c</b>: the files are read when the dashboard or a toast is drawn, so what the
    /// person sees is current when they look. A rename appends a custom title to the
    /// record; a <c>/clear</c> moves the file's <c>sessionId</c> to a session with no record
    /// yet.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reader whose memory handed back the record
    /// of the session it found first, whatever session it was asked about, which named a
    /// cleared conversation by the one before it; and against one that tried the variable
    /// before the file.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARenameAndAClearAreSeenAtTheNextRead()
    {
        using var scratch = ScratchDirectory.Create("conversation-again");
        var config = Path.Combine(scratch.Path, "config");
        var folder = Path.Combine(scratch.Path, "projects", "Pears");
        var memo = new ConversationMemo();

        WriteProcessFile(config, ClientPid, ClientStarted, Live, folder);
        var record = WriteRecord(config, folder, Live, padding: 0, Prompt("sort the pears"), Ai("Sorting pears"));

        await Assert.That(ConversationReader.Files.Read(KnownClaudeCode, Facts(config, sessionId: Live), ClientPid, null, folder, memo).Name?.Text).IsEqualTo("Sorting pears");

        await File.AppendAllTextAsync(record, Custom("Pears, renamed") + "\n");

        await Assert.That(ConversationReader.Files.Read(KnownClaudeCode, Facts(config, sessionId: Live), ClientPid, null, folder, memo).Name?.Text).IsEqualTo("Pears, renamed");

        WriteProcessFile(config, ClientPid, ClientStarted, Resumed, folder);

        var cleared = ConversationReader.Files.Read(KnownClaudeCode, Facts(config, sessionId: Live), ClientPid, null, folder, memo);

        await Assert.That(cleared.Conversation).IsEqualTo(Resumed);
        await Assert.That(cleared.Name).IsEqualTo(new ConversationName("new conversation in Pears", IsTitle: false));
    }

    /// <summary>
    /// A record whose project folder is not the one the slug names is found under
    /// another project folder; and the slug is the folder's name with every character
    /// that is not a letter or a digit made a hyphen, cut and hashed past 200.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The slug is Claude Code's rule</b>, read in the extension at 2.1.296, and the
    /// measurement's own record sat in <c>C--Source-SixFive7-BrowserAI--work-client-tabs-proj-w2</c>
    /// for <c>C:\Source\SixFive7\BrowserAI\.work\client-tabs\proj\w2</c>. A rule that moves
    /// costs a look under every project folder and no name.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reader that looked only where the slug
    /// pointed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARecordIsFoundUnderAnotherProjectFolderWhenTheSlugNamesNone()
    {
        await Assert.That(ClaudeCodeRecords.Slug(@"C:\Source\SixFive7\BrowserAI\.work\client-tabs\proj\w2")).IsEqualTo("C--Source-SixFive7-BrowserAI--work-client-tabs-proj-w2");

        var deep = @"C:\" + string.Join(@"\", Enumerable.Repeat("a-long-folder-name", 14));
        var slug = ClaudeCodeRecords.Slug(deep);

        await Assert.That(slug.Length).IsGreaterThan(ClaudeCodeRecords.SlugWidth);
        await Assert.That(slug[..ClaudeCodeRecords.SlugWidth]).IsEqualTo(deep.Replace(':', '-').Replace('\\', '-')[..ClaudeCodeRecords.SlugWidth]);
        await Assert.That(slug[ClaudeCodeRecords.SlugWidth]).IsEqualTo('-');

        using var scratch = ScratchDirectory.Create("conversation-scan");
        var config = Path.Combine(scratch.Path, "config");
        var folder = Path.Combine(scratch.Path, "projects", "Plums");
        var elsewhere = Path.Combine(config, "projects", "a-folder-the-slug-does-not-name");

        WriteProcessFile(config, ClientPid, ClientStarted, Live, folder);
        _ = Directory.CreateDirectory(elsewhere);
        await File.WriteAllTextAsync(Path.Combine(elsewhere, Live + ".jsonl"), Lines(Prompt("pick the plums"), Ai("Plum picking")));

        var found = ConversationReader.Files.Read(KnownClaudeCode, Facts(config), ClientPid, null, folder, new ConversationMemo());

        await Assert.That(found.Name).IsEqualTo(new ConversationName("Plum picking", IsTitle: true));
    }

    /// <summary>
    /// A per-process file met half written is read once more; met half written twice,
    /// it is passed over for the next source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.3 c: retry a failed parse once.</b> Claude Code rewrites the file at every
    /// change of its status, so a read can meet it torn; none did in the 54 servers of
    /// the measurement, which polled it once a second.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reader that gave up at the first torn read,
    /// and against one that read until it parsed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHalfWrittenFileIsReadOnceMoreAndThenPassedOver()
    {
        using var scratch = ScratchDirectory.Create("conversation-torn");
        var config = Path.Combine(scratch.Path, "config");
        var folder = Path.Combine(scratch.Path, "projects", "Figs");

        WriteProcessFile(config, ClientPid, ClientStarted, Live, folder);
        _ = WriteRecord(config, folder, Live, padding: 0, Ai("Fig tab"));
        _ = WriteRecord(config, folder, Stale, padding: 0, Ai("Stale fig tab"));

        var tornOnce = new TornReads(tears: 1);
        var once = new ConversationReader(tornOnce.Access).Read(KnownClaudeCode, Facts(config, sessionId: Stale), ClientPid, null, folder, new ConversationMemo());

        await Assert.That(once.Source).IsEqualTo(ConversationSource.ProcessFile);
        await Assert.That(once.Name?.Text).IsEqualTo("Fig tab");
        await Assert.That(tornOnce.ProcessFileReads).IsEqualTo(2);

        var tornTwice = new TornReads(tears: 2);
        var twice = new ConversationReader(tornTwice.Access).Read(KnownClaudeCode, Facts(config, sessionId: Stale), ClientPid, null, folder, new ConversationMemo());

        await Assert.That(twice.Source).IsEqualTo(ConversationSource.Environment);
        await Assert.That(twice.Name?.Text).IsEqualTo("Stale fig tab");
        await Assert.That(tornTwice.ProcessFileReads).IsEqualTo(2).Because("a torn file was read more than twice");
    }

    /// <summary>
    /// A file the client holds open to write and to delete is still read, and the read
    /// leaves nothing open behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.3 c: open read-only, sharing read, write and delete, and never hold.</b> A
    /// handle that may delete the file refuses every open that does not share delete,
    /// and Claude Code deletes its per-process file when it exits. The client here holds
    /// both files with write and delete access; after the read, the file is deleted by
    /// closing that handle, which a handle the reader kept would stop.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reader that shared read and write and not
    /// delete.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFileTheClientHoldsToWriteAndToDeleteIsStillReadAndLeftAlone()
    {
        using var scratch = ScratchDirectory.Create("conversation-held");
        var config = Path.Combine(scratch.Path, "config");
        var folder = Path.Combine(scratch.Path, "projects", "Kiwis");

        var processFile = WriteProcessFile(config, ClientPid, ClientStarted, Live, folder);
        var record = WriteRecord(config, folder, Live, padding: 0, Prompt("weigh the kiwis"), Ai("Kiwi scales"));

        ConversationReading read;

        using (HeldLikeTheClient(processFile))
        using (HeldLikeTheClient(record))
        {
            read = ConversationReader.Files.Read(KnownClaudeCode, Facts(config), ClientPid, null, folder, new ConversationMemo());
        }

        await Assert.That(read.Source).IsEqualTo(ConversationSource.ProcessFile);
        await Assert.That(read.Name?.Text).IsEqualTo("Kiwi scales");

        // The client's handles deleted both files as they closed, and a name is free to
        // be written again only once every handle on it has gone: none of the reader's
        // was left open.
        await File.WriteAllTextAsync(processFile, "{}");
        await File.WriteAllTextAsync(record, "{}");
    }

    /// <summary>
    /// A Codex conversation is its first call's thread, named from Codex's index with the
    /// last name it was given; a thread the index does not name, or no thread yet, is
    /// <i>Codex in &lt;folder&gt;</i>; and an index half appended is read once more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.4 a</b>: Codex appends <c>{"id":...,"thread_name":...,"updated_at":...}</c> to
    /// <c>session_index.jsonl</c> each time a thread is named, 3 of 3 in the measurement.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reader that kept the first name an index
    /// gave a thread.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACodexThreadIsNamedFromCodexsIndexByTheLastNameItWasGiven()
    {
        using var scratch = ScratchDirectory.Create("conversation-codex");
        var home = Path.Combine(scratch.Path, "codex-home");
        var folder = Path.Combine(scratch.Path, "projects", "Lemons");
        var index = Path.Combine(home, "session_index.jsonl");
        var facts = new ConversationFacts(null, null, null, null, home);

        _ = Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(index, Lines(
            Indexed("ffffffff-6666-7666-8666-666666666666", "Another thread"),
            Indexed(Thread, "Lemon list"),
            Indexed(Thread, "Lemon list, renamed")));

        var named = ConversationReader.Files.Read(KnownCodex, facts, clientPid: null, Thread, folder, new ConversationMemo());

        await Assert.That(named.Conversation).IsEqualTo(Thread);
        await Assert.That(named.Name).IsEqualTo(new ConversationName("Lemon list, renamed", IsTitle: true));
        await Assert.That(named.Source).IsEqualTo(ConversationSource.CodexCall);
        await Assert.That(named.NameSource).IsEqualTo(NameSource.CodexIndex);

        // A thread the index does not name.
        var unnamed = ConversationReader.Files.Read(KnownCodex, facts, null, "99999999-9999-7999-8999-999999999999", folder, new ConversationMemo());

        await Assert.That(unnamed.Name).IsEqualTo(new ConversationName("Codex in Lemons", IsTitle: false));
        await Assert.That(unnamed.Source).IsEqualTo(ConversationSource.CodexCall);

        // Before its first call there is no thread.
        var before = ConversationReader.Files.Read(KnownCodex, facts, null, threadId: null, folder, new ConversationMemo());

        await Assert.That(before.Conversation).IsNull();
        await Assert.That(before.Name).IsEqualTo(new ConversationName("Codex in Lemons", IsTitle: false));

        // The last line half appended: read once more, and the whole index answers.
        var whole = await File.ReadAllTextAsync(index);
        var halfAppended = whole + Indexed(Thread, "Lemon list, renamed again")[..30];
        var reads = 0;
        var access = new ClientRecordAccess(
            path => path == index && reads++ is 0 ? halfAppended : ClientRecordFile.ReadAll(path),
            ClientRecordFile.ReadEnds,
            ClientRecordFile.FoldersIn);

        await File.AppendAllTextAsync(index, Indexed(Thread, "Lemon list, renamed again") + "\n");

        var again = new ConversationReader(access).Read(KnownCodex, facts, null, Thread, folder, new ConversationMemo());

        await Assert.That(again.Name?.Text).IsEqualTo("Lemon list, renamed again");
        await Assert.That(reads).IsEqualTo(2);
    }

    /// <summary>A value that is not a session id is never taken for one, so it never becomes part of a path.</summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a reader that took the environment's value as
    /// it came, which looked for <c>..\..\</c> under the projects folder.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AValueThatIsNotASessionIdIsNeverTakenForOne()
    {
        using var scratch = ScratchDirectory.Create("conversation-shape");
        var config = Path.Combine(scratch.Path, "config");
        var folder = Path.Combine(scratch.Path, "projects", "Dates");

        // A record where a path climbing out of the projects folder would land.
        await File.WriteAllTextAsync(Path.Combine(scratch.Path, "outside.jsonl"), Lines(Ai("Read from outside the projects folder")));

        var read = ConversationReader.Files.Read(
            KnownClaudeCode,
            new ConversationFacts(config, ClientStarted, @"..\..\outside", "not-a-session-id", null),
            ClientPid,
            null,
            folder,
            new ConversationMemo());

        await Assert.That(read.Conversation).IsNull();
        await Assert.That(read.Name).IsEqualTo(new ConversationName("Claude Code in Dates", IsTitle: false));

        await Assert.That(SessionIds.Valid(Live)).IsEqualTo(Live);
        await Assert.That(SessionIds.Valid(Live.ToUpperInvariant())).IsEqualTo(Live.ToUpperInvariant());
        await Assert.That(SessionIds.Valid(Live[..35] + "g")).IsNull();
        await Assert.That(SessionIds.Valid(Live.Replace('-', '_'))).IsNull();
    }

    private const string KnownClaudeCode = "claude-code";
    private const string KnownCodex = "codex-mcp-client";

    private static ConversationFacts Facts(string config, string? sessionId = null, string? commandLineSessionId = null) =>
        new(config, ClientStarted, sessionId, commandLineSessionId, null);

    /// <summary>Writes Claude Code's file for one process, in the shape 2.1.296 writes it.</summary>
    private static string WriteProcessFile(string config, int pid, long procStart, string sessionId, string folder)
    {
        var sessions = Path.Combine(config, "sessions");
        _ = Directory.CreateDirectory(sessions);

        var path = Path.Combine(sessions, pid.ToString(CultureInfo.InvariantCulture) + ".json");

        File.WriteAllText(path, new JsonObject
        {
            ["pid"] = pid,
            ["sessionId"] = sessionId,
            ["cwd"] = folder,
            ["startedAt"] = 1791499016887,
            ["procStart"] = procStart.ToString(CultureInfo.InvariantCulture),
            ["version"] = "2.1.296",
            ["kind"] = "interactive",
            ["entrypoint"] = "claude-vscode",
            ["name"] = "derived-0c",
            ["nameSource"] = "derived",
            ["status"] = "idle",
        }.ToJsonString());

        return path;
    }

    /// <summary>Writes a session's record where the slug of its folder puts it, padded in the middle.</summary>
    private static string WriteRecord(string config, string folder, string sessionId, int padding, params string[] lines)
    {
        var project = Path.Combine(config, "projects", ClaudeCodeRecords.Slug(folder));
        _ = Directory.CreateDirectory(project);

        var path = Path.Combine(project, sessionId + ".jsonl");
        var text = new StringBuilder(Lines(Prompt("the first prompt of " + sessionId[..8])));

        // Padding: assistant turns that carry no field the rule reads.
        var turn = Line(new JsonObject { ["type"] = "assistant", ["message"] = new JsonObject { ["content"] = new string('x', 900) } }) + "\n";

        while (text.Length < padding)
        {
            _ = text.Append(turn);
        }

        _ = text.Append(Lines(lines));
        File.WriteAllText(path, text.ToString());

        return path;
    }

    /// <summary>A handle on a file the way Claude Code holds one: writing, and deleting it when it closes.</summary>
    private static FileStream HeldLikeTheClient(string path) =>
        new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.DeleteOnClose);

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string Line(JsonObject record) => record.ToJsonString();

    private static string Prompt(string text) =>
        Line(new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = text } });

    private static string Ai(string title) => Line(new JsonObject { ["type"] = "ai-title", ["aiTitle"] = title, ["sessionId"] = Live });

    private static string Custom(string title) => Line(new JsonObject { ["type"] = "custom-title", ["customTitle"] = title, ["sessionId"] = Live });

    private static string Indexed(string id, string name) =>
        Line(new JsonObject { ["id"] = id, ["thread_name"] = name, ["updated_at"] = "2026-10-08T22:49:45.7084484Z" });

    /// <summary>The product's reads, except that the per-process file reads half written a given number of times.</summary>
    /// <param name="tears">How many reads of the per-process file come back torn.</param>
    private sealed class TornReads(int tears)
    {
        public int ProcessFileReads { get; private set; }

        public ClientRecordAccess Access => new(
            path =>
            {
                var whole = ClientRecordFile.ReadAll(path);

                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || whole is null)
                {
                    return whole;
                }

                return ++ProcessFileReads <= tears ? whole[..(whole.Length / 2)] : whole;
            },
            ClientRecordFile.ReadEnds,
            ClientRecordFile.FoldersIn);
    }
}
