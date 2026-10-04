// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
/// Nothing cuts a clean close short: a resume, a release and a shutdown that meet a
/// close still in flight wait for it, bounded by the one close cap, and only a
/// destroy, which deletes what the close would save, goes ahead without it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's warning of 2026-10-04, verbatim:</b> <i>"Just thinking about it,
/// if we were to resume within that close window we will need to handle atomicity and
/// orderign correctly. Beware when building lane c."</i> Until that day a resume that
/// met the idle close still waiting ended the wait at once and ended the child through
/// its stdin, and on that path <c>@playwright/mcp</c> force-kills its own browser about
/// 1 ms into its graceful close
/// (<see href="../../kb/playwright/provisioning-and-timings.md#how-old-a-write-must-be-before-a-hard-kill-keeps-it----measured-2026-10-03">kb</see>).
/// </para>
/// <para>
/// <b>The in-process arms</b> hold the double's close open, which is the one property of
/// a slow close a double can reproduce, and read that a waiter is waiting off the line
/// the product logs when it starts to wait. <b>The real-browser arm</b> holds the close
/// in the suite's probe on its way to a real child, writes a cookie just before the
/// close, and reads it back after the reopen, with the tabs restored.
/// </para>
/// </remarks>
internal sealed class CloseOrderingTests
{
    /// <summary>What a resume that waits for a close in flight logs, and nothing else logs.</summary>
    private const string ReopenWaits = "opens the session again once it has";

    /// <summary>What a teardown that waits for a close in flight logs.</summary>
    private const string TeardownWaits = "before its child is ended";

    /// <summary>What the session logs when the caller of a <c>browser_close</c> stops waiting for it.</summary>
    private const string CallerLeft = "stopped waiting for its answer";

    /// <summary>What a destroy that cuts a close in flight short logs.</summary>
    private const string CutShort = "cut the close in flight";

    /// <summary>What the session's log says when the idle close's own close ran out its cap.</summary>
    private const string UnansweredIdleClose = "did not answer its idle close within";

    /// <summary>The cookie the real-browser arm writes before the close.</summary>
    private const string CookieName = "written-before-the-close";

    /// <summary>Its value.</summary>
    private const string CookieValue = "kept";

    /// <summary>The nominal idle period of the arms that drive the timer; nothing waits for it.</summary>
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// A resume that arrives while the idle close is still waiting for its
    /// <c>browser_close</c> waits for the browser to answer, and only then opens the
    /// session again.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, where the resume
    /// ended the wait at once: it answered while the double still held the close, and
    /// the double was told its close was cancelled.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeThatMeetsAnIdleCloseStillWaitingWaitsForItAndThenReopens()
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

        await NavigateAsync(harness.Client, harness.Session!);

        var first = harness.Child;

        await IdleUntilTheCloseIsHeldAsync(clock, first);

        var resumeId = await harness.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = harness.Session!,
            ["why"] = "the suite resuming a session whose idle close is still waiting",
        }));

        var resumed = harness.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);

        await WaitUntilAsync(
            () => resumed.IsCompleted || harness.Logs.Logged(ReopenWaits),
            "the resume neither answered nor said it was waiting for the close");

        await Assert.That(resumed.IsCompleted).IsFalse()
            .Because("the resume answered while the browser was still closing, so it did not wait for the close");
        await Assert.That(first.HasStopped).IsFalse();
        await Assert.That(first.MethodsReceived).DoesNotContain("notifications/cancelled");
        await Assert.That(rig.SessionChildren.Count).IsEqualTo(1);

        // The browser answers its close, and nothing else moves.
        answer.SetResult();

        var response = await resumed;

        await Assert.That((bool?)response.Result?["isError"]).IsNotEqualTo(true).Because(TextOf(response.Result));
        await Assert.That(rig.SessionChildren.Count).IsEqualTo(2);
        await Assert.That(first.HasStopped).IsTrue();
        await Assert.That(first.MethodsReceived).DoesNotContain("notifications/cancelled");
        await Assert.That(harness.Logs.Logged(UnansweredIdleClose)).IsFalse();

        // The close's row is settled as answered, ahead of the resume's own row.
        var log = RecordedSession.LogOf(harness.Session!);
        var close = log.Single(row => row.Tool == LiveSession.BrowserCloseTool);

        await Assert.That(close.Outcome).IsEqualTo(SessionStore.Successful);
        await Assert.That(log.Last(row => row.Tool == SessionToolSurface.Resume).Id).IsGreaterThan(close.Id);
    }

    /// <summary>
    /// A resume that arrives while the caller's own <c>browser_close</c> is still being
    /// answered waits for it, the caller gets the browser's own answer, and then the
    /// session opens again.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, where the resume
    /// ended the child under the close at once and the caller was answered with the
    /// child's failure to answer.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeThatMeetsTheCallersOwnCloseStillWaitingWaitsForItAndThenReopens()
    {
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour
                {
                    HoldUntil = answer.Task,
                    RawResult = """{"content":[{"type":"text","text":"### Result\nNo open tabs. Navigate to a URL to create one."}]}""",
                };
            },
            opensDefaultSession: false,
            clock: new ManualClock());

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await OpenAsync(rig.Client, sessions, "callers-close-then-resume");

        await NavigateAsync(rig.Client, directory);

        var closeId = await rig.Client.BeginAsync("tools/call", Call(LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing its browser, slowly",
        }));

        var first = sessions.SessionChildren[0];

        await WaitUntilAsync(
            () => first.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool),
            "the caller's close never reached the child");

        var resumeId = await rig.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming while its own close is still being answered",
        }));

        var resumed = rig.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);

        await WaitUntilAsync(
            () => resumed.IsCompleted || rig.Logs.Logged(ReopenWaits),
            "the resume neither answered nor said it was waiting for the close");

        await Assert.That(resumed.IsCompleted).IsFalse()
            .Because("the resume answered while the caller's close was still being answered, so it did not wait for it");
        await Assert.That(first.HasStopped).IsFalse();

        answer.SetResult();

        var response = await resumed;

        await Assert.That((bool?)response.Result?["isError"]).IsNotEqualTo(true).Because(TextOf(response.Result));
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);

        // The caller's close was answered by the browser, byte for byte, and not by
        // the child's ending under it.
        await rig.Client.ReadUntilAsync(() => AnswerTo(rig.Client, closeId) is not null);

        var closed = AnswerTo(rig.Client, closeId)!;

        await Assert.That(closed["error"]).IsNull();
        await Assert.That((bool?)closed["result"]?["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(closed["result"]?.AsObject())).IsEqualTo("### Result\nNo open tabs. Navigate to a URL to create one.");
    }

    /// <summary>
    /// A caller that stops waiting for its own <c>browser_close</c> leaves the close to
    /// finish: the child is not told to stop it, and is ended only once the browser has
    /// answered.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, where the caller's
    /// cancellation went on to the child and the child was ended at once, with the close
    /// still running in it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallerThatStopsWaitingForItsOwnCloseLeavesTheCloseToFinish()
    {
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour { HoldUntil = answer.Task };
            },
            opensDefaultSession: false,
            clock: new ManualClock());

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await OpenAsync(rig.Client, sessions, "caller-left-its-close");

        await NavigateAsync(rig.Client, directory);

        var closeId = await rig.Client.BeginAsync("tools/call", Call(LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing its browser and then giving up on the answer",
        }));

        var first = sessions.SessionChildren[0];

        await WaitUntilAsync(
            () => first.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool),
            "the caller's close never reached the child");

        await rig.Client.NotifyAsync("notifications/cancelled", new JsonObject
        {
            ["requestId"] = closeId,
            ["reason"] = "the suite's caller stopped waiting",
        });

        await WaitUntilAsync(
            () => rig.Logs.Logged(CallerLeft) || first.HasStopped || first.MethodsReceived.Contains("notifications/cancelled"),
            "the caller's cancellation of its close changed nothing anybody could see");

        await Assert.That(first.MethodsReceived).DoesNotContain("notifications/cancelled")
            .Because("the caller's cancellation reached the child, which may stop the close it is running");
        await Assert.That(first.HasStopped).IsFalse()
            .Because("the child was ended while its browser was still closing");

        answer.SetResult();

        await WaitUntilAsync(() => first.HasStopped, "the child was never ended once its close had been answered");

        // And the session is closed the way the caller's close leaves it.
        var refused = await rig.Client.RoundTripAsync("tools/call", Call("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite calling after a close it stopped waiting for",
            ["url"] = "data:text/html,<h1>ok</h1>",
        }));

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains(SessionToolSurface.Resume);
    }

    /// <summary>
    /// A shutdown that lands while the idle close is still waiting for its
    /// <c>browser_close</c> lets it finish before it ends the child.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, where the
    /// shutdown's teardown cancelled the wait, a choice recorded with Q367 a for the
    /// maintainer's review: the double was told its close was cancelled and the shutdown
    /// ended at once.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AShutdownThatMeetsAnIdleCloseStillWaitingLetsItFinish()
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

        var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        try
        {
            await NavigateAsync(harness.Client, harness.Session!);

            var first = harness.Child;

            await IdleUntilTheCloseIsHeldAsync(clock, first);

            var shuttingDown = harness.Proxy.DisposeAsync().AsTask();

            await WaitUntilAsync(
                () => shuttingDown.IsCompleted || harness.Logs.Logged(TeardownWaits),
                "the shutdown neither ended nor said it was waiting for the close");

            await Assert.That(shuttingDown.IsCompleted).IsFalse()
                .Because("the shutdown ended while the browser was still closing, so it did not wait for the close");
            await Assert.That(first.HasStopped).IsFalse();
            await Assert.That(first.MethodsReceived).DoesNotContain("notifications/cancelled");

            answer.SetResult();

            await shuttingDown.WaitAsync(TestDefaults.InProcessHang);

            await Assert.That(first.HasStopped).IsTrue();
            await Assert.That(first.MethodsReceived).DoesNotContain("notifications/cancelled");
            await Assert.That(harness.Logs.Logged(UnansweredIdleClose)).IsFalse();
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    /// <summary>
    /// A destroy that lands while the idle close is still waiting does not wait for it:
    /// it cuts the close short, says so in the session's log, and deletes the session.
    /// </summary>
    /// <remarks>
    /// <b>Decided 2026-10-04 for the maintainer's review</b>: a destroy deletes the
    /// profile the close would flush, so waiting up to the cap would keep its caller
    /// waiting for nothing. The cut itself was already the tree's behaviour; the line in
    /// the log is new, and it is what this arm was planted red on.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADestroyThatMeetsAnIdleCloseStillWaitingCutsItShort()
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

        await NavigateAsync(harness.Client, harness.Session!);

        var first = harness.Child;

        await IdleUntilTheCloseIsHeldAsync(clock, first);

        var destroyed = await harness.Client.RoundTripAsync("tools/call", Call(SessionToolSurface.Destroy, new JsonObject
        {
            ["directory"] = harness.Session!,
            ["why"] = "the suite destroying a session whose idle close is still waiting",
        }));

        await Assert.That(TextOf(destroyed)).Contains("Destroyed the session");
        await Assert.That(first.HasStopped).IsTrue();
        await Assert.That(first.MethodsReceived).Contains("notifications/cancelled");
        await Assert.That(harness.Logs.Logged(CutShort)).IsTrue();
        await Assert.That(harness.Logs.Logged(UnansweredIdleClose)).IsFalse();
    }

    /// <summary>
    /// In the session host: a kept session whose idle close is in flight refuses the
    /// next client's call without touching the close, and that client's resume waits for
    /// the close and then opens the session again for it.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>: the call was
    /// refused as it is now, and the resume ended the close at once.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InTheHostAKeptSessionsIdleCloseIsNotCutShortByTheNextClient()
    {
        var clock = new ManualClock();
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = HostSessions(answer.Task, clock);
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var gone = await rig.ConnectAsync("the client before its restart");
        var directory = await OpenAsync(gone.Client, sessions, "kept-then-idle-closed");

        await NavigateAsync(gone.Client, directory);

        var child = sessions.SessionChildren.Single();

        await gone.EndAsync();

        await Assert.That(rig.Host.Sessions.Find(directory)).IsNotNull()
            .Because("a session whose client went with its browser up is kept");

        await IdleUntilTheCloseIsHeldAsync(clock, child);

        var next = await rig.ConnectAsync("the same client after its restart");

        var refused = await next.CallAsync("browser_snapshot", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite's client after a restart, meeting a session that is closing",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(HostConnection.TextOf(refused)).Contains(SessionToolSurface.Resume);
        await Assert.That(child.ToolCallsReceived).DoesNotContain("browser_snapshot");
        await Assert.That(child.HasStopped).IsFalse();
        await Assert.That(child.MethodsReceived).DoesNotContain("notifications/cancelled");

        var resumeId = await next.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite's client resuming the session it found closing",
        }));

        var resumed = next.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);

        await WaitUntilAsync(
            () => resumed.IsCompleted || rig.Logs.Logged(ReopenWaits),
            "the resume neither answered nor said it was waiting for the close");

        await Assert.That(resumed.IsCompleted).IsFalse()
            .Because("the resume answered while the kept session's browser was still closing");
        await Assert.That(child.HasStopped).IsFalse();

        answer.SetResult();

        var response = await resumed;

        await Assert.That((bool?)response.Result?["isError"]).IsNotEqualTo(true).Because(TextOf(response.Result));
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);
        await Assert.That(child.HasStopped).IsTrue();
        await Assert.That(child.MethodsReceived).DoesNotContain("notifications/cancelled");
        await Assert.That(rig.Host.Sessions.Find(directory)!.AttachedTo).IsEqualTo(next.Proxy.Connection);
    }

    /// <summary>
    /// In the session host: a kept session whose idle close is in flight, taken over by
    /// the next client's resume, is reopened after the close, and the answer does not
    /// say its browser was kept.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, where the resume
    /// ended the close at once and its answer carried both the note that the browser
    /// had been kept running and the note that BrowserAI had closed it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InTheHostAResumeThatTakesOverAClosingSessionWaitsAndDoesNotSayItWasKept()
    {
        var clock = new ManualClock();
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = HostSessions(answer.Task, clock);
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var gone = await rig.ConnectAsync("the client before its restart");
        var directory = await OpenAsync(gone.Client, sessions, "taken-over-while-closing");

        await NavigateAsync(gone.Client, directory);

        var child = sessions.SessionChildren.Single();

        await gone.EndAsync();
        await IdleUntilTheCloseIsHeldAsync(clock, child);

        var next = await rig.ConnectAsync("the same client after its restart");

        var resumeId = await next.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite's client resuming, its first call after the restart",
        }));

        var resumed = next.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);

        await WaitUntilAsync(
            () => resumed.IsCompleted || rig.Logs.Logged(ReopenWaits),
            "the resume neither answered nor said it was waiting for the close");

        await Assert.That(resumed.IsCompleted).IsFalse()
            .Because("the resume took the closing session over and answered before its browser had finished closing");

        answer.SetResult();

        var response = await resumed;
        var text = TextOf(response.Result);

        await Assert.That((bool?)response.Result?["isError"]).IsNotEqualTo(true).Because(text);
        await Assert.That(text).DoesNotContain(SessionManager.KeptWhileItsClientWasAway);
        await Assert.That(text).Contains("BrowserAI closed this session's browser at");
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);
        await Assert.That(rig.Host.Sessions.Find(directory)!.AttachedTo).IsEqualTo(next.Proxy.Connection);
    }

    /// <summary>
    /// In the session host: a client that goes while its session's idle close is in
    /// flight leaves the close to finish before the session is let go, and the next
    /// client's resume waits for both.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, where the release
    /// of a closed session tore it down at once and cut the close short.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InTheHostAClientThatGoesDuringItsSessionsIdleCloseLeavesTheCloseToFinish()
    {
        var clock = new ManualClock();
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = HostSessions(answer.Task, clock);
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var going = await rig.ConnectAsync("the client that goes during the close");
        var directory = await OpenAsync(going.Client, sessions, "released-while-closing");

        await NavigateAsync(going.Client, directory);

        var child = sessions.SessionChildren.Single();

        await IdleUntilTheCloseIsHeldAsync(clock, child);

        var ending = going.EndAsync();

        await WaitUntilAsync(
            () => ending.IsCompleted || rig.Logs.Logged(TeardownWaits),
            "the client's end neither let the session go nor said it was waiting for the close");

        await Assert.That(child.HasStopped).IsFalse()
            .Because("the session was let go with its browser still closing");
        await Assert.That(child.MethodsReceived).DoesNotContain("notifications/cancelled");

        answer.SetResult();

        await ending.WaitAsync(TestDefaults.InProcessHang);

        await WaitUntilAsync(
            () => rig.Host.Sessions.Find(directory) is null && child.HasStopped,
            "the session was not let go once its close had been answered");

        await Assert.That(child.MethodsReceived).DoesNotContain("notifications/cancelled");

        var next = await rig.ConnectAsync("the next client");

        var resumed = await next.CallAsync("browserai_resume", new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite's next client resuming the session that was let go",
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(HostConnection.TextOf(resumed));
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);
    }

    /// <summary>
    /// Against a <b>real</b> Chromium: a resume that meets a slow idle close waits for
    /// it, a cookie written just before the close is there after the reopen, and the
    /// reopen restores the tabs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The slow close is the probe's</b>: it holds the child's <c>browser_close</c>
    /// until the arm writes a file, so BrowserAI has sent its close and the browser has
    /// not begun it when the resume arrives. A Chromium cookie is written to disk 30 s
    /// after it changes unless something commits it sooner, and a clean close commits
    /// it; a child ended through its stdin instead force-kills its browser, which kept
    /// the cookie in 15 of the 16 runs the hard-kill research timed.
    /// </para>
    /// <para>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b> on the ordering,
    /// which is the one property here a run decides every time: the resume answered
    /// while the probe still held the close.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AgainstARealBrowserAResumeDuringASlowCloseWaitsAndTheWriteBeforeItSurvives()
    {
        SuiteEnvironment.RequireProvisionedChromium();

        using var site = LoopbackSite.Start();

        var clock = new ManualClock();
        var period = TimeSpan.FromSeconds(3);
        var release = Path.Combine(ScratchRoot.Path, $"close-release-{Guid.NewGuid():N}");

        await using var sessions = RigSessionEnvironment.Create(
            opensDefaultSession: false,
            browserIdlePeriod: period,
            clock: clock,
            realSessionChildren: true,
            holdBrowserCloseUntil: release);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = await OpenAsync(rig.Client, sessions, "real-slow-close", ProvisionedBrowsers.Chromium);

        await Assert.That((bool?)(await NavigateToAsync(rig.Client, directory, site.Url("first")))["isError"]).IsNotEqualTo(true);

        var opened = await rig.Client.RoundTripAsync("tools/call", Call("browser_tabs", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a second page in a second tab",
            ["action"] = "new",
            ["url"] = site.Url("second"),
        }));

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        // The write, the last call before the session goes idle.
        var written = await rig.Client.RoundTripAsync("tools/call", Call("browser_evaluate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite writing a cookie just before the close",
            ["function"] = $"() => {{ document.cookie = '{CookieName}={CookieValue}; max-age=3600; path=/'; return document.cookie; }}",
        }));

        await Assert.That(TextOf(written)).Contains($"{CookieName}={CookieValue}");

        var child = sessions.RealSessionChildren.Single();
        var timers = clock.TimersCreated;

        // The idle close: a period at a time until it arms its cap, and not a tick
        // after, so the cap is never what ends the close.
        await WaitUntilAsync(
            () =>
            {
                if (clock.TimersCreated > timers)
                {
                    return true;
                }

                clock.Advance(period);
                return clock.TimersCreated > timers;
            },
            "the idle close never armed its cap, however far the clock was moved",
            TestDefaults.ProcessHang);

        var resumeId = await rig.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming while the browser is still closing",
        }));

        var resumed = rig.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);

        await WaitUntilAsync(
            () => resumed.IsCompleted || rig.Logs.Logged(ReopenWaits),
            "the resume neither answered nor said it was waiting for the close",
            TestDefaults.ProcessHang);

        await Assert.That(resumed.IsCompleted).IsFalse()
            .Because("the resume answered while the probe still held the browser's close");
        await Assert.That(child.HoldsMoreThanItsOwnProcesses()).IsTrue()
            .Because("the browser has to be up while its close is held");

        await File.WriteAllTextAsync(release, "the arm lets the close through");

        var response = await resumed;

        await Assert.That((bool?)response.Result?["isError"]).IsNotEqualTo(true).Because(TextOf(response.Result));
        await Assert.That(rig.Logs.Logged(UnansweredIdleClose)).IsFalse();
        await Assert.That(sessions.RealSessionChildren.Count).IsEqualTo(2);

        // The reopen restores both tabs, and the cookie written before the close is
        // on the restored origin.
        var tabs = string.Empty;
        var waited = Stopwatch.StartNew();

        while (!(tabs.Contains(site.Url("first"), StringComparison.Ordinal) && tabs.Contains(site.Url("second"), StringComparison.Ordinal)))
        {
            if (waited.Elapsed > TestDefaults.BrowserHang)
            {
                throw new TimeoutException($"the reopened browser never listed both tabs it had; the last listing was:{Environment.NewLine}{tabs}");
            }

            tabs = TextOf(await rig.Client.RoundTripAsync("tools/call", Call("browser_tabs", new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite reading which tabs the reopened browser restored",
                ["action"] = "list",
            })));
        }

        var read = await rig.Client.RoundTripAsync("tools/call", Call("browser_evaluate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite reading back the cookie written before the close",
            ["function"] = "() => document.cookie",
        }));

        await Assert.That(TextOf(read)).Contains($"{CookieName}={CookieValue}");
    }

    /// <summary>
    /// Against a <b>real</b> Chromium in the session host: a kept session whose slow
    /// idle close is in flight is resumed by the client after its restart, the resume
    /// waits for the close, and the reopened session has the cookie written before the
    /// close and both of its tabs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The host is the real one, in process</b>, with a connection per client as its
    /// pipe gives one, and the session's child is a real <c>node</c> and Chromium behind
    /// the suite's probe, which holds the close back until the arm writes a file. It is
    /// option c's own case: the client went, the session was kept with its browser up,
    /// and the idle close began before the client came back.
    /// </para>
    /// <para>
    /// <b>Planted red against the tree as it stood on 2026-10-04</b>, where the
    /// resume answered while the probe still held the close.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AgainstARealBrowserInTheHostAKeptSessionsSlowCloseIsWaitedForByTheNextClient()
    {
        SuiteEnvironment.RequireProvisionedChromium();

        using var site = LoopbackSite.Start();

        var clock = new ManualClock();
        var period = TimeSpan.FromSeconds(3);
        var release = Path.Combine(ScratchRoot.Path, $"close-release-{Guid.NewGuid():N}");

        await using var sessions = RigSessionEnvironment.Create(
            opensDefaultSession: false,
            browserIdlePeriod: period,
            clock: clock,
            realSessionChildren: true,
            holdBrowserCloseUntil: release);

        await using var rig = await SessionHostRig.StartAsync(sessions);

        var gone = await rig.ConnectAsync("the client before its restart");
        var directory = await OpenAsync(gone.Client, sessions, "host-real-slow-close", ProvisionedBrowsers.Chromium);

        await Assert.That((bool?)(await NavigateToAsync(gone.Client, directory, site.Url("first")))["isError"]).IsNotEqualTo(true);

        var opened = await gone.CallAsync("browser_tabs", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a second page in a second tab",
            ["action"] = "new",
            ["url"] = site.Url("second"),
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(TextOf(opened));

        var written = await gone.CallAsync("browser_evaluate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite writing a cookie just before its client goes",
            ["function"] = $"() => {{ document.cookie = '{CookieName}={CookieValue}; max-age=3600; path=/'; return document.cookie; }}",
        });

        await Assert.That(TextOf(written)).Contains($"{CookieName}={CookieValue}");

        // The client goes, and the host keeps the session with its browser up.
        await gone.EndAsync();

        await Assert.That(rig.Host.Sessions.Find(directory)).IsNotNull()
            .Because("a session whose client went with its browser up is kept");

        var child = sessions.RealSessionChildren.Single();
        var timers = clock.TimersCreated;

        await WaitUntilAsync(
            () =>
            {
                if (clock.TimersCreated > timers)
                {
                    return true;
                }

                clock.Advance(period);
                return clock.TimersCreated > timers;
            },
            "the kept session's idle close never armed its cap, however far the clock was moved",
            TestDefaults.ProcessHang);

        var next = await rig.ConnectAsync("the same client after its restart");

        var resumeId = await next.Client.BeginAsync("tools/call", Call(SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite's client resuming after its restart, while the browser is closing",
        }));

        var resumed = next.Client.AwaitAsync(resumeId, SessionToolSurface.Resume);

        await WaitUntilAsync(
            () => resumed.IsCompleted || rig.Logs.Logged(ReopenWaits),
            "the resume neither answered nor said it was waiting for the close",
            TestDefaults.ProcessHang);

        await Assert.That(resumed.IsCompleted).IsFalse()
            .Because("the resume answered while the probe still held the kept session's close");
        await Assert.That(child.HoldsMoreThanItsOwnProcesses()).IsTrue()
            .Because("the browser has to be up while its close is held");

        await File.WriteAllTextAsync(release, "the arm lets the close through");

        var response = await resumed;
        var text = TextOf(response.Result);

        await Assert.That((bool?)response.Result?["isError"]).IsNotEqualTo(true).Because(text);
        await Assert.That(text).DoesNotContain(SessionManager.KeptWhileItsClientWasAway);
        await Assert.That(sessions.RealSessionChildren.Count).IsEqualTo(2);
        await Assert.That(rig.Logs.Logged(UnansweredIdleClose)).IsFalse();

        var tabs = string.Empty;
        var waited = Stopwatch.StartNew();

        while (!(tabs.Contains(site.Url("first"), StringComparison.Ordinal) && tabs.Contains(site.Url("second"), StringComparison.Ordinal)))
        {
            if (waited.Elapsed > TestDefaults.BrowserHang)
            {
                throw new TimeoutException($"the reopened browser never listed both tabs it had; the last listing was:{Environment.NewLine}{tabs}");
            }

            tabs = TextOf(await next.CallAsync("browser_tabs", new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite reading which tabs the reopened browser restored",
                ["action"] = "list",
            }));
        }

        var read = await next.CallAsync("browser_evaluate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite reading back the cookie written before the close",
            ["function"] = "() => document.cookie",
        });

        await Assert.That(TextOf(read)).Contains($"{CookieName}={CookieValue}");
    }

    /// <summary>A rig for the host's arms whose sessions' close is held until the arm says so.</summary>
    /// <param name="answer">Completed when the close may answer.</param>
    /// <param name="clock">The clock the idle timer reads.</param>
    /// <returns>The environment, which the arm disposes.</returns>
    private static RigSessionEnvironment HostSessions(Task answer, ManualClock clock) =>
        RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools["browser_snapshot"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour { HoldUntil = answer };
            },
            opensDefaultSession: false,
            browserIdlePeriod: ShortPeriod,
            clock: clock);

    /// <summary>
    /// Moves the clock a period at a time until the idle close has armed its cap, and
    /// then waits until its <c>browser_close</c> has reached the double, which holds it.
    /// </summary>
    /// <remarks>
    /// <b>Asked before the clock moves, every time</b>, as
    /// <c>BrowserIdleTimerTests</c>' own loop does, so the cap is never run down.
    /// </remarks>
    /// <param name="clock">The session's clock.</param>
    /// <param name="child">The session's double.</param>
    /// <returns>A task that completes once the double holds the close.</returns>
    private static async Task IdleUntilTheCloseIsHeldAsync(ManualClock clock, FakePlaywrightChild child)
    {
        var timers = clock.TimersCreated;

        await WaitUntilAsync(
            () =>
            {
                if (clock.TimersCreated > timers || child.HasStopped)
                {
                    return true;
                }

                clock.Advance(ShortPeriod);
                return clock.TimersCreated > timers || child.HasStopped;
            },
            "the idle close neither armed a cap nor ended the child, however far the clock was moved");

        await WaitUntilAsync(
            () => child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool) || child.HasStopped,
            "the idle close never asked the browser to close");

        await Assert.That(child.ToolCallsReceived).Contains(LiveSession.BrowserCloseTool);
        await Assert.That(child.HasStopped).IsFalse();
    }

    /// <summary>Opens a session and returns its directory.</summary>
    /// <param name="client">The client to open it from.</param>
    /// <param name="sessions">The rig, whose scratch root it goes under.</param>
    /// <param name="name">The directory's leaf name.</param>
    /// <param name="browser">The family, or the default when <see langword="null"/>.</param>
    /// <returns>The session directory.</returns>
    private static async Task<string> OpenAsync(RawPipeClient client, RigSessionEnvironment sessions, string name, string? browser = null)
    {
        var directory = Path.Combine(sessions.Root, name);

        var arguments = new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session the close ordering arms open",
        };

        if (browser is not null)
        {
            arguments["browser"] = browser;
        }

        var answer = await client.RoundTripAsync("tools/call", Call(SessionToolSurface.Init, arguments));

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not open '{directory}': {TextOf(answer)}");
        }

        return directory;
    }

    /// <summary>Navigates a session, which brings its browser up.</summary>
    /// <param name="client">The client that drives it.</param>
    /// <param name="directory">The session.</param>
    /// <returns>The navigation.</returns>
    private static async Task NavigateAsync(RawPipeClient client, string directory)
    {
        var answer = await client.RoundTripAsync("tools/call", Call("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the call that starts the browser and arms the timer",
            ["url"] = "data:text/html,<h1>ok</h1>",
        }));

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The navigation on '{directory}' failed: {TextOf(answer)}");
        }
    }

    private static Task<JsonObject> NavigateToAsync(RawPipeClient client, string directory, string url) =>
        client.RoundTripAsync("tools/call", Call("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite opening a page the reopen has to bring back",
            ["url"] = url,
        }));

    private static JsonObject Call(string tool, JsonObject arguments) =>
        new()
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        };

    /// <summary>The answer to one request among every frame the client has read, or <see langword="null"/>.</summary>
    /// <param name="client">The client that sent it.</param>
    /// <param name="id">The id the request went out with.</param>
    /// <returns>The envelope.</returns>
    private static JsonObject? AnswerTo(RawPipeClient client, int id) =>
        client.FramesReceived
            .Where(frame => frame.Length is not 0)
            .Select(frame => JsonNode.Parse(Encoding.UTF8.GetString(frame)) as JsonObject)
            .FirstOrDefault(envelope => envelope?["id"] is { } received && (int?)received == id);

    private static string TextOf(JsonObject? result) =>
        string.Concat((result?["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

    /// <summary>Waits for an event, bounded by a hang detector and by no number written here.</summary>
    /// <param name="condition">The event.</param>
    /// <param name="whatWentWrong">What it means when it never comes.</param>
    /// <param name="patience">The hang detector, the suite's in-process one unless a real process is involved.</param>
    /// <returns>A task that completes once the event has happened.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition, string whatWentWrong, TimeSpan? patience = null)
    {
        var bound = patience ?? TestDefaults.InProcessHang;
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > bound)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
