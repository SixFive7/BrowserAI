// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Waits for the <c>job-launcher</c> probe's done marker, and gives up the
/// moment the launcher itself dies.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Watching the launcher is not an optimisation — it is what makes the
/// wait honest.</b> The launcher gives its driven child a bounded time to
/// report and then throws, which kills it, and <c>KILL_ON_JOB_CLOSE</c> takes
/// the whole tree with it. A wait that only polls for a file then sits out the
/// rest of its own patience waiting for a marker no living process can write —
/// and reports the elapsed time as if that were the bound that failed.
/// </para>
/// <para>
/// <b>Measured 2026-08-17.</b> A Firefox launch that failed at t=60 s inside the
/// launcher was reported by the host at t=180 s as <i>"the launcher never wrote
/// 'done' within 180 s"</i>. Read cold, that is a timing budget being too tight
/// on a slow browser; it was nothing of the kind, and observing the machine
/// during the remaining two minutes showed no browser, no <c>node</c> and no
/// launcher alive at all. Both halves of the fix are here: the wait ends when
/// the launcher does, and the message carries what the launcher left rather
/// than a path to it.
/// </para>
/// <para>
/// <b>The evidence is inlined rather than pointed at.</b> Scratch trees are
/// deleted when a test unwinds, so a failure naming a directory names something
/// the reader cannot open. Every small file in it is read into the message
/// instead — which is also why no file name is spelled here: whatever the
/// launcher wrote is what gets reported, so renaming one of its logs cannot
/// silently drop it from the failure.
/// </para>
/// <para>
/// ⚠️ <b>Every file worth reading here is one somebody is still holding open,
/// and until 2026-08-30 the reader could not open one.</b> <i>Corrected
/// 2026-08-30 (previously <c>File.ReadAllText</c> for the content and
/// <c>FileInfo.Length</c> for the byte count).</i> A dump is taken at the
/// moment a launch did <b>not</b> happen, so the writer of every capture file
/// in the tree — the driver's <c>stderr</c> tee, the launcher's own
/// redirections — is by construction still alive and still holding its handle.
/// Both halves of the old reader assumed the opposite, and both are fixed on
/// <see cref="Evidence"/>.
/// </para>
/// </remarks>
internal static class LauncherWait
{
    /// <summary>
    /// Waits for <paramref name="donePath"/> to appear, failing early if the
    /// launcher exits without writing it.
    /// </summary>
    /// <param name="donePath">The marker the launcher writes last.</param>
    /// <param name="patience">The whole budget, which is also the launcher's.</param>
    /// <param name="scratch">The tree to inline into a failure.</param>
    /// <param name="launcher">The launcher's pid.</param>
    /// <param name="launcherCreated">Its creation time, so a recycled pid cannot read as alive.</param>
    /// <returns>A task that completes when the marker exists.</returns>
    /// <exception cref="TimeoutException">The launcher died, or never reported.</exception>
    public static async Task ForDoneAsync(
        string donePath,
        TimeSpan patience,
        string scratch,
        int launcher,
        long launcherCreated)
    {
        var deadline = Stopwatch.StartNew();

        while (true)
        {
            if (File.Exists(donePath))
            {
                return;
            }

            var alive = ProcessIdentity.IsAlive(launcher, launcherCreated);

            // Re-read after the liveness check, never before: a launcher that
            // wrote the marker and then exited must read as a success, and the
            // two reads either side close that window.
            if (!alive && File.Exists(donePath))
            {
                return;
            }

            if (!alive)
            {
                throw new TimeoutException(
                    $"The launcher (pid {launcher.ToString(System.Globalization.CultureInfo.InvariantCulture)}) exited after {deadline.Elapsed.TotalSeconds:F1} s without writing '{donePath}', so nothing was ever going to write it."
                    + Environment.NewLine + Evidence(scratch));
            }

            if (deadline.Elapsed >= patience)
            {
                throw new TimeoutException(
                    $"The launcher never wrote '{donePath}' within {patience.TotalSeconds:F0} s, and it is still running."
                    + Environment.NewLine + Evidence(scratch));
            }

            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Everything the launcher left in its tree, inlined into a failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The share mode is the instrument.</b> <c>File.ReadAllText</c> opens
    /// with <c>FileShare.Read</c>, and a share mode is a statement about what
    /// <i>other</i> handles may do — which Windows checks against the accesses
    /// already granted on the file. A live writer holds <c>GENERIC_WRITE</c>, a
    /// reader offering only <c>FILE_SHARE_READ</c> does not permit it, and the
    /// open is refused <b>however permissive the writer was</b>. Node's
    /// <c>fs.openSync</c> shares read, write <i>and</i> delete precisely so that
    /// a log can be tailed while it is being written, and the old reader lost to
    /// it anyway. So the open here asks for
    /// <c>FileShare.ReadWrite | FileShare.Delete</c>: it permits the writer to go
    /// on writing, and it permits whoever owns the tree to delete it out from
    /// under the read.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-29, in the run this was written for.</b> A Firefox
    /// arm stalled out Playwright's own 180 s <c>initializeServer</c> budget and
    /// the dump it produced said <c>(unreadable: … because it is being used by
    /// another process)</c> for <b>all three</b> capture files — the whole
    /// account of the stall, in three files this instrument had just walked, and
    /// none of it in the failure. Reproduced in process 2026-08-30, both
    /// directions: a writer holding a file with node's own sharing is refused to
    /// <c>File.ReadAllText</c> and opens cleanly with the share mode above.
    /// </para>
    /// <para>
    /// ⚠️ <b>The byte count comes off the handle, and where there is no handle
    /// there is no number.</b> <c>FileInfo.Length</c> is the size the directory
    /// enumeration carried when it produced that <c>FileInfo</c> — cached, never
    /// re-read — so the old dump printed a figure nothing had measured beside
    /// files it had just failed to open. <b>And the figure was wrong, which was
    /// not the expectation.</b> Measured 2026-08-30 through the enumeration this
    /// method actually performs, <c>EnumerateFiles("*")</c>, a file holding 63
    /// bytes behind a live writer's handle reported <b>0</b> — the same phantom
    /// <c>(0 bytes)</c> the 2026-08-29 dump carried, reproduced exactly. The same
    /// file queried by its own name in a separate probe minutes earlier reported
    /// 63, so <i>which</i> enumeration shape hits the stale entry is not
    /// established here and is not relied on: NTFS updates that entry lazily and
    /// the guarantee is one-way. What is fixed is the provenance — a length read
    /// from the open stream, or, for a file nothing could open, no length at all.
    /// </para>
    /// </remarks>
    /// <param name="scratch">The tree.</param>
    /// <returns>The evidence block.</returns>
    public static string Evidence(string scratch)
    {
        var report = new StringBuilder($"--- what the launcher left in {scratch} ---");

        List<FileInfo> files;

        try
        {
            files =
            [
                .. new DirectoryInfo(scratch)
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .OrderBy(file => file.Name, StringComparer.Ordinal),
            ];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return report.Append(Environment.NewLine).Append("(unreadable: ").Append(failure.Message).Append(')').ToString();
        }

        if (files.Count is 0)
        {
            return report.Append(Environment.NewLine).Append("(the tree holds no files)").ToString();
        }

        foreach (var file in files)
        {
            long length;
            string text;

            try
            {
                // The two flags are the whole fix, and the reasoning is on this
                // method's remarks: every capture file in this tree has a live
                // writer, so a reader that does not permit writing cannot open
                // any of them.
                using var stream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                // Off the handle rather than out of the directory entry the
                // enumeration cached, so the number beside the name was measured
                // by the thing that read the bytes.
                length = stream.Length;

                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                text = reader.ReadToEnd();
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // No byte count on this branch, deliberately: nothing opened the
                // file, so nothing measured it, and a cached number here reads as
                // a measurement of a file the very next clause says could not be
                // read. An honest "(unreadable: …)" survives -- what must no
                // longer be the reason for it is our own share mode.
                _ = report.Append(Environment.NewLine)
                    .Append("--- ").Append(file.Name).Append(" ---")
                    .Append(Environment.NewLine)
                    .Append("(unreadable: ").Append(failure.Message).Append(')');

                continue;
            }

            _ = report.Append(Environment.NewLine)
                .Append("--- ").Append(file.Name).Append(" (").Append(length).Append(" bytes) ---")
                .Append(Environment.NewLine)

                // Truncated, because a browser's stderr can run to megabytes and
                // a failure message nobody can read is one nobody reads.
                .Append(text.Length <= 4000 ? text : text[..4000] + "…");
        }

        return report.ToString();
    }
}
