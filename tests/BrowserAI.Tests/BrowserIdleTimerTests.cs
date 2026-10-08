// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The whole of a session's lifetime: one timer, no expiry, and two teardown
/// mechanisms neither of which is a close tool.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invisible defect this closes is that there was no timer at all.</b>
/// Every session held a browser open forever, and nothing in the suite was red,
/// because nothing asked. It is the shape of failure the charter opens with: the
/// product reported healthy while a browser tree sat there.
/// </para>
/// <para>
/// <b>The timer is driven in milliseconds and shipped at ten minutes.</b> The
/// period is a seam on <see cref="SessionEnvironment"/> and the tests below pass
/// hundreds of milliseconds -- but a test-friendly value leaking into the product
/// would be undetectable in every other signal, because a browser closed too
/// eagerly is silently relaunched by the next call. So the shipped constant is
/// asserted directly, and so is the fact that no file in <c>src/</c> assigns the
/// seam.
/// </para>
/// </remarks>
internal sealed partial class BrowserIdleTimerTests
{
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// The nominal idle period every in-process arm in this file is configured
    /// with.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Nominal: nothing waits for it.</b> Every arm that uses it drives a
    /// <see cref="ManualClock"/>, so this is the unit the test advances in and
    /// not a duration anything sleeps for. Its actual value is therefore
    /// irrelevant to how long the suite takes, and a reader should not read
    /// "800 ms" as a promptness decision. The last arm that let a real timer run
    /// against it was converted on 2026-08-18.
    /// </remarks>
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromMilliseconds(800);

    /// <summary>How long a real browser tree gets to go away once it has been asked to.</summary>
    private static readonly TimeSpan TeardownPatience = TestDefaults.ProcessHang;

    /// <summary>
    /// What the session's log says when the idle close's own <c>browser_close</c>
    /// ran out its cap, and nothing else says.
    /// </summary>
    private const string UnansweredIdleClose = "did not answer its idle close within";

    /// <summary>
    /// The shipped period is what §C says, and nothing in the product moves it.
    /// </summary>
    /// <remarks>
    /// <b>Two assertions, because either alone is satisfiable by a broken
    /// build.</b> The constant could be right while <c>Program.cs</c> passed
    /// something else; the seam could be untouched while the constant had drifted
    /// to twenty seconds during a debugging session. The source scan is the half
    /// that cannot be argued with: <see cref="SessionEnvironment"/> is the only
    /// file in <c>src/</c> allowed to name the property at all.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheShippedIdlePeriodIsTenMinutesAndNothingInTheProductChangesIt()
    {
        await Assert.That(BrowserIdleTimer.DefaultIdlePeriod).IsEqualTo(TimeSpan.FromMinutes(10));

        // An ASSIGNMENT anywhere in the product, not a mention: `SessionManager`
        // reads the seam on every open and must, and the property's own
        // declaration initialises it from the constant above. What must not
        // exist is a second value for it in shipped code.
        var offenders = RepositoryLayout.ProductSourceFiles
            .Where(file => IdlePeriodAssignment().IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName))
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    /// <summary>
    /// The shipped clock is the real one, and nothing in the product replaces it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same guard the idle period carries, for the seam added beside
    /// it</b>, and it matters more than the period's does. A test-friendly period
    /// leaking into a shipped build closes browsers too eagerly, which the next
    /// call silently repairs; a test clock leaking in stops the only timer in the
    /// product from <i>ever</i> firing, and a browser that is never closed is
    /// indistinguishable from a browser in use. Nothing anywhere would go red.
    /// </para>
    /// <para>
    /// An ASSIGNMENT, not a mention: <c>SessionManager</c> reads the seam on every
    /// open and must, and the property's own declaration initialises it from
    /// <see cref="TimeProvider.System"/> -- neither matches, because both have
    /// something other than whitespace between the name and the <c>=</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheShippedClockIsTheRealOneAndNothingInTheProductReplacesIt()
    {
        // The declaration itself, read out of the product's own source. A
        // constructed SessionEnvironment would need a provisioner, a payload and
        // a paths object to answer one question about a default, and the
        // assignment scan below cannot see an initialiser -- `{ get; init; } =`
        // has something other than whitespace before the `=` and never matches,
        // which is exactly what keeps the scan from flagging the declaration.
        var seam = RepositoryLayout.ProductSourceFiles
            .Single(file => file.Name is "SessionEnvironment.cs");

        await Assert.That(await File.ReadAllTextAsync(seam.FullName))
            .Contains("public TimeProvider Clock { get; init; } = TimeProvider.System;");

        // ⚠️ THE POSITIVE CONTROL, added 2026-09-16, and it is the reason this
        // arm moved at all. The pattern below carried a literal 0x08 where a
        // word-boundary escape was meant -- so it asked for a BACKSPACE before
        // `Clock` and could not match anything in any file. The offender list
        // below was empty by construction and not by the product being
        // clean, and a scan that cannot match is a green test forever. The match
        // is therefore asserted before the emptiness is believed.
        await Assert.That(ClockAssignment().IsMatch("            Clock = new ManualClock(),")).IsTrue();

        // And the two shapes it must go on refusing. The declaration's own
        // initialiser has `{ get; init; }` between the name and the `=`; a
        // longer identifier ending in the seam's name is what the word boundary
        // is there for, and it is the half that was doing nothing.
        await Assert.That(ClockAssignment().IsMatch("    public TimeProvider Clock { get; init; } = TimeProvider.System;")).IsFalse();
        await Assert.That(ClockAssignment().IsMatch("        var manualClock = new ManualClock();")).IsFalse();

        var offenders = RepositoryLayout.ProductSourceFiles
            .Where(file => ClockAssignment().IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName))
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    /// <summary>
    /// The tool the timer calls is upstream's, by the name upstream publishes, and
    /// since 2026-10-08 BrowserAI is the only one that calls it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An upstream rename must turn the build red instead of turning the
    /// timer into a no-op.</b> A <c>tools/call</c> naming a tool that no longer
    /// exists is answered with an error the timer logs and nothing else notices --
    /// and the browser then stays open forever, which is exactly the defect this
    /// step exists to remove, restored silently by a version bump.
    /// </para>
    /// <para>
    /// ⚠️ <b>Renamed 2026-10-08 (previously
    /// <c>TheCloseToolIsUpstreamsOwnAndIsCallableInEveryMode</c>)</b>, and the second
    /// half inverted: F1 a denies <c>browser_close</c> at the door and withholds it
    /// from the tool list, because <c>browserai_close</c> replaces it. The idle close,
    /// the shutdown and <c>browserai_close</c> send it to the session's own child,
    /// which no verdict is asked about, so the deny costs the timer nothing; the arms
    /// below that watch the double receive it are what hold that.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCloseToolIsUpstreamsOwnAndOnlyBrowserAiCallsIt()
    {
        await Assert.That(UpstreamSurface.DefaultSurface()).Contains(LiveSession.BrowserCloseTool);

        // Callable at all: the timer bypasses the decision anyway -- it is
        // BrowserAI calling its own child, not a caller calling a tool --
        // but a close the policy refused would mean a session whose browser can
        // never be closed, and this is where that would be decided.
        //
        // Corrected 2026-08-18 (previously this also asserted the tool carried a
        // row in `SessionToolPolicy.Classification`). There is no classification
        // table: it was part of the (tool, mode) permission matrix, which was
        // never a boundary against the caller and was removed. What the tool
        // must still be is upstream's own and callable, which is what remains.
        //
        // Corrected 2026-08-18 again (previously a loop over `SessionModes.All`
        // asking `Decide(tool, mode)`). `Decide` no longer takes a mode: what it
        // refuses is refused everywhere, so a per-mode loop here would ask the
        // same question three times and read as coverage it is not.
        //
        // ⚠️ Corrected 2026-08-26: `Decide` is `ToolVerdicts`' and reads
        // tool-verdicts.json, which is why this asks the SHIPPED file and not
        // a constant -- and under deny-by-default the claim is stronger
        // than it was. The idle timer calls this tool itself, so a build that
        // shipped a verdicts file with no row for it would close no browser and
        // report nothing.
        //
        // ⚠️ Inverted 2026-10-08, F1 a (previously "IsAllowed ... IsTrue" and
        // "the timer's own tool must not be the withheld one"): a caller may not
        // call it, and it is not in the surface a caller sees. The timer goes on
        // closing browsers because it never asks the door.
        await Assert.That(RepositoryVerdicts.Committed.Decide(LiveSession.BrowserCloseTool).IsAllowed).IsFalse();
        await Assert.That(RepositoryVerdicts.Committed.IsWithheldFromTheSurface(LiveSession.BrowserCloseTool)).IsTrue();
    }

    /// <summary>
    /// A session driven continuously is never closed; one that goes quiet is
    /// closed exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both halves in one test, against one rig, because separately either is
    /// vacuous.</b> "It closes when idle" passes against a timer that closes
    /// unconditionally; "it never closes while busy" passes against a timer that
    /// never fires. The pair is the behaviour. The child is a double here -- what
    /// is under test is the decision, and the decision is the same code against a
    /// real browser, which the next test drives.
    /// </para>
    /// <para>
    /// ⚠️ <b>Rewritten 2026-08-17 against a <see cref="ManualClock"/> (previously
    /// a wall-clock driving loop with a starvation detector and four retries).</b>
    /// The old shape asserted that it had achieved a rate -- every gap between two
    /// in-process round trips under the 800 ms period -- and on a machine running
    /// every test at once it could not: one round trip measured <b>1.51 s</b> and
    /// <b>2.27 s</b>, so all four attempts starved and the test failed <b>five
    /// times in twenty runs</b> with the product correct every time. Before that
    /// it had already cost a widened budget and the retry itself. A wall clock
    /// cannot distinguish "the timer fired early" from "this thread was not
    /// scheduled", and no amount of retrying makes it able to.
    /// </para>
    /// <para>
    /// <b>Now nothing here reads a wall clock.</b> The clock moves only when this
    /// test moves it, so <i>one tick short of the period</i> means exactly that:
    /// three calls, each followed by an advance of one tick less than a whole
    /// period, is <b>three periods of elapsed time with no close</b> -- which is
    /// impossible unless every call re-armed the timer. Then the clock is moved
    /// past the deadline and the close must arrive.
    /// </para>
    /// <para>
    /// <b>The one thing that is not observable from here</b> is the instant the
    /// proxy releases its in-flight scope: <c>BrowserProxy</c> holds it across the
    /// answer, so it may still be open when this test's round trip returns. A
    /// timer that fires then re-arms for a whole period, correctly, so the quiet
    /// half advances the clock until the close lands instead of assuming one
    /// advance is enough. That is not a retry against flakiness -- every advance
    /// that meets an outstanding call is the product keeping its promise, and the
    /// count assertion at the end is what makes it a bounded claim.
    /// </para>
    /// <para>
    /// ⚠️ <b>What "closed" means moved on 2026-10-03, P4 b.</b> <i>Corrected
    /// 2026-10-03 (previously the close was read off the double receiving a
    /// <c>browser_close</c>).</i> The close ends the session's whole child now,
    /// so the double's read loop stopping is the event, and a
    /// <c>browser_close</c> reaching it would be the old close coming back.
    /// ⚠️ <i>Corrected again the same day, Q367 a (previously "and a
    /// <c>browser_close</c> reaching it would be the old close coming back").</i>
    /// The close asks the browser to close itself before it ends the child, so
    /// exactly one <c>browser_close</c> reaches the double, and the child ending is
    /// still the event.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryToolCallResetsTheTimerAndOnlyASessionThatGoesQuietIsClosed()
    {
        var clock = new ManualClock();

        await using var rig = RigSessionEnvironment.Create(
            configure: child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            browserIdlePeriod: ShortPeriod,
            clock: clock);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var session = Path.Combine(rig.Root, "driven");

        _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browserai_init",
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the session driven continuously",
                ["headed"] = false,
                ["transcript"] = false,
                ["captureNetwork"] = false,
                ["idleMinutes"] = 10,
            },
        });

        var child = rig.SessionChildren[^1];

        // Three whole periods of elapsed time, in three calls. Each advance stops
        // one tick short of the deadline the call just set, so a timer that
        // ignored the call would have fired on the second advance.
        const int Calls = 3;

        for (var call = 1; call <= Calls; call++)
        {
            _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
            {
                ["name"] = "browser_navigate",
                ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = session, ["why"] = "the suite exercising this call" },
            });

            clock.AdvanceTicks(ShortPeriod.Ticks - ManualClock.OneTick);

            await Assert.That(child.HasStopped).IsFalse();
        }

        // Every frame the caller has had so far, for the silence asserted below.
        var framesBefore = harness.Client.FramesReceived.Count;

        // And now nothing at all.
        await WaitUntilAsync(
            () =>
            {
                clock.Advance(ShortPeriod);
                return child.HasStopped;
            },
            TestDefaults.InProcessHang,
            "the idle close never ended the session's child, however far the clock was moved");

        // One close, and only one, however long it is left: the timer is one-shot
        // and stays disarmed until the next call. Twenty periods is twenty
        // chances for a timer that wrongly re-armed, and the round trip after
        // them is a real exchange through the same server -- so anything the
        // close path had queued has been through by the time the count is read.
        clock.Advance(ShortPeriod * 20);

        _ = await harness.Client.RoundTripAsync("tools/list");

        // The evidence a caller would see, which is none: the close is
        // BrowserAI's own and no frame about it reaches the client, so the one
        // frame since the count above is the answer to the list.
        //
        // ⚠️ Corrected 2026-10-03 (previously no frame anywhere could contain
        // the close tool's name). `browserai_resume`'s own description names
        // `browser_close` since that day, as the way to apply a setting while a
        // browser is up, so the name is in every tool list a caller is sent.
        await Assert.That(harness.Client.FramesReceived.Count).IsEqualTo(framesBefore + 1);

        await WaitUntilAsync(
            () => RecordedSession.LogOf(session).Any(row => row.Tool == LiveSession.BrowserCloseTool && row.Outcome != SessionStore.InFlight),
            TestDefaults.InProcessHang,
            "the idle close never settled its row");

        await Assert.That(RecordedSession.LogOf(session).Count(row => row.Tool == LiveSession.BrowserCloseTool)).IsEqualTo(1);

        // ⚠️ AND IT ASKED THE BROWSER TO CLOSE ITSELF, ONCE, Q367 a. Corrected
        // 2026-10-03 (previously "AND IT NEVER ASKED THE CHILD TO CLOSE ANYTHING,
        // P4 b", asserting no `browser_close` at all). The close ends the whole
        // child still, and asks the browser first, so it flushes what it holds;
        // a close that meets an armed debugger pause and never answers is cut off
        // by the cap, which the arms below hold.
        await Assert.That(child.ToolCallsReceived.Count(tool => tool == LiveSession.BrowserCloseTool)).IsEqualTo(1);
    }

    /// <summary>
    /// An autonomous idle close writes a row, so the session's own log says
    /// BrowserAI closed the browser.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It wrote nothing at all until 2026-08-26, and P2 is what made that a
    /// defect, not a gap.</b> The close talks to the child directly and
    /// never touched <c>Lock</c>; while <c>browserai.log</c> existed the event
    /// survived there, and that file is gone. <c>browserai_catch_up</c> tells its
    /// reader the log is <i>"WHAT WAS DONE HERE -- the session's own log ... This is
    /// what BrowserAI did"</i>, an autonomous browser close is something
    /// BrowserAI did, and it was invisible -- so a reader saw an unexplained gap
    /// in wall-clock time, and the next call silently relaunched a browser.
    /// </para>
    /// <para>
    /// <b>The row is written the way every other row is</b>: <c>in-flight</c>
    /// before the call is forwarded, settled from the child's own answer. That
    /// ordering is not decoration here either -- a close that hangs or meets a
    /// dead child leaves the row unsettled, which is exactly the state
    /// <c>catch_up</c> renders as <i>"no answer was recorded"</i>.
    /// </para>
    /// <para>
    /// <b>The <c>why</c> is BrowserAI's own and it says so.</b> Every other row
    /// carries a caller's sentence; this one has no caller, so it names the timer
    /// and the period instead of borrowing a voice it does not have.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnIdleCloseWritesItsOwnRowSoTheLogSaysBrowserAiClosedTheBrowser()
    {
        var clock = new ManualClock();

        await using var rig = RigSessionEnvironment.Create(
            configure: child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            browserIdlePeriod: ShortPeriod,
            clock: clock);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var session = Path.Combine(rig.Root, "closed-by-the-timer");

        _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browserai_init",
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "a session left to go idle",
                ["headed"] = false,
                ["transcript"] = false,
                ["captureNetwork"] = false,
                ["idleMinutes"] = 10,
            },
        });

        // One forwarded call, which is what arms the timer: a session that has
        // never been driven has no browser to close.
        _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = session, ["why"] = "the call that arms the timer" },
        });

        var child = rig.SessionChildren[^1];

        await WaitUntilAsync(
            () =>
            {
                clock.Advance(ShortPeriod);
                return child.HasStopped;
            },
            TestDefaults.InProcessHang,
            "the idle close never ended the session's child, however far the clock was moved");

        // The row is settled on the way back, so the read waits for the outcome
        // and not for a duration -- bounded by the suite's own hang detector
        // and by no number written here.
        await WaitUntilAsync(
            () => RecordedSession.LogOf(session).Any(row =>
                row.Tool == LiveSession.BrowserCloseTool && row.Outcome != SessionStore.InFlight),
            TestDefaults.InProcessHang,
            "the idle close never settled its row");

        var closes = RecordedSession.LogOf(session)
            .Where(row => row.Tool == LiveSession.BrowserCloseTool)
            .ToList();

        await Assert.That(closes.Count).IsEqualTo(1);
        await Assert.That(closes[0].Outcome).IsEqualTo(SessionStore.Successful);

        // It names itself. A row whose `why` could have been written by a caller
        // is a row that reads as a caller's call.
        await Assert.That(closes[0].Why).Contains("idle");
        await Assert.That(closes[0].Why).Contains("BrowserAI");

        // ⚠️ AND IT NAMES THE WAY BACK AND PROMISES NOTHING IT CANNOT KEEP,
        // added 2026-10-03. The sentence it replaces said "Nothing was lost --
        // the next call relaunches the browser and answers normally", and a
        // field report met the next call running on about:blank.
        await Assert.That(closes[0].Why).Contains(SessionToolSurface.Resume);
        await Assert.That(closes[0].Why).DoesNotContain("Nothing was lost");

        // And the reader a caller actually uses sees it, which is the whole
        // finding: catch_up's log half is what says "this is what BrowserAI did".
        var text = TextOf(await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.CatchUp,
            ["arguments"] = new JsonObject { ["session"] = session, ["why"] = "the suite reading back what the idle close wrote" },
        }));

        await Assert.That(text).Contains(LiveSession.BrowserCloseTool);

        // ⚠️ THE POSITIVE CONTROL, and it is what stops this passing against a
        // row written on every session: the caller's own navigation is still
        // there beside it, and the close is not confused with it.
        await Assert.That(text).Contains("the call that arms the timer");
        await Assert.That(RecordedSession.LogOf(session).Count(row => row.Tool == "browser_navigate")).IsEqualTo(1);
    }

    /// <summary>
    /// A single call that outlives the whole period does not have the browser
    /// closed underneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the half of "reset by any tool call" that no wall clock can
    /// make flaky</b>, and it is the case that actually matters in production: a
    /// navigation to a slow site, a download, a <c>browser_wait_for</c>. The call
    /// is held open by the double until this test releases it, so the session is
    /// outstanding for three periods by construction rather than by timing, and a
    /// timer that ignored in-flight calls would close the browser under a caller
    /// that was mid-request.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously a real 800 ms period and
    /// <c>await Task.Delay(ShortPeriod * 3)</c>).</b> The sentence above claimed
    /// "by construction rather than by timing" while the test slept for 2.4 s and
    /// hoped the request had reached the child inside them -- which is a guess at
    /// how long a round trip takes, and on a starved machine it is the wrong
    /// guess in the direction that makes the whole thing vacuous: an advance made
    /// before the call was in flight would fire the timer legitimately. Both
    /// halves are events now. The call being in flight is read off the child, and
    /// "three periods" is three periods of a clock this test moves by hand.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallThatOutlivesThePeriodIsNotClosedUnderneath()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualClock();

        await using var rig = RigSessionEnvironment.Create(
            configure: child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { HoldUntil = held.Task };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            browserIdlePeriod: ShortPeriod,
            clock: clock);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var id = await harness.Client.BeginAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = harness.Session!, ["why"] = "the suite exercising this call" },
        });

        // The event that says the call is really outstanding: the double records
        // the tool name before it starts holding, and the proxy registered its
        // in-flight scope earlier still. Advancing before this point would move
        // the clock past a period during which nothing was in flight, and a close
        // would then be correct -- which is a test that can only fail for the
        // wrong reason.
        await WaitUntilAsync(
            () => harness.Child.ToolCallsReceived.Contains("browser_navigate"),
            TestDefaults.InProcessHang,
            "the held call never reached the child, so nothing was outstanding to protect");

        // Outstanding for three whole periods -- exactly three, because this is
        // the only thing that moves the clock -- and the only thing ending it is
        // the line below.
        clock.Advance(ShortPeriod * 3);

        await Assert.That(harness.Child.HasStopped).IsFalse();

        held.SetResult();

        var answer = await harness.Client.AwaitAsync(id, "browser_navigate");

        await Assert.That(answer.Error).IsNull();

        // And the period restarts from the moment the call was answered, so the
        // close still comes -- a suppressed timer that never re-armed would be
        // the same defect this step exists to remove. Advanced repeatedly, not
        // once: the proxy releases its in-flight scope after the caller's
        // answer is on the wire, so an advance that lands while the release is
        // still in flight re-arms for a whole period, correctly.
        await WaitUntilAsync(
            () =>
            {
                clock.Advance(ShortPeriod);
                return harness.Child.HasStopped;
            },
            TestDefaults.InProcessHang,
            "the timer never re-armed after the held call was answered, however far the clock was moved");
    }

    /// <summary>
    /// The idle close asks the browser to close itself first, waits for the answer
    /// for as long as its cap allows, and ends the child as soon as the browser
    /// has answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q367 a, decided 2026-10-03 by the maintainer, in his words: <i>"Q367 a -
    /// but why just 1 sec.? Why not be very gracefull here?"</i></b> Until then the
    /// idle close ended the child through its stdin with no close first, and the
    /// research had a stdin end lose a store in 1 of 16 Chromium and 1 of 19
    /// Firefox runs, where a <c>browser_close</c> first kept everything, 6 of 6.
    /// </para>
    /// <para>
    /// <b>Generous, and both halves of that are asserted.</b> The double holds the
    /// close open; the clock is stopped one tick short of the cap the close armed,
    /// and the child is still running. Then the double answers and the clock does
    /// not move again, so the child ending can only be the answer ending the wait.
    /// A close that ignored the answer and sat out its whole cap would leave this
    /// arm waiting on the hang detector.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnIdleCloseAsksTheBrowserToCloseItselfAndEndsTheChildOnceItHasAnswered()
    {
        var clock = new ManualClock();
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var rig = RigSessionEnvironment.Create(
            configure: child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour { HoldUntil = answer.Task };
            },
            browserIdlePeriod: ShortPeriod,
            clock: clock);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = harness.Session!, ["why"] = "the call that starts the browser and arms the timer" },
        });

        var child = harness.Child;

        await ClockUntilTheIdleCloseArmsItsCapAsync(clock, child);

        await WaitUntilAsync(
            () => child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool) || child.HasStopped,
            TestDefaults.InProcessHang,
            "the idle close neither asked the browser to close nor ended the child");

        await Assert.That(child.ToolCallsReceived).Contains(LiveSession.BrowserCloseTool);

        // One tick short of the cap the close armed, and the child is still there.
        var left = clock.UntilTheNewestTimerFires();

        await Assert.That(left.HasValue)
            .IsTrue()
            .Because("the cap the idle close armed was no longer running once the close had reached the child, so nothing was waiting for the answer");
        await Assert.That(left!.Value).IsLessThanOrEqualTo(SessionTimes.BrowserCloseCap);

        // ⚠️ AND IT IS THE ONE-MINUTE CAP, D4.1 of 2026-10-04: the loop that fired the
        // close moves the clock a period at a time and stops once the cap is armed, so
        // at most one period has passed since. Planted red against the thirty seconds
        // that stood until that day.
        await Assert.That(left.Value).IsGreaterThanOrEqualTo(SessionTimes.BrowserCloseCap - ShortPeriod);

        clock.AdvanceTicks(left.Value.Ticks - ManualClock.OneTick);

        _ = await harness.Client.RoundTripAsync("tools/list");

        await Assert.That(child.HasStopped).IsFalse();

        // The browser answers, and the clock is not moved again.
        answer.SetResult();

        await WaitUntilAsync(
            () => child.HasStopped,
            TestDefaults.InProcessHang,
            "the idle close did not end the child once the browser had answered its close");

        await Assert.That(child.ToolCallsReceived.Count(tool => tool == LiveSession.BrowserCloseTool)).IsEqualTo(1);
        await Assert.That(child.BrowserIsOpen).IsFalse();
        await Assert.That(harness.Logs.Logged(UnansweredIdleClose)).IsFalse();

        await WaitUntilAsync(
            () => RecordedSession.LogOf(harness.Session!).Any(row =>
                row.Tool == LiveSession.BrowserCloseTool && row.Outcome == SessionStore.Successful),
            TestDefaults.InProcessHang,
            "the idle close never settled its row");
    }

    /// <summary>
    /// An idle close whose <c>browser_close</c> never answers -- an armed debugger
    /// pause -- still ends the child, once its one-minute cap has run out and not
    /// before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The cap exists for this case alone.</b> A pause armed in the browser
    /// parks the next action, a close included: measured 2026-10-03, a
    /// <c>browser_close</c> after an armed pause had no answer after 10 s, 12 of 12
    /// on <c>@playwright/mcp</c> 0.0.82 and 6 of 6 on 0.0.83, and a child whose
    /// stdin closed exited 0.75 to 1.5 s later even while paused. The double holds
    /// the close open, which is the one property of the pause this layer can
    /// reproduce, and the shipped value is asserted here because the suite drives
    /// the cap through the clock and never waits it out.
    /// </para>
    /// <para>
    /// <b>The child is told the close was cancelled</b>, by the close's own request
    /// id, before its stdin is closed: what a caller's cancellation sends, and the
    /// last thing a paused child that will not answer is asked.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnIdleCloseThatIsNeverAnsweredEndsTheChildWhenItsCapRunsOut()
    {
        var clock = new ManualClock();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var rig = RigSessionEnvironment.Create(
            configure: child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour { HoldUntil = never.Task };
            },
            browserIdlePeriod: ShortPeriod,
            clock: clock);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = harness.Session!, ["why"] = "the call that starts the browser and arms the timer" },
        });

        var child = harness.Child;

        await ClockUntilTheIdleCloseArmsItsCapAsync(clock, child);

        await WaitUntilAsync(
            () => child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool) || child.HasStopped,
            TestDefaults.InProcessHang,
            "the idle close neither asked the browser to close nor ended the child");

        await Assert.That(child.ToolCallsReceived).Contains(LiveSession.BrowserCloseTool);

        var left = clock.UntilTheNewestTimerFires();

        await Assert.That(left.HasValue)
            .IsTrue()
            .Because("the cap the idle close armed was no longer running once the close had reached the child, so nothing was waiting for the answer");

        // The one-minute cap, D4.1 of 2026-10-04, and not the thirty seconds before it.
        await Assert.That(left!.Value).IsLessThanOrEqualTo(SessionTimes.BrowserCloseCap);
        await Assert.That(left.Value).IsGreaterThanOrEqualTo(SessionTimes.BrowserCloseCap - ShortPeriod);

        clock.AdvanceTicks(left.Value.Ticks - ManualClock.OneTick);

        _ = await harness.Client.RoundTripAsync("tools/list");

        await Assert.That(child.HasStopped).IsFalse();

        // The cap runs out.
        clock.AdvanceTicks(ManualClock.OneTick);

        await WaitUntilAsync(
            () => child.HasStopped,
            TestDefaults.InProcessHang,
            "the idle close did not end the child once its cap had run out");

        await Assert.That(harness.Logs.Logged(UnansweredIdleClose)).IsTrue();
        await Assert.That(child.MethodsReceived).Contains("notifications/cancelled");

        // And the session is closed the way any idle close leaves it: the row is
        // settled, and the next call is refused with the way back.
        await WaitUntilAsync(
            () => RecordedSession.LogOf(harness.Session!).Any(row =>
                row.Tool == LiveSession.BrowserCloseTool && row.Outcome != SessionStore.InFlight),
            TestDefaults.InProcessHang,
            "the idle close never settled its row");

        var refused = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = harness.Session!, ["why"] = "the call after an idle close the browser never answered" },
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains(SessionToolSurface.Resume);
    }

    // ⚠️ RETIRED 2026-10-04: `AResumeThatMeetsAnIdleCloseStillWaitingEndsTheWaitAtOnce`
    // stood here and held the behaviour the maintainer's warning of that day ruled
    // out -- a resume that met the idle close still waiting ended the wait at once
    // and ended the child through its stdin. Its replacement holds the opposite, that
    // the resume waits for the close: `CloseOrderingTests.AResumeThatMeetsAnIdleCloseStillWaitingWaitsForItAndThenReopens`.

    /// <summary>
    /// Moves the clock a period at a time until the idle close has armed its cap
    /// or the child has stopped, and never once the cap is armed.
    /// </summary>
    /// <remarks>
    /// <b>Asked before the clock moves, every time.</b> The close runs on a
    /// thread of its own once the timer fires, so a loop that moved first and
    /// looked second could run the cap down by a period for every turn it took
    /// the close to arm it; asked first, the most this can move past the cap's
    /// own start is the one advance the close armed it inside. And the arm reads
    /// how far the cap is from firing off the clock itself, so even that cannot
    /// make "one tick short" mean anything else.
    /// </remarks>
    /// <param name="clock">The session's clock.</param>
    /// <param name="child">The session's double.</param>
    /// <returns>A task that completes once the cap is armed or the child has stopped.</returns>
    private static Task ClockUntilTheIdleCloseArmsItsCapAsync(ManualClock clock, FakePlaywrightChild child)
    {
        var timers = clock.TimersCreated;

        return WaitUntilAsync(
            () =>
            {
                if (clock.TimersCreated > timers || child.HasStopped)
                {
                    return true;
                }

                clock.Advance(ShortPeriod);
                return clock.TimersCreated > timers || child.HasStopped;
            },
            TestDefaults.InProcessHang,
            "the idle close neither armed a cap nor ended the child, however far the clock was moved");
    }

    /// <summary>
    /// Against a <b>real</b> browser: idle past the period ends the session's
    /// whole child, node included, the next call is refused with the way back,
    /// and the resume starts a child whose browser answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-03 (previously "idle past the period leaves no
    /// browser process and the node child still running, and the next call
    /// succeeds without ever saying the browser is closed").</b> Under the
    /// maintainer's P4 b the close ends the whole child, and under his P2 a the
    /// next call is refused until <c>browserai_resume</c>. Both halves of the old
    /// pair are now the wrong outcome: a node child still alive is a close that
    /// held 124 MB for nothing, and a call that silently relaunched is the field
    /// report's blank page.
    /// </para>
    /// <para>
    /// <b>Every question about processes is asked of this session's own job</b>,
    /// never of the machine: the suite runs several browsers in parallel and an
    /// image-path scan cannot tell one session's Chromium from another's. The
    /// node child is asked by its own <c>(pid, creation time)</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnIdleSessionEndsItsWholeChildAndTheNextCallIsRefusedUntilResume()
    {
        // A machine that has never been provisioned proves nothing here, so this
        // reports as SKIPPED and not as a pass -- and as a failure under
        // BROWSERAI_RELEASE_RUN, because a release run that never started a
        // browser is the batteries-included premise being silently dead code.
        SuiteEnvironment.RequireProvisionedChromium();

        // ⚠️ A clock this test moves by hand, so the close happens exactly when
        // this test asks for it and at no other moment. See the history of the
        // arm this replaced: a real 3 s period raced every census below.
        var clock = new ManualClock();
        var period = TimeSpan.FromSeconds(3);

        await using var rig = RigSessionEnvironment.Create(
            browserIdlePeriod: period,
            clock: clock,
            realSessionChildren: true);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var navigate = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = harness.Session!, ["why"] = "the suite exercising this call" },
        });

        await Assert.That((bool?)navigate["isError"]).IsNotEqualTo(true);

        var child = rig.RealSessionChildren.Single();
        var node = child.ProcessId!.Value;
        var nodeCreated = ProcessIdentity.CreationTimeOf(node);

        // A real browser really is up, read off the job before anything is
        // closed: a census of a job that is already gone would pass vacuously.
        var members = child.JobProcessIds()
            .Where(pid => pid != node)
            .Select(pid => (Pid: pid, Created: TryCreationTimeOf(pid)))
            .Where(entry => entry.Created is not null)
            .Select(entry => (entry.Pid, Created: entry.Created!.Value))
            .ToList();

        await Assert.That(BrowsersIn(child, rig).Count).IsGreaterThan(0);

        // Now, and only now, the session goes idle. The clock is advanced a period
        // at a time and not once: the proxy releases its in-flight scope after the
        // caller's answer is on the wire, so an advance that lands while a call is
        // still outstanding re-arms for a whole period -- correctly. ⚠️ And it
        // stops the moment the idle close arms its cap (Q367 a, added 2026-10-03):
        // the close asks the real browser to close itself, and a clock moved on
        // from there would run the cap down under a close that is still being
        // answered. What is waited for afterwards is a real process tree ending,
        // which is real time and is bounded by the teardown patience.
        var timers = clock.TimersCreated;

        await WaitUntilAsync(
            () =>
            {
                if (clock.TimersCreated > timers || !ProcessIdentity.IsAlive(node, nodeCreated))
                {
                    return true;
                }

                clock.Advance(period);
                return clock.TimersCreated > timers || !ProcessIdentity.IsAlive(node, nodeCreated);
            },
            TeardownPatience,
            "the idle close never asked the browser to close and the node child was still running, however far the clock was moved");

        await WaitUntilAsync(
            () => !ProcessIdentity.IsAlive(node, nodeCreated),
            TeardownPatience,
            "the node child was still running long after the session went idle");

        // The clock never reached the cap, so nothing but the real browser's own
        // answer to its close can have let the child be ended.
        await Assert.That(harness.Logs.Logged(UnansweredIdleClose)).IsFalse();

        // The browser tree went with it: every process the job held is gone.
        var survivors = new List<int>();

        await WaitUntilAsync(
            () =>
            {
                survivors = [.. members.Where(entry => ProcessIdentity.IsAlive(entry.Pid, entry.Created)).Select(entry => entry.Pid)];
                return survivors.Count is 0;
            },
            TeardownPatience,
            "something in the session's job outlived the idle close");

        await Assert.That(string.Join(", ", survivors)).IsEmpty();

        // And the next call is refused, naming the way back.
        var refused = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = harness.Session!, ["why"] = "the suite calling a closed session" },
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains(SessionToolSurface.Resume);

        var resumed = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Resume,
            ["arguments"] = new JsonObject { ["directory"] = harness.Session!, ["why"] = "the suite resuming the session the timer closed", ["headed"] = false, ["transcript"] = false, ["captureNetwork"] = false, ["idleMinutes"] = 10 },
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true);
        await Assert.That(rig.RealSessionChildren.Count).IsEqualTo(2);

        var again = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = harness.Session!, ["why"] = "the suite exercising this call" },
        });

        await Assert.That((bool?)again["isError"]).IsNotEqualTo(true);
        await Assert.That(BrowsersIn(rig.RealSessionChildren[^1], rig).Count).IsGreaterThan(0);
    }

    /// <summary>
    /// When the timer fires with only the node child in the job, it closes
    /// nothing, writes no row and leaves the session answering.
    /// </summary>
    /// <remarks>
    /// <b>Q327 a, the maintainer's words of 2026-10-03, verbatim: "Q327 a".</b>
    /// Until then a timer that fired with nothing up still wrote a row saying
    /// BrowserAI had closed the browser, and sent a <c>browser_close</c> that
    /// started a browser in order to close it. The double is told that its calls
    /// start no browser, which is the state a crashed or already-closed browser
    /// leaves; the record at debug level is the only trace the timer leaves, and
    /// it is what tells this arm the timer fired at all.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnIdleTimerThatFindsOnlyTheNodeChildClosesNothingAndWritesNoRow()
    {
        var clock = new ManualClock();

        await using var rig = RigSessionEnvironment.Create(
            configure: child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.CallsOpenABrowser = false;
            },
            opensDefaultSession: false,
            browserIdlePeriod: ShortPeriod,
            clock: clock);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var session = Path.Combine(rig.Root, "nothing-up");

        _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browserai_init",
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "a session whose browser is not up when the timer fires",
                ["debug"] = true,
                ["headed"] = false,
                ["transcript"] = false,
                ["captureNetwork"] = false,
                ["idleMinutes"] = 10,
            },
        });

        _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = session, ["why"] = "the call that arms the timer" },
        });

        await WaitUntilAsync(
            () =>
            {
                clock.Advance(ShortPeriod);
                return harness.Logs.Logged("so nothing was closed and no row was written");
            },
            TestDefaults.InProcessHang,
            "the timer never fired, however far the clock was moved");

        var child = rig.SessionChildren[^1];

        await Assert.That(child.HasStopped).IsFalse();
        await Assert.That(child.ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool);
        await Assert.That(RecordedSession.LogOf(session).Any(row => row.Tool == LiveSession.BrowserCloseTool)).IsFalse();

        // And the session was not closed: the next call reaches the same child.
        var again = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = session, ["why"] = "the call after a timer that closed nothing" },
        });

        await Assert.That((bool?)again["isError"]).IsNotEqualTo(true);
        await Assert.That(child.ToolCallsReceived.Count(tool => tool == "browser_navigate")).IsEqualTo(2);
    }

    // ⚠️ DELETED 2026-10-08: `AHeadedSessionIsNeverIdleClosedAndItsConfigTurnsUpstreamsTimeoutOff`,
    // which held Q326 a of 2026-10-03 -- a headed session never idle-closed, its
    // launch writing upstream's own idle timeout as zero, and a headless launch
    // writing upstream's hour -- over fifty idle periods beside a headless control.
    // E2 of 2026-10-07 reverses it: a visible window closes after its own hour, an
    // agent may set either mode to minutes or never, and no launch writes
    // upstream's hour. `IdleCountdownTests.AVisibleWindowClosesAfterItsOwnHourAndNoLaunchCarriesUpstreamsTimeout`
    // holds the reversal, and `IdleCountdownTests.AnIdleSettingIsTheCountdownsLengthAndNeverMeansNever`
    // the fifty periods with nothing closed, for a session set to never.

    /// <summary>The generated config a launch was given, read back off disk.</summary>
    /// <param name="launch">The launch, as the rig recorded it.</param>
    /// <returns>The config.</returns>
    private static JsonObject ConfigOf(BrowserAI.Protocol.ChildProcessOptions launch)
    {
        var file = launch.Arguments[launch.Arguments.ToList().IndexOf("--config") + 1];

        return JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    }

    /// <summary>
    /// stdin EOF reaps everything: no node child, no browser, and every member
    /// of the job gone.
    /// </summary>
    /// <remarks>
    /// <b>This is the graceful path and it is not the same claim
    /// <c>VerticalSliceTests</c> makes.</b> That one terminates BrowserAI from
    /// outside, so the kernel closing the last job handle is what cleans up. Here
    /// BrowserAI runs its own shutdown -- the session's child gets its stdin
    /// closed, which trips upstream's <c>setupExitWatchdog</c> -- and the
    /// assertion is that the graceful path reaches the same end state without
    /// leaning on the 15-second hard exit at the end of it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task StdinEofTearsDownTheNodeChildTheBrowserAndTheJob()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("idle-eof");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            PublishedSlice.Mcp,
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var session = Path.Combine(scratch.Path, "eof-session");

        _ = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browserai_init",
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the session stdin EOF tears down",
                ["headed"] = false,
                ["transcript"] = false,
                ["captureNetwork"] = false,
                ["idleMinutes"] = 10,
            },
        });

        _ = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = session, ["why"] = "the suite exercising this call" },
        });

        // Read while the browser is up: a job holding only BrowserAI itself
        // would satisfy every assertion below for the wrong reason.
        var members = client.JobProcessIds()
            .Where(pid => pid != client.ProcessId)
            .Select(pid => (Pid: pid, Created: TryCreationTimeOf(pid)))
            .Where(entry => entry.Created is not null)
            .Select(entry => (entry.Pid, Created: entry.Created!.Value))
            .ToList();

        await Assert.That(members.Count).IsGreaterThanOrEqualTo(2);

        // Closing stdin, and nothing else. No kill anywhere on this path.
        var exited = await client.CloseAndWaitForExitAsync(TestDefaults.ProcessHang);

        await Assert.That(exited).IsTrue();

        var survivors = new List<int>();

        await WaitUntilAsync(
            () =>
            {
                survivors = [.. members.Where(entry => ProcessIdentity.IsAlive(entry.Pid, entry.Created)).Select(entry => entry.Pid)];
                return survivors.Count is 0;
            },
            TeardownPatience,
            "something in the job outlived stdin EOF");

        await Assert.That(string.Join(", ", survivors)).IsEmpty();

        // The half a survivor count cannot make. A process that is gone from the
        // table but still holds a mapped file leaves a profile Windows refuses
        // to remove, and that is the difference between "reported dead" and
        // "nothing is left".
        var failures = await ScratchDirectory.RemoveTreeWhenReleasedAsync(
            Path.Combine(session, SessionLayout.ProfileFolderName),
            TeardownPatience);

        await Assert.That(string.Join(Environment.NewLine, failures)).IsEmpty();
    }

    /// <summary>
    /// Killing the client BrowserAI holds a handle on tears everything down
    /// <b>without</b> stdin ever reaching EOF.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole test is arranged around making EOF impossible</b>, because
    /// otherwise EOF would be the explanation and the watcher would be unproven.
    /// A wrapper process starts BrowserAI, duplicates the write end of its stdin
    /// into this test, and is then killed: the parent is gone, the pipe is still
    /// open -- a Windows pipe signals EOF only when its <i>last</i> write handle
    /// closes -- and the only remaining route to a teardown is the
    /// <c>OpenProcess</c> handle BrowserAI holds on its client.
    /// </para>
    /// <para>
    /// That the handle is still ours when BrowserAI exits is asserted, not
    /// argued, with <c>GetHandleInformation</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task KillingTheClientTearsTheSessionDownWithoutWaitingForEof()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("client-liveness");
        using var scope = new JobObjectScope();

        var reportPath = Path.Combine(scratch.Path, "report.json");

        var wrapper = scope.Launch(
            ProbePath,
            scratch.Path,
            [
                "client-parent",
                PublishedSlice.Executable,
                scratch.Path,
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                reportPath,
            ]);

        var wrapperCreated = ProcessIdentity.CreationTimeOf(wrapper.Id);

        // ⚠️ Read through ProbeReport and not File.Exists plus
        // File.ReadAllTextAsync, corrected 2026-08-19 after a full-suite run
        // failed here. `File.Exists` is true the instant the NAME appears, which
        // is before the writer has finished with it: the read was refused as a
        // sharing violation once in three consecutive runs, and a read arriving
        // one instant later would have parsed a truncated report and failed on
        // an assertion about the product instead. ProbeReport exists for exactly
        // this, opens FileShare.ReadWrite | FileShare.Delete, and reports a
        // timeout as a timeout; the probe now publishes by rename as the other
        // two already did.
        var report = (JsonObject)await ProbeReport.ReadAsync(reportPath, TestDefaults.ProcessHang);

        // Handed to this process by the wrapper, and this process's to close.
        var standardInput = (nint)(long)report["standardInputHandle"]!;
        var job = (nint)(long)report["jobHandle"]!;

        try
        {
            await Assert.That((bool)report["navigated"]!).IsTrue();

            // ⚠️ The wrapper really is BrowserAI's parent, asserted, not
            // assumed. A watcher pointed at the wrong process fires at the wrong
            // moment and looks identical in every other signal -- which is
            // exactly what happened the first time this test ran.
            await Assert.That((int)report["wrapperPid"]!).IsEqualTo(wrapper.Id);

            var browserAi = (int)report["browserAiPid"]!;
            var browserAiCreated = ProcessIdentity.CreationTimeOf(browserAi);

            var members = report["jobPids"]!.AsArray()
                .Select(node => (int)node!)
                .Where(pid => pid != browserAi)
                .Select(pid => (Pid: pid, Created: TryCreationTimeOf(pid)))
                .Where(entry => entry.Created is not null)
                .Select(entry => (entry.Pid, Created: entry.Created!.Value))
                .ToList();

            // BrowserAI, its node child, a browser and its helpers.
            await Assert.That(members.Count).IsGreaterThanOrEqualTo(2);

            // ⚠️ The assertion that stops this test passing for the wrong
            // reason. Everything below observes BrowserAI going away; without
            // this line, a BrowserAI that had already died -- of a crash, of a
            // watcher pointed at the wrong process, of anything -- would satisfy
            // it instantly. Found by reading the log of a run that passed in
            // three seconds, 2026-08-16, not by the test failing.
            await Assert.That(ProcessIdentity.IsAlive(browserAi, browserAiCreated)).IsTrue();

            // The event under test. TerminateProcess on the wrapper: it runs no
            // code afterwards, so nothing it does can be the explanation.
            ProcessIdentity.Terminate(wrapper.Id, wrapperCreated);

            await WaitUntilAsync(
                () => !ProcessIdentity.IsAlive(browserAi, browserAiCreated),
                TeardownPatience,
                "BrowserAI outlived the client it holds a handle on, with its stdin still open");

            // ⚠️ Asserted at the moment BrowserAI's exit is observed, not
            // afterwards: this is the claim that the exit was NOT stdin EOF. A
            // handle this process still holds is a write end that never closed.
            await Assert.That(HandleIsOurs(standardInput)).IsTrue();

            var survivors = new List<int>();

            await WaitUntilAsync(
                () =>
                {
                    survivors = [.. members.Where(entry => ProcessIdentity.IsAlive(entry.Pid, entry.Created)).Select(entry => entry.Pid)];
                    return survivors.Count is 0;
                },
                TeardownPatience,
                "the session's tree outlived the client");

            await Assert.That(string.Join(", ", survivors)).IsEmpty();
        }
        finally
        {
            // The job first: closing it is what reaps anything an assertion left
            // behind, and it is the containment net the wrapper handed over.
            NativeHandle.Close(job);
            NativeHandle.Close(standardInput);
        }
    }

    /// <summary>
    /// The watch is a handle on the client, signalled by its exit -- never a ping
    /// and never a poll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The prohibition is asserted as well as the behaviour, because the
    /// behaviour cannot distinguish them.</b> A poll every 200 ms and a kernel
    /// wait both look like "it noticed"; what tells them apart is that there is
    /// no <c>ping</c> anywhere in the product -- and there could not be, since MCP
    /// removed the method at protocol revision <c>2026-07-28</c>, so a
    /// ping-shaped watcher would be watching for an answer that a conforming
    /// client is entitled never to give.
    /// </para>
    /// <para>
    /// <b>It fires once.</b> A wait registered <c>executeOnlyOnce</c> that fired
    /// repeatedly would tear a process down more than once, which is harmless
    /// here and would not be in the callback anyone writes next.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClientWatchIsAHandleRatherThanAPingAndFiresExactlyOnce()
    {
        // ⚠️ THE RELAY'S PROBE OF ITS OWN BACKGROUND IS NOT A WATCH ON A CLIENT --
        // 2026-10-08, D10 b. Corrected that day (previously no file of the product
        // could name a ping): the relay asks the background whether it is alive with
        // MCP's own ping while a call is outstanding, between two BrowserAI processes
        // that speak revision 2025-11-25, where the method exists. Every other file
        // still may not, and the relay's two files are named so that a third file
        // that pings is a red build.
        string[] relaysProbe =
        [
            Path.Combine("src", "BrowserAI", "Relay", "RelayEngine.Background.cs"),
            Path.Combine("src", "BrowserAI", "Relay", "RelayEngine.Client.cs"),
        ];

        var pinging = RepositoryLayout.ProductSourceFiles
            .Where(file => File.ReadAllText(file.FullName) is var text
                && (text.Contains("\"ping\"", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("RequestMethods.Ping", StringComparison.Ordinal)
                    || text.Contains("PingAsync", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName))
            .Where(file => !relaysProbe.Contains(file, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join(Environment.NewLine, pinging)).IsEmpty();

        using var scope = new JobObjectScope();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => _ = builder.AddProvider(logs));

        // cmd.exe with no arguments reads its stdin forever, and the scope holds
        // the write end -- so it stays alive without a timer or a script, exactly
        // as PlantedProcess relies on.
        var client = scope.Launch(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Path.GetTempPath());

        var created = ProcessIdentity.CreationTimeOf(client.Id);
        var fires = 0;

        // The creation time is passed and not left to be assumed: since
        // 2026-08-18 the watcher proves the pid is the process it was told about
        // before it arms anything. ProcessLivenessTests covers the refusals.
        using var watcher = ClientLivenessWatcher.ForProcess(
            client.Id,
            created,
            () => Interlocked.Increment(ref fires),
            factory.CreateLogger("watch"))!;

        await Assert.That(watcher).IsNotNull();
        await Assert.That(watcher.ProcessId).IsEqualTo(client.Id);
        await Assert.That(watcher.HasFired).IsFalse();

        ProcessIdentity.Terminate(client.Id, created);

        await WaitUntilAsync(
            () => Volatile.Read(ref fires) > 0,
            TestDefaults.ProcessHang,
            "the watch never fired after the process it holds a handle on was terminated");

        await Assert.That(watcher.HasFired).IsTrue();

        // Left alone for a while: a registration that re-armed would show here.
        await Task.Delay(500);

        await Assert.That(Volatile.Read(ref fires)).IsEqualTo(1);
    }

    /// <summary>Every browser process in one session's job, matched by full image path.</summary>
    private static List<int> BrowsersIn(BrowserAI.Proxy.ChildConnection child, RigSessionEnvironment rig)
    {
        var members = child.JobProcessIds().ToHashSet();

        return [.. BrowserProcesses.RunningFrom(rig.Environment.Paths.BrowsersDirectory)
            .Where(entry => members.Contains(entry.ProcessId))
            .Select(entry => entry.ProcessId)];
    }

    private static long? TryCreationTimeOf(int processId)
    {
        try
        {
            return ProcessIdentity.CreationTimeOf(processId);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Exited between the job reporting it and this call. Its pid is then
            // meaningless, and recording it would make the survivor check act on
            // a number that may be reused.
            return null;
        }
    }

    private static bool HandleIsOurs(nint handle) => NativeHandle.IsValid(handle);

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan patience, string whatWentWrong)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > patience)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds:F1} s.");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>
    /// An assignment to the idle-period seam, as opposed to a mention of it.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"BrowserIdlePeriod\s*=[^=]")]
    private static partial System.Text.RegularExpressions.Regex IdlePeriodAssignment();

    /// <summary>
    /// An assignment to the clock seam, as opposed to a mention of it.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\bClock\s*=[^=]")]
    private static partial System.Text.RegularExpressions.Regex ClockAssignment();

}
