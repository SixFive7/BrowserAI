// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Proxy;
using BrowserAI.Relay;
using BrowserAI.Updates;

namespace BrowserAI.Clients;

/// <summary>Where the background found which conversation a relay serves: the one thing a log record may say about it.</summary>
internal enum ConversationSource
{
    /// <summary>Nowhere: the client's records named none, or the client is not one whose records BrowserAI reads.</summary>
    None,

    /// <summary>Claude Code's file for its process, <c>&lt;config&gt;\sessions\&lt;pid&gt;.json</c>.</summary>
    ProcessFile,

    /// <summary><c>CLAUDE_CODE_SESSION_ID</c>, as the relay found it, which goes stale at <c>/clear</c>.</summary>
    Environment,

    /// <summary><c>--resume</c> or <c>--session-id</c> on the client's command line.</summary>
    CommandLine,

    /// <summary>The <c>threadId</c> of Codex's first tool call carrying one.</summary>
    CodexCall,
}

/// <summary>Where the background found what the conversation is called: the other thing a log record may say about it.</summary>
internal enum NameSource
{
    /// <summary>Not named: the client is not one whose records BrowserAI reads.</summary>
    None,

    /// <summary>The title the person gave it.</summary>
    CustomTitle,

    /// <summary>The title the model gave it.</summary>
    AiTitle,

    /// <summary>The last prompt Claude Code recorded.</summary>
    LastPrompt,

    /// <summary>A summary Claude Code wrote.</summary>
    Summary,

    /// <summary>The first real prompt.</summary>
    FirstPrompt,

    /// <summary>Codex's index of named threads.</summary>
    CodexIndex,

    /// <summary>The thread's first message, from the <c>threads</c> table of Codex's state database.</summary>
    CodexState,

    /// <summary>The thread's first message, from the start of its rollout.</summary>
    CodexRollout,

    /// <summary>BrowserAI's words for a conversation with no record yet: <i>unnamed conversation in &lt;folder&gt;</i>, since 2026-10-10 (previously <i>new conversation in &lt;folder&gt;</i>).</summary>
    NoRecordYet,

    /// <summary>BrowserAI's words for a conversation it cannot tell: <i>Claude Code in &lt;folder&gt;</i> or <i>Codex in &lt;folder&gt;</i>.</summary>
    ClientAndFolder,
}

/// <summary>Which conversation a relay serves, and what the person sees it called, as one read found them.</summary>
/// <param name="Conversation">Claude Code's session id or Codex's thread id, or <see langword="null"/>.</param>
/// <param name="Name">What the person sees it called, or <see langword="null"/> for a client whose records BrowserAI does not read.</param>
/// <param name="Source">Where the conversation was found.</param>
/// <param name="NameSource">Where its name was found.</param>
internal sealed record ConversationReading(string? Conversation, ConversationName? Name, ConversationSource Source, NameSource NameSource)
{
    /// <summary>Nothing read: a client whose records BrowserAI does not read.</summary>
    public static ConversationReading Unread { get; } = new(null, null, ConversationSource.None, NameSource.None);
}

/// <summary>What the reader keeps of one relay between two reads.</summary>
/// <remarks>
/// <b>Where the session's record was found, and nothing of what it says</b>: finding it
/// can mean looking under every project folder, and the place does not move while the
/// session is the same. The title is read again at every draw, which is the point of
/// 1.3 c. <i>Added later on 2026-10-10</i>: for a Codex relay it keeps, the same way,
/// where the thread's rollout is, which can mean looking under every day Codex has kept.
/// </remarks>
internal sealed class ConversationMemo
{
    private readonly Lock _gate = new();
    private string? _sessionId;
    private string? _record;
    private (ConversationSource Source, NameSource Name)? _told;

    /// <summary>Where a session's record was found last, when it is the session last asked about.</summary>
    /// <param name="sessionId">The session.</param>
    /// <returns>The record's path, or <see langword="null"/>.</returns>
    public string? RecordOf(string sessionId)
    {
        lock (_gate)
        {
            return string.Equals(_sessionId, sessionId, StringComparison.OrdinalIgnoreCase) ? _record : null;
        }
    }

    /// <summary>Remembers where a session's record is.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="record">Its record.</param>
    public void Remember(string sessionId, string record)
    {
        lock (_gate)
        {
            _sessionId = sessionId;
            _record = record;
        }
    }

    /// <summary>Whether the sources differ from the ones last said, remembering these as said.</summary>
    /// <param name="source">Where the conversation was found.</param>
    /// <param name="name">Where its name was found.</param>
    /// <returns><see langword="true"/> the first time, and each time either moves.</returns>
    public bool Moved(ConversationSource source, NameSource name)
    {
        lock (_gate)
        {
            if (_told == (source, name))
            {
                return false;
            }

            _told = (source, name);
            return true;
        }
    }
}

/// <summary>
/// Which conversation a relay serves and what the person sees it called, read from the
/// client's own records when the dashboard or a toast is drawn.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all your
/// recommendations"</i></b>, over the measurement of 2026-10-08
/// (<see href="../../../kb/mcp/protocol.md">kb/mcp/protocol.md</see>):
/// </para>
/// <list type="bullet">
/// <item><b>1.1 c, Claude Code's conversation</b>: its file for the client's process,
/// accepted only when its <c>procStart</c> is the client's creation time; then
/// <c>CLAUDE_CODE_SESSION_ID</c>, which goes stale at <c>/clear</c>; then
/// <c>--resume</c> or <c>--session-id</c> on the client's command line; then <i>Claude
/// Code in &lt;folder&gt;</i>. The file named 21 of 21 emulated tabs and 18 of 18
/// terminal runs right at the end; the variable named the previous conversation after
/// every <c>/clear</c>, 6 of 6.</item>
/// <item><b>1.2 a, its name</b>: the VS Code extension's own title rule over the first and
/// the last 64 KB of the session's record (<see cref="ClaudeCodeTitle"/>); a session with
/// no record yet is <i>unnamed conversation in &lt;folder&gt;</i>, since the on-screen check
/// of 2026-10-10 (previously <i>new conversation in &lt;folder&gt;</i>).</item>
/// <item><b>1.3 c, when</b>: here, at every draw, and never held open
/// (<see cref="ClientRecordFile"/>).</item>
/// <item><b>1.4 a, Codex</b>: the thread of its first tool call named from Codex's index
/// (<see cref="CodexThreads"/>), else <i>Codex in &lt;folder&gt;</i>, which is also what a
/// Codex relay is called before its first call. <i>Corrected later on 2026-10-10
/// (previously "else <i>Codex in &lt;folder&gt;</i>")</i>: 1.4 a as the maintainer
/// approved it reads the thread's first message between the two, from Codex's state
/// database (<see cref="CodexState"/>) or else its rollout, on one line and cut as a
/// Claude Code first prompt is.</item>
/// </list>
/// <para>
/// <b>It never throws, and it never stops anything</b>: every file it reads is another
/// product's undocumented record, so each read that fails is the next fallback.
/// </para>
/// </remarks>
/// <param name="access">How the files are reached.</param>
internal sealed class ConversationReader(ClientRecordAccess access)
{
    /// <summary>The product's reader, over the files themselves.</summary>
    public static ConversationReader Files { get; } = new(ClientRecordFile.Files);

    /// <summary>Reads which conversation a relay serves and what it is called, now.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>.</param>
    /// <param name="facts">Where its relay said the client keeps its records, or <see langword="null"/>.</param>
    /// <param name="clientPid">The client's pid, as the relay's greeting named it, or <see langword="null"/>.</param>
    /// <param name="threadId">The thread of Codex's first tool call carrying one, or <see langword="null"/>.</param>
    /// <param name="folder">The folder the client runs in, or <see langword="null"/>.</param>
    /// <param name="memo">What this reader kept of the relay since its last read.</param>
    /// <returns>The reading; <see cref="ConversationReading.Unread"/> for a client whose records BrowserAI does not read.</returns>
    public ConversationReading Read(string? clientName, ConversationFacts? facts, int? clientPid, string? threadId, string? folder, ConversationMemo memo)
    {
        ArgumentNullException.ThrowIfNull(memo);

        try
        {
            return KnownClients.Matches(clientName, KnownClients.Codex) ? Codex(facts, threadId, folder, memo)
                : KnownClients.Matches(clientName, KnownClients.ClaudeCode) ? ClaudeCode(facts, clientPid, folder, memo)
                : ConversationReading.Unread;
        }
#pragma warning disable CA1031 // Another product's records: whatever reading them throws is a name not read, never a draw that fails.
        catch (Exception)
#pragma warning restore CA1031
        {
            return KnownClients.Matches(clientName, KnownClients.Codex) ? new(null, ClientFolder.Unnamed("Codex", folder), ConversationSource.None, NameSource.ClientAndFolder)
                : KnownClients.Matches(clientName, KnownClients.ClaudeCode) ? new(null, ClientFolder.Unnamed("Claude Code", folder), ConversationSource.None, NameSource.ClientAndFolder)
                : ConversationReading.Unread;
        }
    }

    private ConversationReading Codex(ConversationFacts? facts, string? threadId, string? folder, ConversationMemo memo)
    {
        if (SessionIds.Valid(threadId) is not { } thread)
        {
            return new(null, ClientFolder.Unnamed("Codex", folder), ConversationSource.None, NameSource.ClientAndFolder);
        }

        if (facts?.CodexHome is { Length: > 0 } home)
        {
            if (CodexThreads.NameOf(home, thread, access.ReadAll) is { } name && ConversationName.Titled(name) is { } titled)
            {
                return new(thread, titled, ConversationSource.CodexCall, NameSource.CodexIndex);
            }

            // 1.4 a's middle step: the thread's first message, from Codex's state
            // database where there is one, and else from the thread's rollout.
            if (access.StateTitle(Path.Combine(home, CodexState.FileName), thread) is { } title && AsFirstMessage(title) is { } stated)
            {
                return new(thread, stated, ConversationSource.CodexCall, NameSource.CodexState);
            }

            if (FirstMessageOf(home, thread, memo) is { } message && AsFirstMessage(message) is { } rolled)
            {
                return new(thread, rolled, ConversationSource.CodexCall, NameSource.CodexRollout);
            }
        }

        return new(thread, ClientFolder.Unnamed("Codex", folder), ConversationSource.CodexCall, NameSource.ClientAndFolder);
    }

    /// <summary>A thread's first message from its rollout, which is looked for once and read at every draw.</summary>
    private string? FirstMessageOf(string home, string thread, ConversationMemo memo)
    {
        var rollout = memo.RecordOf(thread) ?? CodexThreads.RolloutOf(home, thread, access.FilesUnder);

        if (rollout is null)
        {
            return null;
        }

        memo.Remember(thread, rollout);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (access.ReadHead(rollout, CodexThreads.RolloutHead) is not { } head)
            {
                return null;
            }

            var (message, torn) = CodexThreads.FirstMessage(head);

            if (message is not null || !torn)
            {
                return message;
            }
        }

        return null;
    }

    /// <summary>A first message as a title: on one line, and cut as the extension cuts a first prompt.</summary>
    private static ConversationName? AsFirstMessage(string text) =>
        ConversationName.Titled(text) is { } line ? line with { Text = ClaudeCodeTitle.CutPrompt(line.Text) } : null;

    private ConversationReading ClaudeCode(ConversationFacts? facts, int? clientPid, string? folder, ConversationMemo memo)
    {
        if (facts?.ClaudeConfig is not { Length: > 0 } config)
        {
            return new(null, ClientFolder.Unnamed("Claude Code", folder), ConversationSource.None, NameSource.ClientAndFolder);
        }

        var projectFolder = folder;
        string? sessionId;
        ConversationSource source;

        if (clientPid is { } pid && facts.ClientStarted is { } started
            && ClaudeCodeRecords.FromProcessFile(config, pid, started, access.ReadAll) is { } file)
        {
            (sessionId, source) = (file.SessionId, ConversationSource.ProcessFile);
            projectFolder = file.Folder ?? folder;
        }
        else if (SessionIds.Valid(facts.SessionId) is { } environment)
        {
            (sessionId, source) = (environment, ConversationSource.Environment);
        }
        else if (SessionIds.Valid(facts.CommandLineSessionId) is { } commandLine)
        {
            (sessionId, source) = (commandLine, ConversationSource.CommandLine);
        }
        else
        {
            return new(null, ClientFolder.Unnamed("Claude Code", folder), ConversationSource.None, NameSource.ClientAndFolder);
        }

        var record = memo.RecordOf(sessionId) ?? ClaudeCodeRecords.RecordOf(config, sessionId, projectFolder, access.FoldersIn);

        if (record is not null)
        {
            memo.Remember(sessionId, record);
        }

        return record is not null
            && access.ReadEnds(record, ClaudeCodeTitle.Window) is { } ends
            && ClaudeCodeTitle.Of(ends.Head, ends.Tail) is { } title
            && ConversationName.Titled(title.Title) is { } titled
                ? new(sessionId, titled, source, NameSourceOf(title.Source))
                : new(sessionId, ClientFolder.Unnamed(ClientFolder.UnnamedConversation, folder), source, NameSource.NoRecordYet);
    }

    private static NameSource NameSourceOf(TitleSource source) => source switch
    {
        TitleSource.CustomTitle => NameSource.CustomTitle,
        TitleSource.AiTitle => NameSource.AiTitle,
        TitleSource.LastPrompt => NameSource.LastPrompt,
        TitleSource.Summary => NameSource.Summary,
        _ => NameSource.FirstPrompt,
    };
}
