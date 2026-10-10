// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Relay;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// What a relay tells the background its client needs once an update has ended the
/// relay: a new conversation for Codex, <c>/mcp</c> and Reconnect for a terminal Claude
/// Code, nothing for one that starts BrowserAI again by itself, and unknown whenever it
/// cannot be told.
/// </summary>
/// <remarks>
/// <para>
/// <b>H1-T a, the maintainer's answer of 2026-10-08</b>, and the measurement's option d
/// beneath it, pending his confirmation: the parent's arguments decide a Claude Code
/// session, then its entrypoint variable when the parent cannot be read, then unknown.
/// Option c, never to guess, is the same rule with the parent unread, which
/// <see cref="ClientRecognition.ReadsTheParent"/> chooses for the product.
/// </para>
/// <para>
/// <b>The command lines are spelled the way Windows hands them over</b>, the program
/// quoted when its path has a space, because the rule skips the program and reads only
/// what follows it.
/// </para>
/// </remarks>
internal sealed class ClientRecognitionTests
{
    /// <summary>
    /// Option d's rules, one row per case the measurement named: Codex in every mode,
    /// Claude Code by its parent's arguments, by its entrypoint when the parent cannot
    /// be read, and every other client unknown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The parent decides over an inherited entrypoint</b>: the variable alone read a
    /// terminal as VS Code when the value was inherited, 3 of 3 such runs, so a terminal
    /// session whose environment says <c>claude-vscode</c> still needs Reconnect.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a rule that read the entrypoint
    /// <c>cli</c> as a client that starts BrowserAI again by itself, and again against
    /// one that answered Codex unknown.
    /// </para>
    /// <para>
    /// <b>And the other seventeen rows planted red 2026-10-10</b>, the maintainer's 8 a of
    /// that day: those two plants turned the three Codex rows and the two rows of an
    /// unread parent with the entrypoint <c>cli</c> red, and no plant had turned the rest.
    /// Ten plants, one at a time, each row red under at least one: a rule with no
    /// <c>-p</c> or <c>--print</c> (the two print rows); one that looked for <c>-p</c>
    /// anywhere in the command line, the program and a quoted value included (the two
    /// rows that hold a <c>-p</c> that is not the flag); one that knew a headless flag only
    /// without its equals sign (<c>--output-format=stream-json</c>); one that called a
    /// parent with nothing after its program unknown (the quoted program with a space);
    /// one that called every headless parent VS Code (the three unknown headless rows);
    /// one that called none VS Code (the VS Code transport); one that read the entrypoint
    /// before the parent (a terminal that inherited <c>claude-vscode</c>); an entrypoint
    /// fallback that knew <c>cli</c> alone (<c>sdk-cli</c> and <c>claude-vscode</c>); one
    /// that read an entrypoint it did not know as a terminal (an unknown entrypoint and
    /// none at all); and one that took every client that is not Codex for Claude Code
    /// (<c>Claude-Code</c>, <c>cursor-vscode</c> and a client with no name).
    /// </para>
    /// </remarks>
    /// <param name="clientName">What the client called itself.</param>
    /// <param name="parent">The parent's command line, or <see langword="null"/> when it could not be read.</param>
    /// <param name="entrypoint">The entrypoint variable, or <see langword="null"/>.</param>
    /// <param name="expected">What the relay tells the background.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(ClientRecognition.Codex, null, null, RelayReconnect.NewConversation)]
    [Arguments(ClientRecognition.Codex, "codex exec --json", "cli", RelayReconnect.NewConversation)]
    [Arguments(ClientRecognition.Codex, "codex app-server", "claude-vscode", RelayReconnect.NewConversation)]
    [Arguments(ClientRecognition.ClaudeCode, "claude -p \"open the page\"", "cli", RelayReconnect.None)]
    [Arguments(ClientRecognition.ClaudeCode, "\"C:\\Program Files\\Claude\\claude.exe\" --print --output-format stream-json", null, RelayReconnect.None)]
    [Arguments(ClientRecognition.ClaudeCode, "\"C:\\Program Files\\Claude\\claude.exe\"", "cli", RelayReconnect.McpReconnect)]
    [Arguments(ClientRecognition.ClaudeCode, "claude --resume 7f1c", "claude-vscode", RelayReconnect.McpReconnect)]
    [Arguments(ClientRecognition.ClaudeCode, "claude --append-system-prompt \"-p is not a flag in here\"", null, RelayReconnect.McpReconnect)]
    [Arguments(ClientRecognition.ClaudeCode, "\"C:\\odd -p\\claude.exe\" --continue", null, RelayReconnect.McpReconnect)]
    [Arguments(ClientRecognition.ClaudeCode, "claude --output-format stream-json --input-format stream-json", "claude-vscode", RelayReconnect.None)]
    [Arguments(ClientRecognition.ClaudeCode, "claude --output-format=stream-json", "cli", RelayReconnect.Unknown)]
    [Arguments(ClientRecognition.ClaudeCode, "claude --sdk-url ws://127.0.0.1:9", null, RelayReconnect.Unknown)]
    [Arguments(ClientRecognition.ClaudeCode, "claude --init-only", "sdk-cli", RelayReconnect.Unknown)]
    [Arguments(ClientRecognition.ClaudeCode, null, "cli", RelayReconnect.McpReconnect)]
    [Arguments(ClientRecognition.ClaudeCode, "", "cli", RelayReconnect.McpReconnect)]
    [Arguments(ClientRecognition.ClaudeCode, null, "sdk-cli", RelayReconnect.None)]
    [Arguments(ClientRecognition.ClaudeCode, null, "claude-vscode", RelayReconnect.None)]
    [Arguments(ClientRecognition.ClaudeCode, null, "something-newer", RelayReconnect.Unknown)]
    [Arguments(ClientRecognition.ClaudeCode, null, null, RelayReconnect.Unknown)]
    [Arguments("Claude-Code", null, "cli", RelayReconnect.Unknown)]
    [Arguments("cursor-vscode", "claude -p", "cli", RelayReconnect.Unknown)]
    [Arguments(null, null, "cli", RelayReconnect.Unknown)]
    public async Task OptionDReadsTheClientTheParentAndThenTheEntrypoint(string? clientName, string? parent, string? entrypoint, RelayReconnect expected) =>
        await Assert.That(ClientRecognition.Reconnect(clientName, parent, entrypoint)).IsEqualTo(expected);

    /// <summary>
    /// The product reads the parent, and with the parent unread, option c, every Claude
    /// Code session is unknown, whatever its parent and its entrypoint say, while Codex
    /// still needs a new conversation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's other option</b>: never guess, and name both remedies for
    /// every Claude Code session. The class's remarks say it is the switch set the other
    /// way and nothing else changes, which is what this holds.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a rule that ignored the switch.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithTheParentUnreadEveryClaudeCodeSessionIsUnknownAndCodexIsNot()
    {
        await Assert.That(ClientRecognition.ReadsTheParent).IsTrue().Because("the product runs option d until the maintainer says otherwise");

        (string Parent, string Entrypoint)[] sessions =
        [
            ("claude -p \"open the page\"", "sdk-cli"),
            ("\"C:\\Program Files\\Claude\\claude.exe\"", "cli"),
            ("claude --output-format stream-json", "claude-vscode"),
        ];

        foreach (var (parent, entrypoint) in sessions)
        {
            // Read, each is told.
            await Assert.That(ClientRecognition.Reconnect(ClientRecognition.ClaudeCode, parent, entrypoint, readsTheParent: true)).IsNotEqualTo(RelayReconnect.Unknown);

            // Unread, none is.
            await Assert.That(ClientRecognition.Reconnect(ClientRecognition.ClaudeCode, parent, entrypoint, readsTheParent: false)).IsEqualTo(RelayReconnect.Unknown);
            await Assert.That(ClientRecognition.Reconnect(ClientRecognition.ClaudeCode, parentCommandLine: null, entrypoint, readsTheParent: false)).IsEqualTo(RelayReconnect.Unknown);
        }

        await Assert.That(ClientRecognition.Reconnect(ClientRecognition.Codex, "codex exec", "cli", readsTheParent: false)).IsEqualTo(RelayReconnect.NewConversation);
    }

    /// <summary>
    /// The kind behind each answer: a VS Code tab and <c>claude -p</c> both need nothing
    /// after an update and are two kinds, because only the tab has a window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-10 with 1.5 a</b>: the kind is what decides that a relay reads its
    /// client's window, and the reconnect is read off the kind, so every row of
    /// <see cref="OptionDReadsTheClientTheParentAndThenTheEntrypoint"/> still holds through
    /// it.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a rule that called <c>claude -p</c> a VS Code
    /// tab, and against one that called a terminal whose entrypoint was inherited a tab.
    /// </para>
    /// </remarks>
    /// <param name="clientName">What the client called itself.</param>
    /// <param name="parent">The parent's command line, or <see langword="null"/>.</param>
    /// <param name="entrypoint">The entrypoint variable, or <see langword="null"/>.</param>
    /// <param name="expected">The kind.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(ClientRecognition.ClaudeCode, "claude --output-format stream-json --verbose --input-format stream-json", "claude-vscode", ClientKind.ClaudeCodeVsCode)]
    [Arguments(ClientRecognition.ClaudeCode, "claude -p \"open the page\"", "claude-vscode", ClientKind.ClaudeCodePrint)]
    [Arguments(ClientRecognition.ClaudeCode, "claude", "claude-vscode", ClientKind.ClaudeCodeTerminal)]
    [Arguments(ClientRecognition.ClaudeCode, null, "claude-vscode", ClientKind.ClaudeCodeVsCode)]
    [Arguments(ClientRecognition.ClaudeCode, null, "sdk-cli", ClientKind.ClaudeCodePrint)]
    [Arguments(ClientRecognition.ClaudeCode, null, "sdk-ts", ClientKind.ClaudeCode)]
    [Arguments(ClientRecognition.Codex, "codex app-server", null, ClientKind.Codex)]
    [Arguments("cursor-vscode", "claude", "cli", ClientKind.Unknown)]
    public async Task TheKindTellsAVsCodeTabFromClaudeDashP(string clientName, string? parent, string? entrypoint, ClientKind expected)
    {
        var kind = ClientRecognition.KindOf(clientName, parent, entrypoint);

        await Assert.That(kind).IsEqualTo(expected);
        await Assert.That(ClientRecognition.ReconnectOf(kind)).IsEqualTo(ClientRecognition.Reconnect(clientName, parent, entrypoint));
    }

    /// <summary>
    /// What the relay reads about its client's conversation: for Claude Code its
    /// configuration folder, its own creation time, the session in its environment and on
    /// its command line, and for a VS Code tab its window; for Codex its home; for any
    /// other client nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.1 c, 1.4 a and 1.5 a, decided 2026-10-10</b>: the relay reads what only it can
    /// see, the environment its client handed it and its parent, and the background reads
    /// the records. <c>CLAUDE_CONFIG_DIR</c> moves Claude Code's folder from the profile's
    /// <c>.claude</c>; <c>CODEX_HOME</c> never reaches a server, so Codex's home is the
    /// profile's <c>.codex</c>. Nothing else of the environment is read, which the
    /// environment here holds a decoy for.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reading that ignored
    /// <c>CLAUDE_CONFIG_DIR</c>, against one that sent the window of a terminal, and
    /// against one that took a session id of the wrong shape.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRelayReadsWhereItsClientKeepsTheConversation()
    {
        const string Profile = @"C:\Users\someone";
        const string Session = "aaaaaaaa-1111-4111-8111-111111111111";
        const string Resumed = "cccccccc-3333-4333-8333-333333333333";

        var host = new ProcessStamp(1200, 134360600000000000);
        var vsCodeTab = new ParentReading(2424, 134360637732277608, "claude.exe --output-format stream-json --verbose --input-format stream-json --resume=" + Resumed, host);
        var terminal = new ParentReading(2424, 134360637732277608, "claude.exe", host);

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ClientRecognition.EntrypointVariable] = "claude-vscode",
            [ClientRecognition.SessionIdVariable] = Session,
            [ClientRecognition.ProfileVariable] = Profile,
            ["CLAUDE_CODE_MESSAGING_SOCKET_TOKEN"] = "a decoy nothing may read",
        };

        var tab = ClientRecognition.Read(ClientRecognition.ClaudeCode, vsCodeTab, environment.GetValueOrDefault);

        await Assert.That(tab.Reconnect).IsEqualTo(RelayReconnect.None);
        await Assert.That(tab.Conversation).IsEqualTo(new ConversationFacts(Profile + @"\.claude", 134360637732277608, Session, Resumed, null));
        await Assert.That(tab.Window).IsEqualTo("1200-134360600000000000");

        // CLAUDE_CONFIG_DIR moves the folder; a terminal has no window.
        environment[ClientRecognition.ConfigFolderVariable] = @"D:\Elsewhere\claude-config";

        var moved = ClientRecognition.Read(ClientRecognition.ClaudeCode, terminal, environment.GetValueOrDefault);

        await Assert.That(moved.Reconnect).IsEqualTo(RelayReconnect.McpReconnect);
        await Assert.That(moved.Conversation?.ClaudeConfig).IsEqualTo(@"D:\Elsewhere\claude-config");
        await Assert.That(moved.Window).IsNull();

        // A session id of the wrong shape is no session id; an unread parent leaves no
        // creation time and no window.
        environment[ClientRecognition.SessionIdVariable] = @"..\..\not-a-session";

        var unread = ClientRecognition.Read(ClientRecognition.ClaudeCode, parent: null, environment.GetValueOrDefault);

        await Assert.That(unread.Conversation).IsEqualTo(new ConversationFacts(@"D:\Elsewhere\claude-config", null, null, null, null));
        await Assert.That(unread.Window).IsNull();

        // Codex: its home, and nothing of Claude Code's.
        var codex = ClientRecognition.Read(ClientRecognition.Codex, terminal, environment.GetValueOrDefault);

        await Assert.That(codex.Reconnect).IsEqualTo(RelayReconnect.NewConversation);
        await Assert.That(codex.Conversation).IsEqualTo(new ConversationFacts(null, null, null, null, Profile + @"\.codex"));
        await Assert.That(codex.Window).IsNull();

        // Any other client: nothing.
        var other = ClientRecognition.Read("cursor-vscode", vsCodeTab, environment.GetValueOrDefault);

        await Assert.That(other.Conversation).IsNull();
        await Assert.That(other.Window).IsNull();
    }

    /// <summary>
    /// A session id is read off Claude Code's command line where an option names one, with
    /// a space or an equals sign, and never from a fork, whose id is the session it forked
    /// from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.1 c's third source</b>: <c>--resume</c> and <c>--session-id</c> carried the
    /// id in every arm of the measurement that used them, and a fork's command line named
    /// the old session, 6 of 6.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reading that took a fork's id.
    /// </para>
    /// </remarks>
    /// <param name="commandLine">The command line.</param>
    /// <param name="expected">The session it names, or <see langword="null"/>.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("claude --resume cccccccc-3333-4333-8333-333333333333", "cccccccc-3333-4333-8333-333333333333")]
    [Arguments("claude.exe --output-format stream-json --resume=cccccccc-3333-4333-8333-333333333333", "cccccccc-3333-4333-8333-333333333333")]
    [Arguments("claude -r cccccccc-3333-4333-8333-333333333333", "cccccccc-3333-4333-8333-333333333333")]
    [Arguments("claude -p --session-id aaaaaaaa-1111-4111-8111-111111111111 \"open it\"", "aaaaaaaa-1111-4111-8111-111111111111")]
    [Arguments("claude --resume cccccccc-3333-4333-8333-333333333333 --fork-session", null)]
    [Arguments("claude --continue", null)]
    [Arguments("claude --resume", null)]
    [Arguments("claude --resume \"not a session\"", null)]
    [Arguments("\"C:\\odd --resume cccccccc-3333-4333-8333-333333333333\\claude.exe\"", null)]
    public async Task ASessionIdIsReadOffTheCommandLineOnlyWhereAnOptionNamesOne(string commandLine, string? expected) =>
        await Assert.That(ClientRecognition.SessionIdOnCommandLine(commandLine)).IsEqualTo(expected);
}
