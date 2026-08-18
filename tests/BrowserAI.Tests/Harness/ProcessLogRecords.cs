// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Hosting;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One process's own records, read out of the shared process log.
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
/// <b>Scoped to one pid, never to the whole file.</b> The log is machine-wide
/// and every BrowserAI on the box appends to it, so an unscoped read is
/// answerable by some other run — the same vacuity that made
/// <c>SaturationTests</c>' record count pass on a machine with history and fail
/// on a fresh one.
/// </para>
/// </remarks>
internal static class ProcessLogRecords
{
    /// <summary>Every record the given process wrote to the shared process log.</summary>
    /// <param name="processId">The pid whose records to collect.</param>
    /// <returns>Its records, joined, or an empty string if it wrote none.</returns>
    public static string ForPid(int processId)
    {
        var directory = new LocalAppDataPaths().LogDirectory;

        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        // The header form FileLoggerProvider writes: "<level>  pid=<n>  ". The
        // trailing separator matters -- without it pid=1 also matches pid=1234.
        var marker = string.Create(CultureInfo.InvariantCulture, $"  pid={processId}  ");
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
