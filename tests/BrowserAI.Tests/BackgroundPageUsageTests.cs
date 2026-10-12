// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Background;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The background's reading of what BrowserAI's processes use, over real processes in a real
/// job: a session's job is read whole, its browser told from its server by full image path,
/// and a relay read through the process the roster holds.
/// </summary>
/// <remarks>
/// <b>The maintainer's 59 b, 62 a and 65 a, 2026-10-11.</b> The stand-ins are copies of
/// <c>cmd.exe</c>, started with <c>/d /q /k rem</c> so that each stays, the way
/// <c>SafeModeWatchTests</c> starts its own: one under a scratch browsers root, which a reading
/// must count as a browser, and the same image from <c>System32</c> beside it in the same job,
/// which it must count as the browser's server.
/// </remarks>
internal sealed class BackgroundPageUsageTests
{
    [Test]
    public async Task AReadingCountsASessionsWholeJobAndTellsItsBrowserFromItsServerByFullImagePath()
    {
        using var scratch = await ScratchDirectory.CreateAsync("usage-reader");

        var browsers = Path.Combine(scratch.Path, "browsers");
        var browserImage = Path.Combine(browsers, "chromium-0", "chrome-win64", "chrome.exe");
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        _ = Directory.CreateDirectory(Path.GetDirectoryName(browserImage)!);
        await TestFiles.CopyAsync(system, browserImage);

        var directory = Path.Combine(scratch.Path, "session");

        using var job = JobObject.CreateKillOnClose();
        using var server = JobLauncher.Start(job, system, ["/d", "/q", "/k", "rem", "server"], scratch.Path, ChildEnvironment.Build());
        using var browser = JobLauncher.Start(job, browserImage, ["/d", "/q", "/k", "rem", "browser"], scratch.Path, ChildEnvironment.Build());
        using var reader = new BackgroundPageUsage(() => [(directory, job)], () => [], browsers);

        var reading = await reader.ReadAsync();
        var session = reading.Sessions.Single();

        await Assert.That(reading.Unreadable).IsFalse();
        await Assert.That(session.Id).IsEqualTo(CensusPageSessions.IdOf(directory));
        await Assert.That(session.BrowserBytes).IsGreaterThan(0).Because("the copy under the browsers root is the session's browser");
        await Assert.That(session.ServerBytes).IsGreaterThan(0).Because("the image from System32 is the browser's server");
        await Assert.That(session.SharedBytes).IsGreaterThan(0);
        await Assert.That(session.CpuTicks).IsGreaterThanOrEqualTo(0);

        // The background reads itself: this test host.
        await Assert.That(reading.Background.OwnBytes).IsGreaterThan(0);
        await Assert.That(reading.Background.CpuTicks).IsGreaterThan(0);
        await Assert.That(reading.Processors).IsEqualTo(Environment.ProcessorCount);

        // A second reading meets the same processes and keeps their kinds, read once.
        var again = (await reader.ReadAsync()).Sessions.Single();

        await Assert.That(again.BrowserBytes).IsGreaterThan(0);
        await Assert.That(again.ServerBytes).IsGreaterThan(0);

        // The browser ended: its memory leaves the reading, and the job's CPU total keeps what it used.
        _ = browser.TryTerminate();
        await Assert.That(await browser.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();

        var ended = (await reader.ReadAsync()).Sessions.Single();

        await Assert.That(ended.BrowserBytes).IsEqualTo(0);
        await Assert.That(ended.CpuTicks).IsGreaterThanOrEqualTo(session.CpuTicks);
    }

    [Test]
    public async Task ARelayIsReadThroughTheProcessTheRosterHoldsAndLeftOutOnceItIsClosed()
    {
        using var held = BrowserProcesses.HoldConnected(Environment.ProcessId);

        await Assert.That(held).IsNotNull();

        using var reader = new BackgroundPageUsage(() => [], () => [held!], Path.GetTempPath());

        var reading = await reader.ReadAsync();
        var relay = reading.Relays.Single();

        await Assert.That(relay.ProcessId).IsEqualTo(Environment.ProcessId);
        await Assert.That(relay.CreatedFileTime).IsEqualTo(ProcessLiveness.CreationTimeOfThisProcess());
        await Assert.That(relay.Counters.OwnBytes).IsGreaterThan(0);

        held!.Dispose();

        await Assert.That((await reader.ReadAsync()).Relays.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AReadingRunsOnTheReadersOwnThreadAndNeverOnAPoolWorker()
    {
        Thread? readOn = null;

        using var reader = new BackgroundPageUsage(
            () =>
            {
                readOn = Thread.CurrentThread;
                return [];
            },
            () => [],
            Path.GetTempPath());

        _ = await reader.ReadAsync();

        await Assert.That(readOn).IsNotNull();
        await Assert.That(readOn!.IsThreadPoolThread).IsFalse();
        await Assert.That(readOn.Name).IsEqualTo("BrowserAI usage");
    }
}
