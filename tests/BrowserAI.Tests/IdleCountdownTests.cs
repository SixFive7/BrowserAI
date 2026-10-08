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
    /// <b>Q326 a reversed.</b> Until 2026-10-08 a headed session had no timer at all
    /// and its launch wrote upstream's timeout as zero; a headless launch wrote
    /// upstream's hour. An agent may now set a time past that hour, and every call
    /// that names a session restarts BrowserAI's countdown where upstream's restarts
    /// only on a call it receives, so every launch writes zero.
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

        // The hidden one goes first, at its ten minutes.
        await AdvanceUntilAsync(clock, ShortPeriod, () => hiddenChild.HasStopped, "the hidden session was never idle-closed");

        await Assert.That(visibleChild.HasStopped).IsFalse()
            .Because("a visible window closed at the hidden default instead of its own hour");

        // Three more hidden defaults, which is five at most since its last call and
        // short of its hour whichever way the navigation's own scope was released.
        clock.Advance(ShortPeriod * 3);
        _ = await harness.Client.RoundTripAsync("tools/list");

        await Assert.That(visibleChild.HasStopped).IsFalse()
            .Because("a visible window closed before its hour had passed");

        // And the visible one at its hour, measured from its own last call.
        await AdvanceUntilAsync(clock, ShortPeriod, () => visibleChild.HasStopped, "the visible window was never idle-closed");

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

        await AdvanceUntilAsync(clock, OneMinute, () => threeChild.HasStopped, "the three-minute session was never idle-closed");

        // Three of the minutes and not ten: the hidden default would have needed more.
        await Assert.That(clock.GetUtcNow() - named).IsLessThan(ShortPeriod);

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
