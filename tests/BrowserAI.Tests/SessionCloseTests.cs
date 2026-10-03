// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Net;
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
    /// The caller's own <c>browser_close</c> closes the session the way the idle
    /// close does: answered as upstream answers it, its child ended, and every
    /// later call refused until a resume.
    /// </summary>
    /// <remarks>
    /// <b>P3 b.</b> A field report of 2026-10-01 met the old shape from the other
    /// side: after the caller's own close, the timer wrote a row ten minutes later
    /// saying BrowserAI had closed a browser that was already gone, and sent a
    /// close that started one in order to close it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCallersOwnCloseClosesTheSessionLikeTheIdleClose()
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

        var directory = Path.Combine(sessions.Root, "closed-by-the-caller");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose caller closes its browser",
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser");

        var closed = await CallAsync(rig, LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing the browser it opened",
        });

        // Upstream's own answer, byte for byte: the close was forwarded.
        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(closed)).IsEqualTo("### Result\nNo open tabs. Navigate to a URL to create one.");

        var first = sessions.SessionChildren[0];

        await Assert.That(first.ToolCallsReceived).Contains(LiveSession.BrowserCloseTool);
        await WaitUntilAsync(() => first.HasStopped, "the caller's close never ended the session's child");

        var refused = await NavigateAsync(rig, directory, "the call after the caller's own close");

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains($"by a {LiveSession.BrowserCloseTool} call");
        await Assert.That(TextOf(refused)).Contains(SessionToolSurface.Resume);

        // The timer's sentence belongs to the timer's close and to nothing else.
        await Assert.That(TextOf(refused)).DoesNotContain("no browser call had reached it");

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming after its own close",
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(resumed)).Contains($"by a {LiveSession.BrowserCloseTool} call");
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);

        var after = await NavigateAsync(rig, directory, "the call the new child answers");

        await Assert.That((bool?)after["isError"]).IsNotEqualTo(true);
    }

    /// <summary>
    /// A caller's <c>browser_close</c> with no browser up is not forwarded, is
    /// answered as done, and leaves the session open.
    /// </summary>
    /// <remarks>
    /// <b>Forwarded, it would start a browser in order to close it</b>: measured
    /// 2026-10-03 at <c>@playwright/mcp</c> 0.0.82 and 0.0.83, 8 to 9 browser
    /// processes, a capture rewritten empty and a descriptor nothing reaps. With
    /// nothing up there is nothing to close, so like the idle close in the same
    /// state it closes nothing and changes nothing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallersCloseWithNoBrowserUpIsNotForwardedAndLeavesTheSessionOpen()
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
        });

        var closed = await CallAsync(rig, LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing a browser that never started",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(closed)).IsEqualTo(LiveSession.NothingWasOpenToClose);
        await Assert.That(sessions.SessionChildren[0].ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool);

        // The row is written and settled like any call's.
        await Assert.That(RecordedSession.LogOf(directory).Any(row =>
            row.Tool == LiveSession.BrowserCloseTool && row.Outcome == SessionStore.Successful)).IsTrue();

        // And the session is still open: the next call reaches the same child.
        var after = await NavigateAsync(rig, directory, "the call after a close that closed nothing");

        await Assert.That((bool?)after["isError"]).IsNotEqualTo(true);
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);
        await Assert.That(sessions.SessionChildren[0].ToolCallsReceived).Contains("browser_navigate");
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
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACloseThatNeverAnswersLeavesASessionTheResumeRecovers()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour { HoldUntil = never.Task };
            },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "wedged-close");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose close never answers",
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser");

        var parked = await rig.Client.BeginAsync("tools/call", new JsonObject
        {
            ["name"] = LiveSession.BrowserCloseTool,
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

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite getting a wedged session back",
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true);
        await WaitUntilAsync(() => first.HasStopped, "the resume did not end the child whose close never answered");
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);

        // The parked close is answered now, by the child that ended under it,
        // and not left outstanding for ever. Read off every frame the client
        // has had: the resume's own round trip may already have read it past,
        // because a round trip skips answers to anything else.
        await rig.Client.ReadUntilAsync(() => AnswerTo(rig, parked) is not null);

        var answer = AnswerTo(rig, parked)!;

        await Assert.That(answer["error"] is not null || (bool?)answer["result"]?["isError"] == true).IsTrue();

        var after = await NavigateAsync(rig, directory, "the call the new child answers");

        await Assert.That((bool?)after["isError"]).IsNotEqualTo(true);
        await Assert.That(sessions.SessionChildren[1].ToolCallsReceived).Contains("browser_navigate");
    }

    /// <summary>
    /// A resume of a session whose browser is up applies nothing, refuses a
    /// per-run setting it was asked for that differs, and accepts the same call
    /// without it.
    /// </summary>
    /// <remarks>
    /// <b>Q324 a.</b> A field report of 2026-10-01: two resumes asking for a
    /// window were each told "nothing was changed", with no error, and the
    /// session stayed headless. Only the arguments a call passes are compared, so
    /// a bare resume is never refused for asking for nothing.
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
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser");

        var refused = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite asking for a window on a browser that is up",
            ["purpose"] = "a purpose a refused resume must not record",
            ["headed"] = true,
            ["viewport"] = "1280x720",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).IsEqualTo(SessionErrors.ResumeCannotApplyWhileTheBrowserIsUp(
            location.FullPath,
            [
                "'headed' is false and you asked for true",
                $"'viewport' is '{BrowserConfiguration.DefaultViewport}' and you asked for '1280x720'",
            ]));

        // Nothing changed: no new child, and the purpose the refused call
        // carried is not in the record.
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);
        await Assert.That(SessionLock.ReadRecord(location)!.Purpose).IsEqualTo("the session whose browser is up when it is resumed");

        // The positive control: the same session, resumed without the
        // arguments, is answered and still left alone.
        var bare = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming without asking for anything",
        });

        await Assert.That((bool?)bare["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(bare)).Contains(SessionManager.BrowserIsUpSoNothingWasApplied);

        // An argument that names what is already in use is not a difference.
        var same = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite asking for what the browser already has",
            ["headed"] = false,
        });

        await Assert.That((bool?)same["isError"]).IsNotEqualTo(true);
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A resume of a session with no browser up applies the settings it was
    /// asked for, by starting a new child at them.
    /// </summary>
    /// <remarks>
    /// <b>Q324 c.</b> Nothing a new child can take away is up, so the settings go
    /// into the new child's config; a resume that asked for what the session
    /// already has starts nothing.
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
        });

        var unchanged = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming with what the session already has",
        });

        await Assert.That(TextOf(unchanged)).Contains(SessionManager.NothingNeededApplying);
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);

        // A window and a smaller viewport for a session created headless: the
        // case the field report could not get.
        var applied = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite asking for a window and a smaller viewport",
            ["headed"] = true,
            ["viewport"] = "1280x720",
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
        });

        _ = await NavigateAsync(rig, directory, "the call that starts the browser");

        _ = await CallAsync(rig, LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing a browser that is capturing",
        });

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite capturing again after the close",
            ["captureNetwork"] = true,
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
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AShutdownAsksEveryOpenBrowserToCloseAtOnceAndOneThatNeverAnswersCannotHoldIt()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            opensDefaultSession: false);

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

            release.SetResult();

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
    /// <b>Both directions, through the caller's own close</b>, because it is the
    /// one call whose answer differs: with nothing up it is answered here, and with
    /// a page up it is forwarded and closes the session.
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
        });

        // No browser call yet, so the close is answered here and not forwarded.
        var nothing = await CallAsync(rig, LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing a browser that never started",
        });

        await Assert.That(TextOf(nothing)).IsEqualTo(LiveSession.NothingWasOpenToClose);

        var opened = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a page so that a browser is up",
            ["url"] = SliceRun.TargetUrl,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        // A page is up: the close is forwarded, and it closes the session.
        var closed = await CallAsync(rig, LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing the browser it opened",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(TextOf(closed));
        await Assert.That(TextOf(closed)).IsNotEqualTo(LiveSession.NothingWasOpenToClose);

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
    /// single page. The caller's close is used because it is a clean close the
    /// test can wait for; the idle close is the same teardown and its arm is in
    /// <see cref="BrowserIdleTimerTests"/>.
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

        using var site = PageSite.Start();

        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, $"restore-{browser}");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose tabs a resume brings back",
            ["browser"] = browser,
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

        var closed = await CallAsync(rig, LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing the browser so a resume can reopen it",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true);

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming so the browser restores its tabs",
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

    /// <summary>
    /// Two pages on a loopback port, for the arm whose subject is which pages a
    /// browser reopens.
    /// </summary>
    /// <remarks>
    /// <b>Loopback and HTTP, never a <c>data:</c> URL</b>: the restore measured on
    /// 2026-10-03 was measured against pages served this way, and a URL that
    /// carries its own content would leave a reader unable to tell a restored
    /// page from a navigation.
    /// </remarks>
    private sealed class PageSite : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly string _root;

        private PageSite(HttpListener listener, string root)
        {
            _listener = listener;
            _root = root;
        }

        /// <summary>Binds a loopback port and starts answering.</summary>
        /// <returns>The running site.</returns>
        public static PageSite Start()
        {
            for (var port = 54_300; port < 54_400; port++)
            {
                var root = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/";
                var listener = new HttpListener();
                listener.Prefixes.Add(root);

                try
                {
                    listener.Start();
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                    continue;
                }

                var site = new PageSite(listener, root);
                _ = Task.Run(site.ServeAsync, CancellationToken.None);

                return site;
            }

            throw new InvalidOperationException("no loopback port between 54300 and 54399 could be bound");
        }

        /// <summary>The address of one page.</summary>
        /// <param name="name">The page's name.</param>
        /// <returns>Its URL.</returns>
        public string Url(string name) => $"{_root}{name}";

        /// <inheritdoc />
        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Close();
            _stopping.Dispose();
        }

        private async Task ServeAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception failure) when (failure is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                var name = context.Request.Url?.AbsolutePath.Trim('/') ?? string.Empty;
                var body = Encoding.UTF8.GetBytes($"<!doctype html><title>{name}</title><h1>{name}</h1>");

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }
    }
}
