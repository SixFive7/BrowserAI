// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json;
using BrowserAI.Interop;

namespace BrowserAI.Coordination;

/// <summary>How a background ended cleanly, as its record says.</summary>
internal enum BackgroundEnd
{
    /// <summary>It was asked through its pipe to close every session and end: the uninstall hook, or the suite.</summary>
    Stopped,

    /// <summary>It ended to let an update install, after every relay agreed or a person chose Install now.</summary>
    Update,

    /// <summary>Windows told it the session is ending: a sign-out or a shutdown.</summary>
    SessionEnding,

    /// <summary>The Task Scheduler's End command, or <c>schtasks /end</c>, asked its window to close.</summary>
    EndCommand,
}

/// <summary>What one background's record says.</summary>
/// <param name="ProcessId">The background's pid.</param>
/// <param name="CreatedFileTime">Its creation time, the other half of its identity.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="Build">The version it was.</param>
/// <param name="Image">The file it ran from.</param>
/// <param name="Ended">How it ended cleanly, or <see langword="null"/> while it runs and after a crash.</param>
/// <param name="EndedAt">When it ended cleanly, or <see langword="null"/>.</param>
/// <param name="ExitCode">Its exit code, when a relay that held a handle on it saw it go; <see langword="null"/> otherwise.</param>
/// <param name="ExitedAt">When that relay saw it go, or <see langword="null"/>.</param>
internal sealed record BackgroundRecordState(
    int ProcessId,
    long CreatedFileTime,
    DateTimeOffset StartedAt,
    string Build,
    string Image,
    BackgroundEnd? Ended,
    DateTimeOffset? EndedAt,
    int? ExitCode,
    DateTimeOffset? ExitedAt);

/// <summary>
/// The file a background writes when it starts and marks when it ends cleanly, so
/// that an end without that mark is a crash every relay can name.
/// </summary>
/// <remarks>
/// <para>
/// <b>R, the maintainer's words of 2026-10-08, verbatim: <i>"R I like option 1 and the
/// call response"</i> and <i>"r ok"</i>.</b> A background that ends without a clean exit
/// is recorded as crashed, and from then on every call is answered at once with the
/// crash text, which sends the person at the computer to the log and the bug report
/// and to the Start Menu; nothing restarts it by itself, and only that person's start
/// clears the record. A sign-out, a shutdown and the Task Scheduler's End command are
/// told to the background's hidden window and recorded as clean ends, so none of them
/// reads as a crash.
/// </para>
/// <para>
/// <b>One file per background</b>, under the data root and named for the background's
/// pipe, so the suite's backgrounds over scratch roots and pipes never share one with
/// the real install's. It is replaced whole on every write, through a file beside it
/// and a rename, so a reader meets the old record or the new one and never half of
/// either; a record that does not parse is read as absent, which is the safe
/// direction: a relay then says no background runs, not that one crashed.
/// </para>
/// <para>
/// <b>The identity is the pid and its creation time</b>, the standing rule of this
/// repository: a pid alone is reused within seconds, so a record whose pid now names
/// another process is a background that has gone.
/// </para>
/// </remarks>
internal static class BackgroundRecord
{
    /// <summary>The folder under the data root that holds the records.</summary>
    public const string DirectoryName = "background";

    /// <summary>The record of the background that serves one pipe.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <param name="pipeName">The background's full pipe name.</param>
    /// <returns>The file's path.</returns>
    public static string PathFor(string dataRoot, string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        var leaf = pipeName[(pipeName.LastIndexOf('\\') + 1)..];
        var safe = new StringBuilder(leaf.Length);

        foreach (var character in leaf)
        {
            _ = safe.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_');
        }

        return Path.Combine(dataRoot, DirectoryName, safe + ".json");
    }

    /// <summary>
    /// How long a write goes on trying to rename itself over a record a reader has
    /// open: a hang detector over a read that takes one call, chosen and not measured.
    /// </summary>
    public static TimeSpan WriteBound { get; } = ProcessBounds.BackgroundRecordWriteBound;

    /// <summary>How long a write waits between two tries of the rename.</summary>
    public static TimeSpan WriteRetry { get; } = ProcessBounds.BackgroundRecordWriteRetry;

    /// <summary>Writes this process's record as a background that has started.</summary>
    /// <param name="path">The record's path.</param>
    /// <param name="build">This build's version.</param>
    /// <param name="image">This process's image.</param>
    /// <param name="now">The time.</param>
    /// <returns>What was written.</returns>
    public static BackgroundRecordState Started(string path, string build, string image, DateTimeOffset now)
    {
        var state = new BackgroundRecordState(
            Environment.ProcessId,
            ProcessLiveness.CreationTimeOfThisProcess(),
            now,
            build,
            image,
            Ended: null,
            EndedAt: null,
            ExitCode: null,
            ExitedAt: null);

        Write(path, state);
        return state;
    }

    /// <summary>Marks this process's record as ended cleanly, if it is still this process's.</summary>
    /// <param name="path">The record's path.</param>
    /// <param name="how">How it ended.</param>
    /// <param name="now">The time.</param>
    /// <returns>Whether the record was this process's and is marked now.</returns>
    /// <remarks>
    /// <b>Never throws</b>: it is called from the hidden window's procedure at a
    /// sign-out and from an update's hand-over, where an exception would end the
    /// process mid-way. A record that cannot be written within <see cref="WriteBound"/>
    /// is answered <see langword="false"/>, and the end then reads as a crash, which is
    /// the direction the record is built to err in.
    /// </remarks>
    public static bool EndedCleanly(string path, BackgroundEnd how, DateTimeOffset now)
    {
        if (Read(path) is not { } state
            || state.ProcessId != Environment.ProcessId
            || state.CreatedFileTime != ProcessLiveness.CreationTimeOfThisProcess())
        {
            return false;
        }

        try
        {
            Write(path, state with { Ended = how, EndedAt = now });
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes what a relay saw when the background it was connected to went: its exit
    /// code and the time, when the record does not already say it ended cleanly.
    /// </summary>
    /// <param name="path">The record's path.</param>
    /// <param name="processId">The background's pid, from its answer to the relay's first message.</param>
    /// <param name="createdFileTime">Its creation time, read off the handle the relay held.</param>
    /// <param name="exitCode">Its exit code, or <see langword="null"/> when the relay found it gone and could not read one.</param>
    /// <param name="now">The time.</param>
    /// <returns>Whether the record was that background's, carried no clean end, and is written now.</returns>
    public static bool Exited(string path, int processId, long createdFileTime, int? exitCode, DateTimeOffset now)
    {
        if (Read(path) is not { } state
            || state.ProcessId != processId
            || state.CreatedFileTime != createdFileTime
            || state.Ended is not null)
        {
            return false;
        }

        Write(path, state with { ExitCode = exitCode, ExitedAt = now });
        return true;
    }

    /// <summary>Removes the record: a person's start does this before it starts a new background.</summary>
    /// <param name="path">The record's path.</param>
    public static void Clear(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // No folder, no record: nothing to clear.
        }
    }

    /// <summary>Reads a record, or <see langword="null"/> when there is none or it does not parse.</summary>
    /// <param name="path">The record's path.</param>
    /// <returns>The record.</returns>
    public static BackgroundRecordState? Read(string path)
    {
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;

            return new BackgroundRecordState(
                root.GetProperty("pid").GetInt32(),
                root.GetProperty("created").GetInt64(),
                DateTimeOffset.Parse(root.GetProperty("startedAt").GetString()!, CultureInfo.InvariantCulture),
                root.GetProperty("build").GetString() ?? string.Empty,
                root.GetProperty("image").GetString() ?? string.Empty,
                root.TryGetProperty("ended", out var ended) && ended.ValueKind is JsonValueKind.String
                    && Enum.TryParse<BackgroundEnd>(ended.GetString(), out var how) ? how : null,
                Time(root, "endedAt"),
                root.TryGetProperty("exitCode", out var code) && code.ValueKind is JsonValueKind.Number ? code.GetInt32() : null,
                Time(root, "exitedAt"));
        }
        catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Time(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture)
            : null;

    private static void Write(string path, BackgroundRecordState state)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("pid", state.ProcessId);
            writer.WriteNumber("created", state.CreatedFileTime);
            writer.WriteString("startedAt", state.StartedAt.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("build", state.Build);
            writer.WriteString("image", state.Image);

            if (state.Ended is { } how)
            {
                writer.WriteString("ended", how.ToString());
            }

            if (state.EndedAt is { } endedAt)
            {
                writer.WriteString("endedAt", endedAt.ToString("O", CultureInfo.InvariantCulture));
            }

            if (state.ExitCode is { } exitCode)
            {
                writer.WriteNumber("exitCode", exitCode);
            }

            if (state.ExitedAt is { } exitedAt)
            {
                writer.WriteString("exitedAt", exitedAt.ToString("O", CultureInfo.InvariantCulture));
            }

            writer.WriteEndObject();
        }

        // Beside the record and renamed over it, so a reader never meets half a file.
        var temporary = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";

        File.WriteAllBytes(temporary, buffer.ToArray());

        // ⚠️ A READER HOLDS THE RENAME OFF. Windows renames a file over another only
        // when nobody has the target open, sharing it for deletion or not, measured by
        // lane ARCH's helper T1 on 2026-10-09 (BackgroundRecordTests). A relay or a
        // person's start reads the record whole in one call, so the rename is tried
        // again until WriteBound has gone by, and the file beside it never outlives a
        // write that gave up.
        var deadline = Environment.TickCount64 + (long)WriteBound.TotalMilliseconds;

        while (true)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                if (Environment.TickCount64 >= deadline)
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                    {
                        // The rename's failure is the one worth reporting.
                    }

                    throw;
                }

                Thread.Sleep(WriteRetry);
            }
        }
    }
}
