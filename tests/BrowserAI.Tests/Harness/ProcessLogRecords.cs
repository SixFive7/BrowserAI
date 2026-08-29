// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One process's own records, read out of the shared process log and selected by
/// the pair that identifies it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This exists because stderr is the wrong place to assert a record was
/// written, and CI is what proved it.</b> The two diagnostic channels are not
/// equivalent and only one of them is durable, which
/// <c>ProcessLog</c>'s own remarks already said before anything relied on it:
/// <see cref="Logging.RollingFileWriter"/> "buffers nothing … a record that has
/// been logged is already on disk", whereas stderr goes through
/// <c>AddConsole</c>, which hands records to a background processor thread. A
/// process killed with <c>TerminateProcess</c> — which is exactly what
/// <see cref="SliceRun"/> does, deliberately, to prove containment — discards
/// whatever that queue still held.
/// </para>
/// <para>
/// <b>So a test asserting "the product recorded X" must read the file.</b> On a
/// developer's machine the queue drains before the kill and stderr looks
/// complete; on a four-core runner carrying 431 tests it does not, and
/// <c>ProtocolSplitTests</c> went red on the runner for a record the product had
/// written correctly. Measured twice, 2026-08-18, on two consecutive CI runs
/// that lost different amounts of the tail — which is the signature of a queue
/// rather than of an absent call.
/// </para>
/// <para>
/// <b>Scoped to one process's whole identity — <c>(pid, creationFileTime)</c> —
/// never to the whole file and never to a bare pid.</b> The log is machine-wide
/// and every BrowserAI on the box appends to it, so an unscoped read is
/// answerable by some other run — the same vacuity that made
/// <c>SaturationTests</c>' record count pass on a machine with history and fail
/// on a fresh one. A <i>pid</i>-scoped read is the same vacuity wearing a scope:
/// the log is retained for thirty days and Windows reuses pids inside that
/// window, so a bare pid eventually selects a stranger's records rather than
/// none.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-29 (previously <c>ForPid(int)</c>, matching
/// <c>"  pid=&lt;n&gt;@"</c> — the pid alone, with the creation FILETIME behind
/// the <c>@</c> read past and never compared).</b> The type's own remarks already
/// said a bare pid does not identify a writer, and the method's name said it did;
/// the name was the accurate one. <b>Demonstrated live</b> while the spawn-record
/// reclaim above it was being planted red: a read scoped to a live test host's pid
/// came back holding records written on 2026-08-24 by a different process that had
/// worn that number. The
/// pair is this repository's standing identity for a process — it is what
/// <c>ProcessIdentity.IsAlive(int, long)</c> takes, what <c>browserai.lock</c>
/// spells, and what <see cref="Logging.FileLoggerProvider"/> writes into every
/// record — and the pid-only entry point is gone rather than caveated, because a
/// reader that can be handed half an identity will be.
/// </para>
/// </remarks>
internal static class ProcessLogRecords
{
    /// <summary>
    /// Every record the given process wrote to the machine's shared process log.
    /// </summary>
    /// <param name="processId">The pid half of the writer's identity.</param>
    /// <param name="createdFileTime">
    /// The creation-time half, as <c>ProcessIdentity.CreationTimeOf</c> reads it.
    /// </param>
    /// <returns>Its records, joined, or an empty string if it wrote none.</returns>
    public static string For(int processId, long createdFileTime) =>
        In(BrowserAiPaths.Real.LogDirectory, processId, createdFileTime);

    /// <summary>
    /// The same read, against a nominated log directory.
    /// </summary>
    /// <remarks>
    /// <b>This is what lets the identity scope be watched working.</b> The live
    /// read above can only be answered by whatever the machine's log happens to
    /// hold, and on a box whose log holds no stranger wearing this pid it passes
    /// by matching nothing — so a control has to hand the reader a directory it
    /// composed, holding exactly the record that must come back and the one that
    /// must not.
    /// </remarks>
    /// <param name="directory">The log directory to read.</param>
    /// <param name="processId">The pid half of the writer's identity.</param>
    /// <param name="createdFileTime">The creation-time half.</param>
    /// <returns>Its records, joined, or an empty string if it wrote none.</returns>
    public static string In(string directory, int processId, long createdFileTime)
    {
        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        // The header form FileLoggerProvider writes: "<level>  pid=<n>@<ft>  ".
        // Both separators are part of the marker and neither is decoration. The
        // '@' is what stops pid=1 matching pid=1234; the two spaces after the
        // FILETIME are what stops a creation time matching one that merely
        // begins with it, and they are always there because the provider writes
        // "  " and then the category.
        var marker = string.Create(CultureInfo.InvariantCulture, $"  pid={processId}@{createdFileTime}  ");
        var collected = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(directory, "browserai-*.log").Order(StringComparer.Ordinal))
        {
            string text;

            try
            {
                // FileShare.ReadWrite | Delete because a hundred other BrowserAIs
                // may be appending to this file right now, and a reader must
                // never be locked out of the log it came to read.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                text = reader.ReadToEnd();
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A file that rolled or was swept between the enumeration and
                // the open. The records being looked for are in one of the
                // others, and failing here would report the wrong thing.
                continue;
            }

            foreach (var line in text.Split('\n'))
            {
                if (line.Contains(marker, StringComparison.Ordinal))
                {
                    _ = collected.AppendLine(line.TrimEnd('\r'));
                }
            }
        }

        return collected.ToString();
    }
}
