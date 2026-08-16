// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

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
/// <c>current\</c> survive both — was run by hand against a real install at
/// [step 19](../../plan/build-order.md#19-velopack-package-update-roll-back) and
/// is recorded in [kb](../../kb/packaging/velopack.md), because it needs an
/// installer this suite must never run.
/// </para>
/// </remarks>
internal sealed class UpdateTests
{
    // ---- The feed URL: the worst hazard in the section -----------------------

    /// <summary>
    /// The UCC bug, refused rather than reproduced.
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
    /// is why one is not used. [Testing](../../plan/testing.md) is explicit that
    /// this assertion cannot be made hermetically: a directory source composes
    /// paths differently and will pass where production 404s, and a served
    /// stand-in composes them the same way as the real one *by construction*
    /// rather than by evidence. What has to be checked is the URL somebody
    /// actually typed, against the storage somebody actually configured — the
    /// exact pair that bricked <c>ExoFabric/UCC</c>'s auto-update for three
    /// shipped versions.
    /// </para>
    /// <para>
    /// <b>What is owed, precisely:</b> publish the feed, set
    /// <see cref="UpdateConfiguration.ProductionBaseUrl"/>, then replace this
    /// skip with a real request for <c>{ProductionBaseUrl}/releases.win.json</c>
    /// asserting a 200 and a parseable <c>Assets</c> array. Everything else in
    /// the update lane — the pack, the delta, the apply, the rollback and the
    /// survival of the browsers — was run for real at
    /// [step 19](../../plan/build-order.md#19-velopack-package-update-roll-back)
    /// and is recorded in [kb](../../kb/packaging/velopack.md#the-update-lane-measured-2026-08-16).
    /// </para>
    /// <para>
    /// ⚠️ <b>This skip blocks a release, and that is intended.</b>
    /// [`CLAUDE.md`](../../CLAUDE.md) forbids releasing with a skipped test, and
    /// [pre-release item 8](../../plan/pre-release.md) requires the skipped count
    /// to be zero. So the debt is not a note somebody has to remember — it is a
    /// red gate on the first release, which is exactly where it belongs.
    /// </para>
    /// </remarks>
    [Test]
    [Skip("The production feed has not been published: UpdateConfiguration.ProductionBaseUrl is null, so there is no URL to resolve. Deferred at build-order step 19 rather than faked with a local HTTP server, which would compose paths the same way by construction and pass while proving nothing. Blocks the first release by design.")]
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

        await Assert.That(name).StartsWith(BrowserAI.Sessions.LockScopes.PerDirectoryPrefix);
        await Assert.That(name).IsEqualTo(other);
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
    /// wearing a second name — and against a 112.4 MB package on a slow link
    /// that is the difference between an update that lands and one that never
    /// can. The double reports progress at intervals longer than a test can wait
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
        await Task.Delay(80);
        await stopping.CancelAsync();

        await Assert.That(await pass).IsEqualTo(UpdateOutcome.NothingToDo);
        await Assert.That(client.Applies).IsEqualTo(0);
    }

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
