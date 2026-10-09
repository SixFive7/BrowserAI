// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI;

/// <summary>
/// How long each MCP client waits for a server, to start and for one tool call, declared
/// once.
/// </summary>
/// <remarks>
/// <para>
/// <b>One of the named classes of the numbers index</b>, decision F3 of the one-binary
/// build. The clients' limits are in it because the maintainer asked for them with R,
/// in his words of 2026-10-08 verbatim: <i>"About the timeouts: Do all these timeouts
/// work within both the claude or codex timeouts? And if they differ do the timeouts
/// differ? Add them to our numbers list we discussed earlier."</i>
/// None of these numbers is BrowserAI's. Each was read or measured in the client, and
/// the client may change it in any release, so each has a row in
/// <see href="../../kb/numbers.md">the numbers index</see> that says where it came from
/// and how to establish it again. <c>NumbersIndexTests</c> holds the rows and the
/// members against each other in both directions.
/// </para>
/// <para>
/// <b>The relay's two bounds are half of the stricter tool-call limit</b>:
/// <c>RelayConstants.HoldBound</c> and <c>RelayConstants.HangBound</c> are
/// <see cref="StricterToolCall"/> halved, which the maintainer accepted on 2026-10-08
/// ("r ok") on the ground that a number inside Codex's limit is inside Claude Code's
/// too. Until 2026-10-09 each was a literal 150 s with that reasoning in its remarks.
/// </para>
/// <para>
/// <b>The four start limits are read by no code.</b> The relay answers the handshake
/// and the tool list from the binary, so nothing BrowserAI waits on is derived from
/// them; they are here so that the index can hold them, and what a client does with a
/// server that misses one is in the rows.
/// </para>
/// </remarks>
internal static class ClientLimits
{
    /// <summary>
    /// Codex's default limit on one tool call: 300 s, read in its source at 0.155 and
    /// 0.160 (<c>rmcp_client.rs:104</c> and <c>:106</c>) and never measured to the end.
    /// </summary>
    public static TimeSpan CodexToolCall { get; } = TimeSpan.FromSeconds(300);

    /// <summary>
    /// How long Claude Code waits for a stdio server's answer to one call before it
    /// abandons the call: 30 minutes, read in its binary at 2.1.288 and never measured
    /// to the end.
    /// </summary>
    public static TimeSpan ClaudeCodeToolCall { get; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The shorter of the two tool-call limits, which one set of relay bounds for both
    /// clients is derived from.
    /// </summary>
    public static TimeSpan StricterToolCall { get; } = CodexToolCall < ClaudeCodeToolCall ? CodexToolCall : ClaudeCodeToolCall;

    /// <summary>
    /// Codex's default limit on a server's start, from its launch to the answer to
    /// <c>initialize</c>: 30 s, measured at 0.155 and 0.160 on 2026-10-03.
    /// </summary>
    public static TimeSpan CodexStartup { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long Claude Code waits for a server registered through
    /// <c>--mcp-config</c> before its first request goes out without it: 30 s, measured
    /// at 2.1.288 on 2026-10-04.
    /// </summary>
    public static TimeSpan ClaudeCodeStartup { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long Codex lets a server that is not <c>required</c> start before a turn
    /// goes to the model without its tools: 1 s from the turn's start, read in its
    /// source at 0.155 and 0.160.
    /// </summary>
    public static TimeSpan CodexFirstTurnGrace { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long Claude Code waits for a server registered at user scope, as RegisterAI
    /// registers BrowserAI, before its first turn goes without the server's tools: 2 s,
    /// read in its binary at 2.1.288.
    /// </summary>
    public static TimeSpan ClaudeCodeFirstTurnWait { get; } = TimeSpan.FromSeconds(2);
}
