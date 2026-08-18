// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// First-run provisioning: <c>init</c> does not wait for it, browser calls are
/// refused rather than blocked, and the same child works once it lands.
/// </summary>
/// <remarks>
/// <para>
/// <b>The installer is a double here and the download is real elsewhere.</b>
/// Everything these tests exercise is product code — the marker check, the
/// machine-wide mutex, the phase watcher, all three caps, the removal of a
/// partial tree, the refusal text, the recovery — and the one thing they cannot
/// say anything about is whether upstream's installer works. That is
/// <see cref="FirstRunProvisioningTests"/>, which runs against an empty browsers
/// root through the published binary and downloads for real.
/// </para>
/// <para>
/// <b>The refusal is the feature, not a limitation.</b> A blocking <c>init</c>
/// would hold a caller for minutes with nothing to read; these assert the other
/// shape — the session opens, the call is refused with a size and a route out,
/// and the very next attempt on the same session succeeds.
/// </para>
/// </remarks>
internal sealed class ProvisioningTests
{
    [Test]
    public async Task InitReturnsImmediatelyAndSaysTheBrowserIsDownloading()
    {
        // ⚠️ A GATE, NOT A DURATION, and that is the whole test.
        //
        // Corrected 2026-08-18 (previously `FakeInstaller.Succeeding(..., 30 s)`
        // with `Assert.That(clock.Elapsed).IsLessThan(10 s)` and the note "the
        // installer this rig was given takes thirty seconds and init did not wait
        // for it"). Both halves were guesses about how long things take: that the
        // install would still be running after ten seconds, and that init would
        // answer inside them. Under a saturated machine the second is false while
        // the product is behaving perfectly -- and if a run were ever slow enough
        // for the first to be false too, the test would go GREEN for the wrong
        // reason, because a finished install also answers fast.
        //
        // Never released. The install is therefore provably still running when
        // the assertions below are made, whatever the machine is doing, and
        // "init did not wait for it" is an ordered fact rather than a race
        // against a clock.
        var stillDownloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            installer: (browser, root) => FakeInstaller.SucceedingWhenReleased(
                Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName),
                stillDownloading.Task));

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var answer = await CallAsync(rig, SessionToolSurface.Init, Init(sessions, "downloading"));

        await Assert.That((bool?)answer["isError"]).IsFalse();

        // The whole design in one assertion, and it is an assertion about STATE:
        // the install cannot have completed, so an init that had waited for it
        // could not have returned at all. The state is a word rather than a
        // sentence to parse, and it is the word §A names.
        await Assert.That(TextOf(answer)).Contains("browserProvisioning: downloading");
    }

    [Test]
    public async Task EveryBrowserToolIsRefusedWithRowSixIncludingTheConfigOne()
    {
        // Held open for the same reason, and here the alternative was worse than
        // a promptness assertion: a thirty-second installer is an assumption that
        // this whole test finishes inside thirty seconds, and a run that took
        // longer would see the install LAND and the refusals below turn into
        // successes -- a red build caused by a slow machine, reported as a
        // product defect.
        var stillDownloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            Answering,
            installer: (browser, root) => FakeInstaller.SucceedingWhenReleased(
                Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName),
                stillDownloading.Task));

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);
        var directory = Init(sessions, "refused-while-downloading")["directory"]!.GetValue<string>();

        _ = await CallAsync(rig, SessionToolSurface.Init, Init(sessions, "refused-while-downloading"));

        var navigate = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,<h1>ok</h1>",
            ["session"] = directory,
        });

        var text = TextOf(navigate);

        await Assert.That((bool?)navigate["isError"]).IsTrue();

        // §H.4 row 6, and each clause is something a model acts on: the size it
        // is waiting for, where it is going, and that the same call will work.
        await Assert.That(text).Contains(BrowserProvisioner.FirstRunDownloadSize);
        await Assert.That(text).Contains("call the same tool again on the same session");
        await Assert.That(text).Contains(RigSessionEnvironment.ChromiumDirectoryName);

        // And the child was never asked, which is the difference between
        // refusing and forwarding into a launch that blocks for the whole
        // download.
        await Assert.That(sessions.SessionChildren.Sum(child => child.MethodsReceived.Count(method => method is "tools/call"))).IsEqualTo(0);

        // ⚠️ browser_get_config too, and this is a CORRECTION to §A rather than
        // an implementation choice. Measured 2026-08-16 @ 0.0.79, twice,
        // against the child directly: the tool resolves the browser executable
        // before it answers and fails `throwIfExecutableMissing` when the root
        // is empty. Letting it through would hand the caller upstream's "not
        // installed" advice -- provision it -- while provisioning is already
        // running, instead of row 6, which says how big the download is and that
        // the same call will work shortly.
        var config = await CallAsync(rig, "browser_get_config", new JsonObject { ["session"] = directory });

        await Assert.That((bool?)config["isError"]).IsTrue();
        await Assert.That(TextOf(config)).Contains(BrowserProvisioner.FirstRunDownloadSize);

        // Still nothing reached the child, which is what makes the refusal
        // cheaper as well as more useful than upstream's own error.
        await Assert.That(sessions.SessionChildren.Sum(child => child.MethodsReceived.Count(method => method is "tools/call"))).IsEqualTo(0);

        // And what DOES answer while the download runs is BrowserAI's own
        // tools, which is the property §A was reaching for.
        var listed = await CallAsync(rig, SessionToolSurface.List, new JsonObject { ["directory"] = sessions.Root });

        await Assert.That((bool?)listed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(listed)).Contains(directory);
    }

    [Test]
    public async Task TheSameChildNavigatesOnceTheInstallLandsWithNoRestart()
    {
        // The install lands when this test says so and not a millisecond
        // earlier: with a duration instead, a fast machine finishes the download
        // before the first call arrives and the test fails on the refusal rather
        // than on the recovery it is about. Observed 2026-08-16.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            Answering,
            installer: (browser, root) => FakeInstaller.SucceedingWhenReleased(Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName), release.Task),
            timers: new ProvisioningTimers { Poll = TimeSpan.FromMilliseconds(20) });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);
        var directory = Init(sessions, "recovers-in-session")["directory"]!.GetValue<string>();

        _ = await CallAsync(rig, SessionToolSurface.Init, Init(sessions, "recovers-in-session"));

        var childrenAfterInit = sessions.SessionChildren.Count;
        var refused = await CallAsync(rig, "browser_navigate", Navigate(directory));

        await Assert.That((bool?)refused["isError"]).IsTrue();

        // The install lands. Nothing is restarted, nothing is re-created, and no
        // tool is called to make it happen.
        release.SetResult();
        _ = await sessions.Provisioner.WaitAsync(SessionManager.SupportedBrowser);

        var accepted = await CallAsync(rig, "browser_navigate", Navigate(directory));

        await Assert.That((bool?)accepted["isError"]).IsNotEqualTo(true);

        // ⚠️ The load-bearing half. "It works afterwards" would also be true of a
        // design that quietly replaced the child, and that design would lose the
        // session's whole browser state on every first run. No child was added,
        // and exactly one tools/call reached one of them -- the refused attempt
        // never left BrowserAI.
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(childrenAfterInit);
        await Assert.That(sessions.SessionChildren.Sum(child => child.MethodsReceived.Count(method => method is "tools/call"))).IsEqualTo(1);
    }

    [Test]
    public async Task AnInstallerThatExitsCleanWithoutTheMarkerIsAFailureAndLeavesNothingBehind()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-no-marker");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);

        using var provisioner = new BrowserProvisioner(RepositoryPayload.Layout, root, log)
        {
            StartInstaller = (_, _) => FakeInstaller.ExitingCleanWithoutTheMarker(directory),
        };

        var status = await provisioner.WaitAsync(SessionManager.SupportedBrowser);

        // Exit code 0 and a directory full of files is not an install. Upstream
        // never makes this check at launch, which is why a tree in this state
        // produces `spawn EFTYPE` forever and never re-downloads.
        await Assert.That(status.State).IsEqualTo(ProvisioningState.Failed);
        await Assert.That(status.Detail).Contains(BrowsersManifest.InstallationCompleteMarker);

        // And the partial tree is gone rather than left to be mistaken for one.
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }

    [Test]
    public async Task TheAbsoluteCapStopsADownloadThatNeverProgresses()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-absolute-cap");

        var root = Path.Combine(scratch.Path, "browsers");
        FakeInstaller? started = null;

        using var provisioner = new BrowserProvisioner(
            RepositoryPayload.Layout,
            root,
            log,
            new ProvisioningTimers
            {
                AbsoluteCap = TimeSpan.FromMilliseconds(200),
                Poll = TimeSpan.FromMilliseconds(20),
            })
        {
            StartInstaller = (_, _) => started = FakeInstaller.Hanging(),
        };

        var status = await provisioner.WaitAsync(SessionManager.SupportedBrowser);

        await Assert.That(status.State).IsEqualTo(ProvisioningState.Failed);
        await Assert.That(status.Detail).Contains("cap");

        // The installer was STOPPED rather than merely abandoned. A watcher that
        // gave up without closing the job would leave a 200 MB download running
        // with nobody left to receive it — which is the exact shape a cap exists
        // to prevent, and it is invisible in the status.
        await Assert.That(started!.WasStopped).IsTrue();
    }

    [Test]
    public async Task TheExtractionCapStartsWhenTheBrowserDirectoryAppears()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-extraction-cap");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);
        FakeInstaller? started = null;

        using var provisioner = new BrowserProvisioner(
            RepositoryPayload.Layout,
            root,
            log,
            new ProvisioningTimers
            {
                // Deliberately far apart: an extraction cap that only fired
                // because the absolute one did would pass this test while
                // measuring nothing.
                AbsoluteCap = TimeSpan.FromMinutes(45),
                ExtractionCap = TimeSpan.FromMilliseconds(200),
                Poll = TimeSpan.FromMilliseconds(20),
            })
        {
            StartInstaller = (_, _) => started = FakeInstaller.StallingInExtraction(directory),
        };

        var status = await provisioner.WaitAsync(SessionManager.SupportedBrowser);

        await Assert.That(status.State).IsEqualTo(ProvisioningState.Failed);
        await Assert.That(status.Detail).Contains("Extracting");
        await Assert.That(started!.WasStopped).IsTrue();
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }

    [Test]
    public async Task TwoProvisionersOverOneRootProduceOneInstall()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-one-install");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);

        // Counted per test rather than off FakeInstaller's own total: the suite
        // runs in parallel and other tests are creating doubles at the same
        // moment, so a global counter measures the run instead of this pair.
        var starts = 0;

        // Two provisioners is what two BrowserAI processes look like from the
        // machine's point of view: they share a browsers root and know nothing
        // about each other except through the Global\ mutex.
        using var first = new BrowserProvisioner(RepositoryPayload.Layout, root, log, Quick())
        {
            StartInstaller = (_, _) =>
            {
                _ = Interlocked.Increment(ref starts);
                return FakeInstaller.Succeeding(directory, TimeSpan.FromMilliseconds(300));
            },
        };

        using var second = new BrowserProvisioner(RepositoryPayload.Layout, root, log, Quick())
        {
            StartInstaller = (_, _) =>
            {
                _ = Interlocked.Increment(ref starts);
                return FakeInstaller.Succeeding(directory, TimeSpan.FromMilliseconds(300));
            },
        };

        var both = await Task.WhenAll(
            first.WaitAsync(SessionManager.SupportedBrowser),
            second.WaitAsync(SessionManager.SupportedBrowser));

        await Assert.That(both[0].State).IsEqualTo(ProvisioningState.Installed);
        await Assert.That(both[1].State).IsEqualTo(ProvisioningState.Installed);

        // One download, not two. The loser watches for the winner's marker
        // rather than fetching a second copy of 203.8 MB into the same
        // directory, which is precisely how a half-extracted tree acquires an
        // INSTALLATION_COMPLETE.
        await Assert.That(starts).IsEqualTo(1);
    }

    /// <summary>
    /// A holder that lets go of the provisioning mutex without leaving a complete
    /// tree is overtaken, not waited on forever.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the regression test for a sixty-minute product hang, and it
    /// was found by running the suite with every test at once rather than by
    /// review.</b> Failing to take the mutex was read as <i>somebody is
    /// downloading and a marker is coming</i>. It is not the same statement: the
    /// holder keeps the mutex through its revision prune, which walks every
    /// process on the machine, so there is a real window in which the holder is
    /// finished and no marker will ever appear. A caller that had just deleted
    /// the tree — which is exactly what <c>browserai_reinstall_browser</c>
    /// does — then sat in <see cref="ProvisioningTimers.OuterDeadline"/> with no
    /// browser installed.
    /// </para>
    /// <para>
    /// <b>What makes this test the right shape is that it never mentions the
    /// prune.</b> The prune is only how the window is reached today; the defect
    /// is the inference. So the claim is held by
    /// <see cref="ProvisioningClaim"/> — the product's own cross-process
    /// contract, taken from a thread of its own exactly as another BrowserAI
    /// process would take it — and then released with the tree still incomplete,
    /// which is the condition rather than one route to it.
    /// </para>
    /// <para>
    /// <b>And it asserts the install happened here.</b> "It returned Installed"
    /// would also pass against a provisioner that waited for a marker somebody
    /// else wrote, so the installer-start count is what distinguishes being
    /// overtaken from being lucky.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHolderThatLetsGoWithoutInstallingIsOvertakenRatherThanWaitedOnForever()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-holder-let-go");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);
        var starts = 0;

        using var provisioner = new BrowserProvisioner(RepositoryPayload.Layout, root, log, Quick())
        {
            StartInstaller = (_, _) =>
            {
                _ = Interlocked.Increment(ref starts);
                return FakeInstaller.Succeeding(directory, TimeSpan.Zero);
            },
        };

        // Taken before anything asks for it, and released while the tree is
        // still incomplete. Nothing is ever going to write a marker here.
        var claim = ProvisioningClaim.Take(root, SessionManager.SupportedBrowser);

        try
        {
            // The claim is real, or this test would be asserting nothing at all.
            await Assert.That(claim.Held).IsTrue();
            await Assert.That(Directory.Exists(directory)).IsFalse();

            var provisioning = provisioner.WaitAsync(SessionManager.SupportedBrowser);

            // It must be watching rather than installing: the mutex is held, and
            // a provisioner that started an install here would be the second
            // downloader the mutex exists to prevent.
            await Assert.That(Volatile.Read(ref starts)).IsEqualTo(0);

            claim.Dispose();

            // ⚠️ The bound is the provisioner's OWN outer deadline, not a
            // budget written here, and that is what makes this test bearable in
            // a suite that runs everything at once: Quick() sets it to 30 s, so
            // a regression comes back as `Failed` after thirty seconds rather
            // than hanging. Measured against the injected fault on 2026-08-17 —
            // the pre-fix inference restored, this test failed at 30.4 s with
            // "Expected to be equal to Installed but received Failed". In a
            // shipped build the same path is sixty minutes with no browser
            // installed. Nothing here reads a stopwatch, so nothing here can be
            // made red by a loaded machine.
            var status = await provisioning;

            await Assert.That(status.State).IsEqualTo(ProvisioningState.Installed);

            // Installed HERE. Without this the assertion above passes against
            // the very behaviour that hung.
            await Assert.That(Volatile.Read(ref starts)).IsEqualTo(1);
            await Assert.That(File.Exists(Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker))).IsTrue();
        }
        finally
        {
            claim.Dispose();
        }
    }

    /// <summary>
    /// A process waiting out another one does not claim to be downloading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The status sentence was stating a fact that was not true.</b> Every
    /// unfinished attempt rendered <i>"… is being downloaded into '…'"</i>,
    /// including the one where this process has started nothing at all: it lost
    /// the machine-wide provisioning mutex and is watching for the holder's
    /// marker. What the holder is doing is unknowable from here — downloading,
    /// extracting, or walking every process on the machine inside its revision
    /// prune — and the same window is what produced the sixty-minute hang two
    /// tests above, so it is neither hypothetical nor rare.
    /// </para>
    /// <para>
    /// <b>The state WORD is unchanged and this test says so on purpose.</b>
    /// <c>downloading</c> stays, because every consumer of it branches on
    /// <i>installed</i> / <i>not yet</i> / <i>failed</i> and the loser belongs in
    /// the middle exactly as before; the honest word for the state is
    /// <c>provisioning</c>, and renaming what a model reads is recorded in
    /// <c>QUESTIONS.md</c> as the maintainer's call rather than taken here. What
    /// is fixed is the sentence, which was a claim about the world.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProcessWaitingOnAnotherOneDoesNotSayItIsDownloading()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-waiting-detail");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);

        using var provisioner = new BrowserProvisioner(RepositoryPayload.Layout, root, log, Quick())
        {
            StartInstaller = (_, _) => FakeInstaller.Succeeding(directory, TimeSpan.Zero),
        };

        var claim = ProvisioningClaim.Take(root, SessionManager.SupportedBrowser);

        try
        {
            await Assert.That(claim.Held).IsTrue();

            // Starts the attempt, which loses the mutex on its own thread. Ensure
            // answers immediately by design, so the phase it reports is whatever
            // the background thread has reached -- which is why this reads Peek in
            // a loop rather than asserting on the first answer.
            var started = provisioner.Ensure(SessionManager.SupportedBrowser);

            await Assert.That(started.State).IsEqualTo(ProvisioningState.Downloading);

            var patience = Stopwatch.StartNew();
            ProvisioningStatus waiting;

            while (true)
            {
                waiting = provisioner.Peek(SessionManager.SupportedBrowser);

                if (waiting.Detail.Contains("holds the provisioning lock", StringComparison.Ordinal))
                {
                    break;
                }

                // A hang detector, never an assertion: nothing below reads this
                // clock, so a loaded machine cannot turn this test red -- it can
                // only make it take longer to reach the same answer.
                if (patience.Elapsed > TestDefaults.InProcessHang)
                {
                    throw new TimeoutException(
                        $"The attempt never reported that it was waiting on another process. Last status: {waiting.State} — {waiting.Detail}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }

            // The word is unchanged; the sentence no longer claims a download
            // that this process has not started and cannot see.
            await Assert.That(waiting.State).IsEqualTo(ProvisioningState.Downloading);
            await Assert.That(waiting.Detail).DoesNotContain("is being downloaded");
            await Assert.That(waiting.Detail).Contains("watching for its completion marker");
        }
        finally
        {
            claim.Dispose();
        }
    }

    [Test]
    public async Task TheMutexNameIsPerBrowsersRootRatherThanPerBrowser()
    {
        // ⚠️ Found by the suite rather than by review. Keyed on the family
        // alone, every rig in this suite serialised against every other one, and
        // the losers sat watching for a marker in their OWN root that the winner
        // was never going to write — reported as "downloading" until the outer
        // deadline, on tests that had nothing to do with provisioning.
        var oneRoot = BrowserProvisioner.MutexNameFor(@"C:\a\browsers", "chromium");
        var anotherRoot = BrowserProvisioner.MutexNameFor(@"C:\b\browsers", "chromium");
        var anotherBrowser = BrowserProvisioner.MutexNameFor(@"C:\a\browsers", "firefox");

        await Assert.That(oneRoot).IsNotEqualTo(anotherRoot);
        await Assert.That(oneRoot).IsNotEqualTo(anotherBrowser);

        // Global\, with no Local\ fallback anywhere in this product: a
        // logon-session-scoped name would let a Remote Desktop session and the
        // console session install into one directory at once, each reporting
        // success.
        await Assert.That(oneRoot).StartsWith(LockScopes.GlobalPrefix);

        // Case-folded, because Windows paths are.
        await Assert.That(BrowserProvisioner.MutexNameFor(@"C:\A\Browsers", "Chromium")).IsEqualTo(oneRoot);
    }

    [Test]
    public async Task TheInstallerInheritsNoDownloadHostAndNoStallTimeoutOverride()
    {
        var environment = ChildEnvironment.Build(
            [new KeyValuePair<string, string>(ChildLaunch.BrowsersPathVariable, @"C:\browsers")]);

        // Upstream's per-socket stall timeout is 30 s and BrowserAI sets
        // nothing, so the figure stays upstream's rather than being duplicated
        // into a constant of ours that would drift the day theirs moved.
        await Assert.That(environment.ContainsKey(BrowserProvisioner.UpstreamStallTimeoutVariable)).IsFalse();
        await Assert.That(BrowserProvisioner.UpstreamStallTimeout).IsEqualTo(TimeSpan.FromSeconds(30));

        // And the mirror list survives: retries rotate through it, so a single
        // download host turns five attempts into five attempts at one dead
        // server.
        foreach (var name in ChildEnvironment.Refused.Where(name => name.Contains("DOWNLOAD_HOST", StringComparison.Ordinal)))
        {
            await Assert.That(environment.ContainsKey(name)).IsFalse();
        }

        await Assert.That(environment[ChildLaunch.BrowsersPathVariable]).IsEqualTo(@"C:\browsers");
    }

    [Test]
    public async Task ARelativeBrowsersRootIsRefusedRatherThanResolved()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));

        // A relative PLAYWRIGHT_BROWSERS_PATH resolves against INIT_CWD —
        // inherited from whatever npm ancestor last ran — before the child's own
        // directory, so it lands a 430 MiB tree somewhere nobody chose and
        // reports nothing.
        var refused = Assert.Throws<ArgumentException>(() =>
        {
            using var never = new BrowserProvisioner(RepositoryPayload.Layout, "browsers", log);
        });

        await Assert.That(refused!.Message).Contains("INIT_CWD");
    }

    /// <summary>
    /// Programs the double to answer the two tools these tests call, because it
    /// answers only tools a test asked for.
    /// </summary>
    /// <remarks>
    /// Both results are deliberately trivial: what is under test is whether the
    /// call reaches the child at all, and a rich payload would only make the
    /// failure harder to read.
    /// </remarks>
    private static void Answering(FakePlaywrightChild child)
    {
        child.Tools["browser_navigate"] = new FakeToolBehaviour
        {
            RawResult = """{"content":[{"type":"text","text":"Page URL: data:text/html,<h1>ok</h1>"}],"isError":false}""",
        };

        child.Tools["browser_get_config"] = new FakeToolBehaviour
        {
            RawResult = """{"content":[{"type":"text","text":"### Config the child resolved"}],"isError":false}""",
        };
    }

    /// <summary>
    /// The provisioner's timers for an in-process arm: polled fast, and watched
    /// by a hang detector rather than by a budget.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><c>OuterDeadline</c> corrected 2026-08-18 (previously 30 s).</b>
    /// Nothing here asserts that it fires — the arms that exercise a cap
    /// configure that cap explicitly — so its only job is to stop a wedged
    /// provisioner hanging the run. Thirty seconds is reachable at unbounded
    /// suite parallelism: this loop calls <c>Thread.Sleep</c> on a pool thread
    /// between polls, and the pool grows by about one worker a second, so under
    /// 419 concurrent tests a fake install that costs milliseconds can still be
    /// scheduled late. When it expired, the provisioner reported <i>"another
    /// process has been provisioning for more than 0 minutes"</i>, which is a
    /// product message about a condition that did not exist.
    /// <c>Poll</c> stays at 20 ms because it is a sampling rate, not a bound: it
    /// can make the arm slower, never redder.
    /// </remarks>
    /// <returns>The timers.</returns>
    /// <summary>
    /// The rule an abandoned provisioning mutex obeys, all four combinations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted on the predicate rather than on an interleaving, because the
    /// interleaving cannot be staged.</b> The tree becomes complete when the
    /// holder writes its marker, and the holder abandons the mutex when it dies;
    /// reaching <c>Install</c> with both true at once means landing in the
    /// microseconds between another process's marker write and this process's
    /// acquire, and nothing can put a test there.
    /// <c>Ensure</c> short-circuits on a complete tree, so the end-to-end route
    /// is closed by construction. What <i>is</i> assertable is the rule itself —
    /// and it is the rule that was wrong: an abandoned mutex was taken as
    /// sufficient, and the marker was consulted one line after the tree had gone
    /// ([the adversarial review](../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// A2).
    /// </para>
    /// <para>
    /// The arm that <i>is</i> reachable end to end — abandoned over an unmarked
    /// tree — is asserted below, so the branch is known to be wired rather than
    /// merely correct in isolation.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AnAbandonedMutexIsOnlyAReasonToDeleteAnUnmarkedTree()
    {
        using var scratch = ScratchDirectory.Create("provision-abandoned-predicate");

        var complete = Path.Combine(scratch.Path, "complete");
        var unmarked = Path.Combine(scratch.Path, "unmarked");

        _ = Directory.CreateDirectory(complete);
        _ = Directory.CreateDirectory(unmarked);
        await File.WriteAllTextAsync(Path.Combine(complete, BrowsersManifest.InstallationCompleteMarker), string.Empty);

        // The defect: an abandoned mutex over a COMPLETE tree used to be a
        // reason to delete 203.8 MB that ~100 processes may be running out of.
        // What it actually means is that the holder died inside its prune.
        await Assert.That(BrowserProvisioner.AbandonedTreeIsUnusable(MutexAcquisition.AcquiredAbandoned, complete)).IsFalse();

        // The recovery this branch exists for, unchanged.
        await Assert.That(BrowserProvisioner.AbandonedTreeIsUnusable(MutexAcquisition.AcquiredAbandoned, unmarked)).IsTrue();

        // And an ordinary acquisition is never a reason to delete anything, on
        // either tree.
        await Assert.That(BrowserProvisioner.AbandonedTreeIsUnusable(MutexAcquisition.Acquired, unmarked)).IsFalse();
        await Assert.That(BrowserProvisioner.AbandonedTreeIsUnusable(MutexAcquisition.Acquired, complete)).IsFalse();
    }

    [Test]
    public async Task AnAbandonedMutexOverAnUnmarkedTreeStillDeletesItAndInstallsAgain()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-abandoned-unmarked");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);
        var residue = Path.Combine(directory, "half-extracted.bin");

        _ = Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(residue, "what a holder that died mid-extraction left behind");

        // Held open across the abandonment, because a named mutex is
        // reference-counted by its handles: if the abandoning thread held the
        // last one the kernel object dies with it and the next Create makes a
        // brand new, unabandoned object -- the same trap SessionLockTests
        // records having fallen into.
        using var keepAlive = MachineMutex.Create(BrowserProvisioner.MutexNameFor(root, ProvisionedBrowsers.Chromium));

        AbandonOnAThread(BrowserProvisioner.MutexNameFor(root, ProvisionedBrowsers.Chromium));

        using var provisioner = new BrowserProvisioner(RepositoryPayload.Layout, root, log, Quick())
        {
            StartInstaller = (_, _) => FakeInstaller.Succeeding(directory, TimeSpan.Zero),
            PruneRevisions = _ => { },
        };

        var status = await provisioner.WaitAsync(SessionManager.SupportedBrowser);

        await Assert.That(status.State).IsEqualTo(ProvisioningState.Installed);

        // The unmarked residue went, which is what makes this a recovery rather
        // than a re-run on top of somebody's wreckage.
        await Assert.That(File.Exists(residue)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker))).IsTrue();
    }

    /// <summary>
    /// Abandons a named mutex: a thread that ends while owning one is the whole
    /// of what <c>AbandonedMutexException</c> reports.
    /// </summary>
    /// <param name="name">The <c>Global\</c> name to abandon.</param>
    private static void AbandonOnAThread(string name)
    {
        var thread = new Thread(() =>
        {
#pragma warning disable CA2000 // Deliberately neither released nor disposed: releasing is the opposite of abandoning, and the caller holds a second handle so the kernel object outlives this thread.
            var mutex = MachineMutex.Create(name);
#pragma warning restore CA2000

            _ = mutex.Acquire(TestDefaults.ProcessHang);
        })
        {
            IsBackground = true,
            Name = "browserai-abandoning-holder",
        };

        thread.Start();

        if (!thread.Join(TestDefaults.ProcessHang))
        {
            throw new InvalidOperationException(
                $"The thread that was to abandon '{name}' never ended, so the mutex is held rather than abandoned and this test would measure the wrong branch.");
        }
    }

    private static ProvisioningTimers Quick() => new()
    {
        Poll = TimeSpan.FromMilliseconds(20),
        OuterDeadline = TestDefaults.ProcessHang,
    };

    private static JsonObject Init(RigSessionEnvironment sessions, string name) => new()
    {
        ["directory"] = Path.Combine(sessions.Root, name),
        ["purpose"] = "a session created while the browser is still downloading",
        ["mode"] = "headless",
    };

    private static JsonObject Navigate(string directory) => new()
    {
        ["url"] = "data:text/html,<h1>ok</h1>",
        ["session"] = directory,
    };

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
