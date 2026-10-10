// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrowserAI.Clients;

/// <summary>
/// The title the VS Code extension gives a Claude Code conversation, from the first and
/// the last 64 KB of its record: the rule 1.2 a takes as BrowserAI's label.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all your
/// recommendations"</i></b>, of which 1.2 a is <i>the extension's own rule over the first
/// and last 64 KB of the record</i>. Read in the extension's <c>extension.js</c>, its
/// session list's <c>fetchSessionsInDir</c>, at 2.1.292 on 2026-10-08 and again at
/// 2.1.296 on 2026-10-10, unchanged: the custom title from the end, then from the
/// start; the AI title from the end, then from the start; the last prompt; the summary;
/// the first real prompt; then <i>Image</i> or <i>Document</i> for a first prompt that was
/// one. It named 21 of 21 emulated tabs as their tab showed them, and agreed with a read
/// of the whole file in 138 of 138 real records.
/// </para>
/// <para>
/// <b>Copied in its quirks, because the label is only right while it is the
/// extension's</b>: a field is found as text, <c>"customTitle":"</c> or
/// <c>"customTitle": "</c>, and the occurrence that starts last wins; a value whose
/// closing quote the window cut off is skipped; an empty custom or AI title stops the
/// search for a title and lets the prompts answer. <b>Two of its steps are left out</b>,
/// because they decide which conversations the extension's history lists and not what a
/// live conversation is called: it skips a record whose first line is a side chain, and
/// one written by the SDK or a daemon.
/// </para>
/// <para>
/// <b>Where it cuts, BrowserAI writes three full stops</b> and the extension an
/// ellipsis character, the house rule for every character BrowserAI writes.
/// </para>
/// <para>
/// <b>Undocumented, and any release may change it</b>: the record's layout is Claude
/// Code's own, so a record this rule cannot read names nothing, and the caller falls
/// back to its own words.
/// </para>
/// </remarks>
internal static partial class ClaudeCodeTitle
{
    /// <summary>How much of each end of a record the rule reads: 64 KB, the extension's own window.</summary>
    /// <remarks>
    /// <b>Upstream</b>, read in <c>extension.js</c> as <c>m7=65536</c> at 2.1.292 and
    /// 2.1.296. Over the maintainer's 138 records of 14 days, every title was within the
    /// last 64 KB, the furthest 33,411 bytes from the end, in a file of 80 MB.
    /// </remarks>
    public const int Window = 64 * 1024;

    /// <summary>The longest first prompt the rule keeps whole: 200 characters, then a cut.</summary>
    /// <remarks><b>Upstream</b>, read in the same function at 2.1.296.</remarks>
    public const int PromptWidth = 200;

    /// <summary>The title, from the first and the last <see cref="Window"/> bytes of a record.</summary>
    /// <param name="head">The record's first bytes, as UTF-8 text.</param>
    /// <param name="tail">Its last bytes, the same text when the record is no longer than the window.</param>
    /// <returns>The title, and which rule gave it; or <see langword="null"/> when the record names nothing.</returns>
    public static (string Title, TitleSource Source)? Of(string head, string tail)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(tail);

        // `??` and not `||`, as the extension writes it: an empty title is found and
        // stops the search for one, and the prompts below then answer.
        var title = Field(tail, "customTitle") is { } custom ? (custom, TitleSource.CustomTitle)
            : Field(head, "customTitle") is { } headCustom ? (headCustom, TitleSource.CustomTitle)
            : Field(tail, "aiTitle") is { } ai ? (ai, TitleSource.AiTitle)
            : Field(head, "aiTitle") is { } headAi ? (headAi, TitleSource.AiTitle)
            : ((string, TitleSource)?)null;

        if (title is { Item1.Length: > 0 } named)
        {
            return named;
        }

        if (Field(tail, "lastPrompt") is { Length: > 0 } lastPrompt)
        {
            return (lastPrompt, TitleSource.LastPrompt);
        }

        if (Field(tail, "summary") is { Length: > 0 } summary)
        {
            return (summary, TitleSource.Summary);
        }

        if (FirstPrompt(head) is { Length: > 0 } first)
        {
            return (first, TitleSource.FirstPrompt);
        }

        return ImageOrDocument(head) is { Length: > 0 } attachment ? (attachment, TitleSource.FirstPrompt) : null;
    }

    /// <summary>
    /// The value of the last occurrence of a string field in a window, found as text the
    /// way the extension finds it.
    /// </summary>
    /// <param name="text">The window.</param>
    /// <param name="key">The field.</param>
    /// <returns>The value, its escapes decoded; or <see langword="null"/> when no occurrence has its closing quote.</returns>
    internal static string? Field(string text, string key)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(key);

        string? found = null;
        var foundAt = -1;

        foreach (var pattern in (string[])[$"\"{key}\":\"", $"\"{key}\": \""])
        {
            var from = 0;

            while (from <= text.Length)
            {
                var at = text.IndexOf(pattern, from, StringComparison.Ordinal);

                if (at < 0)
                {
                    break;
                }

                var start = at + pattern.Length;
                var index = start;

                while (index < text.Length)
                {
                    if (text[index] is '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (text[index] is '"')
                    {
                        if (at > foundAt)
                        {
                            found = Unescape(text[start..index]);
                            foundAt = at;
                        }

                        break;
                    }

                    index++;
                }

                from = index + 1;
            }
        }

        return found;
    }

    /// <summary>The first real prompt in a record's first window, the way the extension finds it.</summary>
    /// <param name="head">The window.</param>
    /// <returns>The prompt, cut at <see cref="PromptWidth"/>; a command's name when no prompt is real; or <see langword="null"/>.</returns>
    internal static string? FirstPrompt(string head)
    {
        ArgumentNullException.ThrowIfNull(head);

        string? commandFallback = null;

        foreach (var line in head.Split('\n'))
        {
            if (!IsAUsersLine(line)
                || line.Contains("\"isCompactSummary\":true", StringComparison.Ordinal)
                || line.Contains("\"isCompactSummary\": true", StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement record;

            try
            {
                using var document = JsonDocument.Parse(line);
                record = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            if (PromptOf(record, ref commandFallback) is { } prompt)
            {
                return prompt;
            }
        }

        return commandFallback is { Length: > 0 } ? commandFallback : null;
    }

    /// <summary><i>Image</i> or <i>Document</i>, when the first prompt in a record's first window was one.</summary>
    /// <param name="head">The window.</param>
    /// <returns>The word, or <see langword="null"/>.</returns>
    internal static string? ImageOrDocument(string head)
    {
        ArgumentNullException.ThrowIfNull(head);

        foreach (var line in head.Split('\n'))
        {
            if (!IsAUsersLine(line))
            {
                continue;
            }

            if (line.Contains("\"type\":\"image\"", StringComparison.Ordinal) || line.Contains("\"type\": \"image\"", StringComparison.Ordinal))
            {
                return "Image";
            }

            if (line.Contains("\"type\":\"document\"", StringComparison.Ordinal) || line.Contains("\"type\": \"document\"", StringComparison.Ordinal))
            {
                return "Document";
            }
        }

        return null;
    }

    /// <summary>A line the extension reads for a prompt: a user's, carrying no tool result, and not one Claude Code wrote for itself.</summary>
    private static bool IsAUsersLine(string line) =>
        (line.Contains("\"type\":\"user\"", StringComparison.Ordinal) || line.Contains("\"type\": \"user\"", StringComparison.Ordinal))
        && !line.Contains("\"tool_result\"", StringComparison.Ordinal)
        && !line.Contains("\"isMeta\":true", StringComparison.Ordinal)
        && !line.Contains("\"isMeta\": true", StringComparison.Ordinal);

    /// <summary>The prompt one user record carries, or <see langword="null"/>, keeping the first command's name for when none does.</summary>
    private static string? PromptOf(JsonElement record, ref string? commandFallback)
    {
        if (record.ValueKind is not JsonValueKind.Object
            || !record.TryGetProperty("type", out var type) || type.ValueKind is not JsonValueKind.String || type.GetString() is not "user"
            || IsTrue(record, "isMeta") || IsTrue(record, "isCompactSummary")
            || !record.TryGetProperty("message", out var message) || message.ValueKind is not JsonValueKind.Object
            || !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        var texts = new List<string>();

        if (content.ValueKind is JsonValueKind.String)
        {
            texts.Add(content.GetString()!);
        }
        else if (content.ValueKind is JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind is not JsonValueKind.Object || !block.TryGetProperty("type", out var blockType) || blockType.ValueKind is not JsonValueKind.String)
                {
                    continue;
                }

                if (blockType.GetString() is "tool_result")
                {
                    return null;
                }

                if (blockType.GetString() is "text" && block.TryGetProperty("text", out var text) && text.ValueKind is JsonValueKind.String)
                {
                    texts.Add(text.GetString()!);
                }
            }
        }

        foreach (var raw in texts)
        {
            var prompt = WithPastesOpened(raw).Replace('\n', ' ').Trim();

            if (prompt.Length is 0)
            {
                continue;
            }

            if (CommandName().Match(prompt) is { Success: true } command)
            {
                if (string.IsNullOrEmpty(commandFallback))
                {
                    commandFallback = command.Groups[1].Value;
                }

                continue;
            }

            if (BashInput().Match(prompt) is { Success: true } bash)
            {
                return "! " + bash.Groups[1].Value.Trim();
            }

            if (NotAPrompt().IsMatch(prompt))
            {
                continue;
            }

            return CutPrompt(prompt);
        }

        return null;
    }

    /// <summary>A prompt cut the way the extension cuts a first prompt: past <see cref="PromptWidth"/> characters, to that many and three full stops.</summary>
    /// <remarks>
    /// <i>Added 2026-10-10</i>, when a Codex thread's first message became a name too and
    /// the root asked for it cut like the Claude Code titles.
    /// </remarks>
    /// <param name="prompt">The prompt, on one line.</param>
    /// <returns>It, or its cut form.</returns>
    internal static string CutPrompt(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        return prompt.Length > PromptWidth ? Cut(prompt, PromptWidth).Trim() + "..." : prompt;
    }

    private static bool IsTrue(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    /// <summary>The first characters of a text, never half a surrogate pair.</summary>
    private static string Cut(string text, int width)
    {
        var kept = text[..width];

        return char.IsHighSurrogate(kept[^1]) ? kept[..^1] : kept;
    }

    /// <summary>A JSON string's escapes decoded, as the extension decodes them; the text as it is when it does not parse.</summary>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        try
        {
            using var document = JsonDocument.Parse("\"" + value + "\"");
            return document.RootElement.GetString() ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    /// <summary>
    /// A prompt with each pasted block opened: the extension shows what was pasted and
    /// not the marks around it.
    /// </summary>
    /// <remarks>
    /// Its <c>bU4</c> and <c>ec</c>, read at 2.1.296: a block opens with
    /// <c>&lt;pasted_content id="abcd"&gt;</c> and a line break, four lower-case hex
    /// digits for its id, and closes with a line holding
    /// <c>&lt;/pasted_content id="abcd"&gt;</c>; up to two line breaks either side go with
    /// the marks.
    /// </remarks>
    private static string WithPastesOpened(string text)
    {
        const string Open = "<pasted_content id=\"";

        var parts = new StringBuilder();
        var blocks = 0;
        var kept = 0;
        var from = 0;

        while (true)
        {
            var at = text.IndexOf(Open, from, StringComparison.Ordinal);

            if (at < 0)
            {
                break;
            }

            var idAt = at + Open.Length;

            if (idAt + 4 > text.Length || !IsAPasteId(text.AsSpan(idAt, 4)) || !text.AsSpan(idAt + 4).StartsWith("\">\n", StringComparison.Ordinal))
            {
                from = idAt;
                continue;
            }

            var id = text.Substring(idAt, 4);
            var bodyAt = idAt + 4 + 3;
            var close = $"</pasted_content id=\"{id}\">";
            var closeAt = text.IndexOf("\n" + close, bodyAt - 1, StringComparison.Ordinal) + 1;

            if (closeAt is 0)
            {
                break;
            }

            var before = at;

            for (var step = 0; step < 2 && before > kept && text[before - 1] is '\n'; step++)
            {
                before--;
            }

            if (before > kept)
            {
                _ = parts.Append(text, kept, before - kept);
            }

            _ = parts.Append(text, bodyAt, Math.Max(0, closeAt - 1 - bodyAt));
            blocks++;

            kept = closeAt + close.Length;

            for (var step = 0; step < 2 && kept < text.Length && text[kept] is '\n'; step++)
            {
                kept++;
            }

            from = kept;
        }

        if (blocks is 0)
        {
            return text;
        }

        if (kept < text.Length)
        {
            _ = parts.Append(text, kept, text.Length - kept);
        }

        return parts.ToString();
    }

    private static bool IsAPasteId(ReadOnlySpan<char> id)
    {
        foreach (var character in id)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A slash command's own record: its name stands in when no prompt is real.</summary>
    [GeneratedRegex("<command-name>(.*?)</command-name>", RegexOptions.CultureInvariant)]
    private static partial Regex CommandName();

    /// <summary>A line typed with <c>!</c>: the extension shows it after an exclamation mark.</summary>
    [GeneratedRegex(@"<bash-input>([\s\S]*?)</bash-input>", RegexOptions.CultureInvariant)]
    private static partial Regex BashInput();

    /// <summary>Text Claude Code wrote into a user record itself: a tag first, or an interruption.</summary>
    [GeneratedRegex(@"^(?:\s*<[a-z][\w-]*[\s>]|\[Request interrupted by user[^\]]*\])", RegexOptions.CultureInvariant)]
    private static partial Regex NotAPrompt();
}

/// <summary>Which part of the title rule named a conversation, which is what a log record may say of it.</summary>
internal enum TitleSource
{
    /// <summary>A title the person gave it.</summary>
    CustomTitle,

    /// <summary>A title the model gave it.</summary>
    AiTitle,

    /// <summary>The last prompt Claude Code recorded.</summary>
    LastPrompt,

    /// <summary>A summary Claude Code wrote.</summary>
    Summary,

    /// <summary>The first real prompt, or the attachment it was.</summary>
    FirstPrompt,
}
