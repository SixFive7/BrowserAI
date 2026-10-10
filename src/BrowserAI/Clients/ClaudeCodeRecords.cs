// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json;
using BrowserAI.Relay;

namespace BrowserAI.Clients;

/// <summary>
/// Where Claude Code keeps what BrowserAI reads to name a conversation: its file per
/// running process, and each session's record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured 2026-10-08 at Claude Code 2.1.292 to 2.1.295, the file seen from 2.1.288</b>
/// (<see href="../../../kb/mcp/protocol.md">kb/mcp/protocol.md</see>), and read again in
/// a live 2.1.296 on 2026-10-10. <c>&lt;config&gt;\sessions\&lt;pid&gt;.json</c> is written
/// by every mode of Claude Code, deleted when it exits, and its <c>sessionId</c> follows
/// the live conversation, through <c>/clear</c> within a second (6 of 6) and through a
/// resume (12 of 12). It is accepted only when its <c>procStart</c>, the process's
/// creation time as a decimal FILETIME, is the creation time of the process that started
/// the relay, which held in 284 of 284 reads. ⚠️ <b>A <c>.key</c> file sits beside it
/// and is never opened</b>: this class composes exactly <c>&lt;pid&gt;.json</c> and lists
/// nothing in that folder.
/// </para>
/// <para>
/// <b>A session's record is <c>&lt;config&gt;\projects\&lt;slug&gt;\&lt;id&gt;.jsonl</c></b>,
/// the slug being the folder Claude Code runs in with every character that is not a
/// letter or a digit made a hyphen, and past 200 characters cut there and followed by a
/// hash of the whole path; read in the VS Code extension at 2.1.296. A new conversation
/// has no record until its first prompt (24 of 24). When the folder the slug names is not
/// there, the record is looked for under every project folder, one level deep, because a
/// session id is unique and the slug's rule is Claude Code's to change.
/// </para>
/// <para>
/// <b>Every one of these is undocumented and may change in any release</b>, so each read
/// that fails answers <see langword="null"/> and the caller falls back.
/// </para>
/// </remarks>
internal static class ClaudeCodeRecords
{
    /// <summary>The longest slug before Claude Code cuts it and adds a hash: 200 characters.</summary>
    /// <remarks><b>Upstream</b>, read as <c>KJ=200</c> in the extension's <c>extension.js</c> at 2.1.296 on 2026-10-10.</remarks>
    public const int SlugWidth = 200;

    /// <summary>
    /// The session the per-process file of the client names, when that file is the
    /// client's: its <c>procStart</c> is the creation time the relay read.
    /// </summary>
    /// <remarks>
    /// <b>A parse that fails is read again, once</b>: Claude Code rewrites the file on
    /// every change of its status, and a read can meet it half written. None did in 54
    /// servers polling it once a second for the measurement, which is why once is
    /// enough.
    /// </remarks>
    /// <param name="config">Claude Code's configuration folder.</param>
    /// <param name="clientPid">The client's pid.</param>
    /// <param name="clientStarted">The client's creation time, as a Windows FILETIME.</param>
    /// <param name="readAll">How a whole file is read.</param>
    /// <returns>The session id and the folder Claude Code runs in, or <see langword="null"/>.</returns>
    public static (string SessionId, string? Folder)? FromProcessFile(string config, int clientPid, long clientStarted, Func<string, string?> readAll)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(readAll);

        var path = Path.Combine(config, "sessions", clientPid.ToString(CultureInfo.InvariantCulture) + ".json");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (readAll(path) is not { } text)
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                var file = document.RootElement;

                if (file.ValueKind is not JsonValueKind.Object || !TheClients(file, clientStarted))
                {
                    return null;
                }

                if (!file.TryGetProperty("sessionId", out var id) || id.ValueKind is not JsonValueKind.String || SessionIds.Valid(id.GetString()) is not { } sessionId)
                {
                    return null;
                }

                var folder = file.TryGetProperty("cwd", out var cwd) && cwd.ValueKind is JsonValueKind.String ? cwd.GetString() : null;

                return (sessionId, folder);
            }
            catch (JsonException)
            {
                // Read half written: once more, and then the next source.
            }
        }

        return null;
    }

    /// <summary>Where a session's record is, when it has one.</summary>
    /// <param name="config">Claude Code's configuration folder.</param>
    /// <param name="sessionId">The session, a valid id.</param>
    /// <param name="folder">The folder Claude Code runs in, which the slug is made of, or <see langword="null"/>.</param>
    /// <param name="foldersIn">How the folders inside a folder are listed.</param>
    /// <returns>The record's path, or <see langword="null"/> when the session has none yet.</returns>
    public static string? RecordOf(string config, string sessionId, string? folder, Func<string, IReadOnlyList<string>> foldersIn)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(foldersIn);

        var projects = Path.Combine(config, "projects");
        var name = sessionId + ".jsonl";

        if (folder is { Length: > 0 })
        {
            var project = Path.Combine(projects, Slug(folder));

            // The folder the rule names is there: the record is in it, or there is none
            // yet. A file system that ignores case finds the folder whichever case the
            // drive letter was written in.
            if (Directory.Exists(project))
            {
                var record = Path.Combine(project, name);

                return File.Exists(record) ? record : null;
            }
        }

        foreach (var project in foldersIn(projects))
        {
            var record = Path.Combine(project, name);

            if (File.Exists(record))
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>The name Claude Code gives a folder's project folder.</summary>
    /// <param name="folder">The folder Claude Code runs in, as it spells it.</param>
    /// <returns>The slug.</returns>
    internal static string Slug(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var slug = new StringBuilder(folder.Length);

        foreach (var character in folder)
        {
            _ = slug.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        if (slug.Length <= SlugWidth)
        {
            return slug.ToString();
        }

        // The extension's own hash: a 32-bit string hash over the UTF-16 code units of
        // the whole path, its absolute value in base 36.
        var hash = 0;

        foreach (var character in folder)
        {
            hash = unchecked((hash << 5) - hash + character);
        }

        return slug.ToString(0, SlugWidth) + "-" + Base36(Math.Abs((long)hash));
    }

    private static string Base36(long value)
    {
        const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

        if (value is 0)
        {
            return "0";
        }

        var text = new StringBuilder();

        while (value > 0)
        {
            _ = text.Insert(0, Digits[(int)(value % 36)]);
            value /= 36;
        }

        return text.ToString();
    }

    /// <summary>Whether a per-process file is the client's: its <c>procStart</c> is the client's creation time, to the tick.</summary>
    private static bool TheClients(JsonElement file, long clientStarted)
    {
        if (!file.TryGetProperty("procStart", out var start))
        {
            return false;
        }

        // Written as text by every version measured; a number is read too, in case a
        // release changes that.
        return start.ValueKind switch
        {
            JsonValueKind.String => long.TryParse(start.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var text) && text == clientStarted,
            JsonValueKind.Number => start.TryGetInt64(out var number) && number == clientStarted,
            _ => false,
        };
    }
}
