// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Updates;

namespace BrowserAI.Relay;

/// <summary>
/// What a relay can say about where its client keeps the conversation it serves, for the
/// background to read when it draws the dashboard or a toast.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all your
/// recommendations"</i></b>, of the measurement of 2026-10-08
/// (<see href="../../../kb/mcp/protocol.md">kb/mcp/protocol.md</see>). <b>Every fact
/// here is one only the relay can read</b>: the client's environment reaches the relay
/// and never the background, which the Task Scheduler starts, and the relay knows its
/// parent. So the relay says where to look and the background looks, when somebody
/// looks at what it draws (1.3 c).
/// </para>
/// <para>
/// <b>Claude Code:</b> its configuration folder, <c>CLAUDE_CONFIG_DIR</c> or else
/// <c>%USERPROFILE%\.claude</c>; the creation time of the process that started the relay,
/// which Claude Code's per-process file is accepted against; <c>CLAUDE_CODE_SESSION_ID</c>,
/// which goes stale at <c>/clear</c>; and a session id the client's command line named
/// with <c>--resume</c> or <c>--session-id</c>. <b>Codex:</b> its home,
/// <c>%USERPROFILE%\.codex</c>, because <c>CODEX_HOME</c> never reaches a server; the
/// thread itself is in the first call's <c>_meta</c>, which the background reads off the
/// pipe.
/// </para>
/// <para>
/// <b>None of it is a secret, and none of it is logged</b>: a session id names a
/// conversation, so it travels to the background over the pipe, whose one entry is
/// this user, and no log record carries it.
/// </para>
/// </remarks>
/// <param name="ClaudeConfig">Claude Code's configuration folder, or <see langword="null"/> for another client.</param>
/// <param name="ClientStarted">The creation time of the process that started the relay, as a Windows FILETIME, or <see langword="null"/>.</param>
/// <param name="SessionId">The session id Claude Code put in <c>CLAUDE_CODE_SESSION_ID</c>, or <see langword="null"/>.</param>
/// <param name="CommandLineSessionId">The session id the client's command line named, or <see langword="null"/>.</param>
/// <param name="CodexHome">Codex's home, or <see langword="null"/> for another client.</param>
internal sealed record ConversationFacts(
    string? ClaudeConfig,
    long? ClientStarted,
    string? SessionId,
    string? CommandLineSessionId,
    string? CodexHome)
{
    /// <summary>The greeting's member that carries these facts.</summary>
    public const string Member = "conversation";

    /// <summary>The greeting's member that carries the VS Code window, for a client that is a tab of one.</summary>
    public const string WindowMember = "window";

    private const string ClaudeConfigMember = "claudeConfig";
    private const string ClientStartedMember = "clientStarted";
    private const string SessionIdMember = "sessionId";
    private const string CommandLineSessionIdMember = "commandLineSessionId";
    private const string CodexHomeMember = "codexHome";

    /// <summary>The facts as the greeting carries them: each one present, and nothing else.</summary>
    /// <remarks>
    /// <b>The creation time is text</b>, because a FILETIME is larger than a JSON reader
    /// in another language holds exactly, and Claude Code writes its own the same way.
    /// </remarks>
    /// <returns>The object.</returns>
    public JsonObject ToJson()
    {
        var json = new JsonObject();

        Add(json, ClaudeConfigMember, ClaudeConfig);
        Add(json, ClientStartedMember, ClientStarted?.ToString(CultureInfo.InvariantCulture));
        Add(json, SessionIdMember, SessionId);
        Add(json, CommandLineSessionIdMember, CommandLineSessionId);
        Add(json, CodexHomeMember, CodexHome);

        return json;
    }

    /// <summary>The facts a greeting carried, or <see langword="null"/> when it carried none.</summary>
    /// <param name="node">The greeting's <see cref="Member"/>.</param>
    /// <returns>The facts.</returns>
    public static ConversationFacts? From(JsonNode? node)
    {
        if (node is not JsonObject json)
        {
            return null;
        }

        var started = Text(json, ClientStartedMember) is { } text
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : (long?)null;

        var facts = new ConversationFacts(
            Text(json, ClaudeConfigMember),
            started,
            SessionIds.Valid(Text(json, SessionIdMember)),
            SessionIds.Valid(Text(json, CommandLineSessionIdMember)),
            Text(json, CodexHomeMember));

        return facts == Empty ? null : facts;
    }

    private static ConversationFacts Empty { get; } = new(null, null, null, null, null);

    private static void Add(JsonObject json, string name, string? value)
    {
        if (value is { Length: > 0 })
        {
            json[name] = value;
        }
    }

    private static string? Text(JsonObject json, string name) =>
        json.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0
            ? text
            : null;
}

/// <summary>What a relay read about its client once the client had said who it is.</summary>
/// <param name="Reconnect">What the client needs once an update has ended the relay (H1-T a).</param>
/// <param name="Conversation">Where the client keeps the conversation the relay serves, or <see langword="null"/>.</param>
/// <param name="Window">The VS Code window the client is a tab of, as a key, or <see langword="null"/> for any other client.</param>
internal sealed record ClientReading(RelayReconnect Reconnect, ConversationFacts? Conversation = null, string? Window = null);

/// <summary>What a Claude Code session id looks like, and nothing else is taken for one.</summary>
/// <remarks>
/// <b>The VS Code extension's own test</b>, read in its <c>extension.js</c> at 2.1.296 on
/// 2026-10-10: eight, four, four, four and twelve hexadecimal digits joined by hyphens,
/// either case. An id is also a file name the background composes a path from, so a
/// value of any other shape is dropped where it is read and never reaches a path.
/// </remarks>
internal static class SessionIds
{
    /// <summary>The value when it is a session id, and <see langword="null"/> otherwise.</summary>
    /// <param name="value">The value.</param>
    /// <returns>It, or <see langword="null"/>.</returns>
    public static string? Valid(string? value)
    {
        if (value is not { Length: 36 })
        {
            return null;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var valid = index is 8 or 13 or 18 or 23 ? character is '-' : char.IsAsciiHexDigit(character);

            if (!valid)
            {
                return null;
            }
        }

        return value;
    }
}
