// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A session's idle countdown since 2026-10-08: its own length, minutes or never, a
/// visible window's included, restarted by every call that names the session, and
/// read by the update through one seam.
/// </summary>
/// <remarks>
/// <para>
/// <b>E2, the maintainer's words of 2026-10-07 verbatim:</b> <i>"Right now browsers
/// automatically close after 10 min. of inactivity and interactive windows never do.
/// What if we change the never to 1 hour and then allow the calling agent to change
/// this default behaviour with a parameter?"</i> <b>And F2, of 2026-10-08:</b>
/// <i>"Also, any type of call, even if refused once because the settings are
/// different should reset the countdown timer on live sessions."</i>
/// </para>
/// <para>
/// <b>Every arm drives a <see cref="ManualClock"/></b>, and the rig's idle period is
/// nominal: the hidden default lasts <see cref="ShortPeriod"/>, so one minute of a
/// setting lasts a tenth of it, and a visible window's hour six of them. Nothing here
/// waits for a period to pass.
/// </para>
/// <para>
/// <b>Planted red on 2026-10-08</b> against the batch's own code with each behaviour
/// taken out in turn: a visible window given no countdown, the touch made a no-op,
/// the old answer, an empty seam, and upstream's hour written back into the launch.
/// </para>
/// </remarks>
internal sealed class IdleCountdownTests
{
    /// <summary>The nominal hidden default every arm is configured with; nothing waits for it.</summary>
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromMilliseconds(800);

    /// <summary>One minute of an idle setting in these arms: a tenth of the hidden default.</summary>
    private static readonly TimeSpan OneMinute = ShortPeriod / SessionTimes.HiddenIdleMinutes;

    /// <summary>
    /// A visible window closes after its own hour, not before and not never, and a
    /// hidden session beside it after its ten minutes; and no launch carries
    /// upstream's own idle timeout any more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q326 a reversed.</b> Until 2026-10-08 a headed session had no timer at all
    /// and its launch wrote upstream's timeout as zero; a headless launch wrote
    /// upstream's hour. An agent may now set a time past that hour, and every call
    /// that names a session restarts BrowserAI's countdown where upstream's restarts
    /// only on a call it receives, so every launch writes zero.
    /// </para>
    /// <para>
    /// ⚠️ <b>The clock moves to each deadline exactly and then stands still while the
    /// close runs</b>, since 2026-10-08 (previously it stepped a whole hidden period
    /// every 20 ms until the hidden session's child had stopped, then three periods
    /// more). A close is a round trip to the child on the thread pool, so under load
    /// the stepping ran on past the visible window's hour before the hidden close had
    /// finished, and the arm went red with "a visible window closed before its hour had
    /// passed" in the PowerShell half of lane REC's gate at <c>6694dda8</c>. Planted
    /// red the same day by slowing the hidden child's close by 300 ms, which the old
    /// arm failed and this one passes.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AVisibleWindowClosesAfterItsOwnHourAndNoLaunchCarriesUpstreamsTimeout()
    {
        var clock = new ManualClock();

        await using var rig = Sessions(clock);
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var visible = Path.Combine(rig.Root, "visible");
        var hidden = Path.Combine(rig.Root, "hidden");

        await InitAsync(harness, visible, headed: true);
        await InitAsync(harness, hidden, headed: false);

        await NavigateAsync(harness, visible, "the call that starts the visible browser");
        await NavigateAsync(harness, hidden, "the call that starts the hidden browser");

        var visibleChild = rig.SessionChildren[0];
        var hiddenChild = rig.SessionChildren[1];

        // The clock moves a minute so the navigations' own scopes have gone, and a
        // call BrowserAI answers itself then names both sessions at one moment this
        // arm knows.
        clock.Advance(OneMinute);
        _ = await harness.Client.RoundTripAsync("tools/list");
        await CatchUpAsync(harness, visible);
        await CatchUpAsync(harness, hidden);

        var named = clock.GetUtcNow();
        var hour = OneMinute * SessionTimes.VisibleIdleMinutes;

        // The hidden one goes first, at its ten minutes, and the clock stands still
        // while its close runs.
        clock.Advance(ShortPeriod);
        await WaitUntilAsync(() => hiddenChild.HasStopped, "the hidden session was not idle-closed at its ten minutes");

        await Assert.That(visibleChild.HasStopped).IsFalse()
            .Because("a visible window closed at the hidden default instead of its own hour");

        // One tick short of its hour, measured from the call that named it: open, with
        // its deadline at the hour.
        clock.AdvanceTicks((hour - ShortPeriod).Ticks - ManualClock.OneTick);
        _ = await harness.Client.RoundTripAsync("tools/list");

        await Assert.That(visibleChild.HasStopped).IsFalse()
            .Because("a visible window closed before its hour had passed");
        await Assert.That(CountdownOf(harness, visible).ClosesAt).IsEqualTo(named + hour);

        // And the visible one at its hour.
        clock.AdvanceTicks(ManualClock.OneTick);
        await WaitUntilAsync(() => visibleChild.HasStopped, "the visible window was not idle-closed at its hour");

        await Assert.That(RecordedSession.LogOf(visible).Any(row => row.Tool == LiveSession.BrowserCloseTool)).IsTrue();

        // The refusal after it says nobody had used the window either.
        var refused = await NavigateAsync(harness, visible, "the call after the visible window's idle close");

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains($"because no call had named the session for {SessionErrors.Duration(OneMinute * SessionTimes.VisibleIdleMinutes)}, and {CloseReasons.NobodyUsedTheWindow}");

        // And neither launch carries upstream's own timeout.
        foreach (var launch in rig.Launches)
        {
            await Assert.That((int?)ConfigOf(launch)["timeouts"]?["idle"]).IsEqualTo(BrowserAI.Runtime.BrowserConfiguration.NoIdleTimeout);
        }
    }

    /// <summary>
    /// An idle setting in minutes is the countdown's length, one tick short and one
    /// tick past, and a session set to never is never closed for being idle.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnIdleSettingIsTheCountdownsLengthAndNeverMeansNever()
    {
        var clock = new ManualClock();

        await using var rig = Sessions(clock);
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var three = Path.Combine(rig.Root, "three-minutes");
        var never = Path.Combine(rig.Root, "never");

        await InitAsync(harness, three, headed: false, idle: 3);
        await InitAsync(harness, never, headed: false, idle: IdleSetting.NeverWord);

        // A navigation starts each browser, and the clock moves a minute so its own
        // scope has gone before the countdown is read.
        await NavigateAsync(harness, never, "the call that starts the browser set to never");
        await NavigateAsync(harness, three, "the call that starts the three-minute browser");

        var threeChild = rig.SessionChildren[0];
        var neverChild = rig.SessionChildren[1];

        clock.Advance(OneMinute);
        _ = await harness.Client.RoundTripAsync("tools/list");

        // A call BrowserAI answers itself starts the countdown at a moment this arm
        // knows: one tick short of three minutes after it, the browser is open.
        _ = await ResumeAsync(harness, three);

        var named = clock.GetUtcNow();

        clock.AdvanceTicks((OneMinute * 3).Ticks - ManualClock.OneTick);
        _ = await harness.Client.RoundTripAsync("tools/list");

        await Assert.That(threeChild.HasStopped).IsFalse();

        // ⚠️ Moved to the deadline exactly and then held still while the close runs,
        // since 2026-10-08 (previously stepped a minute every 20 ms until the child had
        // stopped, which a slow close carried past the ten minutes the next line rules
        // out: planted red the same day with the close slowed by a second).
        clock.AdvanceTicks(ManualClock.OneTick);
        await WaitUntilAsync(() => threeChild.HasStopped, "the three-minute session was not idle-closed at its three minutes");

        // Three of the minutes and not ten: the hidden default would have needed more.
        await Assert.That(clock.GetUtcNow() - named).IsEqualTo(OneMinute * 3);

        // Fifty hidden defaults later, the one set to never is still open.
        clock.Advance(ShortPeriod * 50);
        _ = await harness.Client.RoundTripAsync("tools/list");

        await Assert.That(neverChild.HasStopped).IsFalse();
        await Assert.That(RecordedSession.LogOf(never).Any(row => row.Tool == LiveSession.BrowserCloseTool)).IsFalse();
    }

    /// <summary>
    /// An idle setting that is not a whole number of minutes from one, or the word
    /// never, is refused by name, and nothing is created.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnIdleSettingThatIsNotMinutesOrNeverIsRefusedAndNothingIsCreated()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var wrong = new (string Label, JsonNode Value)[]
        {
            ("zero", 0),
            ("negative", -5),
            ("fraction", 1.5),
            ("digits in a string", "10"),
            ("another word", "sometimes"),
            ("a boolean", false),
            ("past a 32-bit count", 3_000_000_000L),
        };

        foreach (var (label, value) in wrong)
        {
            var directory = Path.Combine(rig.Root, $"refused-{label.Replace(' ', '-')}");

            var answer = await CallAsync(harness, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = "never created, because its idle setting is not one",
                [IdleSetting.ParameterName] = value,
            });

            await Assert.That((bool?)answer["isError"]).IsTrue().Because(label);
            await Assert.That(TextOf(answer)).StartsWith($"'{IdleSetting.ParameterName}' must be a whole number of minutes from 1 to {int.MaxValue.ToString(CultureInfo.InvariantCulture)}, or \"never\"").Because(label);
            await Assert.That(TextOf(answer)).Contains("Nothing was created and nothing was changed.").Because(label);
            await Assert.That(File.Exists(Path.Combine(directory, SessionLayout.DataFileName))).IsFalse().Because(label);
        }

        // The positive control: the word in another case is the word.
        var accepted = await CallAsync(harness, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(rig.Root, "never-in-capitals"),
            ["purpose"] = "set to never, written in capitals",
            [IdleSetting.ParameterName] = "NEVER",
        });

        await Assert.That((bool?)accepted["isError"]).IsNotEqualTo(true).Because(TextOf(accepted));
        await Assert.That(TextOf(accepted)).Contains($"  {IdleSetting.ParameterName}: never -- this browser is never closed for being idle");
    }

    /// <summary>
    /// Every call that names a live session starts its countdown again, whatever the
    /// answer: a resume of a live session, <c>browserai_catch_up</c>,
    /// <c>browserai_change_purpose</c>, a tool BrowserAI does not have, a denied one,
    /// and a call refused for a missing <c>why</c>.
    /// </summary>
    /// <remarks>
    /// <b>Six calls, each followed by an advance of one tick less than a whole
    /// period, is six periods with no close</b>, which only a countdown every one of
    /// them restarted allows. None of them is forwarded, so none holds the countdown
    /// through a call in flight: what keeps the browser open is the restart and
    /// nothing else. Until 2026-10-08 only a forwarded call restarted it, and this arm
    /// would have closed the browser at the second advance.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryCallThatNamesALiveSessionStartsItsCountdownAgainWhateverTheAnswer()
    {
        var clock = new ManualClock();

        await using var rig = Sessions(clock);
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var directory = Path.Combine(rig.Root, "named-again-and-again");

        await InitAsync(harness, directory, headed: false);
        await NavigateAsync(harness, directory, "the call that starts the browser");

        var child = rig.SessionChildren[^1];

        // Let the navigation's own scope go before the clock is trusted.
        clock.Advance(OneMinute);
        _ = await harness.Client.RoundTripAsync("tools/list");

        var calls = new (string Tool, JsonObject Arguments)[]
        {
            (SessionToolSurface.Resume, new JsonObject { ["directory"] = directory, ["why"] = "a resume of a session that is live" }),
            (SessionToolSurface.CatchUp, new JsonObject { ["session"] = directory, ["why"] = "reading back what the session did" }),
            (SessionToolSurface.ChangePurpose, new JsonObject { ["session"] = directory, ["purpose"] = "a purpose set to keep the session named", ["why"] = "naming the session again" }),
            ("browser_frobnicate", new JsonObject { ["session"] = directory, ["why"] = "a tool BrowserAI does not have" }),
            (LiveSession.BrowserCloseTool, new JsonObject { ["session"] = directory, ["why"] = "a tool BrowserAI denies" }),
            ("browser_snapshot", new JsonObject { ["session"] = directory }),
        };

        foreach (var (tool, arguments) in calls)
        {
            _ = await CallAsync(harness, tool, arguments);

            clock.AdvanceTicks(ShortPeriod.Ticks - ManualClock.OneTick);

            await Assert.That(child.HasStopped).IsFalse()
                .Because($"'{tool}' named the session and its countdown ran out anyway, so the call did not start it again");
        }

        // And with nothing more naming it, the countdown runs out.
        await AdvanceUntilAsync(clock, ShortPeriod, () => child.HasStopped, "the session was never idle-closed once the calls stopped");
    }

    /// <summary>
    /// A resume of a live session says the session is already live and that the call
    /// started its idle countdown again, and names no time; one set to never says it
    /// has no countdown.
    /// </summary>
    /// <remarks>
    /// <b>F2, the maintainer's words of 2026-10-08 verbatim:</b> <i>"Just generic "the
    /// timeout has been reset" or something alike. Think of your own text."</i> The
    /// texts are asserted as literals as well as against the method that writes them,
    /// because a sentence compared with the method that produces it cannot tell a true
    /// sentence from a false one.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAlreadyLiveAnswerSaysTheCountdownStartedAgainAndNamesNoTime()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var up = Path.Combine(rig.Root, "browser-up");
        var notYet = Path.Combine(rig.Root, "browser-not-started");
        var never = Path.Combine(rig.Root, "set-to-never");

        await InitAsync(harness, up, headed: false);
        await NavigateAsync(harness, up, "the call that starts the browser");
        await InitAsync(harness, notYet, headed: false);
        await InitAsync(harness, never, headed: false, idle: IdleSetting.NeverWord);

        var upAnswer = TextOf(await ResumeAsync(harness, up));
        var notYetAnswer = TextOf(await ResumeAsync(harness, notYet));
        var neverAnswer = TextOf(await ResumeAsync(harness, never, idle: IdleSetting.NeverWord));

        await Assert.That(upAnswer).StartsWith(SessionManager.AlreadyLive(browserUp: true, purposeChanged: false));
        await Assert.That(upAnswer).StartsWith(
            "The session is already live, so nothing changed: its browser, its tabs and its settings are as they were. "
            + "This call started its idle countdown again, as every call that names the session does.");

        await Assert.That(notYetAnswer).StartsWith(
            "The session is already live, so nothing changed: its browser has not started yet, and the first browser call starts it with the settings the session already has. "
            + "This call started its idle countdown again, as every call that names the session does.");

        await Assert.That(neverAnswer).StartsWith(
            "The session is already live, so nothing changed: its browser has not started yet, and the first browser call starts it with the settings the session already has. "
            + "It has no idle countdown, because its idle setting is never.");

        // No time, in either unit, in the lead line of any of them.
        foreach (var answer in new[] { upAnswer, notYetAnswer, neverAnswer })
        {
            var lead = answer.Split('\n')[0];

            await Assert.That(lead).DoesNotContain("minute");
            await Assert.That(lead).DoesNotContain("hour");
        }
    }

    /// <summary>
    /// The seam the update reads says, of every open session, its directory, its
    /// purpose, whether it has a window and when its countdown ends; the deadline moves
    /// on a call that names it, is empty for never, and a closed session is not
    /// listed.
    /// </summary>
    /// <remarks>
    /// <b>The contract is lane UI's <c>UpdateHolds.HoldingSession</c> of 2026-10-08</b>:
    /// a deadline and never a remaining time, and <see langword="null"/> exactly when
    /// the agent set the session to never.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCountdownsSayWhatEachOpenSessionHoldsAndMoveWithEveryCallThatNamesIt()
    {
        var clock = new ManualClock();

        await using var rig = Sessions(clock);
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var hidden = Path.Combine(rig.Root, "seam-hidden");
        var visible = Path.Combine(rig.Root, "seam-visible");
        var never = Path.Combine(rig.Root, "seam-never");
        var closed = Path.Combine(rig.Root, "seam-closed");

        await InitAsync(harness, hidden, headed: false);
        await InitAsync(harness, visible, headed: true);
        await InitAsync(harness, never, headed: false, idle: IdleSetting.NeverWord);
        await InitAsync(harness, closed, headed: false);

        _ = await CallAsync(harness, SessionToolSurface.Close, new JsonObject { ["session"] = closed, ["why"] = "a session the seam must leave out" });

        var opened = clock.GetUtcNow();
        var countdowns = harness.Proxy.SessionCountdowns().ToDictionary(countdown => countdown.Directory, StringComparer.OrdinalIgnoreCase);

        await Assert.That(string.Join(",", countdowns.Keys.Select(Path.GetFileName).Order(StringComparer.Ordinal)))
            .IsEqualTo("seam-hidden,seam-never,seam-visible");

        var h = countdowns[SessionPath.For(hidden).FullPath];
        var v = countdowns[SessionPath.For(visible).FullPath];
        var n = countdowns[SessionPath.For(never).FullPath];

        await Assert.That(h.Purpose).IsEqualTo("a session whose countdown the suite reads");
        await Assert.That(h.Visible).IsFalse();
        await Assert.That(v.Visible).IsTrue();
        await Assert.That(h.ClosesAt).IsEqualTo(opened + ShortPeriod);
        await Assert.That(v.ClosesAt).IsEqualTo(opened + (OneMinute * SessionTimes.VisibleIdleMinutes));
        await Assert.That(n.ClosesAt).IsNull();
        await Assert.That(h.BrowserIsOpen).IsFalse();

        // A call that names the session moves its deadline, refused or not.
        clock.Advance(OneMinute * 4);

        _ = await CallAsync(harness, "browser_frobnicate", new JsonObject { ["session"] = hidden, ["why"] = "a refused call that still names the session" });

        var moved = harness.Proxy.SessionCountdowns().Single(countdown => string.Equals(countdown.Directory, SessionPath.For(hidden).FullPath, StringComparison.OrdinalIgnoreCase));

        await Assert.That(moved.ClosesAt).IsEqualTo(clock.GetUtcNow() + ShortPeriod);
        await Assert.That(moved.LastActivity).IsEqualTo(clock.GetUtcNow());
    }

    /// <summary>
    /// A person's keyboard or mouse input in a visible window, while it is in front,
    /// starts the session's countdown again; input in another window, or with a
    /// hidden session's browser in front, counts for nothing; and the check runs only
    /// while a visible session is in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>F4, decided 2026-10-08 by the maintainer, in his words verbatim:</b> <i>"f4 a -
    /// but only if this is easy."</i> The rig's desktop is a double
    /// (<see cref="RigDesktop"/>), so no arm touches the keyboard or the mouse: it sets
    /// the window in front and the last input's tick, and runs the check by hand.
    /// </para>
    /// <para>
    /// <b>Three hours of typing, each check one tick short of the hour after the last,
    /// is three hours with no close</b>, which only input that counted allows. Planted
    /// red against the tree with the session never joining the check.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APersonsInputInAVisibleWindowStartsItsCountdownAgainAndInputElsewhereDoesNot()
    {
        var clock = new ManualClock();

        await using var rig = Sessions(clock);
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var visible = Path.Combine(rig.Root, "typed-into");
        var hidden = Path.Combine(rig.Root, "never-typed-into");

        await InitAsync(harness, hidden, headed: false);
        await NavigateAsync(harness, hidden, "the call that starts the hidden browser");

        // A hidden session has no window to use, so it starts no check.
        await Assert.That(rig.Desktop.CheckIsRunning).IsFalse();

        await InitAsync(harness, visible, headed: true);
        await NavigateAsync(harness, visible, "the call that starts the visible browser");

        var hiddenChild = rig.SessionChildren[0];
        var visibleChild = rig.SessionChildren[1];

        await Assert.That(rig.Desktop.CheckIsRunning).IsTrue();

        // Input with the hidden browser in front counts for nothing: the hidden
        // session closes at its ten minutes all the same.
        rig.Desktop.InFront(hiddenChild.BrowserProcessId);
        rig.Desktop.PersonTypes();
        rig.Desktop.Check();

        // At its ten minutes, and the clock stands still while the close runs, so no
        // step can carry it on towards the visible window's hour (2026-10-08).
        clock.Advance(ShortPeriod);
        await WaitUntilAsync(() => hiddenChild.HasStopped, "the hidden session was not idle-closed at its ten minutes");

        // A call BrowserAI answers itself starts the visible countdown at a moment
        // this arm knows.
        _ = await ResumeAsync(harness, visible);

        var hour = OneMinute * SessionTimes.VisibleIdleMinutes;

        rig.Desktop.InFront(visibleChild.BrowserProcessId);

        for (var typed = 1; typed <= 3; typed++)
        {
            clock.AdvanceTicks(hour.Ticks - ManualClock.OneTick);
            rig.Desktop.TimePasses();
            rig.Desktop.PersonTypes();
            rig.Desktop.Check();

            await Assert.That(visibleChild.HasStopped).IsFalse()
                .Because("the person typed into the window and its countdown ran out anyway");
        }

        // Input with another window in front counts for nothing: one tick short of the
        // hour since the last input that counted, and then past it, the window closes.
        clock.AdvanceTicks(hour.Ticks - ManualClock.OneTick);
        rig.Desktop.InFront(4321);
        rig.Desktop.TimePasses();
        rig.Desktop.PersonTypes();
        rig.Desktop.Check();

        clock.AdvanceTicks(ManualClock.OneTick * 2);

        await WaitUntilAsync(() => visibleChild.HasStopped, "input in another window kept the visible window open");

        // And with no visible session left in it, the check stops.
        await Assert.That(rig.Desktop.CheckIsRunning).IsFalse();
    }

    /// <summary>
    /// Input in the last seconds before a visible window's hour runs out, which no
    /// check has read yet, is read before the window is closed, and keeps it open.
    /// </summary>
    /// <remarks>
    /// <b>The check runs every two seconds, and a person typing into the window in the
    /// seconds before its hour must not have it closed under their hands</b>, which is
    /// the hole the root named when F4 was proposed: <i>"The window could then close
    /// under their hands at the hour."</i> So the countdown makes the check once more
    /// before it decides. Planted red against the tree with that read taken out.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InputInTheLastSecondsBeforeTheHourIsReadBeforeTheWindowIsClosed()
    {
        var clock = new ManualClock();

        await using var rig = Sessions(clock);
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var visible = Path.Combine(rig.Root, "typed-at-the-last-moment");

        await InitAsync(harness, visible, headed: true);
        await NavigateAsync(harness, visible, "the call that starts the visible browser");

        var child = rig.SessionChildren[^1];

        _ = await ResumeAsync(harness, visible);

        var hour = OneMinute * SessionTimes.VisibleIdleMinutes;

        // The person types one tick before the hour runs out, and no check runs.
        rig.Desktop.InFront(child.BrowserProcessId);
        clock.AdvanceTicks(hour.Ticks - ManualClock.OneTick);
        rig.Desktop.TimePasses();
        rig.Desktop.PersonTypes();

        // The hour runs out: the countdown reads the input before it decides. The
        // clock fires the countdown on this thread, so what it decided is readable at
        // once: a deadline a whole hour away, and not a close that has begun.
        clock.AdvanceTicks(ManualClock.OneTick);

        var countdown = harness.Proxy.SessionCountdowns().Single(entry => string.Equals(entry.Directory, SessionPath.For(visible).FullPath, StringComparison.OrdinalIgnoreCase));

        await Assert.That(countdown.ClosesAt).IsEqualTo(clock.GetUtcNow() + hour)
            .Because("the countdown decided the window was idle with input in it that no check had read");

        _ = await harness.Client.RoundTripAsync("tools/list");

        await Assert.That(child.HasStopped).IsFalse();

        // With nothing more, the next hour closes it.
        rig.Desktop.TimePasses();
        await AdvanceUntilAsync(clock, OneMinute, () => child.HasStopped, "the visible window was never idle-closed once the input stopped");
    }

    private static RigSessionEnvironment Sessions(ManualClock clock) =>
        RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools["browser_snapshot"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            opensDefaultSession: false,
            browserIdlePeriod: ShortPeriod,
            clock: clock);

    private static async Task InitAsync(McpTestHarness harness, string directory, bool headed, JsonNode? idle = null)
    {
        var arguments = new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose countdown the suite reads",
            ["headed"] = headed,
        };

        if (idle is not null)
        {
            arguments[IdleSetting.ParameterName] = idle;
        }

        var answer = await CallAsync(harness, SessionToolSurface.Init, arguments);

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not open '{directory}': {TextOf(answer)}");
        }
    }

    private static Task<JsonObject> ResumeAsync(McpTestHarness harness, string directory, JsonNode? idle = null)
    {
        var arguments = new JsonObject { ["directory"] = directory, ["why"] = "the suite resuming a session that is live" };

        if (idle is not null)
        {
            arguments[IdleSetting.ParameterName] = idle;
        }

        return CallAsync(harness, SessionToolSurface.Resume, arguments);
    }

    private static async Task CatchUpAsync(McpTestHarness harness, string directory)
    {
        var answer = await CallAsync(harness, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite naming the session at a moment it knows",
        });

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not name '{directory}': {TextOf(answer)}");
        }
    }

    private static SessionCountdown CountdownOf(McpTestHarness harness, string directory) =>
        harness.Proxy.SessionCountdowns().Single(entry => string.Equals(entry.Directory, SessionPath.For(directory).FullPath, StringComparison.OrdinalIgnoreCase));

    private static Task<JsonObject> NavigateAsync(McpTestHarness harness, string directory, string why) =>
        CallAsync(harness, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = why,
            ["url"] = "data:text/html,<h1>ok</h1>",
        });

    private static async Task<JsonObject> CallAsync(McpTestHarness harness, string tool, JsonObject arguments) =>
        await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

    private static JsonObject ConfigOf(BrowserAI.Protocol.ChildProcessOptions launch)
    {
        var file = launch.Arguments[launch.Arguments.ToList().IndexOf("--config") + 1];

        return JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    }

    /// <summary>Waits for an event with the clock still, bounded by the suite's hang detector.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string whatWentWrong)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds:F1} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    /// <summary>
    /// Moves the clock a step at a time until an event, bounded by the suite's hang
    /// detector and by no number written here.
    /// </summary>
    /// <remarks>
    /// A step that meets a call still in flight re-arms the countdown for a whole
    /// period, correctly, so the loop moves until the event instead of assuming one
    /// step is enough; the arms assert what must NOT have happened before it.
    /// </remarks>
    private static async Task AdvanceUntilAsync(ManualClock clock, TimeSpan step, Func<bool> condition, string whatWentWrong)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds:F1} s.");
            }

            clock.Advance(step);
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
