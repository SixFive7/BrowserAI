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
            _ = report.Append(Environment.NewLine)
                .Append("--- ").Append(file.Name).Append(" (").Append(file.Length).Append(" bytes) ---")
                .Append(Environment.NewLine);

            try
            {
                var text = File.ReadAllText(file.FullName);

                // Truncated, because a browser's stderr can run to megabytes and
                // a failure message nobody can read is one nobody reads.
                _ = report.Append(text.Length <= 4000 ? text : text[..4000] + "…");
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                _ = report.Append("(unreadable: ").Append(failure.Message).Append(')');
            }
        }

        return report.ToString();
    }
}
