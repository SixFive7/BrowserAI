// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What a session does once its browser has been closed, what a resume applies
/// and when, and what a shutdown does to every browser at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's decisions of 2026-10-03, verbatim: "p2 a / p3 b / p4 b",
/// "Q324 your recommendation", and "p7 b + e and if e is impossible or
/// difficult c".</b> After the idle close or the caller's own
/// <c>browser_close</c>, every forwarded call is refused until
/// <c>browserai_resume</c>, with a sentence that says what was kept and what was
/// lost; a resume applies its per-run settings only when no browser is up; and a
/// shutdown asks every open browser to close itself, all at once and bounded,
/// before it ends the children.
/// </para>
/// <para>
/// <b>The layer is the in-process rig</b> except where a real browser is the
/// subject, and the rig answers <i>is a browser up</i> from the double's own
/// state (<see cref="FakePlaywrightChild.BrowserIsOpen"/>), because a double has
/// no job for the kernel to count. The real-browser arm says so.
/// </para>
/// </remarks>
internal sealed class SessionCloseTests
{
    /// <summary>What the double answers a navigation with.</summary>
    private const string NavigateResult = """{"content":[{"type":"text","text":"Page URL: data:text/html,<h1>ok</h1>"}]}""";

    /// <summary>The page Chromium opens when it restores nothing.</summary>
    private const string NewTabPage = "chrome://new-tab-page/";

    /// <summary>The nominal idle period of the arms that drive the timer; nothing waits for it.</summary>
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// After the idle close, the next call is refused with a sentence that names
    /// <c>browserai_resume</c> and the period, says nothing was run and says what
    /// was kept and lost -- and the resume starts a new child that answers.
    /// </summary>
    /// <remarks>
    /// <b>What this replaces is a call that silently ran on a blank page.</b> A
    /// field report of 2026-10-01 met it: after ten idle minutes the next call
    /// relaunched a browser on <c>about:blank</c> and answered as though nothing
    /// had happened.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AfterTheIdleCloseEveryCallIsRefusedUntilResumeAndTheResumeStartsANewChild()
    {
        var clock = new ManualClock();

        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult },
            opensDefaultSession: false,
            browserIdlePeriod: ShortPeriod,
            clock: clock);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "idle-closed");
        var location = SessionPath.For(directory).FullPath;

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session left to go idle",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser and arms the timer");

        var first = sessions.SessionChildren[0];

        await WaitUntilAsync(
            () =>
            {
                clock.Advance(ShortPeriod);
                return first.HasStopped;
            },
            "the idle close never ended the session's child, however far the clock was moved");

        var refused = await NavigateAsync(rig, directory, "the call that meets a closed session");
        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(text).StartsWith("'browser_navigate' was not run: nothing was sent to the browser.");
        await Assert.That(text).Contains($"Call {SessionToolSurface.Resume} with directory='{location}' first");
        await Assert.That(text).Contains(SessionErrors.Duration(ShortPeriod));
        await Assert.That(text).Contains("Kept:");
        await Assert.That(text).Contains("Lost:");

        // Nothing reached a child: the closed one is gone and no other was
        // started, which is the difference between a refusal and a relaunch.
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);
        await Assert.That(first.ToolCallsReceived.Count(tool => tool == "browser_navigate")).IsEqualTo(1);

        // The refusal is in the record, as every refused call is.
        await Assert.That(RecordedSession.LogOf(directory).Any(row =>
            row.Tool == "browser_navigate" && row.Outcome == SessionStore.Failed)).IsTrue();

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming a session the timer closed",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(resumed)).Contains("BrowserAI closed this session's browser at");
        await Assert.That(TextOf(resumed)).Contains(SessionManager.WhatTheFirstBrowserCallReopens);

        // The connection made the session, and a resume that opened it again
        // must not tell it otherwise.
        await Assert.That(TextOf(resumed)).DoesNotContain("this connection did not create");

        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);

        var after = await NavigateAsync(rig, directory, "the call the new child answers");

        await Assert.That((bool?)after["isError"]).IsNotEqualTo(true);
        await Assert.That(sessions.SessionChildren[1].ToolCallsReceived).Contains("browser_navigate");
    }

    /// <summary>
    /// <c>browserai_close</c> ends the session's browser the way the idle close does
    /// and keeps the session: BrowserAI's own clean close goes to the browser, the
    /// child is ended, the answer says what was kept, and every later call is refused
    /// until a resume, with the reason naming who closed it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>F1 a, decided 2026-10-08 by the maintainer</b>: one tool of BrowserAI's own,
    /// <c>browserai_close</c>, and Playwright's <c>browser_close</c> denied. Until then
    /// this arm drove <c>browser_close</c> itself (P3 b), whose name and description are
    /// Playwright's and say it closes the page.
    /// </para>
    /// <para>
    /// <b>Planted red on 2026-10-08</b> against the tree with <c>browserai_close</c>
    /// listed and not answered: the call was refused as no BrowserAI session tool and
    /// the double never received a close.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task BrowserAiCloseEndsTheBrowserKeepsTheSessionAndSaysWhoClosedIt()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour
                {
                    RawResult = """{"content":[{"type":"text","text":"### Result\nNo open tabs. Navigate to a URL to create one."}]}""",
                };
            },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "closed-by-the-agent");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose agent closes its browser",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser");

        var closed = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing the browser it opened",
        });

        // BrowserAI's own answer, and not upstream's: the close is BrowserAI's.
        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(TextOf(closed));
        await Assert.That(TextOf(closed)).IsEqualTo(SessionManager.ClosedByTheAgent);

        var first = sessions.SessionChildren[0];

        await Assert.That(first.ToolCallsReceived.Count(tool => tool == LiveSession.BrowserCloseTool)).IsEqualTo(1);
        await WaitUntilAsync(() => first.HasStopped, "browserai_close never ended the session's child");

        // The row is the call's own, settled as answered.
        await Assert.That(RecordedSession.LogOf(directory).Any(row =>
            row.Tool == SessionToolSurface.Close && row.Outcome == SessionStore.Successful)).IsTrue();

        var refused = await NavigateAsync(rig, directory, "the call after the agent's own close");

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains($"by a {SessionToolSurface.Close} call from this client, which gave the reason \"the suite closing the browser it opened\"");
        await Assert.That(TextOf(refused)).Contains(SessionToolSurface.Resume);

        // The timer's sentence belongs to the timer's close and to nothing else.
        // Corrected 2026-10-08 (previously "no browser call had reached it"), with
        // the idle close's own sentence, E2.
        await Assert.That(TextOf(refused)).DoesNotContain("because no call had named the session for");

        // Nothing reached a child.
        await Assert.That(first.ToolCallsReceived.Count(tool => tool == "browser_navigate")).IsEqualTo(1);

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming after its own close",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(resumed)).Contains($"by a {SessionToolSurface.Close} call");
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);

        var after = await NavigateAsync(rig, directory, "the call the new child answers");

        await Assert.That((bool?)after["isError"]).IsNotEqualTo(true);
    }

    /// <summary>
    /// <c>browserai_close</c> with no browser up closes the session all the same,
    /// without starting a browser in order to close it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Forwarded, a close would start a browser in order to close it</b>: measured
    /// 2026-10-03 at <c>@playwright/mcp</c> 0.0.82 and 0.0.83, 8 to 9 browser
    /// processes, a capture rewritten empty and a descriptor nothing reaps. So no
    /// <c>browser_close</c> reaches the child.
    /// </para>
    /// <para>
    /// ⚠️ <b>The session is closed, which the caller's own <c>browser_close</c> did not
    /// do</b> (P3 b, previously "closes nothing and changes nothing"). A decision taken
    /// 2026-10-08 for the maintainer's review: after <c>browserai_close</c> a session is
    /// closed, whatever it held, so one sentence describes the tool and the child that
    /// was waiting for a browser is ended too.
    /// </para>
    /// <para>
    /// <b>Planted red on 2026-10-08</b> against the tree with <c>browserai_close</c>
    /// listed and not answered.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task BrowserAiCloseWithNoBrowserUpClosesTheSessionWithoutStartingOne()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "nothing-to-close");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose browser never started",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        var closed = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing a browser that never started",
        });

        var first = sessions.SessionChildren[0];

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(TextOf(closed));
        await Assert.That(TextOf(closed)).IsEqualTo(SessionManager.ClosedWithNoBrowserUp);
        await Assert.That(first.ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool);
        await WaitUntilAsync(() => first.HasStopped, "browserai_close left the child of a session with no browser running");

        // The row is written and settled like any call's.
        await Assert.That(RecordedSession.LogOf(directory).Any(row =>
            row.Tool == SessionToolSurface.Close && row.Outcome == SessionStore.Successful)).IsTrue();

        // And the session is closed: the next call is refused, and nothing new starts.
        var after = await NavigateAsync(rig, directory, "the call after a close that found nothing up");

        await Assert.That((bool?)after["isError"]).IsTrue();
        await Assert.That(TextOf(after)).Contains(SessionToolSurface.Resume);
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);
        await Assert.That(first.ToolCallsReceived).DoesNotContain("browser_navigate");
    }

    /// <summary>
    /// <c>browserai_close</c> with nothing to close is an answer and not a refusal:
    /// a session closed already says why, one whose browser server has ended says
    /// so, and one no BrowserAI holds says it is not open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>F1 a, 2026-10-08.</b> The call asked for a closed session and has one, so
    /// none of the three is an error, and none of them starts a browser or a browser
    /// server. Each says the way back, which is <c>browserai_resume</c>.
    /// </para>
    /// <para>
    /// <b>Planted red on 2026-10-08</b> against the tree with <c>browserai_close</c>
    /// listed and not answered.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task BrowserAiCloseWithNothingToCloseSaysSoAndChangesNothing()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        // Closed already: the second close quotes the first.
        var closedTwice = Path.Combine(sessions.Root, "closed-twice");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = closedTwice,
            ["purpose"] = "the session closed twice",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        _ = await NavigateAsync(rig, closedTwice, "the call that starts the browser");

        _ = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = closedTwice,
            ["why"] = "the first close",
        });

        var again = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = closedTwice,
            ["why"] = "the second close",
        });

        await Assert.That((bool?)again["isError"]).IsNotEqualTo(true).Because(TextOf(again));
        await Assert.That(TextOf(again)).StartsWith("Nothing was done: this session's browser was already closed.");
        await Assert.That(TextOf(again)).Contains($"by a {SessionToolSurface.Close} call from this client, which gave the reason \"the first close\"");
        await Assert.That(TextOf(again)).EndsWith($"{SessionToolSurface.Resume} opens it again.");
        await Assert.That(sessions.SessionChildren[0].ToolCallsReceived.Count(tool => tool == LiveSession.BrowserCloseTool)).IsEqualTo(1);

        // A browser server that ended on its own: nothing to close, and resume says
        // what it restarts.
        var ended = Path.Combine(sessions.Root, "server-ended");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = ended,
            ["purpose"] = "the session whose browser server dies",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await sessions.SessionChildren[^1].DisposeAsync();
        await WaitUntilAsync(() => rig.Logs.Logged("the peer closed its end of the connection"), "the transport never noticed the child had gone");

        var nothingLeft = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = ended,
            ["why"] = "closing a session whose browser server has gone",
        });

        await Assert.That((bool?)nothingLeft["isError"]).IsNotEqualTo(true).Because(TextOf(nothingLeft));
        await Assert.That(TextOf(nothingLeft)).IsEqualTo(SessionManager.ServerHadAlreadyEnded);

        // A session no BrowserAI holds: its record says it exists, and nothing is open.
        var nobody = Path.Combine(sessions.Root, "held-by-nobody");

        _ = Directory.CreateDirectory(nobody);

        SessionLock.TryAcquire(
            SessionPath.For(nobody),
            new SessionLockRequest { Browser = ProvisionedBrowsers.Chromium, Purpose = "a session nobody holds" },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).Acquired!.Dispose();

        var notOpen = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = nobody,
            ["why"] = "closing a session nobody has open",
        });

        await Assert.That((bool?)notOpen["isError"]).IsNotEqualTo(true).Because(TextOf(notOpen));
        await Assert.That(TextOf(notOpen)).IsEqualTo(SessionManager.NotOpenSoNothingToClose(SessionPath.For(nobody).FullPath, lastClose: null));

        // Nothing was started for any of the three.
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);
    }

    /// <summary>
    /// A close that never answers -- the armed-close wedge -- still leaves a
    /// session that refuses the next call with the way back, and the resume ends
    /// the wedged child and starts one that answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The wedge, measured 2026-10-03:</b> with a debugger pause armed, a
    /// <c>browser_close</c> never answered (12 of 12 on <c>@playwright/mcp</c>
    /// 0.0.82, 6 of 6 on 0.0.83), every page tool then failed at once with
    /// <i>waitForInitialized</i>, and only upstream's <c>browser_resume</c> got
    /// the session out -- the tool denied the same day. So the session is closed
    /// BEFORE the close is forwarded, and the resume ends the child through its
    /// stdin, which a paused child obeys.
    /// </para>
    /// <para>
    /// <b>The double is not paused, and what it stands for is stated.</b> It holds
    /// the close open, which is the one property of the wedge this layer can
    /// reproduce; that a real paused child exits when its stdin closes was
    /// measured by the research into debugger tools on 0.0.82 and is not asserted
    /// here.
    /// </para>
    /// <para>
    /// ⚠️ <b>The resume waits for the close since 2026-10-04, and the cap is what lets
    /// it go</b> (previously it ended the wedged child the moment it arrived). Nothing
    /// may cut a clean close short, so the resume waits for the caller's close for
    /// <see cref="SessionTimes.BrowserCloseCap"/> from when it was sent, on the
    /// session's clock: one tick short of it the resume is still waiting, and at it the
    /// resume ends the child and opens the session again. Planted red against the tree
    /// as it stood, where the resume answered before the cap had moved at all.
    /// </para>
    /// <para>
    /// ⚠️ <b>Only the first child holds its close, and the hold is let go on every way
    /// out.</b> The rig's teardown is a shutdown, and a shutdown sends every open
    /// browser its close and waits for it up to the cap on this arm's own clock, which
    /// nothing moves once the arm is over. A second child that held its close too
    /// kept the teardown waiting for good: the first run of this arm on 2026-10-04 had
    /// written every row of its session record and was still in its teardown ten
    /// minutes later.
    /// </para>
    /// <para>
    /// ⚠️ <b>The close is <c>browserai_close</c> since 2026-10-08, F1 a</b> (previously
    /// the caller's own <c>browser_close</c>, forwarded, which stayed unanswered until the
    /// child ended under it). BrowserAI's close answers its caller at the cap itself,
    /// saying the browser did not finish closing, so the caller is never left waiting on
    /// a wedged browser. Planted red against the tree with the tool listed and not
    /// answered.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACloseThatNeverAnswersLeavesASessionTheResumeRecovers()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualClock();
        var children = 0;

        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };

                if (Interlocked.Increment(ref children) is 1)
                {
                    child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour { HoldUntil = never.Task };
                }
            },
            opensDefaultSession: false,
            clock: clock);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        try
        {
            await TheResumeRecoversAWedgedSessionAsync(sessions, rig, clock);
        }
        finally
        {
            _ = never.TrySetResult();
        }
    }

    /// <summary>The body of <see cref="ACloseThatNeverAnswersLeavesASessionTheResumeRecovers"/>, with the first child's close held.</summary>
    /// <param name="sessions">The rig's session environment.</param>
    /// <param name="rig">The rig.</param>
    /// <param name="clock">The session clock.</param>
    /// <returns>The assertion task.</returns>
    private static async Task TheResumeRecoversAWedgedSessionAsync(RigSessionEnvironment sessions, McpTestHarness rig, ManualClock clock)
    {
        var directory = Path.Combine(sessions.Root, "wedged-close");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose close never answers",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser");

        var parked = await rig.Client.BeginAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Close,
            ["arguments"] = new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite sending a close that will never answer",
            },
        });

        var first = sessions.SessionChildren[0];

        await WaitUntilAsync(
            () => first.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool),
            "the close never reached the child, so nothing was wedged");

        var refused = await NavigateAsync(rig, directory, "the call that meets the wedge");

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains(SessionToolSurface.Resume);
        await Assert.That(first.ToolCallsReceived.Count(tool => tool == "browser_navigate")).IsEqualTo(1);

        var resumeId = await rig.Client.BeginAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Resume,
            ["arguments"] = new JsonObject
            {
                ["directory"] = directory,
                ["why"] = "the suite getting a wedged session back",
                ["headed"] = false,
                ["transcript"] = false,
                ["captureNetwork"] = false,
                ["idleMinutes"] = 10,
            },
        });

        var resuming = rig.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);

        await WaitUntilAsync(
            () => resuming.IsCompleted || rig.Logs.Logged("opens the session again once it has"),
            "the resume neither answered nor said it was waiting for the close");

        await Assert.That(resuming.IsCompleted).IsFalse()
            .Because("the resume ended the close before the cap had moved at all");

        // The cap is the newest timer on the session's clock, armed when the close was
        // sent, and the clock has not moved since: one tick short of it, nothing fires.
        var left = clock.UntilTheNewestTimerFires();

        await Assert.That(left).IsEqualTo(SessionTimes.BrowserCloseCap);

        clock.AdvanceTicks(left!.Value.Ticks - ManualClock.OneTick);

        await Assert.That(resuming.IsCompleted).IsFalse();
        await Assert.That(first.HasStopped).IsFalse();
        await Assert.That(AnswerTo(rig, parked)).IsNull()
            .Because("browserai_close answered before its cap had run out, while the browser was still closing");

        // The cap runs out, and the resume goes ahead.
        clock.AdvanceTicks(ManualClock.OneTick);

        var resumed = (await resuming).Result!;

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true);
        await WaitUntilAsync(() => first.HasStopped, "the resume did not end the child whose close never answered");
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);
        await Assert.That(rig.Logs.Logged("did not answer the agent's close within")).IsTrue();

        // The parked close is answered at the cap by BrowserAI itself, saying the
        // browser did not finish closing, and not left outstanding for ever. Read off
        // every frame the client has had: the resume's own round trip may already have
        // read it past, because a round trip skips answers to anything else.
        await rig.Client.ReadUntilAsync(() => AnswerTo(rig, parked) is not null);

        var answer = AnswerTo(rig, parked)!;

        await Assert.That(answer["error"]).IsNull();
        await Assert.That((bool?)answer["result"]?["isError"]).IsNotEqualTo(true);
        await Assert.That(string.Concat((answer["result"]?["content"]?.AsArray() ?? []).Select(block => (string?)block?["text"] ?? string.Empty)))
            .IsEqualTo(SessionManager.ClosedWhenTheCapRanOut);

        var after = await NavigateAsync(rig, directory, "the call the new child answers");

        await Assert.That((bool?)after["isError"]).IsNotEqualTo(true);
        await Assert.That(sessions.SessionChildren[1].ToolCallsReceived).Contains("browser_navigate");
    }

    /// <summary>
    /// A resume of a session whose browser is up applies nothing, refuses a
    /// per-run setting it was asked for that differs, and answers <i>the session
    /// is already live</i> to the same call without it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q324 a.</b> A field report of 2026-10-01: two resumes asking for a
    /// window were each told "nothing was changed", with no error, and the
    /// session stayed headless. Only the arguments a call passes are compared, so
    /// a bare resume is never refused for asking for nothing.
    /// </para>
    /// <para>
    /// <b>And the maintainer's rule of 2026-10-03, in his words:</b> <i>"A resume
    /// on an active session is fine and a noop and returns "the session is already
    /// live" if and only if there are no conflicting settings. So the same settings
    /// or no settings given or a mix. If any of the settings are different the
    /// resume is refused with an explicit message that the models needs to call
    /// close and then resume with the different settings. Name the parameters that
    /// triggered this refusal. Also explain in the response that this will close
    /// and re-open the playwright browser."</i> The texts are asserted as
    /// LITERALS as well as against the method that writes them, because comparing
    /// a sentence to the method that produces it cannot tell a true sentence from
    /// a false one.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeWithTheBrowserUpAppliesNothingAndRefusesWhatItCannotApply()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "browser-up");
        var location = SessionPath.For(directory);

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose browser is up when it is resumed",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser");

        var refused = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite asking for a window on a browser that is up",
            ["purpose"] = "a purpose a refused resume must not record",
            ["headed"] = true,
            ["transcript"] = true,
            ["viewport"] = "1280x720",
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).IsEqualTo(SessionErrors.ResumeCannotApplyWhileTheBrowserIsUp(
            location.FullPath,
            [
                "'headed' (running: false, asked: true)",
                "'transcript' (running: false, asked: true)",
                $"'viewport' (running: {BrowserConfiguration.DefaultViewport}, asked: 1280x720)",
            ]));

        // What the maintainer asked the refusal to say, each half on its own:
        // every parameter that triggered it with both values, that nothing
        // changed, the two calls that apply them, that those close the
        // Playwright browser and open a new one, and that a live session needs
        // no resume at all.
        var refusal = TextOf(refused);

        await Assert.That(refusal).StartsWith($"'{location.FullPath}' is already live, so {SessionToolSurface.Resume} changed nothing.");
        await Assert.That(refusal).Contains("'headed' (running: false, asked: true)");
        await Assert.That(refusal).Contains("'transcript' (running: false, asked: true)");
        await Assert.That(refusal).Contains($"'viewport' (running: {BrowserConfiguration.DefaultViewport}, asked: 1280x720)");
        await Assert.That(refusal).Contains($"call {SessionToolSurface.Close} on this session, then {SessionToolSurface.Resume} with them");
        await Assert.That(refusal).Contains("closes the Playwright browser and opens a new one with the new settings");
        await Assert.That(refusal).Contains("page snapshots and element references from before no longer apply");
        await Assert.That(refusal).Contains("no resume is needed: the session is live, so carry on with its tools");
        await Assert.That(refusal).DoesNotContain("leave those");

        // Nothing changed: no new child, and the purpose the refused call
        // carried is not in the record.
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);
        await Assert.That(SessionLock.ReadRecord(location)!.Purpose).IsEqualTo("the session whose browser is up when it is resumed");

        // The positive control: the same session, resumed without the
        // arguments, is answered and still left alone -- and the answer leads
        // with the maintainer's own words.
        var bare = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming without asking for anything",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)bare["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(bare)).StartsWith(SessionManager.AlreadyLive(browserUp: true, purposeChanged: false));
        await Assert.That(TextOf(bare)).StartsWith("The session is already live");
        await Assert.That(TextOf(bare)).Contains("nothing changed");

        // An argument that names what is already in use is not a difference,
        // and neither is a mix of one with an argument left out.
        foreach (var asked in new[]
        {
            new JsonObject { ["headed"] = false, ["transcript"] = false, ["captureNetwork"] = false, ["idleMinutes"] = 10 },
            new JsonObject { ["headed"] = false, ["viewport"] = BrowserConfiguration.DefaultViewport.ToString(), ["transcript"] = false, ["captureNetwork"] = false, ["idleMinutes"] = 10 },
        })
        {
            asked["directory"] = directory;
            asked["why"] = "the suite asking for what the browser already has";

            var same = await CallAsync(rig, SessionToolSurface.Resume, asked);

            await Assert.That((bool?)same["isError"]).IsNotEqualTo(true);
            await Assert.That(TextOf(same)).StartsWith("The session is already live");
            await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);
        }
    }

    /// <summary>
    /// A resume of a live session whose browser has not started, passing
    /// nothing, is the no-op the maintainer's rule describes: the session keeps
    /// the settings it has and nothing is started.
    /// </summary>
    /// <remarks>
    /// <b>The rule of 2026-10-03: <i>"no settings given"</i> is a no-op that
    /// answers <i>"the session is already live"</i>.</b> Until then a resume with no
    /// browser up compared the call's DEFAULTS with what the session was running,
    /// so a bare resume of a session started at another viewport opened it again
    /// at the default one and said it had applied settings nobody had asked for.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABareResumeWithNoBrowserUpKeepsTheSettingsTheSessionHas()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "bare-resume-no-browser");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session resumed bare before its browser started",
            ["viewport"] = "1280x720",
            ["transcript"] = true,
            ["headed"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        var bare = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming without asking for anything",
        });

        await Assert.That((bool?)bare["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(bare)).StartsWith(SessionManager.AlreadyLive(browserUp: false, purposeChanged: false));
        await Assert.That(TextOf(bare)).StartsWith("The session is already live");
        await Assert.That(TextOf(bare)).Contains("nothing changed");
        await Assert.That(TextOf(bare)).Contains("viewport: 1280x720");

        // Nothing was started, and the one child there is keeps the config it
        // was launched with: the viewport and the transcript the init asked for.
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);

        var config = ConfigOf(sessions.Launches[^1]);

        await Assert.That((int?)config["browser"]?["contextOptions"]?["viewport"]?["width"]).IsEqualTo(1280);
        await Assert.That((bool?)config["saveSession"]).IsTrue();
    }

    /// <summary>
    /// A resume of a session with no browser up applies the settings it was
    /// asked for, by starting a new child at them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q324 c.</b> Nothing a new child can take away is up, so the settings go
    /// into the new child's config; a resume that asked for what the session
    /// already has starts nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kept on 2026-10-04 beside the maintainer's rule of 2026-10-03, and
    /// recorded for his review.</b> His rule refuses a differing setting on an
    /// active session and tells the caller to close and resume; a session whose
    /// browser has not started has no Playwright browser to close, so this case
    /// still applies the setting by opening the session again, and nothing is
    /// lost by it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeWithNoBrowserUpAppliesItsSettingsByStartingANewChild()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "no-browser-up");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session resumed before its browser ever started",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        var unchanged = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming with what the session already has",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That(TextOf(unchanged)).StartsWith("The session is already live");
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);

        // A window and a smaller viewport for a session created headless: the
        // case the field report could not get.
        var applied = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite asking for a window and a smaller viewport",
            ["headed"] = true,
            ["viewport"] = "1280x720",
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 60,
        });

        await Assert.That((bool?)applied["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(applied)).Contains(SessionManager.AppliedWithNoBrowserUp);
        await Assert.That(TextOf(applied)).Contains("viewport: 1280x720");
        await Assert.That(TextOf(applied)).Contains("headed: true");
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);
        await WaitUntilAsync(() => sessions.SessionChildren[0].HasStopped, "the resume that applied its settings did not end the old child");

        // The new child's config is the one with the window and the new
        // viewport: what a browser launched from it gets.
        var config = ConfigOf(sessions.Launches[^1]);

        await Assert.That((bool?)config["browser"]?["launchOptions"]?["headless"]).IsFalse();
        await Assert.That((int?)config["browser"]?["contextOptions"]?["viewport"]?["width"]).IsEqualTo(1280);
        await Assert.That((int?)config["browser"]?["contextOptions"]?["viewport"]?["height"]).IsEqualTo(720);
    }

    /// <summary>
    /// A capture taken across a close is given an archive of its own at the
    /// resume, so the browser after the close cannot write over the one before.
    /// </summary>
    /// <remarks>
    /// <b>Measured 2026-10-03 at <c>@playwright/mcp</c> 0.0.82 and 0.0.83:</b>
    /// <c>recordHar</c> truncates its file at every context creation and the name
    /// was chosen once per child, so a browser started again inside one child
    /// wrote over the capture taken before it. Every browser after a close is a
    /// new child now, with a config and a name of its own; this holds that the
    /// resume really does write a second name.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACaptureAcrossACloseIsGivenAnArchiveOfItsOwnAtTheResume()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "capture-across-a-close");

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session capturing across a close",
            ["captureNetwork"] = true,
            ["headed"] = false,
            ["transcript"] = false,
            ["idleMinutes"] = 10,
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser");

        _ = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing a browser that is capturing",
        });

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite capturing again after the close",
            ["captureNetwork"] = true,
            ["headed"] = false,
            ["transcript"] = false,
            ["idleMinutes"] = 10,
        });

        // Read off each answer, which names the archive its launch writes: the
        // config file has one name per session and the second launch rewrote it.
        var before = ArchiveNamedIn(TextOf(opened));
        var after = ArchiveNamedIn(TextOf(resumed));

        await Assert.That(sessions.Launches.Count).IsEqualTo(2);
        await Assert.That(before).IsNotNull();
        await Assert.That(after).IsNotNull();
        await Assert.That(after).IsNotEqualTo(before);
    }

    /// <summary>The archive an init or resume answer says this launch is writing.</summary>
    /// <param name="answer">The answer's text.</param>
    /// <returns>The path, or <see langword="null"/> when the answer names none.</returns>
    private static string? ArchiveNamedIn(string answer)
    {
        const string Lead = "NETWORK CAPTURE IS ON for this run: '";

        var at = answer.IndexOf(Lead, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var start = at + Lead.Length;
        var end = answer.IndexOf('\'', start);

        return end < 0 ? null : answer[start..end];
    }

    /// <summary>
    /// A shutdown asks every open browser to close itself, all at once, and one
    /// that never answers cannot hold it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>e2 of P7.</b> Claude Code kills a server's tree 0.53 to 1.15 s after it
    /// closes the server's input, measured 2026-10-03, so sessions closed one
    /// after another meant the second was never reached.
    /// </para>
    /// <para>
    /// <b>All at once is asserted as an ordering and not a duration:</b> both
    /// closes are outstanding at the same moment, which a shutdown that waited
    /// for one session's teardown before the next session's close cannot
    /// produce. The close that never answers is the wedge, and the bound is what
    /// lets the shutdown end anyway.
    /// </para>
    /// <para>
    /// ⚠️ <b>The bound is the one-minute cap since 2026-10-04, D4.2</b>, the
    /// maintainer's words verbatim: <i>"Same 1 min. under option d (lane c)"</i>
    /// (previously one second, a client's kill window). It runs on the session's clock,
    /// so this arm stops one tick short of it and the wedged close is still waited
    /// for, and then lets it run out. Planted red against the one second that stood
    /// until that day.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AShutdownAsksEveryOpenBrowserToCloseAtOnceAndOneThatNeverAnswersCannotHoldIt()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualClock();
        var opened = 0;

        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };

                // The first session's close never answers; the second's answers
                // when this test says so.
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour
                {
                    HoldUntil = Interlocked.Increment(ref opened) is 1 ? never.Task : release.Task,
                };
            },
            opensDefaultSession: false,
            clock: clock);

        var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        try
        {
            var wedged = Path.Combine(sessions.Root, "shutdown-wedged");
            var healthy = Path.Combine(sessions.Root, "shutdown-healthy");

            foreach (var directory in new[] { wedged, healthy })
            {
                _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
                {
                    ["directory"] = directory,
                    ["purpose"] = "a session open when the server shuts down",
                    ["headed"] = false,
                    ["transcript"] = false,
                    ["captureNetwork"] = false,
                    ["idleMinutes"] = 10,
                });

                _ = await NavigateAsync(rig, directory, "the call that starts the browser");
            }

            var children = sessions.SessionChildren;

            await Assert.That(children.Count).IsEqualTo(2);

            // The shutdown, started and not awaited.
            var shuttingDown = rig.Proxy.DisposeAsync().AsTask();

            await WaitUntilAsync(
                () => children.All(child => child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool)),
                "a shutdown never asked every open browser to close");

            // Both closes are outstanding at once: neither child has been ended.
            await Assert.That(children.Any(child => child.HasStopped)).IsFalse();

            // Both caps were armed together, as the shutdown began, and the clock has
            // not moved since: each is the one-minute cap, to the tick.
            var left = clock.UntilTheNewestTimerFires();

            await Assert.That(left).IsEqualTo(SessionTimes.BrowserCloseCap);

            release.SetResult();

            await WaitUntilAsync(() => children.Count(child => child.HasStopped) is 1, "the shutdown did not end the child whose close was answered");

            // One tick short of the cap, the wedged close is still waited for.
            clock.AdvanceTicks(left!.Value.Ticks - ManualClock.OneTick);

            await Assert.That(shuttingDown.IsCompleted).IsFalse();
            await Assert.That(children.Count(child => child.HasStopped)).IsEqualTo(1);

            // And at it, the shutdown ends the wedged child too.
            clock.AdvanceTicks(ManualClock.OneTick);

            await shuttingDown.WaitAsync(TestDefaults.InProcessHang);

            await WaitUntilAsync(() => children.All(child => child.HasStopped), "a shutdown left a session's child running");
        }
        finally
        {
            await rig.DisposeAsync();
        }
    }

    /// <summary>
    /// A session's child is given a temporary folder inside the run's own
    /// directory, and never the user's.
    /// </summary>
    /// <remarks>
    /// <b>P5 a.</b> Measured 2026-10-03: every browser launch a kill ends leaves
    /// one empty <c>playwright-artifacts-*</c> in the temporary folder its child
    /// was given, and with the user's <c>%TEMP%</c> nothing ever removed one.
    /// Inside the run's directory it goes when the directory does.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionChildsTemporaryFolderIsInsideTheRunsOwnDirectory()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var launch = sessions.Launches[^1];
        var expected = Path.Combine(sessions.Environment.InstanceDirectory, ChildLaunch.TemporaryFolderName);

        await Assert.That(launch.Environment["TEMP"]).IsEqualTo(expected);
        await Assert.That(launch.Environment["TMP"]).IsEqualTo(expected);
        await Assert.That(Directory.Exists(expected)).IsTrue();

        // The positive control: the user's own TEMP is a different folder, so
        // the equality above is not satisfied by inheritance.
        await Assert.That(Environment.GetEnvironmentVariable("TEMP")).IsNotEqualTo(expected);
    }

    /// <summary>
    /// Against a <b>real</b> child: one that has opened no browser reads as having
    /// none, and one with a page open reads as having one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-03, when the question was found answering yes for every
    /// real child.</b> It counted anything in the child's job beyond one process,
    /// and Windows puts a console host in the job beside node: the job holds two
    /// processes before any browser starts and two again after a
    /// <c>browser_close</c>, measured the same day. Every arm above asks the
    /// double, so none of them could see it, and in the product a close with
    /// nothing up started a browser in order to close it.
    /// </para>
    /// <para>
    /// <b>Both directions, through the agent's own close</b>, because it is the
    /// one call whose answer differs: with nothing up no close is sent, and with a
    /// page up BrowserAI's own close reaches the browser.
    /// </para>
    /// <para>
    /// ⚠️ <b>Through <c>browserai_close</c> since 2026-10-08, F1 a</b> (previously the
    /// caller's own <c>browser_close</c>, which with nothing up left the session open).
    /// A close with nothing up closes the session now, so the page is opened by a
    /// resume in between.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARealChildWithNoBrowserReadsAsNoneAndOneWithAPageReadsAsOne()
    {
        SuiteEnvironment.RequireProvisionedChromium();

        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "real-child");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose child is asked whether a browser is up",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        // No browser call yet, so no close is sent to the child.
        var nothing = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing a browser that never started",
        });

        await Assert.That(TextOf(nothing)).IsEqualTo(SessionManager.ClosedWithNoBrowserUp);

        var reopened = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite reopening the session it closed with nothing up",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)reopened["isError"]).IsNotEqualTo(true).Because(TextOf(reopened));

        var opened = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a page so that a browser is up",
            ["url"] = SliceRun.TargetUrl,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        // A page is up: BrowserAI's own close goes to the browser, and it closes the session.
        var closed = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing the browser it opened",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(TextOf(closed));
        await Assert.That(TextOf(closed)).IsEqualTo(SessionManager.ClosedByTheAgent);

        var refused = await NavigateAsync(rig, directory, "the suite calling after its own close");

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains(SessionToolSurface.Resume);
    }

    /// <summary>
    /// Against a <b>real</b> browser: after the caller's own close, a resume
    /// reopens the tabs that were open through the browser's own session
    /// restore.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>P1, the maintainer's words of 2026-10-03, verbatim: "Restore as much as
    /// possible without adding lot's of complexity. So basically, use whatever
    /// playwright offers in capabilities."</b> The restore is a launch option and
    /// nothing in BrowserAI drives it, so the only thing that can show it works
    /// through this product is a real browser.
    /// </para>
    /// <para>
    /// <b>Two tabs on two pages of a loopback site, both families.</b> A restore
    /// that brought one tab back, or a blank one, would read as half a pass with a
    /// single page. The agent's own close is used because it is a clean close the
    /// test can wait for; the idle close is the same teardown and its arm is in
    /// <see cref="BrowserIdleTimerTests"/>. <i>Corrected 2026-10-08 (previously "The
    /// caller's close is used"): the close is <c>browserai_close</c> since F1 a.</i>
    /// </para>
    /// </remarks>
    /// <param name="browser">The family.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(ProvisionedBrowsers.Chromium)]
    [Arguments(ProvisionedBrowsers.Firefox)]
    public async Task AResumeReopensTheTabsThatWereOpenWhenTheBrowserWasClosed(string browser)
    {
        if (BrowserConfiguration.IsFirefox(browser))
        {
            SuiteEnvironment.RequireProvisionedFirefox();
        }
        else
        {
            SuiteEnvironment.RequireProvisionedChromium();
        }

        using var site = LoopbackSite.Start();

        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, $"restore-{browser}");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose tabs a resume brings back",
            ["browser"] = browser,
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)(await NavigateToAsync(rig, directory, site.Url("first")))["isError"]).IsNotEqualTo(true);

        var opened = await CallAsync(rig, "browser_tabs", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a second page in a second tab",
            ["action"] = "new",
            ["url"] = site.Url("second"),
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true);

        var before = TextOf(await TabsAsync(rig, directory));

        // The precondition: both pages are open before anything is closed.
        await Assert.That(before).Contains(site.Url("first"));
        await Assert.That(before).Contains(site.Url("second"));

        // P5 a, read off a real launch: the browser's own artifacts folder is in
        // the temporary folder the child was given, inside the run's directory,
        // and not in the user's TEMP.
        var temporary = Path.Combine(sessions.Environment.InstanceDirectory, ChildLaunch.TemporaryFolderName);

        await Assert.That(Directory.EnumerateDirectories(temporary, "playwright-artifacts-*").Any()).IsTrue();

        var closed = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing the browser so a resume can reopen it",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true);

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming so the browser restores its tabs",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true);

        // The first browser call starts the browser, and the restore attaches
        // its tabs as they load -- the research waited 2 s before reading them.
        // Asked again until both are there, bounded by the hang detector and by
        // no number written here.
        var after = string.Empty;
        var waited = Stopwatch.StartNew();

        while (!(after.Contains(site.Url("first"), StringComparison.Ordinal) && after.Contains(site.Url("second"), StringComparison.Ordinal)))
        {
            if (waited.Elapsed > TestDefaults.BrowserHang)
            {
                throw new TimeoutException($"the resumed browser never listed both tabs it had; the last listing was:{Environment.NewLine}{after}");
            }

            after = TextOf(await TabsAsync(rig, directory));
        }

        await Assert.That(after).Contains(site.Url("first"));
        await Assert.That(after).Contains(site.Url("second"));
    }

    /// <summary>
    /// Against a <b>real</b> Chromium: after the browser was killed and not closed, a
    /// resume reopens the tabs it had on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q376, the maintainer's words of 2026-10-04, verbatim: "Q376 a".</b> A kill
    /// leaves the profile marked as crashed, and Chromium does not restore on the
    /// launch after an unclean exit: measured 2026-10-03 at <c>chromium-1247</c>, 0 of
    /// 27 runs restored with <c>--restore-last-session</c> alone, 21 of them with the
    /// tabs on disk, and 11 of 11 that had them on disk restored with
    /// <c>--hide-crash-restore-bubble</c> added. A client's kill, the coordinator
    /// ending and a close cut off at its cap all leave a browser in that state.
    /// </para>
    /// <para>
    /// <b>The kill is the whole job, read off the session's own child</b>: every
    /// process in it is terminated by its pid and creation time, the browser before
    /// <c>node</c>, so nothing in it runs a graceful path. Once both pages are open it
    /// opens a third tab and waits for a write to the session folder after that
    /// instant, which Chromium makes 2.5 s after a change, because a kill before that
    /// loses the tabs whatever the switch says, and for the profile's Preferences
    /// file, without which the relaunch takes the profile for a new one and restores
    /// nothing; and it reads the session files once the kill is over, because Chromium
    /// holds them for exclusive reading while it runs, so both pages on disk at the
    /// kill is established and not assumed.
    /// </para>
    /// <para>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, where the launch
    /// carried no <c>--hide-crash-restore-bubble</c>: the relaunch opened its new tab
    /// page and restored nothing.
    /// </para>
    /// <para>
    /// <b>Chromium only.</b> Firefox restored whatever had reached disk after a kill in
    /// 27 of 27 runs with no switch, and its session file is compressed and written on
    /// a 15 s interval, so there is nothing of BrowserAI's to hold there.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeAfterTheBrowserWasKilledReopensTheTabsItHadOnDisk()
    {
        SuiteEnvironment.RequireProvisionedChromium();

        using var site = LoopbackSite.Start();

        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "restore-after-a-kill");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose browser is killed and then resumed",
            ["browser"] = ProvisionedBrowsers.Chromium,
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)(await NavigateToAsync(rig, directory, site.Url("first")))["isError"]).IsNotEqualTo(true);

        var opened = await CallAsync(rig, "browser_tabs", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a second page in a second tab",
            ["action"] = "new",
            ["url"] = site.Url("second"),
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true);

        // ⚠️ THE SESSION FILE CANNOT BE READ WHILE THE BROWSER RUNS. Chromium opens
        // it for exclusive reading and writing (command_storage_backend.cc:747-750 at
        // 155.0.8059.12), so no share flag lets a reader in: the first run of this arm,
        // on 2026-10-04, read it and waited out the hang detector with both pages in a
        // file it could not open. Its write time can be read, and is what the
        // measurement's own watcher read. A save writes everything changed before it,
        // so a write after the instant both pages were open carries both; and a third
        // tab opened after that instant is a change Chromium saves 2.5 s later, so the
        // write comes whatever the machine's load. The second form of this arm waited
        // for a write after the second tab's answer alone, and in a gate's PowerShell
        // half on 2026-10-04 waited out the hang detector: a save that comes before the
        // answer leaves nothing to wait for. The files are read once the kill is over,
        // for the precondition the measurement had.
        var sessionFiles = Path.Combine(directory, SessionLayout.ProfileFolderName, "Default", "Sessions");
        var bothOpen = DateTime.UtcNow;

        var third = await CallAsync(rig, "browser_tabs", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a third tab, a change Chromium saves 2.5 s later",
            ["action"] = "new",
            ["url"] = site.Url("third"),
        });

        await Assert.That((bool?)third["isError"]).IsNotEqualTo(true);

        await WaitUntilAsync(
            () => SessionFolderWrittenSince(sessionFiles, bothOpen),
            "the browser never wrote its session folder after both pages were open, so a kill now would test nothing");

        // ⚠️ AND THE PROFILE'S PREFERENCES FILE. A relaunch that finds none takes the
        // profile for a new one, and a new profile is not given the last session
        // whatever --restore-last-session says (startup_browser_creator.cc:937-939 and
        // profile_impl.cc:1657-1668 at 155.0.8059.12; Playwright launches with
        // --no-first-run, so only the file decides). Chromium writes Preferences 10 s
        // after a change, and the second run of this arm, on 2026-10-04, killed a fresh
        // profile's first launch 3.1 s after the call that started it and restored
        // nothing with the switch on.
        var preferences = Path.Combine(directory, SessionLayout.ProfileFolderName, "Default", "Preferences");

        await WaitUntilAsync(
            () => File.Exists(preferences),
            "the browser never wrote its Preferences file, so the relaunch would take the profile for a new one and restore nothing whatever the switch says");

        var child = sessions.RealSessionChildren.Single();
        var node = child.ProcessId!.Value;

        var members = child.JobProcessIds()
            .Select(pid => (Pid: pid, Created: TryCreationTimeOf(pid)))
            .Where(member => member.Created is not null)
            .Select(member => (member.Pid, Created: member.Created!.Value))
            .OrderBy(member => member.Pid == node ? 1 : 0)
            .ToList();

        await Assert.That(members.Count(member => member.Pid != node)).IsGreaterThan(0)
            .Because("a browser has to be up in the job for a kill to take it");

        foreach (var (pid, created) in members)
        {
            try
            {
                ProcessIdentity.Terminate(pid, created);
            }
            catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
            {
                // Gone already: a browser's helpers end with the browser.
            }
        }

        await WaitUntilAsync(
            () => members.All(member => !ProcessIdentity.IsAlive(member.Pid, member.Created)) && child.ChildHasGone,
            "a process the suite terminated in the session's job was still running");

        await Assert.That(SessionFilesName(sessionFiles, site.Url("first")) && SessionFilesName(sessionFiles, site.Url("second"))).IsTrue()
            .Because("the killed browser's session file names both pages, or the resume has nothing on disk to restore and the arm tests nothing");

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming a session whose browser was killed",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(TextOf(resumed));

        // The first browser call starts the browser. A relaunch that does not
        // restore opens the new tab page and nothing else, which is the failure
        // this arm exists for, so that page with neither of ours is the answer.
        var after = string.Empty;
        var waited = Stopwatch.StartNew();

        while (!(after.Contains(site.Url("first"), StringComparison.Ordinal) && after.Contains(site.Url("second"), StringComparison.Ordinal)))
        {
            if (after.Contains(NewTabPage, StringComparison.Ordinal)
                && !after.Contains(site.Url("first"), StringComparison.Ordinal)
                && !after.Contains(site.Url("second"), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"the relaunched browser opened its new tab page and restored nothing; the listing was:{Environment.NewLine}{after}");
            }

            if (waited.Elapsed > TestDefaults.BrowserHang)
            {
                throw new TimeoutException($"the resumed browser never listed both tabs it had; the last listing was:{Environment.NewLine}{after}");
            }

            after = TextOf(await TabsAsync(rig, directory));
        }

        await Assert.That(after).Contains(site.Url("first"));
        await Assert.That(after).Contains(site.Url("second"));
    }

    /// <summary>Whether any file in a Chromium profile's session folder was written at or after an instant.</summary>
    /// <remarks>
    /// Each write time is read through a <see cref="FileInfo"/> made from the file's
    /// path, as the durability measurement's watcher read them while the browser held
    /// the files open.
    /// </remarks>
    /// <param name="folder">The profile's <c>Sessions</c> folder.</param>
    /// <param name="since">The instant, in UTC.</param>
    /// <returns>Whether one of the files was written since.</returns>
    private static bool SessionFolderWrittenSince(string folder, DateTime since) =>
        Directory.Exists(folder)
        && Directory.EnumerateFiles(folder).Any(file => new FileInfo(file).LastWriteTimeUtc >= since);

    /// <summary>Whether any file in a Chromium profile's session folder carries an address.</summary>
    /// <remarks>
    /// Only for a browser that has gone: a running Chromium holds its session file for
    /// exclusive reading, and a file that cannot be opened reads as naming nothing.
    /// </remarks>
    /// <param name="folder">The profile's <c>Sessions</c> folder.</param>
    /// <param name="url">The address, looked for as UTF-8 and as UTF-16.</param>
    /// <returns>Whether one of the files names it.</returns>
    private static bool SessionFilesName(string folder, string url)
    {
        if (!Directory.Exists(folder))
        {
            return false;
        }

        var narrow = Encoding.UTF8.GetBytes(url);
        var wide = Encoding.Unicode.GetBytes(url);

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            byte[] bytes;

            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var copy = new MemoryStream();

                stream.CopyTo(copy);
                bytes = copy.ToArray();
            }
            catch (IOException)
            {
                continue;
            }

            if (bytes.AsSpan().IndexOf(narrow) >= 0 || bytes.AsSpan().IndexOf(wide) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A process's creation time, or <see langword="null"/> once it has gone.</summary>
    /// <param name="processId">The pid.</param>
    /// <returns>Its creation time.</returns>
    private static long? TryCreationTimeOf(int processId)
    {
        try
        {
            return ProcessIdentity.CreationTimeOf(processId);
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The answer to one request among every frame the client has read, or <see langword="null"/>.</summary>
    /// <param name="rig">The rig whose client sent it.</param>
    /// <param name="id">The id the request went out with.</param>
    /// <returns>The envelope.</returns>
    private static JsonObject? AnswerTo(McpTestHarness rig, int id) =>
        rig.Client.FramesReceived
            .Where(frame => frame.Length is not 0)
            .Select(frame => JsonNode.Parse(Encoding.UTF8.GetString(frame)) as JsonObject)
            .FirstOrDefault(envelope => envelope?["id"] is { } received && (int?)received == id);

    /// <summary>The generated config a launch was given, read back off disk.</summary>
    /// <param name="launch">The launch, as the rig recorded it.</param>
    /// <returns>The config.</returns>
    private static JsonObject ConfigOf(BrowserAI.Protocol.ChildProcessOptions launch)
    {
        var file = launch.Arguments[launch.Arguments.ToList().IndexOf("--config") + 1];

        return JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    }

    private static Task<JsonObject> NavigateAsync(McpTestHarness rig, string directory, string why) =>
        CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = why,
            ["url"] = "data:text/html,<h1>ok</h1>",
        });

    private static Task<JsonObject> NavigateToAsync(McpTestHarness rig, string directory, string url) =>
        CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a page the restore has to bring back",
            ["url"] = url,
        });

    private static Task<JsonObject> TabsAsync(McpTestHarness rig, string directory) =>
        CallAsync(rig, "browser_tabs", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite reading which tabs are open",
            ["action"] = "list",
        });

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

    /// <summary>Waits for an event, bounded by the suite's hang detector and by no number written here.</summary>
    /// <param name="condition">The event.</param>
    /// <param name="whatWentWrong">What it means when it never comes.</param>
    /// <returns>A task that completes once the event has happened.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition, string whatWentWrong)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
