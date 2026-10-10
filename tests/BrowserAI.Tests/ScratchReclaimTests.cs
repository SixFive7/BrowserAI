// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The reclaim of the scratch root every worktree's runs share, driven from a second
/// test host the way another worktree's run drives it.
/// </summary>
/// <remarks>
/// <para>
/// <b>5 a, the maintainer's words of 2026-10-10, verbatim: "5 a".</b> Until that day
/// the reclaim deleted every folder under <c>%LOCALAPPDATA%\BrowserAI-test-scratch</c>
/// with no owner check, and that root is shared by every checkout of this repository.
/// It was proven to delete a live folder of another worktree's gate on 2026-10-08,
/// whose orphaned install left a Settings entry, a Start Menu shortcut, a sign-in
/// task and a user PATH entry behind; and it is the one mechanism found that leaves
/// the end state of Playwright's installer dying with its own <c>__dirlock</c> gone
/// the same afternoon (<c>docs/evidence/2026-10-08-pw-lock</c>).
/// </para>
/// </remarks>
internal sealed partial class ScratchReclaimTests
{
    /// <summary>
    /// A second test host's reclaim takes the folders whose owners are gone, names
    /// each with the time and itself, and leaves a live run's folder alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two hosts, because one cannot show it.</b> The pass runs once per process,
    /// before anything of that process exists, so a host's own pass can never meet
    /// a folder of its own that is in use. Another worktree's run meets this run's
    /// folders exactly that way, and the child started here is that run: the same
    /// assembly, the same pass, over the same root, while this host holds a folder.
    /// </para>
    /// <para>
    /// <b>Four folders, one for each answer.</b> This host's own, whose owner is
    /// alive and which must survive; one whose owner record names this pid with a
    /// creation time no process of it had, which is a run that is gone; one with no
    /// record made a moment ago, which a harness older than the record may be using;
    /// and one with no record made two days ago, which nothing still runs in.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnotherTestHostsReclaimTakesOnlyTheFoldersWhoseOwnersAreGone()
    {
        // The second host's half. The variable is set only by the launcher below.
        if (Environment.GetEnvironmentVariable(ScratchRoot.ReclaimProbeVariable) is { Length: > 0 } probeReport)
        {
            var lines = ScratchRoot.ReclaimProfileScratchNow();
            var self = string.Create(CultureInfo.InvariantCulture, $"{Environment.ProcessId}@{ProcessIdentity.CreationTimeOf(Environment.ProcessId)}");

            await File.WriteAllLinesAsync(probeReport, [$"host={self}", .. lines]);
            return;
        }

        var host = Path.Combine(AppContext.BaseDirectory, "BrowserAI.Tests.exe");

        await Assert.That(File.Exists(host)).IsTrue().Because(host);

        using var live = ScratchDirectory.CreateUnderProfile("reclaim-live-owner");
        var marker = Path.Combine(live.Path, "still-here.txt");

        await File.WriteAllTextAsync(marker, "a live run's app root");

        var root = ScratchRoot.ProfileScratch;
        var dead = Path.Combine(root, $"reclaim-dead-owner-{Guid.NewGuid():N}");
        var young = Path.Combine(root, $"reclaim-unowned-young-{Guid.NewGuid():N}");
        var old = Path.Combine(root, $"reclaim-unowned-old-{Guid.NewGuid():N}");

        // ⚠️ A FIFTH, round 2 of the texts review, 2026-10-10, #230: a gone run's folder
        // holding a file this host keeps open with no delete sharing, so the second host
        // can take only part of it. Its line in the process log said "deleted" all the
        // same; planted red against that.
        var held = Path.Combine(root, $"reclaim-dead-owner-held-{Guid.NewGuid():N}");
        FileStream? holding = null;

        try
        {
            await File.WriteAllTextAsync(dead + ScratchRoot.OwnerSuffix, ScratchRoot.OwnerRecord(new InstallerLockHolder(Environment.ProcessId, 1)));
            _ = Directory.CreateDirectory(dead);
            await File.WriteAllTextAsync(Path.Combine(dead, "left-behind.txt"), "a killed run's app root");

            await File.WriteAllTextAsync(held + ScratchRoot.OwnerSuffix, ScratchRoot.OwnerRecord(new InstallerLockHolder(Environment.ProcessId, 1)));
            _ = Directory.CreateDirectory(held);
            await File.WriteAllTextAsync(Path.Combine(held, "gone.txt"), "a file the reclaim can take");
            holding = new FileStream(Path.Combine(held, "still-open.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

            _ = Directory.CreateDirectory(young);
            _ = Directory.CreateDirectory(old);
            Directory.SetCreationTimeUtc(old, DateTime.UtcNow.AddDays(-2));

            using var probe = ScratchDirectory.Create("reclaim-second-host");
            var report = Path.Combine(probe.Path, "reclaim.txt");

            var startInfo = new ProcessStartInfo(host)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            };

            startInfo.ArgumentList.Add("--disable-logo");
            startInfo.ArgumentList.Add("--treenode-filter");
            startInfo.ArgumentList.Add("/*/*/ScratchReclaimTests/" + nameof(AnotherTestHostsReclaimTakesOnlyTheFoldersWhoseOwnersAreGone));

            startInfo.Environment[ScratchRoot.ReclaimProbeVariable] = report;

            using var child = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{host}'.");

            // Started outside a job object, so the spawn record is the only thing that
            // can name it if this run is killed while it is running.
            SpawnRecord.Add(child.Id);

            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();

            using var patience = new CancellationTokenSource(TestDefaults.ProcessHang);

            await child.WaitForExitAsync(patience.Token);

            var console = await stdout + await stderr;

            await Assert.That(File.Exists(report)).IsTrue().Because(console);

            var said = await File.ReadAllLinesAsync(report);
            var everything = string.Join(Environment.NewLine, said);

            await Assert.That(File.Exists(marker))
                .IsTrue()
                .Because($"another test host's reclaim deleted '{live.Path}', whose owner, this host, is running.{Environment.NewLine}{everything}");
            await Assert.That(Directory.Exists(young)).IsTrue().Because(everything);
            await Assert.That(Directory.Exists(dead)).IsFalse().Because(everything);
            await Assert.That(File.Exists(dead + ScratchRoot.OwnerSuffix)).IsFalse().Because(everything);
            await Assert.That(Directory.Exists(old)).IsFalse().Because(everything);

            // Every deletion is named with the moment, in UTC, and with the host that did it.
            var deleter = said[0]["host=".Length..];

            foreach (var folder in new[] { dead, old })
            {
                var line = said.SingleOrDefault(entry => entry.StartsWith($"deleted {folder} ", StringComparison.Ordinal));

                await Assert.That(line).IsNotNull().Because(everything);
                await Assert.That(Deletion().IsMatch(line!)).IsTrue().Because(line!);
                await Assert.That(line!).Contains($" by {deleter}");
            }

            await Assert.That(said.Any(entry => entry.Contains(live.Path, StringComparison.OrdinalIgnoreCase))).IsFalse().Because(everything);

            // And the machine's process log carries the same, under the second host's own identity.
            var at = deleter.IndexOf('@', StringComparison.Ordinal);
            var records = ProcessLogRecords.For(
                int.Parse(deleter[..at], NumberStyles.None, CultureInfo.InvariantCulture),
                long.Parse(deleter[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture));

            await Assert.That(records).Contains(dead);
            await Assert.That(records).Contains(old);

            // The folder that would not go whole: the pass's own line and the process
            // log's both say part of it went, and the file still open is still there.
            await Assert.That(said.Any(entry => entry.StartsWith($"deleted part of {held} ", StringComparison.Ordinal))).IsTrue().Because(everything);
            await Assert.That(File.Exists(Path.Combine(held, "still-open.txt"))).IsTrue();
            await Assert.That(records).Contains($"deleted part of {held} at ").Because(records);
            await Assert.That(records).DoesNotContain($"deleted {held} at ").Because(records);
        }
        finally
        {
            if (holding is not null)
            {
                await holding.DisposeAsync();
            }

            foreach (var folder in new[] { dead, young, old, held })
            {
                _ = ScratchDirectory.RemoveTree(folder);
                File.Delete(folder + ScratchRoot.OwnerSuffix);
            }
        }
    }

    /// <summary>
    /// A deletion as the reclaim names it: the folder, the UTC moment, and the host
    /// that deleted it as <c>pid@createdFileTime</c>.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^deleted .+ at \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z by \d+@\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Deletion();
}
