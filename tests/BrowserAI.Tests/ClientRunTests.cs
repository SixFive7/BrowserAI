// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A process an arm starts for a real client leaves nothing behind once the arm has ended.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>64 c (b), the maintainer's words verbatim: <i>"64 c now"</i>.</b> The arms that
/// start a real Claude Code or Codex start it through <see cref="ClientRun"/>, and what the
/// client starts is the client's business: on 2026-10-11 a real Claude Code's git launcher
/// left a suspended git behind, pid 102292, which held the test host's standard output until
/// it was ended by hand (HAZARDS.md, the frozen-child wedge).
/// </para>
/// <para>
/// <b>The client here is the test probe</b>, which starts a process that inherits its
/// standard output and outlives it by twice <see cref="TestDefaults.ProcessHang"/>, writes
/// that process's pid and creation time, and exits: the shape of a client whose child
/// outlives it. The arm ends the run the way an arm does, and the process left behind must
/// be gone within the hang detector. Whatever the outcome, the arm ends that process itself
/// at the end, by its verified identity, so a red run leaves nothing either.
/// </para>
/// </remarks>
internal sealed class ClientRunTests
{
    /// <summary>The test probe, which plays the client.</summary>
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// A child the client started, still running when the client exits, is gone once the
    /// arm that started the client has ended.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientAnArmStartedLeavesNoProcessBehindOnceTheArmEnds()
    {
        using var work = ScratchDirectory.Create("client-run");

        var document = Path.Combine(work.Path, "document.txt");
        var report = Path.Combine(work.Path, "holder.txt");
        var hold = (TestDefaults.ProcessHang * 2).TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);

        await File.WriteAllTextAsync(document, "the client's output\n");

        using (var client = ClientRun.Start(ProbePath, work.Path, new Dictionary<string, string>(StringComparer.Ordinal), ["output-holder", document, hold, report]))
        {
            await Assert.That(await client.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue()
                .Because("the stand-in client writes one line, starts its child and exits at once");
        }

        // The arm has ended: its run is disposed, as every arm's is on its way out.
        await Assert.That(File.Exists(report)).IsTrue()
            .Because("the stand-in client writes its child's identity before it exits, and without it this arm is about nothing");

        var parts = (await File.ReadAllTextAsync(report)).Trim().Split(' ');
        var pid = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var created = long.Parse(parts[1], CultureInfo.InvariantCulture);

        try
        {
            var gone = await GoneWithinAsync(pid, created, TestDefaults.ProcessHang);

            await Assert.That(gone).IsTrue()
                .Because($"the child the client started, pid {pid}, was still running {TestDefaults.ProcessHang.TotalMinutes:F0} minutes after the arm that started the client had ended, holding every handle it inherited");
        }
        finally
        {
            // The arm's own cleaning up, by the identity the stand-in wrote and never by a
            // bare pid: a red run must not leave the child for the next run to meet.
            if (ProcessIdentity.IsAlive(pid, created))
            {
                ProcessIdentity.Terminate(pid, created);
            }
        }
    }

    /// <summary>Whether a process has gone within a bound, read without parking a thread.</summary>
    /// <param name="pid">Its pid.</param>
    /// <param name="created">Its creation time.</param>
    /// <param name="patience">The hang detector.</param>
    /// <returns>Whether it went.</returns>
    private static async Task<bool> GoneWithinAsync(int pid, long created, TimeSpan patience)
    {
        using var deadline = new CancellationTokenSource(patience);

        while (ProcessIdentity.IsAlive(pid, created))
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return !ProcessIdentity.IsAlive(pid, created);
            }
        }

        return true;
    }
}
