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
