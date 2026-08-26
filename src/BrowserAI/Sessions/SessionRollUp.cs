// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json;

namespace BrowserAI.Sessions;

/// <summary>One session, as the per-root roll-up lists it.</summary>
/// <param name="Directory">The session directory.</param>
/// <param name="Purpose">What it says it is for.</param>
/// <param name="Created">When it was created.</param>
/// <param name="LastUsed">When it was last used.</param>
/// <param name="Bytes">Its size on disk.</param>
internal sealed record RollUpEntry(
    string Directory,
    string Purpose,
    DateTimeOffset Created,
    DateTimeOffset LastUsed,
    long Bytes);

/// <summary>
/// The file beside a set of sessions that says what they are, for whoever opens
/// the directory next.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Extracted from <c>Artifacts.ArtifactRouter</c> on 2026-08-26, which is
/// the day everything else in that type was deleted.</b> The router routed
/// caller-named files into typed folders, swept the output root, pinned the
/// names the child had published, kept a reservation set and wrote a
/// <c>session.json</c> index; none of that exists. Nothing between the two
/// servers touches a file any more. The roll-up survives because it is not
/// about traffic at all — it is the session system telling a human, or an agent
/// arriving cold, what sits under a directory.
/// </para>
/// <para>
/// <b>Scoped by root, never by machine.</b> BrowserAI is registered once and
/// serves every repository on the host, so an aggregate over everything would
/// pull unrelated projects' sessions into whatever context happens to be open.
/// That is a noise problem rather than a security boundary — the paths were the
/// caller's own choice — but noise in an agent's context is a real cost, and the
/// cheap fix is to default the aggregate to the root in play.
/// </para>
/// </remarks>
internal static class SessionRollUp
{
    /// <summary>The per-root roll-up, beside the sessions it covers.</summary>
    public const string FileName = "browserai-sessions.json";

    /// <summary>
    /// The schema version stamped into the roll-up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The version ships and a parser does not.</b> This file is written by
    /// BrowserAI and read by nobody in this build. A strict reader with no
    /// caller is a shape this project has already deleted once. What a version
    /// buys with no reader is the thing that cannot be added afterwards: a later
    /// BrowserAI meeting a file written by this one can tell what it is looking
    /// at, and adding the field later would leave every file written before the
    /// change indistinguishable from version 1, forever.
    /// </para>
    /// <para>
    /// ⚠️ <b>3 since 2026-08-26 (previously 2, and 1 before that).</b> Version 2
    /// dropped the per-session <c>mode</c> when session modes were deleted.
    /// Version 3 is the day the sibling file this constant also stamped —
    /// <c>session.json</c>, the artifact index — stopped existing at all. The
    /// roll-up's own members are unchanged; what moved is that a reader can no
    /// longer expect an index beside the sessions this file lists, and a file
    /// that has lost a companion should announce it rather than be read as a
    /// version-2 file somebody half-wrote.
    /// </para>
    /// </remarks>
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,

        // A Windows path is mostly backslashes and this file is read by people
        // and by models. The default encoder additionally escapes characters a
        // path may legitimately carry, which round-trips perfectly and is
        // unreadable.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Rewrites the roll-up covering one root.</summary>
    /// <param name="root">The directory the sessions sit under.</param>
    /// <param name="sessions">Every session beneath it, already filtered.</param>
    /// <returns>Whether it was written. A caller that names the file must say so when it was not.</returns>
    public static bool Write(string root, IReadOnlyList<RollUpEntry> sessions)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sessions);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteString(
                "_what_this_is",
                "Every BrowserAI session beneath this directory, and nothing outside it. Written by BrowserAI when a session under this root is opened or destroyed. A machine-wide view is available by asking browserai_list about a wider directory.");

            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("root", root);
            writer.WriteString("updated", Stamp(DateTimeOffset.Now));
            writer.WriteNumber("sessions", sessions.Count);

            writer.WriteStartArray("beneath");

            foreach (var session in sessions)
            {
                writer.WriteStartObject();
                writer.WriteString("directory", session.Directory);
                writer.WriteString("purpose", session.Purpose);
                writer.WriteString("created", Stamp(session.Created));
                writer.WriteString("lastUsed", Stamp(session.LastUsed));
                writer.WriteNumber("bytes", session.Bytes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return TryWrite(Path.Combine(root, FileName), buffer.ToArray());
    }

    private static string Stamp(DateTimeOffset moment) => moment.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes a file BrowserAI owns, and never lets failing to do so fail the
    /// call that provoked it.
    /// </summary>
    /// <remarks>
    /// The roll-up is a record of work that already happened. A read-only volume
    /// or a virus scanner holding the file open must not turn a session that was
    /// opened into a session that failed to open — but it must not be silent
    /// either, so the answer carries what could not be written.
    /// </remarks>
    private static bool TryWrite(string path, byte[] bytes)
    {
        try
        {
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
