// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
}
