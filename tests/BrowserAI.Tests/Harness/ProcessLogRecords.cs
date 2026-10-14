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
/// <see cref="Logging.RollingFileWriter"/> "buffers nothing ... a record that has
/// been logged is already on disk", whereas stderr goes through
/// <c>AddConsole</c>, which hands records to a background processor thread. A
/// process killed with <c>TerminateProcess</c> -- which is exactly what
/// <see cref="SliceRun"/> does, deliberately, to prove containment -- discards
/// whatever that queue still held.
/// </para>
/// <para>
/// <b>So a test asserting "the product recorded X" must read the file.</b> On a
/// developer's machine the queue drains before the kill and stderr looks
/// complete; on a four-core runner carrying 431 tests it does not, and
/// <c>ProtocolSplitTests</c> went red on the runner for a record the product had
/// written correctly. Measured twice, 2026-08-18, on two consecutive CI runs
/// that lost different amounts of the tail -- which is the signature of a queue
/// and not of an absent call.
/// </para>
/// <para>
/// <b>Scoped to one process's whole identity -- <c>(pid, creationFileTime)</c> --
/// never to the whole file and never to a bare pid.</b> The log is machine-wide
/// and every BrowserAI on the box appends to it, so an unscoped read is
/// answerable by some other run -- the same vacuity that made
/// <c>SaturationTests</c>' record count pass on a machine with history and fail
/// on a fresh one. A <i>pid</i>-scoped read is the same vacuity wearing a scope:
/// the log is retained for thirty days and Windows reuses pids inside that
/// window, so a bare pid eventually selects a stranger's records and not
/// none.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-29 (previously <c>ForPid(int)</c>, matching
/// <c>"  pid=&lt;n&gt;@"</c> -- the pid alone, with the creation FILETIME behind
/// the <c>@</c> read past and never compared).</b> The type's own remarks already
/// said a bare pid does not identify a writer, and the method's name said it did;
/// the name was the accurate one. <b>Demonstrated live</b> while the spawn-record
/// reclaim above it was being planted red: a read scoped to a live test host's pid
/// came back holding records written on 2026-08-24 by a different process that had
/// worn that number. The
/// pair is this repository's standing identity for a process -- it is what
/// <c>ProcessIdentity.IsAlive(int, long)</c> takes, what <c>browserai.lock</c>
/// spells, and what <see cref="Logging.FileLoggerProvider"/> writes into every
/// record -- and the pid-only entry point is gone and not caveated, because a
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
    public static async Task<string> ForAsync(int processId, long createdFileTime) =>
        await InAsync(BrowserAiPaths.Real.LogDirectory, processId, createdFileTime);

    /// <summary>
    /// The same records, read only out of the files the process could have written to:
    /// those named for its UTC day of creation or a later one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For a read an arm makes while it holds something up, and for a poll, which reads the
    /// log again and again.</b> The log keeps thirty days of every BrowserAI on the machine,
    /// 186 MB in 42 files on 2026-10-14, and a read of all of it each time is a load of the
    /// suite's own making. <c>RollingFileWriter</c>
    /// names every file <c>browserai-&lt;UTC day&gt;-&lt;index&gt;.log</c> for the day of
    /// the records it holds, so a process cannot have written into a file named for a day
    /// before it was created. A file whose name carries no day is read.
    /// </para>
    /// <para>
    /// <i>Added 2026-10-14</i> for <c>ServerRegistryReapTests</c>' wait on the startup sweep's
    /// reap.
    /// </para>
    /// </remarks>
    /// <param name="processId">The pid half of the writer's identity.</param>
    /// <param name="createdFileTime">The creation-time half.</param>
    /// <returns>Its records, joined, or an empty string if it wrote none.</returns>
    public static async Task<string> SinceItsCreationAsync(int processId, long createdFileTime) =>
        await InAsync(BrowserAiPaths.Real.LogDirectory, processId, createdFileTime, FilesFrom(createdFileTime));

    /// <summary>
    /// Which log files a process created at <paramref name="createdFileTime"/> could have
    /// written to, for <see cref="InAsync"/>.
    /// </summary>
    /// <param name="createdFileTime">The process's creation time.</param>
    /// <returns>True for a file named for its UTC day of creation or a later one, and for a name with no day.</returns>
    public static Func<string, bool> FilesFrom(long createdFileTime)
    {
        var day = DateTime.FromFileTimeUtc(createdFileTime).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        return file => !NamedForADayBefore(file, day);
    }

    /// <summary>Whether a log file's name carries a UTC day earlier than <paramref name="day"/>.</summary>
    /// <param name="file">The file's path.</param>
    /// <param name="day">A UTC day, spelled <c>yyyyMMdd</c>.</param>
    /// <returns><see langword="false"/> for a file named for that day or a later one, and for a name with no day.</returns>
    private static bool NamedForADayBefore(string file, string day)
    {
        const string Prefix = "browserai-";
        var name = Path.GetFileName(file);

        if (name.Length < Prefix.Length + day.Length || !name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var named = name.AsSpan(Prefix.Length, day.Length);

        foreach (var character in named)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return named.CompareTo(day, StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// The same read, against a nominated log directory.
    /// </summary>
    /// <remarks>
    /// <b>This is what lets the identity scope be watched working.</b> The live
    /// read above can only be answered by whatever the machine's log happens to
    /// hold, and on a box whose log holds no stranger wearing this pid it passes
    /// by matching nothing -- so a control has to hand the reader a directory it
    /// composed, holding exactly the record that must come back and the one that
    /// must not.
    /// </remarks>
    /// <param name="directory">The log directory to read.</param>
    /// <param name="processId">The pid half of the writer's identity.</param>
    /// <param name="createdFileTime">The creation-time half.</param>
    /// <param name="reads">Which of the directory's log files are read; every one when none is given.</param>
    /// <returns>Its records, joined, or an empty string if it wrote none.</returns>
    public static async Task<string> InAsync(string directory, int processId, long createdFileTime, Func<string, bool>? reads = null)
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

        foreach (var file in Directory.EnumerateFiles(directory, "browserai-*.log").Where(file => reads?.Invoke(file) ?? true).Order(StringComparer.Ordinal))
        {
            string text;

            try
            {
                // FileShare.ReadWrite | Delete because a hundred other BrowserAIs
                // may be appending to this file right now, and a reader must
                // never be locked out of the log it came to read.
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous);
                using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                text = await reader.ReadToEndAsync();
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
