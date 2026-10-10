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
/// </remarks>
internal static class CodexThreads
{
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
