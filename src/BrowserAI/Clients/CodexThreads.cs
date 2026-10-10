// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Clients;

/// <summary>
/// The name a person gave a Codex thread, from Codex's own index of named threads.
/// </summary>
/// <remarks>
/// <para>
/// <b>1.4 a, decided 2026-10-10</b>: the thread is the <c>threadId</c> in the
/// <c>_meta</c> of the first tool call (24 of 24 calls in the measurement of 2026-10-08,
/// Codex 0.162.0), and its name is <c>thread_name</c> in
/// <c>%USERPROFILE%\.codex\session_index.jsonl</c>, which Codex appends a line
/// <c>{"id":...,"thread_name":...,"updated_at":...}</c> to when a thread is named (3 of
/// 3). The real <c>~\.codex</c> of the desktop app at 0.159 alpha has the file, with no
/// other store beside it. A thread named twice has two lines, and the later one is its
/// name now.
/// </para>
/// <para>
/// <b>Undocumented, and any release may change it</b>: a file that cannot be read, or
/// names nothing for the thread, leaves the thread unnamed, and the caller says
/// <i>Codex in &lt;folder&gt;</i>.
/// </para>
/// <para>
/// ⚠️ <i>Added later on 2026-10-10, by addition.</i> <b>1.4 a as the maintainer approved
/// it has a middle step</b>, which the build brief left out and the root sent on the same
/// day: a thread the index does not name is called by its first message, from Codex's
/// state database (<see cref="CodexState"/>) or else from the thread's rollout
/// (<see cref="FirstMessage"/>), and only then <i>Codex in &lt;folder&gt;</i>.
/// </para>
/// </remarks>
internal static class CodexThreads
{
    /// <summary>How much of a rollout's start is read for the thread's first message: 1 MiB.</summary>
    /// <remarks>
    /// <b>Measured 2026-10-10</b> over the 18 rollouts in the six scratch homes of the
    /// measurement of 2026-10-08, Codex 0.162.0: the first <c>UserMessage</c> record began
    /// between 97,241 and 100,061 bytes in, after Codex's own instructions, the
    /// project's instructions and the world state, so the 64 KB the Claude Code extension
    /// reads of a record would have found it in none of them. What comes first grows with
    /// the project's own instructions, so the bound is ten times the furthest seen, and a
    /// first message that begins later names nothing here.
    /// </remarks>
    public const int RolloutHead = 1024 * 1024;

    /// <summary>The thread's rollout, <c>&lt;home&gt;\sessions\&lt;year&gt;\&lt;month&gt;\&lt;day&gt;\rollout-&lt;time&gt;-&lt;thread&gt;.jsonl</c>, where 0.162.0 wrote all 18.</summary>
    /// <param name="home">Codex's home.</param>
    /// <param name="threadId">The thread, a session id in form, so the pattern holds no wildcard of its own.</param>
    /// <param name="filesUnder">How a folder's files at any of the three depths below it are found by a pattern.</param>
    /// <returns>The first in ordinal order, or <see langword="null"/>.</returns>
    public static string? RolloutOf(string home, string threadId, Func<string, string, IReadOnlyList<string>> filesUnder)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(filesUnder);

        var found = filesUnder(Path.Combine(home, "sessions"), $"rollout-*-{threadId}.jsonl");

        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>The first message a person sent in a thread, from the start of its rollout.</summary>
    /// <remarks>
    /// <para>
    /// <b>The first <c>event_msg</c> record whose <c>item_completed</c> payload carries an
    /// item of type <c>UserMessage</c></b>, its text parts joined: 18 of 18 rollouts of
    /// 0.162.0 equal their thread's <c>title</c> by this rule. A rollout also records,
    /// before it, a <c>user</c>-role message Codex wrote itself, the project's
    /// instructions and its environment, one in each of the 18, and no
    /// <c>UserMessage</c> item goes with that one. A <c>UserMessage</c> with no text, an
    /// image alone, is passed over for the next.
    /// </para>
    /// <para>
    /// <b>A record that does not parse</b> is passed over too, and when it is the last of
    /// the text read, which is where a write in progress leaves a half record, the caller
    /// is told, to read once more.
    /// </para>
    /// </remarks>
    /// <param name="head">The rollout's first <see cref="RolloutHead"/> bytes, as UTF-8 text.</param>
    /// <returns>The message, or <see langword="null"/>; and whether the last record was cut short.</returns>
    internal static (string? Message, bool Torn) FirstMessage(string head)
    {
        ArgumentNullException.ThrowIfNull(head);

        var lines = head.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            // Most records are not a person's message, and parsing the instructions'
            // tens of kilobytes to learn that is the cost this skips.
            if (line.Length is 0 || !line.Contains("\"UserMessage\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);

                if (MessageIn(document.RootElement) is { } message)
                {
                    return (message, false);
                }
            }
            catch (JsonException)
            {
                if (index == lines.Length - 1)
                {
                    return (null, true);
                }
            }
        }

        return (null, false);
    }

    private static string? MessageIn(JsonElement record)
    {
        if (record.ValueKind is not JsonValueKind.Object
            || Text(record, "type") is not "event_msg"
            || !record.TryGetProperty("payload", out var payload) || payload.ValueKind is not JsonValueKind.Object
            || Text(payload, "type") is not "item_completed"
            || !payload.TryGetProperty("item", out var item) || item.ValueKind is not JsonValueKind.Object
            || Text(item, "type") is not "UserMessage"
            || !item.TryGetProperty("content", out var content) || content.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        var texts = new List<string>();

        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind is JsonValueKind.Object && Text(part, "type") is "text" && Text(part, "text") is { Length: > 0 } text)
            {
                texts.Add(text);
            }
        }

        var joined = string.Join("\n", texts);

        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
    /// <summary>The name a thread was last given, when Codex's index has one.</summary>
    /// <remarks>
    /// <b>A last line that does not parse is read again, once</b>: it is the one line a
    /// read can meet half appended.
    /// </remarks>
    /// <param name="home">Codex's home.</param>
    /// <param name="threadId">The thread.</param>
    /// <param name="readAll">How a whole file is read.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public static string? NameOf(string home, string threadId, Func<string, string?> readAll)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(readAll);

        var path = Path.Combine(home, "session_index.jsonl");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (readAll(path) is not { } text)
            {
                return null;
            }

            string? name = null;
            var tornAtTheEnd = false;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();

                if (line.Length is 0)
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var entry = document.RootElement;

                    tornAtTheEnd = false;

                    if (entry.ValueKind is JsonValueKind.Object
                        && entry.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String
                        && string.Equals(id.GetString(), threadId, StringComparison.OrdinalIgnoreCase)
                        && entry.TryGetProperty("thread_name", out var named) && named.ValueKind is JsonValueKind.String)
                    {
                        name = named.GetString();
                    }
                }
                catch (JsonException)
                {
                    tornAtTheEnd = true;
                }
            }

            if (!tornAtTheEnd || attempt is 1)
            {
                return name is { Length: > 0 } ? name : null;
            }
        }

        return null;
    }
}
