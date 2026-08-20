// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Security.AccessControl;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The update path: the feed URL, the apply gate, the three timers, and the
/// things that must never appear in the product's source.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here runs without an install, which is the whole reason the
/// seams exist.</b> Under a test host this process is not a Velopack install and
/// <c>VelopackLocator.Current</c> has never been set, so <c>UpdateService</c> is
/// driven through <see cref="IUpdateClient"/> and the install-shaped facts are
/// asserted on the source instead. What the seams cannot cover — that a real
/// package applies, that a rollback applies, and that the browsers beside
/// <c>current\</c> survive both — was run by hand against a real install and is
/// recorded in
/// [kb](../../kb/packaging/velopack.md#the-update-lane-end-to-end-against-a-real-feed),
/// because it needs an installer this suite must never run.
/// </para>
/// </remarks>
internal sealed class UpdateTests
{
    // ---- The feed URL: the worst hazard in the section -----------------------

    /// <summary>
    /// The shipped channel-in-the-URL bug, refused rather than reproduced.
    /// </summary>
    /// <remarks>
    /// A base URL ending in the channel makes Velopack fetch
    /// <c>{base}/{channel}/releases.{channel}.json</c>, which 404s and surfaces
    /// as *"no update available"*. It bricked auto-update for three shipped
    /// versions of a sibling project and the only recovery was a manual
    /// reinstall of every client — which is why this is a refusal at
    /// construction rather than a comment.
    /// </remarks>
    [Test]
    public async Task AFeedUrlCarryingTheChannelIsRefused()
    {
        var failure = Assert.Throws<ArgumentException>(
            () => UpdateFeed.Create("https://example.invalid/browserai/win", "win"));

        await Assert.That(failure!.Message).Contains("releases.win.json");
        await Assert.That(failure.Message).Contains("ExplicitChannel");

        // Case does not save it: the composed path is what 404s, and NTFS would
        // resolve the directory either way.
        _ = Assert.Throws<ArgumentException>(() => UpdateFeed.Create("https://example.invalid/browserai/WIN/", "win"));
    }

    /// <summary>An empty channel is a channel, not "unset".</summary>
    /// <remarks>
    /// Velopack null-coalesces <c>ExplicitChannel</c>, so an empty string
    /// reaches the composer and produces <c>releases..json</c>.
    /// </remarks>
    [Test]
    public async Task AnEmptyChannelIsRefusedBecauseItComposesReleasesDotDotJson()
    {
        var failure = Assert.Throws<ArgumentException>(() => UpdateFeed.Create("https://example.invalid/browserai", " "));

        await Assert.That(failure!.Message).Contains("releases..json");
    }

    /// <summary>
    /// <c>vpk</c> lower-cases the channel and the client does not.
    /// </summary>
    /// <remarks>
    /// The two therefore agree on NTFS and disagree on a case-sensitive object
    /// store, which is exactly a sibling project's S3 setup — a feed that works
    /// on the developer's machine and 404s in production.
    /// </remarks>
    [Test]
    public async Task AChannelThatIsNotLowerCaseIsRefused()
    {
        var failure = Assert.Throws<ArgumentException>(() => UpdateFeed.Create("https://example.invalid/browserai", "Beta"));

        await Assert.That(failure!.Message).Contains("beta");
    }

    /// <summary>The composed URL is the one Velopack will actually request.</summary>
    [Test]
    public async Task TheManifestUrlIsComposedTheWayVelopackComposesIt()
    {
        var feed = UpdateFeed.Create("https://example.invalid/browserai/", "win");

        await Assert.That(feed.BaseUrl).IsEqualTo("https://example.invalid/browserai");
        await Assert.That(feed.ManifestUrl).IsEqualTo("https://example.invalid/browserai/releases.win.json");
        await Assert.That(feed.IsLocalDirectory).IsFalse();
    }

    /// <summary>
    /// A directory feed is accepted and says so, because a green update test
    /// against one has not tested the feed.
    /// </summary>
    [Test]
    public async Task ALocalDirectoryFeedIsAcceptedAndDeclaresItself()
    {
        using var scratch = ScratchDirectory.Create("update-feed");
        var feed = UpdateFeed.Create(scratch.Path);

        await Assert.That(feed.IsLocalDirectory).IsTrue();
        await Assert.That(feed.Channel).IsEqualTo("win");
    }

    /// <summary>
    /// ⛔ <b>THE ONE BULLET OF STEP 19'S DONE-TEST THAT IS NOT DONE.</b> The real
    /// production feed URL must resolve over HTTP and return a manifest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is skipped rather than absent, and rather than faked.</b>
    /// <see cref="UpdateConfiguration.ProductionBaseUrl"/> is <c>null</c>:
    /// the feed will be a public GitHub repository, the maintainer has agreed to
    /// make it public, and **nothing has been published**. There is no URL to
    /// resolve.
    /// </para>
    /// <para>
    /// <b>A local HTTP server would satisfy this test and prove nothing</b>, which
    /// is why one is not used. [TESTING.md](../../TESTING.md) is explicit that
    /// this assertion cannot be made hermetically: a directory source composes
    /// paths differently and will pass where production 404s, and a served
    /// stand-in composes them the same way as the real one *by construction*
    /// rather than by evidence. What has to be checked is the URL somebody
    /// actually typed, against the storage somebody actually configured — the
    /// exact pair that bricked a production deployment's auto-update for three
    /// shipped versions.
    /// </para>
    /// <para>
    /// <b>What is owed, precisely:</b> publish the feed, set
    /// <see cref="UpdateConfiguration.ProductionBaseUrl"/>, then replace this
    /// skip with a real request for <c>{ProductionBaseUrl}/releases.win.json</c>
    /// asserting a 200 and a parseable <c>Assets</c> array. Everything else in
    /// the update lane — the pack, the delta, the apply, the rollback and the
    /// survival of the browsers — was run for real and is recorded in
    /// [kb](../../kb/packaging/velopack.md#the-update-lane-end-to-end-against-a-real-feed).
    /// </para>
    /// <para>
    /// ⚠️ <b>This skip blocks a release, and that is intended.</b>
    /// [`CLAUDE.md`](../../CLAUDE.md) forbids releasing with a skipped test, and
    /// [release checklist item 8](../../RELEASING.md) requires the skipped count
    /// to be zero. So the debt is not a note somebody has to remember — it is a
    /// red gate on the first release, which is exactly where it belongs.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheProductionFeedUrlResolvesOverHttpAndReturnsAManifest()
    {
        await Assert.That(UpdateConfiguration.ProductionBaseUrl).IsNotNull();

        var feed = UpdateFeed.Create(UpdateConfiguration.ProductionBaseUrl!);
        using var http = new HttpClient();

        using var answer = await http.GetAsync(new Uri(feed.ManifestUrl));

        await Assert.That((int)answer.StatusCode).IsEqualTo(200);
        await Assert.That(await answer.Content.ReadAsStringAsync()).Contains("\"Assets\"");
    }

    // ---- The apply gate ------------------------------------------------------

    /// <summary>
    /// One process is alone; two are not. This is the gate that stops an update
    /// terminating every other agent's browsers.
    /// </summary>
    /// <remarks>
    /// In-process, because what is being asserted is the file-handle mechanism
    /// rather than process boundaries: the handle is
    /// <c>FileAccess.ReadWrite, FileShare.Read</c>, so a second holder is
    /// refused by the kernel whether it is in this process or another one, and
    /// the OS releases it on death either way.
    /// </remarks>
    [Test]
    public async Task ASecondLiveInstanceIsSeenAndTheFirstThenRefusesToApply()
    {
        using var scratch = ScratchDirectory.Create("update-live");
        var paths = new LocalAppDataPaths(scratch.Path);

        using var first = LiveInstances.Join(paths, NullLogger.Instance);
        await Assert.That(first).IsNotNull();
        await Assert.That(first!.AmIAlone()).IsTrue();

        var second = LiveInstances.Join(paths, NullLogger.Instance);
        await Assert.That(second).IsNotNull();

        await Assert.That(first.AmIAlone()).IsFalse();
        await Assert.That(second!.AmIAlone()).IsFalse();

        // And it is the HANDLE that decides, not the file: releasing one makes
        // the other alone again without anything being told.
        second.Dispose();
        await Assert.That(first.AmIAlone()).IsTrue();
    }

    /// <summary>
    /// A marker left by a process that died is reclaimed rather than counted
    /// forever.
    /// </summary>
    /// <remarks>
    /// The file outliving its holder is the normal case — BrowserAI is
    /// terminated from outside by design, so a <c>finally</c> is by construction
    /// the path that does not run when it matters. An unreclaimed marker would
    /// disable updating permanently after the first hard kill, and nothing would
    /// report it.
    /// </remarks>
    [Test]
    public async Task AMarkerWhoseHolderIsGoneIsReclaimedRatherThanCountedForever()
    {
        using var scratch = ScratchDirectory.Create("update-live-stale");
        var paths = new LocalAppDataPaths(scratch.Path);

        _ = Directory.CreateDirectory(paths.LiveInstanceDirectory);
        var abandoned = Path.Combine(paths.LiveInstanceDirectory, "4242-deadbeef.live");
        await File.WriteAllTextAsync(abandoned, string.Empty);

        using var live = LiveInstances.Join(paths, NullLogger.Instance);

        await Assert.That(live!.AmIAlone()).IsTrue();
        await Assert.That(File.Exists(abandoned)).IsFalse();
    }

    /// <summary>
    /// The live-instance name comes out of the same canonicalisation every other
    /// directory-keyed name does.
    /// </summary>
    [Test]
    public async Task TheLiveSetIsKeyedOnTheInstallRootWithTheOneCanonicalisation()
    {
        var name = LiveInstances.MutexNameFor(@"C:\Users\x\AppData\Local\BrowserAI");
        var other = LiveInstances.MutexNameFor(@"c:\users\x\appdata\local\browserai\");

        await Assert.That(name).StartsWith(LockScopes.PerDirectoryPrefix);
        await Assert.That(name).IsEqualTo(other);
    }

    // ---- Liveness is three-valued -------------------------------------------

    /// <summary>
    /// The census answers <b>Alone</b>, <b>NotAlone</b> or <b>Undetermined</b>,
    /// and only the first of the three reads as <see langword="true"/> to the
    /// updater.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The mapping is the whole assertion.</b> Widening a return type is
    /// exactly where a consumer changes silently, so this drives all three states
    /// through <see cref="LiveInstances.Census"/> and requires
    /// <see cref="LiveInstances.AmIAlone"/> to agree with the pre-widening
    /// behaviour on every one of them: <i>true</i> for <c>Alone</c> and
    /// <i>false</i> for both of the others.
    /// </para>
    /// <para>
    /// <b>Every state is produced by a real mechanism rather than constructed.</b>
    /// One instance is alone; two are not; and an instance that has left the set
    /// cannot speak for it, which is the cheapest genuine <c>Undetermined</c>
    /// there is.
    /// </para>
    /// </remarks>
    [Test]
    public async Task EveryCensusAnswerOtherThanAloneStillReadsAsNotAloneToTheUpdater()
    {
        using var scratch = ScratchDirectory.Create("update-three-valued");
        var paths = new LocalAppDataPaths(scratch.Path);

        using var first = LiveInstances.Join(paths, NullLogger.Instance);

        await Assert.That(first!.Census().State).IsEqualTo(Liveness.Alone);
        await Assert.That(first.Census().Why).IsNull();
        await Assert.That(first.AmIAlone()).IsTrue();

        var second = LiveInstances.Join(paths, NullLogger.Instance);
        var crowded = first.Census();

        await Assert.That(crowded.State).IsEqualTo(Liveness.NotAlone);
        await Assert.That(crowded.Others).IsEqualTo(1);
        await Assert.That(first.AmIAlone()).IsFalse();

        second!.Dispose();
        await Assert.That(first.AmIAlone()).IsTrue();

        // The third state, and the reason this widening exists: it is neither of
        // the other two and it says what stopped it.
        first.Dispose();
        var undetermined = first.Census();

        await Assert.That(undetermined.State).IsEqualTo(Liveness.Undetermined);
        await Assert.That(undetermined.Why).IsNotNull();
        await Assert.That(undetermined.Why!).Contains(paths.LiveInstanceDirectory);
        await Assert.That(first.AmIAlone()).IsFalse();
    }

    /// <summary>
    /// A marker whose held-ness cannot be established makes the census
    /// <b>undetermined</b> — and it is left exactly where it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the arm that used to answer <i>not alone</i>, and both
    /// answers keep the updater on the same side.</b> What the old one could not
    /// do is tell a maintainer that the problem is an ACL on a named path rather
    /// than a peer that is genuinely running — which is a refusal nothing can act
    /// on. The assertion is therefore on the <i>reason</i> as much as on the
    /// state.
    /// </para>
    /// <para>
    /// <b>And the marker survives.</b> A file this pass cannot open is a file it
    /// knows nothing about, and removing one on that basis is how a live
    /// instance would become invisible.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AMarkerThatCannotBeOpenedIsLeftAloneAndMakesTheCensusUndeterminedRatherThanAlone()
    {
        using var scratch = ScratchDirectory.Create("update-live-denied");
        var paths = new LocalAppDataPaths(scratch.Path);

        // Joined BEFORE the denial: this process's own marker has to be created,
        // and the denial below is what stops a marker being opened at all.
        using var mine = LiveInstances.Join(paths, NullLogger.Instance);
        await Assert.That(mine!.Census().State).IsEqualTo(Liveness.Alone);

        var stranger = Path.Combine(paths.LiveInstanceDirectory, "4242-cannot-be-read.live");
        await File.WriteAllTextAsync(stranger, string.Empty);

        LivenessAnswer answer;
        LiveMarkerReclaim reclaim;

        // Captured INSIDE the denial below, because the denial is the whole
        // condition: asserting on it afterwards would be asserting about a
        // directory that had already been made readable again.
        bool aloneWhileTheMarkerCouldNotBeRead;

        // WriteData and not ReadData: the probe asks for ReadWrite, so denying
        // the write half refuses it while leaving the directory enumerable and
        // the file readable -- which is what makes this an unanswered question
        // rather than a directory that vanished.
        using (DirectoryDenial.Apply(
            paths.LiveInstanceDirectory,
            FileSystemRights.WriteData,
            InheritanceFlags.ObjectInherit,
            PropagationFlags.InheritOnly))
        {
            answer = mine.Census();
            aloneWhileTheMarkerCouldNotBeRead = mine.AmIAlone();
            reclaim = LiveInstances.ReclaimStaleMarkers(paths, NullLogger.Instance);
        }

        await Assert.That(answer.State).IsEqualTo(Liveness.Undetermined);
        await Assert.That(answer.Why!).Contains(stranger);
        await Assert.That(aloneWhileTheMarkerCouldNotBeRead).IsFalse();

        await Assert.That(reclaim.Reclaimed).IsEqualTo(0);
        await Assert.That(reclaim.Undetermined).IsGreaterThanOrEqualTo(1);
        await Assert.That(File.Exists(stranger)).IsTrue();

        // The positive control: with the denial lifted the same marker is
        // reclaimed and the same census is Alone, so the two answers above came
        // from the ACL and not from something structural about this directory.
        await Assert.That(LiveInstances.ReclaimStaleMarkers(paths, NullLogger.Instance).Reclaimed).IsEqualTo(1);
        await Assert.That(File.Exists(stranger)).IsFalse();
        await Assert.That(mine.Census().State).IsEqualTo(Liveness.Alone);
    }

    // ---- Reclaiming the markers ---------------------------------------------

    /// <summary>
    /// The reclaim removes a marker nobody holds and <b>leaves a held one
    /// exactly where it is</b> — proved in both directions with the same file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the positive control the reclaim is not allowed to ship
    /// without.</b> Reclaiming another process's live marker would make a running
    /// instance invisible to every later census and therefore killable by an
    /// update apply, so <i>it did not delete the held one</i> has to be
    /// distinguished from <i>it did not delete anything</i>. The same marker is
    /// released and the pass is run again: a reclaim that removed nothing at all
    /// fails the second half, and a reclaim that removed everything fails the
    /// first.
    /// </para>
    /// <para>
    /// <b>The handle is the product's own</b> —
    /// <c>FileAccess.ReadWrite, FileShare.Read</c>, byte for byte what
    /// <see cref="LiveInstances.Join"/> takes — so what is being asserted is the
    /// kernel's sharing rule rather than a convention this test invented.
    /// </para>
    /// <para>
    /// <b>In-process, deliberately.</b> Sharing modes are enforced by the kernel
    /// against handles, not against processes, so a second holder is refused
    /// whether it is in this process or another one — which is the same argument
    /// <see cref="ASecondLiveInstanceIsSeenAndTheFirstThenRefusesToApply"/>
    /// already makes.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AHeldMarkerSurvivesTheReclaimAndTheSameMarkerGoesOnceItIsReleased()
    {
        using var scratch = ScratchDirectory.Create("update-live-reclaim");
        var paths = new LocalAppDataPaths(scratch.Path);

        _ = Directory.CreateDirectory(paths.LiveInstanceDirectory);

        var held = Path.Combine(paths.LiveInstanceDirectory, "1234-held-by-a-peer.live");
        var stale = Path.Combine(paths.LiveInstanceDirectory, "4242-nobody-is-there.live");

        await File.WriteAllTextAsync(stale, string.Empty);

        var peer = new FileStream(held, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);

        try
        {
            var first = LiveInstances.ReclaimStaleMarkers(paths, NullLogger.Instance);

            await Assert.That(first.Outcome).IsEqualTo(LiveMarkerReclaimOutcome.Ran);
            await Assert.That(first.Held).IsEqualTo(1);
            await Assert.That(first.Reclaimed).IsEqualTo(1);
            await Assert.That(first.Undetermined).IsEqualTo(0);

            // THE INVARIANT. A peer's marker is another process's proof of life.
            await Assert.That(File.Exists(held)).IsTrue();
            await Assert.That(File.Exists(stale)).IsFalse();
        }
        finally
        {
            await peer.DisposeAsync();
        }

        // The other half of the control: nothing about that file made it
        // un-reclaimable except the handle, and the handle is gone.
        var second = LiveInstances.ReclaimStaleMarkers(paths, NullLogger.Instance);

        await Assert.That(second.Outcome).IsEqualTo(LiveMarkerReclaimOutcome.Ran);
        await Assert.That(second.Held).IsEqualTo(0);
        await Assert.That(second.Reclaimed).IsEqualTo(1);
        await Assert.That(File.Exists(held)).IsFalse();
    }

    /// <summary>
    /// A peer holding the gate makes the reclaim <b>skip instantly</b> rather
    /// than wait, and nothing is touched while it does.
    /// </summary>
    /// <remarks>
    /// <b>This is what stops a hundred starting processes becoming a thundering
    /// herd.</b> The gate is taken at <see cref="LockScopes.NeverWaits"/> — one
    /// process reclaims and the rest pay an acquire and leave — which is the
    /// discipline the stray sweep already applies machine-wide, reused rather
    /// than reinvented. The mutex is held on <i>another thread</i> because a
    /// Windows mutex is owned by the thread that waited on it, so this thread
    /// would otherwise be granted it recursively and the test would prove
    /// nothing.
    /// </remarks>
    [Test]
    public async Task AReclaimWhosePeerHoldsTheGateSkipsAtOnceAndRemovesNothing()
    {
        using var scratch = ScratchDirectory.Create("update-live-contended");
        var paths = new LocalAppDataPaths(scratch.Path);

        _ = Directory.CreateDirectory(paths.LiveInstanceDirectory);
        var stale = Path.Combine(paths.LiveInstanceDirectory, "4242-nobody-is-there.live");
        await File.WriteAllTextAsync(stale, string.Empty);

        using var taken = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        // Kept rather than discarded, for two reasons that are both about this
        // test being able to fail honestly. It is the PRECONDITION -- a peer
        // that did not take the gate would leave the assertions below measuring
        // a skip that happened for some other reason -- and `Release` on a mutex
        // this thread does not own throws, on a background thread, which ends
        // the test host instead of failing a test.
        var acquisition = MutexAcquisition.NotAcquired;

        var holder = new Thread(() =>
        {
            using var gate = MachineMutex.Create(LiveInstances.MutexNameFor(paths.RootAppDir));
            acquisition = gate.Acquire(LockScopes.LiveInstanceGate);
            taken.Set();
            release.Wait();

            if (acquisition is not MutexAcquisition.NotAcquired)
            {
                gate.Release();
            }
        })
        {
            IsBackground = true,
        };

        holder.Start();
        taken.Wait();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var skipped = LiveInstances.ReclaimStaleMarkers(paths, NullLogger.Instance);
        clock.Stop();

        release.Set();
        holder.Join();

        // `Join` is the barrier that makes this read safe, and the precondition
        // is asserted before anything that depends on it.
        await Assert.That(acquisition).IsNotEqualTo(MutexAcquisition.NotAcquired);

        await Assert.That(skipped.Outcome).IsEqualTo(LiveMarkerReclaimOutcome.Skipped);
        await Assert.That(skipped.Reclaimed).IsEqualTo(0);
        await Assert.That(File.Exists(stale)).IsTrue();

        // ⚠️ A HANG DETECTOR AND NOT A BUDGET, and it is the only thing that can
        // tell "skipped at once" from "waited five seconds and then skipped" --
        // both of which return Skipped. The bound is the product's OWN gate
        // timeout, read rather than typed rather than guessed at: reaching it
        // means this call waited on a gate it is supposed to try once, which is
        // the defect. Nothing about a machine's load can approach it, because the
        // work bounded is one zero-timeout acquire.
        await Assert.That(clock.Elapsed).IsLessThan(LockScopes.LiveInstanceGate);

        // The positive control: the same call, the same directory, nothing
        // holding the gate. A skip that was really a no-op would fail here.
        await Assert.That(LiveInstances.ReclaimStaleMarkers(paths, NullLogger.Instance).Reclaimed).IsEqualTo(1);
        await Assert.That(File.Exists(stale)).IsFalse();
    }

    // ---- The service ---------------------------------------------------------

    /// <summary>An update is downloaded, staged and NOT applied when something else is live.</summary>
    [Test]
    public async Task AnUpdateIsStagedButNotAppliedWhileAnotherInstanceIsLive()
    {
        using var scratch = ScratchDirectory.Create("update-staged");
        var paths = new LocalAppDataPaths(scratch.Path);

        using var mine = LiveInstances.Join(paths, NullLogger.Instance);
        using var other = LiveInstances.Join(paths, NullLogger.Instance);

        var client = new ScriptedUpdateClient();
        var shutdowns = 0;
        var service = new UpdateService(client, mine, NullLogger.Instance, () => shutdowns++);

        var outcome = await service.RunOnceAsync(CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(UpdateOutcome.StagedButNotAlone);
        await Assert.That(client.Downloads).IsEqualTo(1);
        await Assert.That(client.Applies).IsEqualTo(0);
        await Assert.That(shutdowns).IsEqualTo(0);
    }

    /// <summary>
    /// An <b>undetermined</b> census stages the update and applies nothing —
    /// byte for byte the outcome a <b>not alone</b> census produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Asserted through <see cref="UpdateService"/> rather than through
    /// <see cref="LiveInstances.AmIAlone"/>'s signature, because the signature is
    /// not what the maintainer's instruction was about.</b> The requirement was
    /// that the updater keep treating <c>Undetermined</c> exactly as it treats
    /// <c>NotAlone</c>, and only the service can be asked that: it is the one
    /// consumer, and every assertion below is on what it <i>did</i> — one
    /// download, zero applies, no shutdown request.
    /// </para>
    /// <para>
    /// <b>The census is asserted to be undetermined first, so this cannot pass
    /// for the wrong reason.</b> Without that line an implementation that
    /// answered <c>NotAlone</c> here — the pre-widening behaviour — would produce
    /// an identical result and the test would report a property it never checked.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AnUndeterminedCensusStagesTheUpdateExactlyAsANotAloneOneDoes()
    {
        using var scratch = ScratchDirectory.Create("update-undetermined");
        var paths = new LocalAppDataPaths(scratch.Path);

        var mine = LiveInstances.Join(paths, NullLogger.Instance);

        // Left the set: this process is no longer a member of the thing it is
        // being asked about, which is undetermined and is not "not alone".
        mine!.Dispose();
        await Assert.That(mine.Census().State).IsEqualTo(Liveness.Undetermined);

        var client = new ScriptedUpdateClient();
        var shutdowns = 0;
        var service = new UpdateService(client, mine, NullLogger.Instance, () => shutdowns++);

        var outcome = await service.RunOnceAsync(CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(UpdateOutcome.StagedButNotAlone);
        await Assert.That(client.Downloads).IsEqualTo(1);
        await Assert.That(client.Applies).IsEqualTo(0);
        await Assert.That(shutdowns).IsEqualTo(0);
    }

    /// <summary>
    /// The staged-but-not-applied line says what it is waiting on and how far in
    /// it got.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-08-20, and the line it asserts replaced one that said
    /// only "another BrowserAI is running out of this install".</b> That read the
    /// same whether one peer was up or forty, and read the same again when the
    /// census could not be taken at all — which is not a wait but a permanent
    /// block. Whoever finds this line in a log has to act differently in those
    /// two cases, so the line has to distinguish them.
    /// </para>
    /// <para>
    /// <b>Both arms are here rather than one.</b> A version that hard-coded a
    /// count would satisfy the first and fail the second, and one that printed
    /// the census enum would satisfy neither.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStagedLineSaysHowManyItIsWaitingOnAndWhatWasAlreadyFetched()
    {
        using var scratch = ScratchDirectory.Create("update-staged-says-why");
        var paths = new LocalAppDataPaths(scratch.Path);

        using var provider = new CapturingLoggerProvider();

        using var mine = LiveInstances.Join(paths, NullLogger.Instance);
        using var other = LiveInstances.Join(paths, NullLogger.Instance);

        _ = await new UpdateService(
            new ScriptedUpdateClient(),
            mine,
            provider.CreateLogger("BrowserAI.Updates"),
            () => { }).RunOnceAsync(CancellationToken.None);

        // One peer, counted rather than implied, and the package the apply is no
        // longer waiting on.
        await Assert.That(provider.Logged("at least 1 other BrowserAI process(es) are running")).IsTrue();
        await Assert.That(provider.Logged("112.4 MB was fetched in")).IsTrue();
        await Assert.That(provider.Logged("Nothing more has to be downloaded")).IsTrue();

        // And the other arm: a census that could not be taken is a permanent
        // block rather than a queue, and the line says so and says why.
        using var undetermined = new CapturingLoggerProvider();
        var lost = LiveInstances.Join(paths, NullLogger.Instance);

        lost!.Dispose();

        await Assert.That(lost.Census().State).IsEqualTo(Liveness.Undetermined);

        _ = await new UpdateService(
            new ScriptedUpdateClient(),
            lost,
            undetermined.CreateLogger("BrowserAI.Updates"),
            () => { }).RunOnceAsync(CancellationToken.None);

        await Assert.That(undetermined.Logged("the census could not be taken, so solitude cannot be proven")).IsTrue();
        await Assert.That(undetermined.Logged("at least")).IsFalse();
    }

    /// <summary>Alone, the same pass applies and asks the process to end.</summary>
    /// <remarks>
    /// <b>It asks rather than exits.</b> <c>Update.exe</c> is waiting on this
    /// pid and will not swap <c>current\</c> until it is gone, so the ordinary
    /// shutdown has to run first — the session locks release, the job objects
    /// close, the log flushes. An <c>Environment.Exit</c> here would skip all
    /// three.
    /// </remarks>
    [Test]
    public async Task AloneTheSamePassAppliesAndAsksForShutdownRatherThanExiting()
    {
        using var scratch = ScratchDirectory.Create("update-applies");
        var paths = new LocalAppDataPaths(scratch.Path);

        using var mine = LiveInstances.Join(paths, NullLogger.Instance);

        var client = new ScriptedUpdateClient();
        var shutdowns = 0;
        var service = new UpdateService(client, mine, NullLogger.Instance, () => shutdowns++);

        var outcome = await service.RunOnceAsync(CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(UpdateOutcome.Applying);
        await Assert.That(client.Applies).IsEqualTo(1);
        await Assert.That(shutdowns).IsEqualTo(1);
    }

    /// <summary>Nothing on offer is not a failure, and nothing is asked of the process.</summary>
    [Test]
    public async Task NothingOnOfferIsAQuietPass()
    {
        using var scratch = ScratchDirectory.Create("update-nothing");
        var paths = new LocalAppDataPaths(scratch.Path);
        using var mine = LiveInstances.Join(paths, NullLogger.Instance);

        var client = new ScriptedUpdateClient { Candidate = null };
        var shutdowns = 0;
        var service = new UpdateService(client, mine, NullLogger.Instance, () => shutdowns++);

        await Assert.That(await service.RunOnceAsync(CancellationToken.None)).IsEqualTo(UpdateOutcome.NothingToDo);
        await Assert.That(shutdowns).IsEqualTo(0);
    }

    /// <summary>A failing feed is a log line and nothing else.</summary>
    /// <remarks>
    /// A 404 is what an unpublished channel returns as well as what a
    /// misconfigured URL returns, so nothing here treats one as an alarm. What
    /// matters is that BrowserAI keeps serving.
    /// </remarks>
    [Test]
    public async Task AFeedThatThrowsDoesNotTakeTheProcessWithIt()
    {
        using var scratch = ScratchDirectory.Create("update-failing");
        var paths = new LocalAppDataPaths(scratch.Path);
        using var mine = LiveInstances.Join(paths, NullLogger.Instance);

        var client = new ScriptedUpdateClient { CheckFailure = new HttpRequestException("404") };
        var shutdowns = 0;
        var service = new UpdateService(client, mine, NullLogger.Instance, () => shutdowns++);

        await Assert.That(await service.RunOnceAsync(CancellationToken.None)).IsEqualTo(UpdateOutcome.Failed);
        await Assert.That(shutdowns).IsEqualTo(0);
    }

    /// <summary>
    /// The stall timer is reset by progress, so a slow-but-moving download is
    /// not aborted.
    /// </summary>
    /// <remarks>
    /// <b>This is the assertion the three-timer design exists for.</b> A stall
    /// timer that is not reset by the thing it watches is an absolute timeout
    /// wearing a second name — and against a large package on a slow link that
    /// is the difference between an update that lands and one that never can.
    /// The double's <c>FullPackageSize</c> is 112.4 MB, which is what the
    /// 30-minute budget carries at ~500 kbit/s rather than a measured package
    /// size (the real one is <b>49,050,382 bytes</b>): the double is
    /// deliberately larger than life, because a stall timer has to hold for the
    /// worst package this design admits and not for today's.
    /// The double reports progress at intervals longer than a test can wait
    /// for the real 60 s budget, so what is asserted is the reset itself: a
    /// download that reports progress repeatedly completes, and the pass gets to
    /// the apply.
    /// </remarks>
    [Test]
    public async Task ProgressResetsTheStallTimerSoASlowButMovingDownloadSurvives()
    {
        using var scratch = ScratchDirectory.Create("update-stall");
        var paths = new LocalAppDataPaths(scratch.Path);
        using var mine = LiveInstances.Join(paths, NullLogger.Instance);

        var client = new ScriptedUpdateClient { ProgressSteps = 40, DelayPerStep = TimeSpan.FromMilliseconds(25) };
        var service = new UpdateService(client, mine, NullLogger.Instance, () => { });

        await Assert.That(await service.RunOnceAsync(CancellationToken.None)).IsEqualTo(UpdateOutcome.Applying);
        await Assert.That(client.ProgressReported).IsEqualTo(40);
    }

    /// <summary>
    /// The three budgets are ordered, and the tripwire is outside the other two
    /// combined.
    /// </summary>
    /// <remarks>
    /// Asserted rather than commented, because the outer deadline stops being a
    /// crash tripwire the moment it is small enough to fire on a slow link — at
    /// which point it is a second absolute timeout and the design has silently
    /// become the one-timer version it was written against.
    /// </remarks>
    [Test]
    public async Task TheOuterDeadlineIsATripwireRatherThanASecondBudget()
    {
        await Assert.That(UpdateService.StallBudget).IsLessThan(UpdateService.AbsoluteBudget);
        await Assert.That(UpdateService.CrashTripwire).IsGreaterThan(UpdateService.AbsoluteBudget + UpdateService.StallBudget);
    }

    /// <summary>
    /// Running uninstalled is a supported configuration and must not warn — and
    /// a genuine locator failure still must.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The record this demotes fired on every startup of a binary Velopack did
    /// not install</b> — <c>dotnet run</c>, every test host, and the configuration
    /// CI runs in — at <c>Warning</c>, on stderr, which is the stream this project
    /// relies on for diagnosis. A hundred of them per saturation run, every one
    /// of them saying that nothing was wrong.
    /// </para>
    /// <para>
    /// <b>What makes the demotion safe is that the two cases ARE distinguishable
    /// from outside Velopack, and the arms below are the proof.</b> The
    /// not-installed notice is one sentence emitted from one branch at
    /// <c>Warn</c>; upstream's other warnings and all of its errors carry
    /// different text and different levels. The <c>Error</c> arm matters most:
    /// upstream's message admits it cannot tell <i>not installed</i> from
    /// <i>packaged improperly</i>, and the record that DOES tell them apart —
    /// <i>"unable to locate a valid manifest file"</i> — is logged at
    /// <c>Error</c> and is untouched.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OnlyTheNotInstalledNoticeIsDemotedAndOnlyWhenNotInstalled()
    {
        const string Notice =
            "Failed to initialize WindowsVelopackLocator. This could be because the program is not installed or packaged properly.";

        // The one routine case: warning level, upstream's own sentence, and a
        // process that really is not an install.
        await Assert.That(VelopackStartup.IsRoutineNotInstalledNotice(VelopackLogLevel.Warning, Notice, installed: false)).IsTrue();

        // An INSTALLED process saying it cannot locate itself is a real problem,
        // whatever the text.
        await Assert.That(VelopackStartup.IsRoutineNotInstalledNotice(VelopackLogLevel.Warning, Notice, installed: true)).IsFalse();

        // Error is never demoted. This is the arm that keeps a genuinely broken
        // package loud: upstream logs "unable to locate a valid manifest file" at
        // Error from the branch that distinguishes broken from absent.
        await Assert.That(VelopackStartup.IsRoutineNotInstalledNotice(VelopackLogLevel.Error, Notice, installed: false)).IsFalse();

        await Assert.That(VelopackStartup.IsRoutineNotInstalledNotice(
            VelopackLogLevel.Error,
            @"Update.exe in parent dir, but unable to locate a valid manifest file at: C:\x\sq.version",
            installed: false)).IsFalse();

        // Upstream's other WARNINGS are not this one and stay at Warning.
        foreach (var other in new[]
        {
            "Update.exe in parent dir, Legacy app-* directory detected, sq.version not found. Using directory name for AppId and Version.",
            "Running in deeply nested directory. This is not an advised use-case.",
        })
        {
            await Assert.That(VelopackStartup.IsRoutineNotInstalledNotice(VelopackLogLevel.Warning, other, installed: false)).IsFalse();
        }

        // A null message must not be treated as the notice, and the matched
        // clause is upstream's leading sentence rather than the whole record --
        // so a reword of the second half changes nothing and a reword of the
        // first half sends it back to Warning, which is the safe direction.
        await Assert.That(VelopackStartup.IsRoutineNotInstalledNotice(VelopackLogLevel.Warning, null, installed: false)).IsFalse();
        await Assert.That(Notice).StartsWith(VelopackStartup.NotInstalledNotice);
    }

    /// <summary>A process going down abandons the pass without reporting a failure.</summary>
    [Test]
    public async Task ShutdownAbandonsThePassQuietly()
    {
        using var scratch = ScratchDirectory.Create("update-shutdown");
        var paths = new LocalAppDataPaths(scratch.Path);
        using var mine = LiveInstances.Join(paths, NullLogger.Instance);

        using var stopping = new CancellationTokenSource();
        var client = new ScriptedUpdateClient { ProgressSteps = 200, DelayPerStep = TimeSpan.FromMilliseconds(20) };
        var service = new UpdateService(client, mine, NullLogger.Instance, () => { });

        var pass = service.RunOnceAsync(stopping.Token);

        // ⚠️ Cancelled on the EVENT that the download really started, not after a
        // guess at how long starting takes.
        //
        // Corrected 2026-08-18 (previously `await Task.Delay(80)`). Eighty
        // milliseconds against a scripted pass of 200 × 20 ms assumed the pass
        // would still be running when the cancel landed; at unbounded suite
        // parallelism that delay can overshoot the whole four seconds, the pass
        // then COMPLETES, and this test fails claiming the product applied an
        // update during shutdown. The first reported progress step is the
        // observable that says the download is under way, and it cannot be
        // reached late relative to itself.
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (client.ProgressReported is 0)
        {
            if (waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new TimeoutException("The scripted download never reported a step, so there was no pass in flight to cancel.");
            }

            await Task.Delay(5);
        }

        await stopping.CancelAsync();

        await Assert.That(await pass).IsEqualTo(UpdateOutcome.NothingToDo);
        await Assert.That(client.Applies).IsEqualTo(0);
    }

    // ---- The single lines, each of which survived its own deletion green -----

    /// <summary>
    /// <c>SetAutoApplyOnStartup(false)</c> is there, and the call carrying it is
    /// still the first thing <c>Main</c> does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The code calls this "the single most important line in this file" and
    /// nothing asserted it.</b> Its default is <see langword="true"/>: an
    /// installed BrowserAI would <c>exit(0)</c> at handshake time and relaunch
    /// detached with dead pipes, which presents to the client as a server that
    /// started and vanished — the exact failure shape this project exists to
    /// remove.
    /// </para>
    /// <para>
    /// <b>Order is asserted as well as presence, because the hazard is a
    /// reorder.</b> The same call serves the installer's own fast-exit hooks, so
    /// anything placed above it runs inside every hook too — and a line moved
    /// above it breaks nothing that any other test looks at. The four names
    /// checked below are the startup steps that must come after it, each read
    /// from <c>Program.cs</c> by position.
    /// </para>
    /// <para>
    /// A source scan rather than a behavioural one, for the reason this whole
    /// file states: under a test host this process is not an install, and
    /// <c>VelopackApp.Build().Run()</c> cannot be driven twice in one process.
    /// It is the same mechanism <c>MachineMutex</c> and the never-by-image-name
    /// rule are guarded with.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AutoApplyIsOffAndTheVelopackCallIsStillTheFirstThingMainDoes()
    {
        var startup = await RepositoryLayout.ReadCodeAsync(ProductFile("Updates", "VelopackStartup.cs"));

        await Assert.That(startup).Contains(".SetAutoApplyOnStartup(false)");

        var program = await RepositoryLayout.ReadCodeAsync(ProductFile("Program.cs"));
        var velopack = program.IndexOf("VelopackStartup.Run(", StringComparison.Ordinal);

        await Assert.That(velopack).IsGreaterThan(-1);

        var late = new List<string>();

        foreach (var after in new[] { "InstallLocation.RootAppDir", "new LocalAppDataPaths(", "ProcessLog.Create(", "Environment.GetEnvironmentVariable(" })
        {
            var at = program.IndexOf(after, StringComparison.Ordinal);

            if (at >= 0 && at < velopack)
            {
                late.Add($"'{after}' now runs BEFORE VelopackStartup.Run, which also serves the installer's fast-exit hooks");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, late)).IsEmpty();
    }

    /// <summary>
    /// Both halves of the rollback mechanism, asserted together because §G's
    /// requirement is that they agree.
    /// </summary>
    /// <remarks>
    /// <b>Either half alone is a defect, and the one-sided version is a shipping
    /// product.</b> <c>AllowVersionDowngrade</c> defaults to
    /// <see langword="false"/>, which reports an available rollback as *"no
    /// updates"* — silently. The pipeline half is the release-validation rule
    /// reading *monotonic <b>or</b> an explicit rollback republish*; with the
    /// client half on and the pipeline half missing, the runtime accepts a
    /// rollback the build refuses to emit, which is the state a shipping
    /// product examined for this project is in. Only the pipeline half was
    /// driven before 2026-08-17.
    /// </remarks>
    [Test]
    public async Task BothHalvesOfTheRollbackMechanismAreThereAndTheyAgree()
    {
        var client = await RepositoryLayout.ReadCodeAsync(ProductFile("Updates", "VelopackUpdateClient.cs"));

        await Assert.That(client).Contains("AllowVersionDowngrade = true");

        var validation = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "build", "Test-ReleaseVersion.ps1"));

        // The pipeline half, in the two shapes it has to have: a rollback is
        // refused by default and permitted as a stated intent.
        await Assert.That(validation).Contains("RollbackRepublish");
        await Assert.That(validation).Contains("'rollback'");
        await Assert.That(validation).Contains("'monotonic'");
    }

    /// <summary>
    /// The channel reaches Velopack through <c>UpdateOptions.ExplicitChannel</c>
    /// and through nothing else.
    /// </summary>
    /// <remarks>
    /// <b>§G calls this its worst hazard because it is unrecoverable in the
    /// field</b> — a client that cannot reach the feed cannot be told to roll
    /// back either, and the only fix is a manual reinstall of every machine. The
    /// three feed-URL shapes are asserted above; <b>the assignment that consumes
    /// them was asserted by nothing</b>, so a deletion would leave every URL
    /// test green while Velopack composed the default channel's manifest name.
    /// </remarks>
    [Test]
    public async Task TheChannelReachesVelopackThroughExplicitChannelAndNothingElse()
    {
        var client = await RepositoryLayout.ReadCodeAsync(ProductFile("Updates", "VelopackUpdateClient.cs"));

        await Assert.That(client).Contains("ExplicitChannel = feed.Channel");

        // And assigned nowhere else in the product: a second assignment is a
        // second place the channel can be got wrong.
        //
        // ⚠️ The needle is the ASSIGNMENT rather than the name, and the first
        // draft of this test proved why: `UpdateFeed`'s refusal messages name
        // `UpdateOptions.ExplicitChannel` in the sentence that tells a caller
        // where the channel belongs, so a scan for the bare name fails on the
        // documentation of the very rule it is enforcing — which trains the next
        // person to delete the explanation to make a test pass.
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            if (code.Contains("ExplicitChannel =", StringComparison.Ordinal)
                && !file.Name.Equals("VelopackUpdateClient.cs", StringComparison.Ordinal))
            {
                offenders.Add($"{file.Name} assigns ExplicitChannel; VelopackUpdateClient is the one place that may");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    /// <summary>
    /// Nothing the product ships resolves a path from
    /// <c>AppContext.BaseDirectory</c>, except the one type for which it is
    /// correct.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It reads as "next to the binary" and resolves inside
    /// <c>current\</c></b>, which an update replaces wholesale — so a log, a
    /// cache or a browser tree placed there is deleted by the event most likely
    /// to have produced the line somebody came to read. A shipped product
    /// examined for this project does exactly this and carries a 10-day log
    /// retention policy that can therefore never once have applied.
    /// </para>
    /// <para>
    /// <b><c>PayloadLayout</c> is the sanctioned exception and is named
    /// rather than excluded silently.</b> The payload is the one thing that
    /// <i>should</i> be replaced wholesale by an update: it is the vendored copy
    /// of upstream the running build was tested against, and a payload surviving
    /// an update would mean the new binary driving the old upstream.
    /// </para>
    /// </remarks>
    [Test]
    public async Task NoProductPathIsResolvedFromAppContextBaseDirectory()
    {
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            if (!code.Contains("AppContext.BaseDirectory", StringComparison.Ordinal))
            {
                continue;
            }

            if (!file.Name.Equals("PayloadLayout.cs", StringComparison.Ordinal))
            {
                offenders.Add(
                    $"{file.Name} resolves a path from AppContext.BaseDirectory, which is inside current\\ and is replaced by every update. PayloadLayout is the one type for which that is correct.");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    /// <summary>
    /// The release script archives the full <c>.nupkg</c>, and refuses when
    /// there is none to archive.
    /// </summary>
    /// <remarks>
    /// <b>Velopack prunes <c>packages\</c> to the current full package and deltas
    /// are forward-only</b> — watched happening at step 19: after 0.9.0 → 0.9.1,
    /// the 0.9.0 package it had been reconstructed from was gone. So an
    /// unarchived release has no rollback target at all, and the symptom arrives
    /// only when somebody needs to roll back. The refusal is asserted with the
    /// copy, because an archive step that silently skips a missing package is the
    /// same defect wearing a green tick.
    /// </remarks>
    [Test]
    public async Task TheReleaseScriptArchivesTheFullPackageAndRefusesWhenThereIsNone()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "build", "New-Release.ps1"));

        await Assert.That(script).Contains("Copy-Item -LiteralPath $full -Destination $ArchiveDir -Force");
        await Assert.That(script).Contains("there is nothing to archive and no rollback target for this release");
    }

    private static FileInfo ProductFile(params string[] segments) =>
        new(Path.Combine([RepositoryLayout.Root.FullName, "src", "BrowserAI", .. segments]));

    /// <summary>
    /// A scripted <see cref="IUpdateClient"/>: everything the service asks for,
    /// and nothing that touches Velopack.
    /// </summary>
    private sealed class ScriptedUpdateClient : IUpdateClient
    {
        public string ManifestUrl => "file:///scripted/releases.win.json";

        public UpdateCandidate? Candidate { get; init; } = new()
        {
            Version = "0.9.1",
            IsDowngrade = false,
            DeltaCount = 1,
            FullPackageSize = 112_400_000,
        };

        public Exception? CheckFailure { get; init; }

        public int ProgressSteps { get; init; } = 10;

        public TimeSpan DelayPerStep { get; init; } = TimeSpan.Zero;

        public int Downloads { get; private set; }

        public int Applies { get; private set; }

        public int ProgressReported { get; private set; }

        public Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken) =>
            CheckFailure is not null ? Task.FromException<UpdateCandidate?>(CheckFailure) : Task.FromResult(Candidate);

        public async Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
        {
            Downloads++;

            for (var step = 1; step <= ProgressSteps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (DelayPerStep > TimeSpan.Zero)
                {
                    await Task.Delay(DelayPerStep, cancellationToken).ConfigureAwait(false);
                }

                ProgressReported++;
                progress(step * 100 / ProgressSteps);
            }
        }

        public void ApplyAfterThisProcessExits(UpdateCandidate candidate) => Applies++;
    }
}
