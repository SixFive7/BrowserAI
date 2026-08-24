// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The sixth authored tool: it refuses rather than coordinates, and when it does
/// act it deletes before it downloads.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refusal is the feature.</b> The browser install is shared by every
/// session on the machine, so "make this safe" would mean terminating browsers
/// other agents are driving. There is deliberately no force argument, and the
/// tests below assert both halves of that: it names what is live, and nothing it
/// found is still running afterwards because nothing was ever killed.
/// </para>
/// <para>
/// <b>The delete is real product code and only the download is doubled.</b>
/// §E's shared post-order routine removes the tree here exactly as it does in
/// production; what a double replaces is the 203.8 MB that would otherwise be
/// fetched on every suite run. The real fetch is
/// <see cref="FirstRunProvisioningTests"/>.
/// </para>
/// </remarks>
internal sealed class ReinstallBrowserTests
{
    /// <summary>The probe executable, which is how a claim is held from another process.</summary>
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>The one argument the tool takes, named once so no assertion carries a literal array.</summary>
    private static readonly string[] TheOnlyArgument = ["browser"];

    /// <summary>The family every arm of this class reinstalls, which is the one the rig seeds.</summary>
    private static JsonObject Chromium => new() { ["browser"] = ProvisionedBrowsers.Chromium };

    [Test]
    public async Task ItDeletesTheTreeAndDownloadsItAgainWhenNothingIsRunning()
    {
        var installs = 0;

        await using var sessions = RigSessionEnvironment.Create(
            // ⚠️ No default session since 2026-08-19, and that is the tool's own
            // new gate rather than a test convenience: a reinstall now refuses
            // while ANY session of that family is open, whether or not a browser
            // is currently running out of the tree. The rig opens one, so with it
            // this arm would measure the refusal instead of the delete.
            opensDefaultSession: false,
            installer: (_, root) =>
            {
                Interlocked.Increment(ref installs);
                return FakeInstaller.Succeeding(Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName), TimeSpan.Zero);
            },
            timers: new ProvisioningTimers { Poll = TimeSpan.FromMilliseconds(20) });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        // ⚠️ Before anything else, and the ORDER of these three lines is the
        // whole fix. The rig opens a default session, and that init legitimately
        // starts an install against a root this test left empty -- but `Ensure`
        // RETURNS BEFORE the installer runs, which is the whole non-blocking
        // design, so the count below has to be taken after that install has
        // finished. Waiting is not enough on its own: `WaitAsync` begins with an
        // `Ensure`, which short-circuits on a complete tree, so waiting AFTER
        // planting the marker below returns immediately without joining the
        // install still in flight -- and it then lands between the baseline and
        // the assertion, making the delta 2. Observed twice on 2026-08-16 under
        // a loaded machine, the second time against a version of this comment
        // that had the wait in the wrong place.
        _ = await sessions.Environment.Provisioner.WaitAsync(SessionManager.DefaultBrowser);

        // And then for the mutex, which WaitAsync does not answer for -- see
        // WaitUntilNoInstallIsInFlight. Since 2026-08-18 a reinstall refuses
        // while an install holds it, so without this the call below measures
        // the refusal rather than the delete.
        WaitUntilNoInstallIsInFlight(sessions.Environment.Paths.BrowsersDirectory);

        // A complete tree with a file in it that must not survive, so "the
        // directory exists afterwards" cannot pass for "it was replaced".
        var stale = Path.Combine(sessions.ChromiumDirectory, "stale-from-the-old-install.bin");
        _ = Directory.CreateDirectory(sessions.ChromiumDirectory);
        await File.WriteAllTextAsync(stale, new string('x', 4096));
        InstallationMarker.Write(sessions.ChromiumDirectory);

        var before = Volatile.Read(ref installs);
        var answer = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, Chromium);
        var text = TextOf(answer);

        await Assert.That((bool?)answer["isError"]).IsNotEqualTo(true);
        await Assert.That(text).Contains("Re-provisioned");

        // Deleted, not merely written over: the old file is gone and the marker
        // the new install wrote is there.
        await Assert.That(File.Exists(stale)).IsFalse();
        await Assert.That(Volatile.Read(ref installs) - before).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(sessions.ChromiumDirectory, BrowsersManifest.InstallationCompleteMarker))).IsTrue();

        // And the size it removed is reported, because the whole point of the
        // tool is that it is destructive and the caller should see what it cost.
        await Assert.That(text).Contains("MiB");
    }

    [Test]
    public async Task ItRefusesWhileSomethingIsRunningFromTheTreeAndNamesTheSessionThatIsOpen()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var session = Path.Combine(sessions.Root, "holds-a-browser");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the session a reinstall must refuse to act around",
        });

        // A real process whose image path is inside the browsers root. That is
        // the only property the product matches on: an image NAME would find the
        // user's own Chrome, and this test would then pass on a machine where
        // BrowserAI had never run. The helper does not return until the
        // product's own enumeration can see it.
        using var scope = new JobObjectScope();
        var (running, planted) = await PlantedProcess.StartInAsync(
            scope,
            Path.Combine(sessions.ChromiumDirectory, "chrome-win64"),
            sessions.ChromiumDirectory);

        var answer = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, Chromium);
        var text = TextOf(answer);

        await Assert.That((bool?)answer["isError"]).IsTrue();

        // It names what is live rather than saying "in use". A model told only
        // that something is busy has nothing to act on.
        await Assert.That(text).Contains(session);
        await Assert.That(text).Contains("no force option");

        // Nothing was killed and nothing was deleted, which is the half the
        // message cannot claim for itself.
        await Assert.That(ProcessIdentity.IsAlive(running.Id, ProcessIdentity.CreationTimeOf(running.Id))).IsTrue();
        await Assert.That(File.Exists(planted)).IsTrue();
    }

    [Test]
    public async Task ItReportsWhatWouldNotDeleteRatherThanDownloadingOnTopOfIt()
    {
        var installs = 0;

        await using var sessions = RigSessionEnvironment.Create(
            // No default session, for the reason in
            // `ItDeletesTheTreeAndDownloadsItAgainWhenNothingIsRunning`: an open
            // session of this family now refuses the call outright, and this arm
            // is about what happens to the DELETE.
            opensDefaultSession: false,
            installer: (_, root) =>
            {
                Interlocked.Increment(ref installs);
                return FakeInstaller.Succeeding(Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName), TimeSpan.Zero);
            },
            timers: new ProvisioningTimers { Poll = TimeSpan.FromMilliseconds(20) });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        // ⚠️ Before the marker is written, for the reason spelled out in
        // `ItDeletesTheTreeAndDownloadsItAgainWhenNothingIsRunning`: the rig's
        // own `init` starts an install and `Ensure` returns before the installer
        // runs, so the count below has to be taken after it has finished — and
        // `WaitAsync` short-circuits on a complete tree, so waiting after
        // planting the marker would join nothing and the in-flight install would
        // land inside the window this test measures. Observed 2026-08-16.
        _ = await sessions.Environment.Provisioner.WaitAsync(SessionManager.DefaultBrowser);

        // And for the mutex, for the reason in WaitUntilNoInstallIsInFlight.
        WaitUntilNoInstallIsInFlight(sessions.Environment.Paths.BrowsersDirectory);

        InstallationMarker.Write(sessions.ChromiumDirectory);

        var held = Path.Combine(sessions.ChromiumDirectory, "held-open.bin");

        // Counted from HERE rather than from the start of the test: the rig
        // opens a default session, and that init legitimately starts an install
        // against a root this test deliberately left empty.
        var before = Volatile.Read(ref installs);

        // FileShare.None, so the delete meets a file it genuinely cannot remove
        // -- and no process is running from the tree, so the check above passes
        // and the delete is what fails.
        using (var _ = new FileStream(held, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var answer = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, Chromium);
            var text = TextOf(answer);

            await Assert.That((bool?)answer["isError"]).IsTrue();
            await Assert.That(text).Contains(held);

            // ⚠️ The load-bearing assertion. Downloading on top of a tree that
            // would not delete is how a directory ends up half old and half new
            // with an INSTALLATION_COMPLETE over it, which every later check
            // then short-circuits on without validating anything.
            await Assert.That(Volatile.Read(ref installs) - before).IsEqualTo(0);
        }
    }

    /// <summary>
    /// The question the running-process guard cannot ask: is something
    /// <b>writing into</b> the tree?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A concurrent installer is invisible to every check this tool used to
    /// make.</b> It is <c>node.exe</c> out of the payload directory, extracting
    /// <i>into</i> the browser tree, so <c>BrowserProcesses.RunningFrom</c>
    /// returns empty and the guard passes. The reinstall then deleted the
    /// installer's partially-extracted files; the installer finished and wrote
    /// <c>INSTALLATION_COMPLETE</c> over what was left; both processes reported
    /// success; and <c>IsComplete</c> answered <i>installed</i> for ever after,
    /// with <c>spawn EFTYPE</c> at every launch and upstream's thirty-day
    /// <c>DEPENDENCIES_VALIDATED</c> suppression on top
    /// ([the adversarial review](../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// A3).
    /// </para>
    /// <para>
    /// <b>The claim below is not a stand-in for that installer, it is the same
    /// object.</b> A BrowserAI extracting into this tree holds exactly this
    /// machine-wide mutex, for exactly this reason, and holds it on a thread of
    /// its own for the same reason the product does. The positive control is
    /// <see cref="ItDeletesTheTreeAndDownloadsItAgainWhenNothingIsRunning"/>,
    /// which is the same call with nothing holding it.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ItDeletesNothingWhileAnotherProcessIsInstallingIntoTheTree()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("reinstall-while-installing");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);
        var beingExtracted = Path.Combine(directory, "being-extracted.bin");

        _ = Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(beingExtracted, "the other process's half-written tree");

        using var claim = ProvisioningClaim.Take(root, ProvisionedBrowsers.Chromium);

        await Assert.That(claim.Held).IsTrue();

        var installs = 0;

        using var provisioner = new BrowserProvisioner(
            RepositoryPayload.Layout,
            root,
            log,
            new ProvisioningTimers { Poll = TimeSpan.FromMilliseconds(20), StallCap = TestDefaults.ProcessHang })
        {
            StartInstaller = (_, installRoot) =>
            {
                Interlocked.Increment(ref installs);
                return FakeInstaller.Succeeding(Path.Combine(installRoot, RigSessionEnvironment.ChromiumDirectoryName), TimeSpan.Zero);
            },
            PruneRevisions = _ => { },
        };

        var outcome = await provisioner.ReinstallAsync(SessionManager.DefaultBrowser);

        // The whole point: the other process's files are still there.
        await Assert.That(File.Exists(beingExtracted)).IsTrue();
        await Assert.That(outcome.RemovedBytes).IsEqualTo(0);
        await Assert.That(outcome.Failures).IsEmpty();

        // And nothing was downloaded on top of them either, which is the second
        // half of the corruption: a download into a tree somebody else is
        // extracting into produces one that is neither install.
        await Assert.That(installs).IsEqualTo(0);

        // Said, not merely done. A refusal a model cannot act on is the failure
        // shape this project exists to remove.
        await Assert.That(outcome.Deleted).IsFalse();
        await Assert.That(outcome.Status.State).IsEqualTo(ProvisioningState.Failed);
        await Assert.That(outcome.Status.Detail).Contains("nothing was deleted");
        await Assert.That(outcome.Status.Detail).Contains(SessionManager.DefaultBrowser);
    }

    /// <summary>
    /// An open session refuses a reinstall by itself, with no browser running
    /// out of the tree at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the arm that is red against the gate this tool shipped with.</b>
    /// Until 2026-08-19 the family path asked <i>are there open sessions</i> only
    /// <b>inside</b> <c>if (running.Count is not 0)</c>, so a session whose
    /// browser was not launched at that instant let the delete through — and the
    /// browser it is about to launch lands in a tree being removed. The
    /// maintainer's decision, verbatim: <i>"No reinstall if there is any session
    /// running system wide."</i>
    /// </para>
    /// <para>
    /// <b>Nothing is planted, and that is the whole condition.</b> The rig's
    /// children are doubles over a pipe, so no process anywhere is running out of
    /// the chromium tree; the only thing standing between this call and a delete
    /// is the session.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ItRefusesWhileASessionIsOpenEvenWithNothingRunningFromTheTree()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var session = Path.Combine(sessions.Root, "open-but-not-launched");

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "open, with no browser process of its own",
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        // The control: nothing is running out of the tree, so the OLD gate would
        // have deleted it.
        await Assert.That(BrowserProcesses.RunningFrom(sessions.ChromiumDirectory)).IsEmpty();

        var refused = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, Chromium);
        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue().Because(text);
        await Assert.That(text).Contains(session);

        // ⚠️ It no longer claims that no new session can start meanwhile, and the
        // deletion is a CORRECTION rather than a loss (previously
        // `Contains("no new session can start meanwhile")`). Under the
        // reader/writer claim this call did NOT get the root -- the session has
        // it -- so nothing is stopping a second session starting, and the old
        // sentence was true only of the design where the reinstall took the
        // claim first and counted afterwards.
        await Assert.That(text.Contains("no new session can start meanwhile", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text).Contains("did not wait for those sessions and never will");
        await Assert.That(text).Contains("shared for its whole life");
        await Assert.That(text).Contains("published no intent");
    }

    /// <summary>
    /// A reinstall is mutual against itself: the second one is refused while the
    /// first holds the browsers root.
    /// </summary>
    /// <remarks>
    /// <b>The maintainer asked for this in the same breath as the rest</b> —
    /// <i>"Including any reinstall sessions."</i> Two of them over one root would
    /// have the second's recursive delete land inside the first's extraction,
    /// which is precisely the corruption the provisioning mutex prevents between
    /// two installers and cannot prevent between a delete and an installer. The
    /// claim is taken here exactly as the product takes it, so this is the same
    /// object rather than a stand-in.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASecondReinstallIsRefusedWhileOneHoldsTheBrowsersRoot()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        using var claim = MaintenanceLock.TryTakeExclusive(sessions.Environment.Paths.BrowsersDirectory, ProvisionedBrowsers.Chromium, out _, out _);

        await Assert.That(claim).IsNotNull();

        var refused = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, Chromium);
        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue().Because(text);
        await Assert.That(text).Contains("no second reinstall can begin");

        // It names the holder rather than saying "busy": the claim carries the
        // pid and its start time, which is this repository's rule for naming a
        // process at all.
        await Assert.That(text).Contains($"is reinstalling '{ProvisionedBrowsers.Chromium}'");

        // And the tree is untouched, which is what "nothing was changed" means.
        await Assert.That(File.Exists(Path.Combine(sessions.ChromiumDirectory, BrowsersManifest.InstallationCompleteMarker))).IsTrue();
    }

    /// <summary>
    /// <c>browserai_init</c> and <c>browserai_resume</c> are both refused while a
    /// reinstall holds the browsers root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the half the running-process census could never do.</b> A
    /// reinstall establishes that nothing is running out of the tree and then
    /// deletes it; a peer's <c>init</c> in that window launches a browser into a
    /// directory that is disappearing, and the census was right when it was
    /// asked. Only a claim held across the whole operation closes it.
    /// </para>
    /// <para>
    /// <b><c>resume</c> is asserted as well as <c>init</c>, and it is the one
    /// that would be forgotten.</b> It opens a browser into an existing profile
    /// under the same tree, so it is exactly as unsafe and reaches the browser by
    /// a different method.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InitAndResumeAreBothRefusedWhileAReinstallHoldsTheBrowsersRoot()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var built = Path.Combine(sessions.Root, "created-before-the-reinstall");
        var session = Path.Combine(sessions.Root, "resumable-and-held-by-nobody");

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = built,
            ["purpose"] = "a session that exists so resume has something to name",
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        // ⚠️ A RECORD COPIED ASIDE, AND THEN NOTHING LIVE ANYWHERE, which is a
        // consequence of the reader/writer claim rather than ceremony: since
        // 2026-08-20 every open session holds the browsers root SHARED, so a
        // reinstall cannot take it exclusively while one exists. Leaving the
        // session open -- which is what this test did until that day -- would now
        // fail the exclusive open and test the opposite direction.
        //
        // The copy is exactly the shape `browserai_resume` needs: a directory
        // holding a record that no live process owns.
        _ = Directory.CreateDirectory(session);
        File.Copy(
            Path.Combine(built, SessionLayout.LockFileName),
            Path.Combine(session, SessionLayout.LockFileName));

        _ = await CallAsync(rig, SessionToolSurface.Destroy, new JsonObject { ["directory"] = built, ["why"] = "the suite exercising this call" });

        using var claim = MaintenanceLock.TryTakeExclusive(sessions.Environment.Paths.BrowsersDirectory, ProvisionedBrowsers.Chromium, out _, out _);

        // The control: it really was taken, which is only possible because
        // nothing holds the root shared any more.
        await Assert.That(claim).IsNotNull();

        var refusedInit = TextOf(await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "must-not-be-created"),
            ["purpose"] = "a session that must not start while the browsers are being replaced",
        }));

        var refusedResume = TextOf(await CallAsync(rig, SessionToolSurface.Resume, new JsonObject { ["directory"] = session, ["why"] = "the suite exercising this call" }));

        foreach (var text in new[] { refusedInit, refusedResume })
        {
            await Assert.That(text).Contains("BrowserAI is replacing the browsers under");
            await Assert.That(text).Contains("no session can start");
        }

        // ⚠️ Refused BEFORE anything was created, which is the property that
        // makes this a lock rather than a message: a directory made and then
        // abandoned is a session record nobody owns.
        await Assert.That(Directory.Exists(Path.Combine(sessions.Root, "must-not-be-created"))).IsFalse();
    }

    /// <summary>
    /// Blocks until no install is in flight against a browsers root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><c>WaitAsync</c> is not enough for this, and never was — it is the
    /// wait the two tests below already do and it does not answer this
    /// question.</b> <c>WaitAsync</c> begins with <c>Ensure</c>, which
    /// short-circuits the instant <c>INSTALLATION_COMPLETE</c> appears; the
    /// installing thread is still inside <c>Install</c> at that moment, holding
    /// the machine-wide provisioning mutex while it prunes superseded revisions,
    /// and it releases only on the way out. The gap was invisible until
    /// 2026-08-18, when <c>browserai_reinstall_browser</c> started refusing while
    /// that mutex is held — which is the point of the fix and is exactly what a
    /// caller would meet.
    /// </para>
    /// <para>
    /// <b>Found by CI rather than locally</b>, because a fake installer that
    /// finishes in microseconds closes the gap on a fast machine and does not on
    /// a slower one. Waiting on the mutex is waiting for the thing the product
    /// waits for, which is why this is not a sleep.
    /// </para>
    /// <para>
    /// <b>Acquire and release with no <c>await</c> between them</b>: a named
    /// mutex is owned by the thread that waited on it, and a continuation
    /// resuming elsewhere would make the release throw about "an unsynchronized
    /// block of code".
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root whose install to wait out.</param>
    private static void WaitUntilNoInstallIsInFlight(string browsersDirectory)
    {
        using var mutex = MachineMutex.Create(
            BrowserProvisioner.MutexNameFor(browsersDirectory, ProvisionedBrowsers.Chromium));

        if (mutex.Acquire(TestDefaults.ProcessHang) is MutexAcquisition.NotAcquired)
        {
            throw new InvalidOperationException(
                $"An install has held the provisioning mutex for '{browsersDirectory}' for longer than this suite's hang detector, "
                + "so whatever this test goes on to assert about a reinstall would be about a refusal rather than about a delete.");
        }

        mutex.Release();
    }

    /// <summary>
    /// It takes the family and nothing else, and says why in its description.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Renamed 2026-08-19 (previously
    /// <c>ItTakesNoArgumentsAndSaysWhyInItsDescription</c>, asserting a property
    /// count of zero).</b> The no-arguments property was real and its stated
    /// reason — <i>"there is nothing to name"</i> — expired when
    /// <c>browserai_init</c> began offering a second family. What survives
    /// unchanged is everything else: still no <c>session</c> argument, because
    /// this tool is machine-scoped rather than session-scoped, and still no force
    /// flag. The assertion is on the exact argument set rather than on a count,
    /// so a second argument appearing is as red as the first one vanishing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ItTakesTheFamilyAndNothingElseAndSaysWhyInItsDescription()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var tools = await rig.Client.RoundTripAsync("tools/list");

        var tool = (tools["tools"]?.AsArray() ?? [])
            .Single(entry => (string?)entry!["name"] == SessionToolSurface.ReinstallBrowser)!;

        await Assert.That(tool["inputSchema"]!["properties"]!.AsObject().Select(property => property.Key))
            .IsEquivalentTo(TheOnlyArgument);
        await Assert.That(tool["inputSchema"]!["required"]!.AsArray().Select(entry => (string)entry!))
            .IsEquivalentTo(TheOnlyArgument);

        // No session argument, which is the property that never depended on how
        // many families there are.
        await Assert.That(tool["inputSchema"]!["properties"]![SessionToolSurface.SessionParameter]).IsNull();

        var description = (string)tool["description"]!;

        await Assert.That(description).Contains("REFUSES");
        await Assert.That(description).Contains(SessionToolSurface.Init);

        // The reason the argument exists, in the sentence a model reads: without
        // it, a caller has a required argument and no way to know why guessing
        // is worse than asking.
        await Assert.That(description).Contains("REQUIRED");
        await Assert.That(description).Contains("no default");
    }

    /// <summary>
    /// <c>shared</c> deletes every shared component's tree and downloads them
    /// all again, and it is the only value that can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The gap this closes, added 2026-08-19.</b> <c>ffmpeg</c> and
    /// <c>winldd</c> are downloaded into the browsers root by <b>both</b>
    /// families, each carries its own <c>INSTALLATION_COMPLETE</c>, and a family
    /// reinstall deletes only that family's revision directory — so a corrupted
    /// <c>ffmpeg</c>, which is what the <c>video</c> artifact type needs, was
    /// permanent through this server's own surface.
    /// </para>
    /// <para>
    /// <b>Both directions, because the interesting half is what it does NOT
    /// touch.</b> A family reinstall must still leave the shared trees alone, and
    /// a shared reinstall must leave the family trees alone — an implementation
    /// that deleted the browsers root would satisfy any assertion about the
    /// component that came back.
    /// </para>
    /// <para>
    /// <b>No default session, because that is the refusal this target has</b> —
    /// see <see cref="ASessionOfEitherFamilyBlocksEveryReinstallTarget"/>. The
    /// rig opens one, and it would block this.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSharedTargetRemovesAndRebuildsEveryComponentBothFamiliesUse()
    {
        var asked = new List<string>();

        await using var sessions = RigSessionEnvironment.Create(
            opensDefaultSession: false,
            installer: (browser, root) =>
            {
                lock (asked)
                {
                    asked.Add(browser);
                }

                // The double stands in for `install-browser ffmpeg`, which was
                // measured on 2026-08-19 to lay down BOTH components -- so it
                // writes both markers, or the product's per-component
                // completeness check would be asserted against a fake that is
                // less capable than the thing it replaces.
                //
                // Built from the browsers root the product hands it, rather than
                // from the rig: this lambda is constructed before the rig exists.
                return FakeInstaller.SucceedingForAll(BrowserAiPaths.SharedComponentDirectoriesIn(root), TimeSpan.Zero);
            },
            timers: new ProvisioningTimers { Poll = TimeSpan.FromMilliseconds(20) });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        WaitUntilNoInstallIsInFlight(sessions.Environment.Paths.BrowsersDirectory);

        var shared = SharedDirectoriesIn(sessions);
        var stale = new List<string>();

        foreach (var directory in shared)
        {
            var file = Path.Combine(directory, "corrupt-and-marked-complete.bin");
            _ = Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(file, new string('x', 4096));
            InstallationMarker.Write(directory);
            stale.Add(file);
        }

        // A complete chromium tree beside them, which this call must not touch.
        _ = Directory.CreateDirectory(sessions.ChromiumDirectory);
        var family = Path.Combine(sessions.ChromiumDirectory, "the-browser-that-was-fine.bin");
        await File.WriteAllTextAsync(family, "untouched");
        InstallationMarker.Write(sessions.ChromiumDirectory);

        var answer = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, new JsonObject { ["browser"] = ProvisionedBrowsers.Shared });
        var text = TextOf(answer);

        await Assert.That((bool?)answer["isError"]).IsNotEqualTo(true).Because(text);
        await Assert.That(text).Contains("Re-provisioned shared");

        // ⚠️ EVERY component, and the marker as well as the directory. The
        // marker is the only evidence a tree is complete rather than merely
        // present, and it is the thing upstream short-circuits on for thirty
        // days, so a rebuild that left one unmarked would be the exact state
        // this tool exists to repair.
        foreach (var (directory, file) in shared.Zip(stale))
        {
            await Assert.That(File.Exists(file)).IsFalse().Because($"{file}\n\n{text}");
            await Assert.That(File.Exists(Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker))).IsTrue().Because(text);
            await Assert.That(text).Contains(directory);
        }

        // One invocation for both, named for the component that brings the other
        // with it. Measured, not assumed -- see ProvisionedBrowsers.SharedInstallTarget.
        string invoked;

        lock (asked)
        {
            invoked = string.Join(", ", asked);
        }

        await Assert.That(invoked).IsEqualTo(ProvisionedBrowsers.SharedInstallTarget);

        // And the browser beside them is untouched, which is the half an
        // over-broad delete would fail.
        await Assert.That(File.Exists(family)).IsTrue();
    }

    /// <summary>
    /// A shared reinstall is refused while any session is open, whichever family
    /// it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THIS IS THE DECISION, AND IT IS STRICTER THAN THE FAMILY PATH ON
    /// PURPOSE.</b> A family reinstall is gated on a process running out of that
    /// tree, and for a family that is the same question as <i>a session is
    /// driving this browser</i>: <c>chrome.exe</c> lives for the session's life
    /// and holds its own image open. For the shared components the two questions
    /// come apart — <c>ffmpeg-win64.exe</c> exists only while a recording runs —
    /// so a process-only gate answers <i>nothing is using it</i> on a machine
    /// full of live sessions, any of which starts the codec the instant a
    /// <c>video</c> artifact is asked for.
    /// </para>
    /// <para>
    /// <b>The session here is Firefox and the components are shared, which is
    /// what makes this arm the decision rather than a restatement.</b> A Firefox
    /// session does <b>not</b> block a Chromium reinstall — the arm below asserts
    /// that too, so this cannot be satisfied by a filter that simply stopped
    /// filtering.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionOfEitherFamilyBlocksEveryReinstallTarget()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        // Seeded complete so opening a firefox session does not start a download
        // this rig has no installer for.
        InstallationMarker.Write(Path.Combine(sessions.Environment.Paths.BrowsersDirectory, $"firefox-{BrowserAiPaths.FirefoxRevision}"));

        var session = Path.Combine(sessions.Root, "a-firefox-session-that-could-record");

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "open on the other family while the shared components are asked for",
            ["browser"] = ProvisionedBrowsers.Firefox,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        var refused = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, new JsonObject { ["browser"] = ProvisionedBrowsers.Shared });
        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue().Because(text);
        await Assert.That(text).Contains(session);
        await Assert.That(text).Contains("no force option");

        // ⚠️ AND IT SAYS WHY A SESSION THAT IS NOT RUNNING ANYTHING BLOCKS IT.
        // Without that sentence the refusal reads as a bug to whoever meets it:
        // nothing is running out of those trees, and the tool refused anyway.
        await Assert.That(text).Contains("shared for its whole life");

        // The shared trees are untouched, which is what "nothing was changed"
        // has to mean.
        foreach (var directory in SharedDirectoriesIn(sessions))
        {
            await Assert.That(Directory.Exists(directory)).IsFalse().Because(text);
        }

        // ⚠️ THE CONTROL IS NOW THE OPPOSITE ASSERTION, and the inversion is the
        // decision (previously: "the same Firefox session does NOT block a
        // chromium reinstall, because nothing is running out of the chromium
        // tree"). The maintainer's words on 2026-08-20 were "any init or resume
        // should take a system level lock. No matter the browser type", and the
        // claim is one file at the root of the browsers directory that knows
        // nothing about families -- so the same Firefox session blocks a chromium
        // reinstall too, and the refusal names it. A filter that "simply stopped
        // filtering" is now the correct behaviour rather than the failure this
        // arm guarded against, and what guards the property instead is that the
        // refusal has to NAME the session the caller must close.
        var chromium = TextOf(await CallAsync(rig, SessionToolSurface.ReinstallBrowser, Chromium));

        await Assert.That(chromium.Contains(session, StringComparison.OrdinalIgnoreCase))
            .IsTrue()
            .Because(chromium);
    }

    /// <summary>
    /// <c>shared</c> is offered by the reinstall tool and refused by
    /// <c>browserai_init</c>.
    /// </summary>
    /// <remarks>
    /// <b>The two accepted sets are different and the difference is the point.</b>
    /// A session's <c>browser</c> is a thing that renders web pages; until
    /// 2026-08-19 both tools read one list, so widening the reinstall would have
    /// widened <c>init</c> in the same edit and nothing would have failed. What
    /// would then exist is a session bound for its whole life to a codec.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task SharedIsAReinstallTargetAndIsNotABrowserASessionCanBeOpenedAgainst()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var tools = (await rig.Client.RoundTripAsync("tools/list"))["tools"]!.AsArray();

        var reinstall = tools.Single(entry => (string?)entry!["name"] == SessionToolSurface.ReinstallBrowser)!;
        var init = tools.Single(entry => (string?)entry!["name"] == SessionToolSurface.Init)!;

        var offered = reinstall["inputSchema"]!["properties"]!["browser"]!["enum"]!.AsArray().Select(entry => (string)entry!).ToList();
        var sessionBrowsers = init["inputSchema"]!["properties"]!["browser"]!["enum"]!.AsArray().Select(entry => (string)entry!).ToList();

        await Assert.That(string.Join(", ", offered)).IsEqualTo(string.Join(", ", ProvisionedBrowsers.ReinstallTargets));
        await Assert.That(string.Join(", ", sessionBrowsers)).IsEqualTo(string.Join(", ", ProvisionedBrowsers.Families));
        await Assert.That(offered).Contains(ProvisionedBrowsers.Shared);
        await Assert.That(sessionBrowsers).DoesNotContain(ProvisionedBrowsers.Shared);

        // The description says what it is, because "shared" is the one value a
        // model cannot guess the meaning of from the argument's name.
        var description = (string)reinstall["description"]!;

        foreach (var component in ProvisionedBrowsers.SharedComponents)
        {
            await Assert.That(description).Contains(component);
        }

        // And the schema is refused rather than merely undocumented: an init
        // naming it must fail, and the refusal must list what is accepted.
        var refused = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "a-session-driven-by-a-codec"),
            ["purpose"] = "names something that is not a browser",
            ["browser"] = ProvisionedBrowsers.Shared,
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains(ProvisionedBrowsers.Chromium);
        await Assert.That(TextOf(refused)).Contains("Nothing was changed");
    }

    /// <summary>Where this rig's shared component trees are, or would be.</summary>
    /// <param name="sessions">The rig.</param>
    /// <returns>The absolute directories, in the product's own order.</returns>
    private static IReadOnlyList<string> SharedDirectoriesIn(RigSessionEnvironment sessions) =>
        sessions.Environment.Provisioner.SharedComponentDirectories();

    [Test]
    public async Task EveryAuthoredToolIsAnsweredAndAnUnknownOneIsRefused()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var advertised = (await rig.Client.RoundTripAsync("tools/list"))["tools"]!.AsArray()
            .Select(tool => (string)tool!["name"]!)
            .Where(SessionToolSurface.IsAuthored)
            .ToList();

        // Both directions, which is what makes this a mechanism rather than a
        // count: a tool declared and never routed answers "not a BrowserAI
        // session tool", and a tool routed and never declared is invisible to
        // every caller.
        await Assert.That(string.Join(", ", advertised)).IsEqualTo(string.Join(", ", SessionToolSurface.Names));

        // ⚠️ And every declared name is actually ROUTED. The comparison above is
        // between two readings of one list, so a seventh tool added to the
        // surface and forgotten in the dispatch would satisfy it while answering
        // "not a BrowserAI session tool" to every caller. Called with no
        // arguments on purpose: each answers or refuses on its own merits, and
        // the only answer that fails here is the one that says the tool does not
        // exist.
        var unrouted = new List<string>();

        foreach (var tool in SessionToolSurface.Names)
        {
            var answer = await CallAsync(rig, tool, []);

            if (TextOf(answer).Contains("is not a BrowserAI session tool", StringComparison.Ordinal))
            {
                unrouted.Add(tool);
            }
        }

        await Assert.That(string.Join(", ", unrouted)).IsEmpty();

        // Deny-by-default in the authored half of the surface. The prefix match
        // is what routes a call here at all, so a name nobody implemented must
        // be refused rather than forwarded to the child as an upstream tool.
        var invented = await CallAsync(rig, "browserai_do_something_nobody_built", []);

        await Assert.That((bool?)invented["isError"]).IsTrue();
        await Assert.That(TextOf(invented)).Contains("is not a BrowserAI session tool");
    }

    /// <summary>
    /// The claims are cumulative: two sessions both hold the root, and the
    /// reinstall is refused until <b>both</b> are gone.
    /// </summary>
    /// <remarks>
    /// <b>Two rather than one, because one proves nothing about cumulativeness.</b>
    /// The maintainer's word for it was <i>"cumulative"</i>, and what that means
    /// on Windows is that any number of <c>FileAccess.Read</c> /
    /// <c>FileShare.Read</c> opens coexist with no count kept anywhere — so the
    /// arm that would fail against a design holding one claim per process is the
    /// one where the first session goes and the second still holds it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TwoSessionsBothHoldTheRootAndOneClosingIsNotEnough()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var root = sessions.Environment.Paths.BrowsersDirectory;
        var first = Path.Combine(sessions.Root, "cumulative-one");
        var second = Path.Combine(sessions.Root, "cumulative-two");

        foreach (var directory in new[] { first, second })
        {
            var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = "one of two sessions holding the browsers root at once",
            });

            await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));
        }

        // Both are open, so the exclusive open is refused and the refusal names
        // both -- a list that named one would leave the caller closing half of
        // what is in the way.
        var refused = TextOf(await CallAsync(rig, SessionToolSurface.ReinstallBrowser, Chromium));

        await Assert.That(refused).Contains(first);
        await Assert.That(refused).Contains(second);
        await Assert.That(refused).Contains("2 session(s) are holding it");

        _ = await CallAsync(rig, SessionToolSurface.Destroy, new JsonObject { ["directory"] = first, ["why"] = "the suite exercising this call" });

        // ⚠️ THE ARM THAT MATTERS. One is gone and the other still holds it, so
        // the claim must still be refused. A design that kept one claim per
        // process, or a count released on the first close, is red here.
        using (var stillHeld = MaintenanceLock.TryTakeExclusive(root, ProvisionedBrowsers.Chromium, out _, out _))
        {
            await Assert.That(stillHeld).IsNull();
        }

        _ = await CallAsync(rig, SessionToolSurface.Destroy, new JsonObject { ["directory"] = second, ["why"] = "the suite exercising this call" });

        using var free = MaintenanceLock.TryTakeExclusive(root, ProvisionedBrowsers.Chromium, out _, out _);

        // And the control: with both gone it really is available, so the arm
        // above is about the second session rather than about something that
        // never releases.
        await Assert.That(free).IsNotNull();
    }

    /// <summary>
    /// A reader that <b>dies</b> releases the claim, with nothing running to
    /// clean up after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the property that decided the mechanism, and it cannot be
    /// proven in one process.</b> A named semaphore spans threads exactly as a
    /// file does, and its count is <i>not</i> restored when its holder dies — so
    /// one crashed session would refuse every reinstall on the machine until the
    /// next reboot. Windows closes a file handle when the process object goes,
    /// however it went.
    /// </para>
    /// <para>
    /// <b>The probe takes the claim through <c>MaintenanceLock.TakeShared</c></b>,
    /// so what is killed holds the product's own open with the product's own
    /// share mode rather than a <c>FileStream</c> a test wrote.
    /// </para>
    /// <para>
    /// <b>The kill is the job object closing</b>, which is a
    /// <c>TerminateProcess</c> with no unwinding at all — the harshest death
    /// available, and the one a semaphore could not survive.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReaderThatDiesReleasesTheRootWithNothingRunningToCleanUp()
    {
        using var scratch = ScratchDirectory.Create("claim-holder-dies");

        var root = Path.Combine(scratch.Path, "browsers");
        var ready = Path.Combine(scratch.Path, "ready.json");

        _ = Directory.CreateDirectory(root);

        using (var scope = new JobObjectScope())
        {
            _ = scope.Launch(ProbePath, scratch.Path, "browsers-claim", root, ready);

            await WaitForFileAsync(ready);

            // Held by a process this one has no handle on and shares no memory
            // with, which is the only arrangement that proves anything here.
            using var blocked = MaintenanceLock.TryTakeExclusive(root, ProvisionedBrowsers.Chromium, out _, out _);

            await Assert.That(blocked).IsNull();
        }

        // The job closed, so the probe was terminated. Nothing ran on its way
        // out -- no finally, no Dispose, no release.
        //
        // ⚠️ POLLED WITH A HANG DETECTOR RATHER THAN ASSERTED ON THE NEXT LINE,
        // and the reason is the same one ScratchDirectory.RemoveTreeWhenReleased
        // gives: `TerminateProcess` returning -- and even the process object
        // signalling -- is not proof that the kernel has finished closing that
        // process's handles. What is under test is whether the claim becomes
        // free AT ALL with nothing running to release it, never how fast, so the
        // bound is a hang detector and its failure names what it was waiting
        // for.
        var deadline = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            using var afterwards = MaintenanceLock.TryTakeExclusive(root, ProvisionedBrowsers.Chromium, out _, out _);

            if (afterwards is not null)
            {
                return;
            }

            await Assert.That(deadline.Elapsed < TestDefaults.BrowserHang)
                .IsTrue()
                .Because($"the claim on '{root}' was still held after the process holding it was terminated, so nothing released it");

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// The refusal a session tool meets during a reinstall says how far in the
    /// reinstall is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted on a figure the test can predict, exactly as the first-run
    /// progress arm is.</b> The staging directory is filled by hand with a known
    /// number of bytes; elapsed and the rate derived from it are the speed of the
    /// machine, so what is asserted is the byte figure and that a rate was given
    /// at all.
    /// </para>
    /// <para>
    /// <b>And the zero-bytes arm is asserted too</b>, because it is the one a
    /// careless renderer gets wrong: an empty staging directory is the delete, or
    /// an extraction already under way, and never a stall.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRefusalDuringAReinstallSaysHowFarInItIs()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var root = sessions.Environment.Paths.BrowsersDirectory;

        using var claim = MaintenanceLock.TryTakeExclusive(root, ProvisionedBrowsers.Chromium, out _, out _);

        await Assert.That(claim).IsNotNull();

        // Nothing staged yet: the delete comes first, and the sentence must say
        // which two things it cannot tell apart rather than implying a stall.
        var beforeTheDownload = TextOf(await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "refused-before-the-download"),
            ["purpose"] = "refused while the tree is being deleted",
        }));

        await Assert.That(beforeTheDownload).Contains("nothing in the download staging directory");
        await Assert.That(beforeTheDownload).Contains("an extraction already under way");

        // An archive in flight, as upstream's installer would have written it:
        // the child's TEMP points at this directory, so it is the one place a
        // peer can read a download's progress from outside the process running
        // it.
        var staging = Path.Combine(root, BrowserProvisioner.DownloadDirectoryName, ProvisionedBrowsers.Chromium);

        _ = Directory.CreateDirectory(staging);
        await File.WriteAllBytesAsync(Path.Combine(staging, "chrome-win64.zip"), new byte[2_500_000]);

        var duringTheDownload = TextOf(await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "refused-during-the-download"),
            ["purpose"] = "refused while the archive is arriving",
        }));

        await Assert.That(duringTheDownload).Contains("2.5 MB downloaded in");
        await Assert.That(duringTheDownload).Contains("Mbps observed");

        // And it still names the holder, which is the half the progress clause
        // must not have displaced.
        await Assert.That(duringTheDownload).Contains($"is reinstalling '{ProvisionedBrowsers.Chromium}'");
    }

    /// <summary>Waits for a probe's ready file, with a hang detector rather than a budget.</summary>
    /// <param name="path">The file the probe writes once it holds the claim.</param>
    /// <returns>The wait.</returns>
    private static async Task WaitForFileAsync(string path)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();

        while (!File.Exists(path))
        {
            if (deadline.Elapsed > TestDefaults.BrowserHang)
            {
                throw new InvalidOperationException(
                    $"The probe never wrote '{path}', so it never took the claim and nothing below would be testing what it says it is.");
            }

            await Task.Delay(10);
        }
    }

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
