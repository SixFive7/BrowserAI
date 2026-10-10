// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Proxy;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The background's update core: the ten-minute check and its record, the pack-id
/// check, what holds a downloaded update, the two-phase question to the relays, the
/// person's install-now, and what the toast and the dashboard read.
/// </summary>
/// <remarks>
/// <para>
/// <b>In process, with every seam a double and the clock a
/// <see cref="ManualClock"/></b> (<see cref="BackgroundUpdateRig"/>). Nothing is
/// started, nothing touches Velopack, and no assertion waits on real time: a
/// countdown is asserted by moving the clock to one tick short of it and then by
/// that tick.
/// </para>
/// <para>
/// <b>The decisions these hold</b>, all of 2026-10-08: D9 (only the background
/// checks, at most every ten minutes, across restarts), H2 and RESOLUTIONS 17 (the
/// source is an argument; a pre-release build checks a folder and never a URL), H1
/// as adjusted and RESOLUTIONS 13 (what holds an update, and the two-phase
/// agreement), and T (the toasts).
/// </para>
/// </remarks>
internal sealed class BackgroundUpdatesTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(ManualClock.OneTick);

    // ---- D9: the check, its interval and its record ---------------------------

    /// <summary>
    /// The record of the last check survives a restart, and the restarted background
    /// does not check again until the interval since that check has run out.
    /// </summary>
    /// <remarks>
    /// <b>The record is the real file</b>, in a scratch data root, so what is held
    /// here is the round trip a crash and a restart make through the disk and not a
    /// value carried in memory between two objects.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRecordOfTheLastCheckSurvivesARestartAndNoSecondCheckRunsInsideTheInterval()
    {
        using var scratch = ScratchDirectory.Create("update-stamp-restart");
        using var rig = new BackgroundUpdateRig { Stamp = UpdateCheckStampFile.In(scratch.Path) };

        // No record yet: the first run checks at once, and says why.
        var first = rig.Start();

        await Assert.That(rig.Client.Checks).IsEqualTo(1);
        await Assert.That(rig.Logged(34)).IsTrue().Because("event 34 says no earlier check is recorded");
        await Assert.That(File.Exists(Path.Combine(scratch.Path, UpdateCheckStampFile.FileName))).IsTrue();

        // Three minutes on, the background goes down and a new one starts.
        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        first.Dispose();
        _ = rig.Start();

        await Assert.That(rig.Client.Checks)
            .IsEqualTo(1)
            .Because("the record says the last check began three minutes ago, inside the interval");
        await Assert.That(rig.Logged(38)).IsTrue().Because("event 38 names the last check and when the next one runs");

        // One tick short of the interval since the first check: still one.
        rig.Clock.Advance(BackgroundUpdates.CheckInterval - TimeSpan.FromMinutes(3) - OneTick);

        await Assert.That(rig.Client.Checks).IsEqualTo(1);

        // And at it, exactly one more.
        rig.Clock.Advance(OneTick);

        await Assert.That(rig.Client.Checks).IsEqualTo(2);
    }

    /// <summary>
    /// A record that cannot be used, or one older than the interval, or one later
    /// than now, starts a check at once, and the log says which of the three it was.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACheckRunsAtOnceWhenTheRecordIsUnreadableOldOrAheadAndTheLogSaysWhich()
    {
        using var scratch = ScratchDirectory.Create("update-stamp-cases");
        var record = Path.Combine(scratch.Path, UpdateCheckStampFile.FileName);

        // Unreadable.
        await File.WriteAllTextAsync(record, "not a time\n");

        using (var rig = new BackgroundUpdateRig { Stamp = UpdateCheckStampFile.In(scratch.Path) })
        {
            _ = rig.Start();

            await Assert.That(rig.Client.Checks).IsEqualTo(1);
            await Assert.That(rig.Logged(35)).IsTrue().Because("event 35 says the record could not be read");
            await Assert.That(rig.Log.Logged("not a time")).IsTrue().Because("the line quotes what the record held");
        }

        // Older than the interval.
        using (var rig = new BackgroundUpdateRig { Stamp = UpdateCheckStampFile.In(scratch.Path) })
        {
            var old = rig.Clock.GetUtcNow() - BackgroundUpdates.CheckInterval - TimeSpan.FromMinutes(1);
            await File.WriteAllTextAsync(record, old.ToString("O", CultureInfo.InvariantCulture));

            _ = rig.Start();

            await Assert.That(rig.Client.Checks).IsEqualTo(1);
            await Assert.That(rig.Logged(37)).IsTrue().Because("event 37 says the last check is older than the interval");
        }

        // Later than now, as after the clock was set back.
        using (var rig = new BackgroundUpdateRig { Stamp = UpdateCheckStampFile.In(scratch.Path) })
        {
            var ahead = rig.Clock.GetUtcNow() + TimeSpan.FromHours(1);
            await File.WriteAllTextAsync(record, ahead.ToString("O", CultureInfo.InvariantCulture));

            _ = rig.Start();

            await Assert.That(rig.Client.Checks).IsEqualTo(1);
            await Assert.That(rig.Logged(36)).IsTrue().Because("event 36 says the record names a time later than now");
        }

        // And the positive control: a record inside the interval does hold the check back.
        using (var rig = new BackgroundUpdateRig { Stamp = UpdateCheckStampFile.In(scratch.Path) })
        {
            var recent = rig.Clock.GetUtcNow() - TimeSpan.FromMinutes(1);
            await File.WriteAllTextAsync(record, recent.ToString("O", CultureInfo.InvariantCulture));

            _ = rig.Start();

            await Assert.That(rig.Client.Checks).IsEqualTo(0);
        }
    }

    /// <summary>
    /// A pre-release build checks a folder source and never a URL, and a release
    /// build with the same URL does check, so the refusal is the pre-release rule.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APreReleaseBuildChecksAFolderAndNeverAUrl()
    {
        const string PreRelease = "1.2.0-alpha.0.3";
        var url = UpdateSource.Read([UpdateSource.Argument, "https://example.invalid/browserai"]);

        using (var rig = new BackgroundUpdateRig { Install = new UpdateInstall(true, BackgroundUpdateRig.PackId, PreRelease) })
        {
            _ = rig.Start();

            await Assert.That(rig.Client.Checks).IsEqualTo(1).Because("a pre-release build checks a folder source (H2)");
        }

        using (var rig = new BackgroundUpdateRig { Install = new UpdateInstall(true, BackgroundUpdateRig.PackId, PreRelease), Source = url })
        {
            _ = rig.Start();
            rig.Clock.Advance(BackgroundUpdates.CheckInterval * 3);

            await Assert.That(rig.Client.Checks).IsEqualTo(0).Because("a pre-release build never checks a URL");
            await Assert.That(rig.ClientsBuilt).IsEqualTo(0);
            await Assert.That(rig.Logged(33)).IsTrue().Because("event 33 says why it never checks");
        }

        using (var rig = new BackgroundUpdateRig { Source = url })
        {
            _ = rig.Start();

            await Assert.That(rig.Client.Checks).IsEqualTo(1).Because("a release build checks the same URL");
        }
    }

    /// <summary>A build that is not installed never checks, never holds and builds no client.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABuildThatIsNotInstalledNeverChecks()
    {
        using var rig = new BackgroundUpdateRig { Install = new UpdateInstall(false, null, "1.1.0") };
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");
        rig.Client.StagedCandidate = BackgroundUpdateRig.Candidate("1.2.0");

        var updates = rig.Start();
        rig.Clock.Advance(BackgroundUpdates.CheckInterval * 3);

        await Assert.That(rig.Client.Checks).IsEqualTo(0);
        await Assert.That(rig.ClientsBuilt).IsEqualTo(0);
        await Assert.That(rig.EventsStartingWith("toast")).IsEmpty();
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.None);
        await Assert.That(rig.Logged(31)).IsTrue().Because("event 31 says this build is not installed");
    }

    // ---- The pack-id check ----------------------------------------------------

    /// <summary>
    /// A package published under another pack id is never downloaded and never held,
    /// whether the source offers it or the packages directory holds it.
    /// </summary>
    /// <remarks>
    /// Velopack 1.2.161 picks the newest full package without looking at its pack
    /// id, and BrowserAI allows downgrades, so a folder holding the suite's test pack
    /// beside the shipping one would otherwise offer it. A candidate that names no
    /// pack at all is refused with them, and the positive control is the same offer
    /// under the installed pack id, which is downloaded.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APackageOfAnotherPackIsNeverDownloadedOrHeld()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(BusyRelay(rig, "a"));
        rig.Client.Offer = BackgroundUpdateRig.Candidate("9.9.9", BackgroundUpdateRig.OtherPackId);
        rig.Client.StagedCandidate = BackgroundUpdateRig.Candidate("9.9.8", BackgroundUpdateRig.OtherPackId);

        var updates = rig.Start();

        await Assert.That(rig.Client.Checks).IsEqualTo(1);
        await Assert.That(rig.Client.Downloads).IsEqualTo(0);
        await Assert.That(rig.EventsStartingWith("toast ready")).IsEmpty();
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.None);
        await Assert.That(rig.Logged(43)).IsTrue().Because("event 43 names both pack ids");

        // A candidate that names no pack is refused too.
        rig.Client.Offer = BackgroundUpdateRig.Candidate("9.9.9", packId: null);
        rig.Clock.Advance(BackgroundUpdates.CheckInterval);

        await Assert.That(rig.Client.Checks).IsEqualTo(2);
        await Assert.That(rig.Client.Downloads).IsEqualTo(0);

        // The positive control: the same version under the installed pack is downloaded and held.
        rig.Client.Offer = BackgroundUpdateRig.Candidate("9.9.9");
        rig.Clock.Advance(BackgroundUpdates.CheckInterval);

        await Assert.That(rig.Client.Downloads).IsEqualTo(1);
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.Held);
    }

    // ---- A pass that does not go to plan --------------------------------------------
    //
    // Ported 2026-10-08 from the arms of UpdateTests that drove UpdateService, a server's
    // own update lane, deleted with it in dcded85b: ACheckThatNeverAnswersEndsOnItsOwnBudgetAndSaysSo,
    // AFeedThatThrowsDoesNotTakeTheProcessWithIt, NothingOnOfferIsAQuietPass,
    // ProgressResetsTheStallTimerSoASlowButMovingDownloadSurvives and
    // ShutdownAbandonsThePassQuietly. The behaviours moved into the background's update
    // core with the budgets, and these hold them there.

    /// <summary>
    /// A check that never answers is given up at the check budget, the log says so,
    /// and the next check runs as soon as it is due.
    /// </summary>
    /// <remarks>
    /// <b>Velopack's own check takes no token</b>, so the core bounds it by waiting and
    /// not by cancelling (<see cref="UpdateBudgets.CheckBudget"/>). The budget is longer
    /// than <see cref="BackgroundUpdates.CheckInterval"/>, so the next check is already
    /// due when it runs out. <b>Planted red 2026-10-08</b> with the check awaited through
    /// the shutdown token alone: one tick past the budget nothing was logged and no
    /// second check ran.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACheckThatNeverAnswersEndsAtItsBudgetAndTheNextCheckRuns()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Client.CheckHangs = true;

        _ = rig.Start();

        await Assert.That(rig.Client.Checks).IsEqualTo(1);

        rig.Clock.Advance(UpdateBudgets.CheckBudget - OneTick);

        await Assert.That(rig.Logged(65)).IsFalse().Because("the check still has a tick of its budget left");
        await Assert.That(rig.Client.Checks).IsEqualTo(1);

        rig.Client.CheckHangs = false;
        rig.Clock.Advance(OneTick);
        rig.Settle();

        await Assert.That(rig.Logged(65)).IsTrue().Because("event 65 says the check outlived its budget");
        await Assert.That(rig.Logged(64)).IsFalse().Because("a check that ran out of time is not a pass that failed");
        await Assert.That(rig.Client.Checks).IsEqualTo(2).Because("the next check was due ten minutes after the first began");
    }

    /// <summary>
    /// A version the feed offers below the installed one is read as older, so the update
    /// page and the ready toast can say so; a newer one is not.
    /// </summary>
    /// <remarks>
    /// <b>Q308 a, the maintainer's words of 2026-10-03 verbatim: <i>"Q308 a"</i></b>:
    /// automatic rollback stays, and the interface says when the offered version is
    /// older. The update core knew it and only logged it until 2026-10-10.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARollbackOnOfferIsReadAsOlder()
    {
        using var rollback = new BackgroundUpdateRig();

        // A hidden session holds each update, so it stays held and is read as such.
        rollback.Sessions.Open(new ListedSession(@"C:\work\hidden", "reads the docs", Visible: false, rollback.Clock.GetUtcNow() + TimeSpan.FromMinutes(7)));

        var back = rollback.Build();

        rollback.Client.Offer = BackgroundUpdateRig.Candidate("1.0.0") with { IsDowngrade = true };
        back.Start();
        rollback.Settle();

        var held = back.Read();

        await Assert.That(held.State).IsEqualTo(UpdateHoldState.Held);
        await Assert.That(held.Version).IsEqualTo("1.0.0");
        await Assert.That(held.Older).IsTrue();

        using var forward = new BackgroundUpdateRig();

        forward.Sessions.Open(new ListedSession(@"C:\work\hidden", "reads the docs", Visible: false, forward.Clock.GetUtcNow() + TimeSpan.FromMinutes(7)));

        var ahead = forward.Build();

        forward.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");
        ahead.Start();
        forward.Settle();

        await Assert.That(ahead.Read().State).IsEqualTo(UpdateHoldState.Held);
        await Assert.That(ahead.Read().Older).IsFalse();
    }

    /// <summary>
    /// A feed that throws costs that pass and nothing else: the log says so, the next
    /// check runs at its time, and a check with nothing on offer is quiet.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-08</b> with the next check armed only when a pass
    /// returns, as a <c>try</c> without its <c>finally</c> would: at the interval no
    /// second check ran.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFeedThatThrowsCostsThatPassAndTheNextCheckRunsAtItsTime()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(BusyRelay(rig, "a"));
        rig.Client.CheckFailure = new HttpRequestException("the feed is down");

        var updates = rig.Start();

        await Assert.That(rig.Client.Checks).IsEqualTo(1);
        await Assert.That(rig.Logged(64)).IsTrue().Because("event 64 says the pass failed and when the next one runs");

        rig.Client.CheckFailure = null;
        rig.Clock.Advance(BackgroundUpdates.CheckInterval - OneTick);

        await Assert.That(rig.Client.Checks).IsEqualTo(1);

        rig.Clock.Advance(OneTick);

        await Assert.That(rig.Client.Checks).IsEqualTo(2);

        // Nothing was on offer: no download, no toast, nothing held.
        await Assert.That(rig.Logged(41)).IsTrue().Because("event 41 says nothing is available");
        await Assert.That(rig.Client.Downloads).IsEqualTo(0);
        await Assert.That(rig.EventsStartingWith("toast")).IsEmpty();
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.None);
    }

    /// <summary>
    /// A download that keeps reporting progress is never stopped by the stall budget,
    /// however long it takes, and one that stops reporting is abandoned at it.
    /// </summary>
    /// <remarks>
    /// <b>The reset is the mechanism</b>: a stall bound the download does not push back
    /// is an absolute bound under a second name. <b>Planted red 2026-10-08</b> with the
    /// reset taken out: the download that moved every 59 s was abandoned at the first
    /// minute.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADownloadThatKeepsMovingOutlivesTheStallBudgetAndOneThatStopsIsAbandonedAtIt()
    {
        using (var rig = new BackgroundUpdateRig())
        {
            rig.Relays.Connect(BusyRelay(rig, "a"));
            rig.Client.Offer = BackgroundUpdateRig.Candidate("9.9.9");
            rig.Client.DownloadWaits = true;

            var updates = rig.Start();

            await Assert.That(rig.Client.Downloads).IsEqualTo(1);

            // Five reports, each a tick inside the stall budget of the one before.
            for (var step = 1; step <= 5; step++)
            {
                rig.Clock.Advance(UpdateBudgets.StallBudget - OneTick);
                rig.Client.ReportProgress(step * 10);
            }

            rig.Client.FinishDownload();
            rig.Settle();

            await Assert.That(rig.Logged(48)).IsFalse().Because("a download that moves is never a stall, " + string.Join(" | ", rig.Events));
            await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.Held);
        }

        using (var rig = new BackgroundUpdateRig())
        {
            rig.Relays.Connect(BusyRelay(rig, "a"));
            rig.Client.Offer = BackgroundUpdateRig.Candidate("9.9.9");
            rig.Client.DownloadWaits = true;

            var updates = rig.Start();

            rig.Client.ReportProgress(10);
            rig.Clock.Advance(UpdateBudgets.StallBudget - OneTick);

            await Assert.That(rig.Logged(48)).IsFalse();

            rig.Clock.Advance(OneTick);
            rig.Settle();

            await Assert.That(rig.Logged(48)).IsTrue().Because("event 48 says the download was abandoned and why");
            await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.None);
        }
    }

    /// <summary>
    /// A background that stops during a check abandons the pass quietly: no failure in
    /// the log, and no check after the stop.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-08</b> with the shutdown's cancellation no longer told
    /// apart from a failure: the stop was logged as a pass that failed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStopDuringACheckAbandonsThePassQuietly()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Client.CheckHangs = true;

        var updates = rig.Start();

        await Assert.That(rig.Client.Checks).IsEqualTo(1);

        updates.Dispose();
        rig.Settle();

        await Assert.That(rig.Logged(64)).IsFalse().Because("a stop is not a pass that failed");
        await Assert.That(rig.Logged(65)).IsFalse();

        rig.Clock.Advance(UpdateBudgets.CheckBudget + BackgroundUpdates.CheckInterval);

        await Assert.That(rig.Client.Checks).IsEqualTo(1).Because("nothing checks after the stop");
    }

    // ---- What holds ---------------------------------------------------------------

    /// <summary>
    /// The ready toast is raised once per version: holding the same package again,
    /// as after a later download failed, raises nothing, and a new version does.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReadyToastIsRaisedOncePerVersion()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(BusyRelay(rig, "a"));
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

        _ = rig.Start();

        await Assert.That(Joined(rig.EventsStartingWith("toast ready"))).IsEqualTo("toast ready 1.2.0");

        // 1.2.1 is offered and its download fails: what is on disk is still 1.2.0,
        // and holding it again raises nothing.
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.1");
        rig.Client.DownloadFailure = new IOException("the scripted download failed");
        rig.Clock.Advance(BackgroundUpdates.CheckInterval);

        await Assert.That(rig.Client.Downloads).IsEqualTo(2);
        await Assert.That(Joined(rig.EventsStartingWith("toast ready"))).IsEqualTo("toast ready 1.2.0");

        // The next check downloads it, and 1.2.1 gets its own toast.
        rig.Client.DownloadFailure = null;
        rig.Clock.Advance(BackgroundUpdates.CheckInterval);

        await Assert.That(Joined(rig.EventsStartingWith("toast ready"))).IsEqualTo("toast ready 1.2.0|toast ready 1.2.1");
        await Assert.That(rig.EventsStartingWith("ask")).IsEmpty().Because("the busy relay held the update the whole time");
    }

    /// <summary>
    /// A session holds the update until its countdown runs out, and one an agent set
    /// never to close holds it for good.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionHoldsTheUpdateUntilItsCountdownRunsOut()
    {
        using (var rig = new BackgroundUpdateRig())
        {
            var closesAt = rig.Clock.GetUtcNow() + TimeSpan.FromMinutes(4);
            rig.Sessions.Open(new ListedSession(@"C:\work\hidden", "reads the docs", Visible: false, closesAt));
            rig.Relays.Connect(QuietRelay(rig, "a"));
            rig.Relays.Answer = _ => Yes(rig);
            rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

            _ = rig.Start();

            await Assert.That(rig.EventsStartingWith("ask")).IsEmpty().Because("the session's countdown runs");

            rig.Clock.Advance(closesAt - rig.Clock.GetUtcNow() - OneTick);

            await Assert.That(rig.EventsStartingWith("ask")).IsEmpty().Because("one tick short of the session's countdown");

            rig.Clock.Advance(OneTick);

            await Assert.That(Joined(rig.EventsStartingWith("ask"))).IsEqualTo("ask a 1.2.0");
            await Assert.That(rig.IndexOf("apply 1.2.0 with [--after-update 1.2.0]")).IsGreaterThanOrEqualTo(0);
        }

        using (var rig = new BackgroundUpdateRig())
        {
            rig.Sessions.Open(new ListedSession(@"C:\work\kept", null, Visible: true, ClosesAt: null));
            rig.Relays.Answer = _ => Yes(rig);
            rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

            var updates = rig.Start();
            rig.Clock.Advance(BackgroundUpdates.CheckInterval * 6);

            await Assert.That(rig.EventsStartingWith("apply")).IsEmpty().Because("a session set never to close holds the update for good");
            await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.Held);
        }
    }

    /// <summary>
    /// A relay whose countdown has run out does not hold the update; one with a call
    /// in flight does, until the background says the call ended.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AQuietRelayDoesNotHoldTheUpdateAndOneWithACallInFlightDoes()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(QuietRelay(rig, "quiet"));
        rig.Relays.Connect(QuietRelay(rig, "busy") with { CallInFlight = true });
        rig.Relays.Answer = _ => Yes(rig);
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

        var updates = rig.Start();
        rig.Clock.Advance(BackgroundUpdates.CheckInterval * 2);

        await Assert.That(rig.EventsStartingWith("ask")).IsEmpty().Because("a call is in flight on the busy relay");
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.Held);

        // The call ends and the background says so: the quiet relay holds nothing,
        // so both are asked and the update installs.
        rig.Relays.Connect(QuietRelay(rig, "busy"));
        updates.Changed();
        rig.Settle();

        await Assert.That(rig.EventsStartingWith("ask").Count).IsEqualTo(2);
        await Assert.That(rig.IndexOf("apply 1.2.0 with [--after-update 1.2.0]")).IsGreaterThanOrEqualTo(0);
    }

    // ---- The two-phase agreement ------------------------------------------------

    /// <summary>
    /// When every relay says yes, the installing toast goes up, every relay ends, the
    /// package goes to <c>Update.exe</c> with <c>--after-update</c> and its version,
    /// and the background is asked to exit, in that order.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhenEveryRelaySaysYesTheUpdateInstallsWithTheRestartArguments()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(QuietRelay(rig, "a"));
        rig.Relays.Connect(QuietRelay(rig, "b"));
        rig.Relays.Answer = _ => Yes(rig);
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

        var updates = rig.Start();

        var ready = rig.IndexOf("toast ready 1.2.0");
        var askA = rig.IndexOf("ask a 1.2.0");
        var askB = rig.IndexOf("ask b 1.2.0");
        var installing = rig.IndexOf("toast installing 1.2.0");
        var ended = rig.IndexOf("end relays 1.2.0");
        var applied = rig.IndexOf("apply 1.2.0 with [--after-update 1.2.0]");
        var exited = rig.IndexOf("exit");

        await Assert.That(applied).IsGreaterThanOrEqualTo(0).Because(string.Join(" | ", rig.Events));
        await Assert.That(ready).IsGreaterThanOrEqualTo(0);
        await Assert.That(askA).IsGreaterThan(ready);
        await Assert.That(askB).IsGreaterThan(ready);
        await Assert.That(installing).IsGreaterThan(Math.Max(askA, askB));
        await Assert.That(ended).IsGreaterThan(installing);
        await Assert.That(applied).IsGreaterThan(ended);
        await Assert.That(exited).IsGreaterThan(applied);

        await Assert.That(rig.EventsStartingWith("call off")).IsEmpty();
        await Assert.That(rig.EventsStartingWith("end relays now")).IsEmpty();
        await Assert.That(rig.ExitRequests).IsEqualTo(1);
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.Installing);
    }

    /// <summary>
    /// One relay's no calls the update off for every relay and nothing ends; nobody is
    /// asked again before the countdown that no carried runs out, and then everyone is.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OneNoCallsTheUpdateOffForEveryRelayAndNothingEnds()
    {
        using var rig = new BackgroundUpdateRig();
        var busyUntil = rig.Clock.GetUtcNow() + TimeSpan.FromMinutes(5);
        rig.Relays.Connect(QuietRelay(rig, "a"));
        rig.Relays.Connect(QuietRelay(rig, "b"));
        rig.Relays.Answer = relay => relay is "b" ? new RelayReadiness(false, busyUntil, false) : Yes(rig);
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

        var updates = rig.Start();

        await Assert.That(Joined(rig.EventsStartingWith("call off"))).IsEqualTo("call off 1.2.0");
        await AssertNothingEnded(rig);
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.Held);
        await Assert.That(rig.Logged(53)).IsTrue().Because("event 53 says what called it off");

        // Not asked again one tick before the countdown b's no carried.
        rig.Clock.Advance(busyUntil - rig.Clock.GetUtcNow() - OneTick);

        await Assert.That(rig.EventsStartingWith("ask").Count).IsEqualTo(2);

        // At it, everyone is asked again; this time b says yes and the update installs.
        rig.Relays.Answer = _ => Yes(rig);
        rig.Clock.Advance(OneTick);

        await Assert.That(rig.EventsStartingWith("ask").Count).IsEqualTo(4);
        await Assert.That(rig.IndexOf("apply 1.2.0 with [--after-update 1.2.0]")).IsGreaterThanOrEqualTo(0);
    }

    /// <summary>
    /// A relay that does not answer within the bound calls the update off and nothing
    /// ends; its answer, arriving late, changes nothing.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AMissingAnswerCallsTheUpdateOffAndNothingEnds()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(QuietRelay(rig, "a"));
        rig.Relays.Connect(QuietRelay(rig, "b"));
        rig.Relays.Answer = relay => relay is "a" ? Yes(rig) : null;
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

        _ = rig.Start();

        await Assert.That(rig.EventsStartingWith("ask").Count).IsEqualTo(2);
        await Assert.That(rig.EventsStartingWith("call off")).IsEmpty();

        rig.Clock.Advance(BackgroundUpdates.ReadyToEndBound - OneTick);

        await Assert.That(rig.EventsStartingWith("call off")).IsEmpty().Because("one tick short of the bound");

        rig.Clock.Advance(OneTick);

        await Assert.That(Joined(rig.EventsStartingWith("call off"))).IsEqualTo("call off 1.2.0");
        await AssertNothingEnded(rig);

        // The late yes is not read.
        _ = rig.Relays.AnswerLater("b", Yes(rig));
        rig.Settle();

        await AssertNothingEnded(rig);
    }

    /// <summary>
    /// A relay that said yes and then heard from its client calls the update off for
    /// everyone, before the others have answered, and nothing ends.
    /// </summary>
    /// <remarks>
    /// <b>The withdraw arrives before the relay's new countdown does</b>: the relays
    /// double still lists a as quiet, the order the background's two reports could
    /// come in. So the core asks again at once, a says no this time, as a relay
    /// whose client just sent a message does, and the second question is called off
    /// in turn. What must never happen is the yes b gives afterwards installing
    /// anything.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARelayThatWithdrawsItsYesCallsTheUpdateOff()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(QuietRelay(rig, "a"));
        rig.Relays.Connect(QuietRelay(rig, "b"));
        rig.Relays.Answer = relay => relay is "a" ? Yes(rig) : null;
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

        var updates = rig.Start();

        await Assert.That(rig.EventsStartingWith("call off")).IsEmpty().Because("b has not answered yet");

        // a's client sends a message: a withdraws its yes, and from now on says no.
        rig.Relays.Answer = relay => relay is "a" ? No(rig) : null;
        updates.RelayWithdrew("a");
        rig.Settle();

        await Assert.That(rig.EventsStartingWith("call off").Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(rig.IndexOf("call off 1.2.0")).IsGreaterThan(rig.IndexOf("ask b 1.2.0"));
        await AssertNothingEnded(rig);

        // b's yes, arriving after the call off, does not install anything.
        _ = rig.Relays.AnswerLater("b", Yes(rig));
        rig.Settle();

        await AssertNothingEnded(rig);
    }

    /// <summary>With no relay connected and nothing holding, the update installs without a question.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNoRelayConnectedTheUpdateInstallsWithoutAsking()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

        _ = rig.Start();

        var installing = rig.IndexOf("toast installing 1.2.0");
        var applied = rig.IndexOf("apply 1.2.0 with [--after-update 1.2.0]");
        var exited = rig.IndexOf("exit");

        await Assert.That(rig.EventsStartingWith("ask")).IsEmpty();
        await Assert.That(installing).IsGreaterThanOrEqualTo(0).Because(string.Join(" | ", rig.Events));
        await Assert.That(applied).IsGreaterThan(installing);
        await Assert.That(exited).IsGreaterThan(applied);
        await Assert.That(rig.Logged(55)).IsTrue().Because("event 55 says nothing held it and nobody was there to ask");
    }

    // ---- The person's install-now ---------------------------------------------

    /// <summary>
    /// Install now for a version that is not the one held is refused with one
    /// sentence, and so is Install now with nothing held; nothing ends either time.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InstallNowRefusesAVersionThatIsNotTheOneHeld()
    {
        using (var rig = new BackgroundUpdateRig())
        {
            rig.Relays.Connect(BusyRelay(rig, "a"));
            rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

            var updates = rig.Start();
            var refusal = await updates.InstallNowAsync("1.1.9", CancellationToken.None);

            await Assert.That(refusal).IsNotNull();
            await Assert.That(refusal!).Contains("1.2.0");
            await Assert.That(refusal).Contains("1.1.9");
            await AssertNothingEnded(rig);
            await Assert.That(rig.EventsStartingWith("close sessions")).IsEmpty();
            await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.Held);
            await Assert.That(rig.Logged(59)).IsTrue();
        }

        using (var rig = new BackgroundUpdateRig())
        {
            var updates = rig.Start();
            var refusal = await updates.InstallNowAsync("1.2.0", CancellationToken.None);

            await Assert.That(refusal).IsEqualTo("No update is downloaded and waiting, so there is nothing to install.");
            await AssertNothingEnded(rig);
        }
    }

    /// <summary>
    /// Install now ends every relay at once, closes every session, raises the
    /// installing toast, hands over with the restart arguments and asks the background
    /// to exit, in that order, with a call in flight and two sessions still holding.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InstallNowEndsEveryRelayNowClosesEverySessionAndHandsOver()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(QuietRelay(rig, "a") with { CallInFlight = true });
        rig.Sessions.Open(new ListedSession(@"C:\work\hidden", null, Visible: false, rig.Clock.GetUtcNow() + TimeSpan.FromMinutes(5)));
        rig.Sessions.Open(new ListedSession(@"C:\work\window", null, Visible: true, ClosesAt: null));
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");

        var updates = rig.Start();
        var result = await updates.InstallNowAsync("1.2.0", CancellationToken.None);

        await Assert.That(result).IsNull();

        var ended = rig.IndexOf("end relays now 1.2.0");
        var closed = rig.IndexOf("close sessions");
        var installing = rig.IndexOf("toast installing 1.2.0");
        var applied = rig.IndexOf("apply 1.2.0 with [--after-update 1.2.0]");
        var exited = rig.IndexOf("exit");

        await Assert.That(ended).IsGreaterThanOrEqualTo(0).Because(string.Join(" | ", rig.Events));
        await Assert.That(closed).IsGreaterThan(ended);
        await Assert.That(installing).IsGreaterThan(closed);
        await Assert.That(applied).IsGreaterThan(installing);
        await Assert.That(exited).IsGreaterThan(applied);

        await Assert.That(rig.EventsStartingWith("ask")).IsEmpty();
        await Assert.That(rig.EventsStartingWith("call off")).IsEmpty();
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.Installing);
    }

    // ---- What the toast and the dashboard read ----------------------------------

    /// <summary>
    /// <see cref="IUpdateHoldsReader.Read"/> splits the sessions by visibility and lists
    /// every connected relay, holding or not, with what its client needs afterwards.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ReadSplitsTheSessionsByVisibilityAndListsEveryRelay()
    {
        using var rig = new BackgroundUpdateRig();
        var now = rig.Clock.GetUtcNow();
        var hiddenClosesAt = now + TimeSpan.FromMinutes(7);
        var claudeIdleAt = now + TimeSpan.FromMinutes(9);

        rig.Sessions.Open(new ListedSession(@"C:\work\hidden", "reads the docs", Visible: false, hiddenClosesAt));
        rig.Sessions.Open(new ListedSession(@"C:\work\window", null, Visible: true, ClosesAt: null));
        rig.Relays.Connect(new RelayState("1", KnownClients.Codex, "0.155.0", @"C:\repo", now - TimeSpan.FromMinutes(3), CallInFlight: false));
        rig.Relays.Connect(new RelayState("2", KnownClients.ClaudeCode, "2.1.300", @"C:\project", claudeIdleAt, CallInFlight: true));
        rig.Relays.Connect(new RelayState("3", null, null, null, now - TimeSpan.FromMinutes(1), CallInFlight: false));

        var updates = rig.Build();

        // Before anything is held: nothing, and no lists.
        var before = updates.Read();

        await Assert.That(before.State).IsEqualTo(UpdateHoldState.None);
        await Assert.That(before.Relays).IsEmpty();

        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");
        updates.Start();
        rig.Settle();

        var snapshot = updates.Read();

        await Assert.That(snapshot.State).IsEqualTo(UpdateHoldState.Held);
        await Assert.That(snapshot.Version).IsEqualTo("1.2.0");
        await Assert.That(snapshot.ReadAt).IsEqualTo(rig.Clock.GetUtcNow());

        await Assert.That(snapshot.HiddenSessions.Count).IsEqualTo(1);
        await Assert.That(snapshot.HiddenSessions[0]).IsEqualTo(new HoldingSession(@"C:\work\hidden", "reads the docs", hiddenClosesAt));
        await Assert.That(snapshot.VisibleWindows.Count).IsEqualTo(1);
        await Assert.That(snapshot.VisibleWindows[0]).IsEqualTo(new HoldingSession(@"C:\work\window", null, null));

        await Assert.That(snapshot.Relays.Count).IsEqualTo(3).Because("every connected relay is listed, holding or not");

        var codex = snapshot.Relays.Single(relay => relay.ProjectFolder is @"C:\repo");
        var claude = snapshot.Relays.Single(relay => relay.ProjectFolder is @"C:\project");
        var unnamed = snapshot.Relays.Single(relay => relay.ProjectFolder is null);

        await Assert.That(codex).IsEqualTo(new HoldingRelay("codex-mcp-client 0.155.0", @"C:\repo", now - TimeSpan.FromMinutes(3), false, RelayReconnect.NewConversation));

        // Listed, and holding nothing: its countdown has run out and no call is in
        // flight, which is how a reader of the snapshot decides a relay holds.
        await Assert.That(codex.IdleAt).IsLessThanOrEqualTo(snapshot.ReadAt);
        await Assert.That(codex.CallInFlight).IsFalse();
        await Assert.That(claude).IsEqualTo(new HoldingRelay("claude-code 2.1.300", @"C:\project", claudeIdleAt, true, RelayReconnect.Unknown));
        await Assert.That(unnamed.Client).IsEqualTo("unnamed client");
        await Assert.That(unnamed.Reconnect).IsEqualTo(RelayReconnect.Unknown);
    }

    /// <summary>
    /// The snapshot the toast and the dashboard draw names each relay's conversation from
    /// the read that reads its client's records, and the core's own passes never make
    /// that read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.3 c, the maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all
    /// your recommendations"</i></b>: the files are read when the dashboard or a toast is
    /// drawn. The core decides whether an update may install every few seconds, and a
    /// name decides nothing there, so a pass that read every client's records would read
    /// them for nobody.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a snapshot read through
    /// <see cref="IUpdateRelays.Connected"/>, which named nothing, and against a countdown
    /// read that named every conversation.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSnapshotADrawReadsNamesEachConversationAndThePassesReadNoName()
    {
        using var rig = new BackgroundUpdateRig();
        var now = rig.Clock.GetUtcNow();
        var window = new ClientWindow("1200-134360600000000000", @"C:\project");

        rig.Relays.Connect(new RelayState("1", KnownClients.ClaudeCode, "2.1.296", @"C:\project", now + TimeSpan.FromMinutes(9), CallInFlight: false) { Reconnect = RelayReconnect.None });
        rig.Relays.Names["1"] = ("aaaaaaaa-1111-4111-8111-111111111111", new ConversationName("Apple scales", IsTitle: true), window);

        var updates = rig.Build();

        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");
        updates.Start();
        rig.Settle();

        // Passes ran and a download is held, and nothing has drawn the snapshot yet:
        // the rig's toasts are recorded, not drawn.
        await Assert.That(rig.Relays.NamedReads).IsEqualTo(0).Because("one of the core's own passes read every client's records");

        var snapshot = updates.Read();
        var relay = snapshot.Relays.Single();

        await Assert.That(snapshot.State).IsEqualTo(UpdateHoldState.Held);
        await Assert.That(relay.Label).IsEqualTo(new ConversationName("Apple scales", IsTitle: true));
        await Assert.That(relay.Conversation).IsEqualTo("aaaaaaaa-1111-4111-8111-111111111111");
        await Assert.That(relay.Window).IsEqualTo(window);
        await Assert.That(rig.Relays.NamedReads).IsEqualTo(1).Because("a draw read the names more than once, or not at all");

        // Time passes and the core decides again: it reads no name.
        var reads = rig.Relays.NamedReads;

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.Settle();

        await Assert.That(rig.Relays.NamedReads).IsEqualTo(reads).Because("one of the core's own passes read every client's records");

        // And the toast's countdown reads the same snapshot with no name in it.
        var countdown = updates.ReadCountdown();

        await Assert.That(countdown.State).IsEqualTo(UpdateHoldState.Held);
        await Assert.That(countdown.Relays.Single().Label).IsNull();
        await Assert.That(countdown.Relays.Single().IdleAt).IsEqualTo(relay.IdleAt);
        await Assert.That(rig.Relays.NamedReads).IsEqualTo(reads).Because("the countdown's read read every client's records");
    }

    // ---- Two more paths the hold depends on ------------------------------------

    /// <summary>A package an earlier run left staged is held at once, with no download.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APackageAnEarlierRunStagedIsHeldWithoutADownload()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Relays.Connect(BusyRelay(rig, "a"));
        rig.Client.StagedCandidate = BackgroundUpdateRig.Candidate("1.2.0");

        var updates = rig.Start();

        await Assert.That(updates.Read().Version).IsEqualTo("1.2.0");
        await Assert.That(rig.Client.Downloads).IsEqualTo(0);
        await Assert.That(Joined(rig.EventsStartingWith("toast ready"))).IsEqualTo("toast ready 1.2.0");
        await Assert.That(rig.Logged(51)).IsTrue().Because("event 51 says an earlier run downloaded it");
    }

    /// <summary>
    /// A hand-over that throws raises the failed toast, asks nothing to exit, and is
    /// not tried again in the same run.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHandOverThatFailsRaisesTheFailedToastAndTheBackgroundStays()
    {
        using var rig = new BackgroundUpdateRig();
        rig.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");
        rig.Client.ApplyFailure = new InvalidOperationException("Update.exe is missing");

        var updates = rig.Start();

        await Assert.That(Joined(rig.EventsStartingWith("toast failed"))).IsEqualTo("toast failed 1.2.0");
        await Assert.That(rig.ExitRequests).IsEqualTo(0);
        await Assert.That(updates.Read().State).IsEqualTo(UpdateHoldState.None);
        await Assert.That(rig.Logged(58)).IsTrue();

        // The next check offers it again and it is not downloaded or tried again.
        rig.Clock.Advance(BackgroundUpdates.CheckInterval);

        await Assert.That(rig.Client.Checks).IsEqualTo(2);
        await Assert.That(rig.Client.Downloads).IsEqualTo(1);
        await Assert.That(rig.Logged(44)).IsTrue();
    }

    /// <summary>A relay whose countdown has run out and that has no call in flight.</summary>
    /// <param name="rig">The rig whose clock the countdown is read against.</param>
    /// <param name="id">Its id.</param>
    /// <returns>The relay.</returns>
    private static RelayState QuietRelay(BackgroundUpdateRig rig, string id) =>
        new(id, KnownClients.ClaudeCode, "2.1.300", @"C:\project", rig.Clock.GetUtcNow() - TimeSpan.FromMinutes(1), CallInFlight: false);

    /// <summary>A relay whose countdown runs for longer than any arm moves the clock.</summary>
    /// <param name="rig">The rig whose clock the countdown is read against.</param>
    /// <param name="id">Its id.</param>
    /// <returns>The relay.</returns>
    private static RelayState BusyRelay(BackgroundUpdateRig rig, string id) =>
        QuietRelay(rig, id) with { IdleAt = rig.Clock.GetUtcNow() + TimeSpan.FromDays(1) };

    /// <summary>A yes, read against the rig's clock at the moment the relay answers.</summary>
    /// <param name="rig">The rig.</param>
    /// <returns>The answer.</returns>
    private static RelayReadiness Yes(BackgroundUpdateRig rig) =>
        new(true, rig.Clock.GetUtcNow() - TimeSpan.FromMinutes(1), CallInFlight: false);

    /// <summary>A no from a relay whose client has just sent a message: its countdown runs ten minutes from now.</summary>
    /// <param name="rig">The rig.</param>
    /// <returns>The answer.</returns>
    private static RelayReadiness No(BackgroundUpdateRig rig) =>
        new(false, rig.Clock.GetUtcNow() + TimeSpan.FromMinutes(10), CallInFlight: false);

    /// <summary>A list of events, joined so that order and count are one comparison.</summary>
    /// <param name="events">The events.</param>
    /// <returns>Them, joined by a bar.</returns>
    private static string Joined(List<string> events) => string.Join('|', events);

    /// <summary>Nothing ended, nothing was handed over, nobody was asked to exit.</summary>
    /// <param name="rig">The rig.</param>
    /// <returns>The assertion task.</returns>
    private static async Task AssertNothingEnded(BackgroundUpdateRig rig)
    {
        await Assert.That(rig.EventsStartingWith("end relays")).IsEmpty().Because(string.Join(" | ", rig.Events));
        await Assert.That(rig.EventsStartingWith("apply")).IsEmpty();
        await Assert.That(rig.EventsStartingWith("toast installing")).IsEmpty();
        await Assert.That(rig.ExitRequests).IsEqualTo(0);
    }
}
