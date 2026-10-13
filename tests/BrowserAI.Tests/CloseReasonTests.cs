// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// Every close of a session's browser is recorded with its reason, a browser that
/// ends with nobody asking closes the session, and every answer that sends an agent
/// to <c>browserai_resume</c> says why the session was last closed.
/// </summary>
/// <remarks>
/// <para>
/// <b>8 b, decided 2026-10-04 by the maintainer, in his words verbatim:</b> <i>"8 b -
/// log in our catchup resume that it was the user who closed it. Also whe ntelling
/// the agent it needs to resume first give it the reason for the last close. Was it a
/// user? Was it a timeout? Was it a close call from the agent or another agent?"</i>
/// Measured the same day before the change: a person closing a headed window went
/// unnoticed, and the next call started a new browser on its own.
/// </para>
/// <para>
/// <b>The layer is the in-process rig</b>, whose doubles end their browser by hand
/// with the exit code an arm is about, and the session host's rig for the arms about
/// two clients and a kept session. One arm kills a real Chromium, so the wait on a
/// real browser process is held too.
/// </para>
/// </remarks>
internal sealed class CloseReasonTests
{
    /// <summary>What the double answers a navigation with.</summary>
    private const string NavigateResult = """{"content":[{"type":"text","text":"Page URL: data:text/html,<h1>ok</h1>"}]}""";

    /// <summary>
    /// A person closing a headed session's window closes the session: the next call is
    /// refused with that reason and reaches no browser, the record says so, and the
    /// resume and <c>browserai_catch_up</c> say so too.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree before 8 b</b>: the next call was forwarded to
    /// the double and answered, as <c>@playwright/mcp</c> answers it by starting a new
    /// browser.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APersonClosingAHeadedWindowClosesTheSessionAndEveryAnswerSaysSo()
    {
        await using var sessions = await SessionsAsync();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "window-closed");

        await InitAsync(rig, directory, headed: true);
        await NavigateAsync(rig, directory, "the call that brings the window up");

        var child = sessions.SessionChildren.Single();

        child.CloseTheWindow();

        await WaitUntilAsync(() => child.HasStopped, "the session never ended its browser server after the window closed");

        var refused = await NavigateAsync(rig, directory, "the call after a person closed the window");
        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(text).StartsWith("'browser_navigate' was not run: nothing was sent to the browser.");
        await Assert.That(text).Contains("A person closed this session's browser window at");
        await Assert.That(text).Contains($"Call {SessionToolSurface.Resume} with directory='{SessionPath.For(directory).FullPath}' first");

        // Refused and not forwarded: the double heard one navigation, and no second
        // child was started.
        await Assert.That(child.ToolCallsReceived.Count(tool => tool == "browser_navigate")).IsEqualTo(1);
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);

        var record = (await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!;

        await Assert.That(record.ClosedHistory.Select(close => close.Value.Cause).ToArray()).IsEquivalentTo([SessionCloseCause.WindowClosed]);
        await Assert.That(record.ClosedHistory[^1].Value.ExitCode).IsEqualTo(0);
        await Assert.That((await RecordedSession.LogOfAsync(directory)).Any(row =>
            row.Tool == CloseReasons.LogRowTool && row.Why.StartsWith("A person closed this session's browser window", StringComparison.Ordinal))).IsTrue();

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming after a person closed the window",

            // The last run's own settings, so the resume goes through at once: since
            // 2026-10-08 (F2 d) a resume that changes them is held back once first.
            ["headed"] = true,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 60,
        });

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(TextOf(resumed));
        await Assert.That(TextOf(resumed)).Contains("A person closed this session's browser window at");

        var caughtUp = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite reading what the record says about the close",
        });

        await Assert.That(TextOf(caughtUp)).Contains("last recorded close: A person closed this session's browser window at");
    }

    /// <summary>
    /// A browser that ends with a code other than zero is read as a crash or a kill,
    /// with the code, and a hidden one that ends with zero as the browser's own exit.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABrowserThatEndsWithACodeIsACrashOrAKillAndWithZeroItsOwnExit()
    {
        foreach (var (code, says) in new[]
        {
            (1, "with exit code 1 (0x00000001), which a crash or a kill leaves"),
            (unchecked((int)0xC0000005), "with exit code -1073741819 (0xC0000005), which a crash or a kill leaves"),
            (0, "exited on its own at"),
        })
        {
#pragma warning disable CA2000 // Disposed on every path by the await using; the rule does not follow an awaited factory inside a loop.
            await using var sessions = await SessionsAsync();
#pragma warning restore CA2000
            await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

            var directory = Path.Combine(sessions.Root, $"ended-{code.ToString(CultureInfo.InvariantCulture)}");

            await InitAsync(rig, directory, headed: false);
            await NavigateAsync(rig, directory, "the call that brings the browser up");

            var child = sessions.SessionChildren.Single();

            child.CloseTheWindow(code);

            await WaitUntilAsync(() => child.HasStopped, "the session never ended its browser server after its browser ended");

            var refused = await NavigateAsync(rig, directory, "the call after the browser ended");

            await Assert.That((bool?)refused["isError"]).IsTrue();
            await Assert.That(TextOf(refused)).Contains(says);

            var record = (await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!;

            await Assert.That(record.ClosedHistory[^1].Value.Cause)
                .IsEqualTo(code is 0 ? SessionCloseCause.BrowserEnded : SessionCloseCause.BrowserCrashed);
            await Assert.That(record.ClosedHistory[^1].Value.ExitCode).IsEqualTo(code);
        }
    }

    /// <summary>
    /// A browser that ends during a forwarded call is one warning in the session's log,
    /// with its exit code and the call, whichever BrowserAI hears first: the wait on the
    /// browser, or the call's answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's 94, 2026-10-12, in his words verbatim:</b> <i>"94 your
    /// recommendation"</i>. The real Chromium arm of <see cref="ScreenshotLimitTests"/>
    /// lost its browser during a full-page capture in 7 of 77 full runs, and nothing in a
    /// run's log said how the browser had ended: the close records the exit code in the
    /// session's record, which went with the rig's scratch.
    /// </para>
    /// <para>
    /// <b>Both orders are driven.</b> First the double ends its browser and tells the
    /// session's wait at once, so the session closes and ends its browser server under the
    /// call. Then the double ends its browser without telling the wait, answers the call,
    /// and tells the wait only after that, which is the order a browser server that says
    /// in its answer that the browser closed can produce.
    /// </para>
    /// <para>
    /// <b>Planted red against the tree before the line existed</b>, the product's call to
    /// <c>SessionLog.BrowserEndedDuringACall</c> left out: no record carried its event in
    /// either order.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABrowserThatEndsDuringACallIsOneWarningWithItsExitCodeWhicheverBrowserAiHearsFirst()
    {
        foreach (var waitFirst in new[] { true, false })
        {
            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable CA2000 // Disposed on every path by the await using; the rule does not follow an awaited factory inside a loop.
            await using var sessions = await SessionsAsync(released.Task);
#pragma warning restore CA2000
            await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

            var directory = Path.Combine(sessions.Root, waitFirst ? "ended-wait-first" : "ended-answer-first");

            await InitAsync(rig, directory, headed: false);
            await NavigateAsync(rig, directory, "the call that brings the browser up");

            var child = sessions.SessionChildren.Single();
            var held = HeldCallAsync(rig, directory);

            await WaitUntilAsync(() => child.ToolCallsReceived.Contains(HeldTool), "the held call never reached the browser server");

            if (waitFirst)
            {
                child.CloseTheWindow(CrashCode);
            }
            else
            {
                child.CloseTheWindow(CrashCode, tellTheWatch: false);
                released.SetResult();
            }

            // A JSON-RPC error when the session ended its browser server under the call,
            // and the double's own answer otherwise: either is an answer.
            var answered = await held;

            await WaitUntilSettledAsync(directory);

            if (!waitFirst)
            {
                child.TellTheWatch();
                await WaitUntilAsync(() => child.HasStopped, "the session never ended its browser server after its browser ended");
            }

            released.TrySetResult();

            var said = EndsDuringACall(rig);
            var why = $"{(waitFirst ? "the wait first" : "the answer first")}; the call was answered: {answered.Envelope.ToJsonString()}";

            await Assert.That(said.Count).IsEqualTo(1).Because(why);
            await Assert.That(said[0].Level).IsEqualTo(LogLevel.Warning).Because(why);
            await Assert.That(said[0].Message)
                .IsEqualTo($"The browser of {SessionPath.For(directory).FullPath} ended with exit code -1073741819 (0xC0000005) while '{HeldTool}' was running; BrowserAI had not asked it to close.")
                .Because(why);
        }
    }

    /// <summary>
    /// A browser that ends between calls is no such warning: it came under no call, and
    /// the close's record is what says how it ended.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against a product that wrote the line where the wait on the browser
    /// fires</b>, whatever was running then.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABrowserThatEndsBetweenCallsIsNoSuchWarning()
    {
        await using var sessions = await SessionsAsync();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "ended-between-calls");

        await InitAsync(rig, directory, headed: false);
        await NavigateAsync(rig, directory, "the call that brings the browser up");

        var child = sessions.SessionChildren.Single();

        child.CloseTheWindow(CrashCode);

        await WaitUntilAsync(() => child.HasStopped, "the session never ended its browser server after its browser ended");

        var refused = await NavigateAsync(rig, directory, "the call after the browser ended");

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That((await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!.ClosedHistory.Single().Value.ExitCode).IsEqualTo(CrashCode);
        await Assert.That(EndsDuringACall(rig)).IsEmpty();
    }

    /// <summary>
    /// A browser BrowserAI closes while a call runs is no such warning: the agent's own
    /// <c>browserai_close</c> asked for that end.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against a product that wrote the line without asking whether a
    /// close of BrowserAI's own was in flight or recorded.</b>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABrowserThatBrowserAiClosesUnderACallIsNoSuchWarning()
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = await SessionsAsync(released.Task);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "closed-under-a-call");

        await InitAsync(rig, directory, headed: false);
        await NavigateAsync(rig, directory, "the call that brings the browser up");

        var child = sessions.SessionChildren.Single();

        // Two calls outstanding on one client, so both are begun and their answers read
        // in one place: a client waiting for one answer drops the other's.
        var heldId = await rig.Client.BeginAsync("tools/call", HeldCall(directory));

        await WaitUntilAsync(() => child.ToolCallsReceived.Contains(HeldTool), "the held call never reached the browser server");

        var closeId = await rig.Client.BeginAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Close,
            ["arguments"] = new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite closing the browser while a call runs",
            },
        });

        await rig.Client.ReadUntilAsync(() => AnswerTo(rig, heldId) is not null && AnswerTo(rig, closeId) is not null);

        var closed = AnswerTo(rig, closeId)!;

        await Assert.That(closed["result"]?["isError"]?.GetValue<bool>()).IsNotEqualTo(true).Because(closed.ToJsonString());

        await WaitUntilSettledAsync(directory);
        released.TrySetResult();

        await Assert.That((await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!.ClosedHistory.Single().Value.Cause).IsEqualTo(SessionCloseCause.Caller);
        await Assert.That(EndsDuringACall(rig)).IsEmpty();
    }

    /// <summary>
    /// A call that ran beside a <c>browser_tabs</c> close is no such warning when the
    /// browser exits cleanly, which is what a last tab's close does to Firefox; a crash
    /// beside the same close is still one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found on 2026-10-12 by a reading of the release texts</b>: only the closing
    /// call's own scope was left out, so a call running beside it, whose answer reached
    /// BrowserAI before the wait on the browser fired, wrote that BrowserAI had not asked
    /// the browser to close, and the close that followed recorded the agent's last tab.
    /// <b>Two calls of one session do run at once</b>: nothing above <c>SessionManager</c>
    /// serialises tool calls, and both halves of this arm hold a <c>browser_tabs</c> close
    /// in flight at the double beside a held call of the same session.
    /// </para>
    /// <para>
    /// <b>Planted red against the tree before the fix</b>: the clean exit's half wrote
    /// the line, naming <c>'browser_snapshot'</c> and exit code 0. The crash's half was
    /// watched red against a fix that left out every call beside a tab close, whatever
    /// the exit code: <i>"Expected to be 1, because a crash beside a tab close but found
    /// 0"</i>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallBesideATabCloseThatEndedTheBrowserIsNoSuchWarningAndACrashBesideItIsOne()
    {
        await BesideATabCloseAsync(0, SessionCloseCause.LastTabClosed);
        await BesideATabCloseAsync(CrashCode, SessionCloseCause.BrowserCrashed);
    }

    /// <summary>
    /// Holds a call open, starts a <c>browser_tabs</c> close beside it, ends the browser
    /// with <paramref name="code"/> while both run, answers the held call before the wait on
    /// the browser hears of it, and then reads what the session wrote and recorded.
    /// </summary>
    /// <param name="code">The code the browser ends with: 0 for a last tab's close, or a crash's.</param>
    /// <param name="closedFor">The close the session must record.</param>
    /// <returns>The assertion task.</returns>
    private static async Task BesideATabCloseAsync(int code, SessionCloseCause closedFor)
    {
        var snapshotReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tabsReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = await SessionsAsync(snapshotReleased.Task, tabsReleased.Task);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, $"beside-a-tab-close-{code.ToString(CultureInfo.InvariantCulture)}");

        await InitAsync(rig, directory, headed: false);
        await NavigateAsync(rig, directory, "the call that brings the browser up");

        var child = sessions.SessionChildren.Single();

        // Two calls outstanding on one client, so both are begun and their answers read
        // in one place, as in the arm about BrowserAI's own close.
        var heldId = await rig.Client.BeginAsync("tools/call", HeldCall(directory));

        await WaitUntilAsync(() => child.ToolCallsReceived.Contains(HeldTool), "the held call never reached the browser server");

        var tabCloseId = await rig.Client.BeginAsync("tools/call", new JsonObject
        {
            ["name"] = TabsTool,
            ["arguments"] = new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite closing the last tab beside a running call",
                ["action"] = "close",
            },
        });

        await WaitUntilAsync(() => child.ToolCallsReceived.Contains(TabsTool), "the tab close never reached the browser server");

        // The browser ends while both run, and the held call's answer reaches BrowserAI
        // before the wait on the browser hears of it.
        child.CloseTheWindow(code, tellTheWatch: false);
        snapshotReleased.SetResult();

        await rig.Client.ReadUntilAsync(() => AnswerTo(rig, heldId) is not null);
        await WaitUntilSettledAsync(directory);

        tabsReleased.SetResult();

        await rig.Client.ReadUntilAsync(() => AnswerTo(rig, tabCloseId) is not null);
        await WaitUntilSettledAsync(directory, TabsTool);

        child.TellTheWatch();

        await WaitUntilAsync(() => child.HasStopped, "the session never ended its browser server after its browser ended");

        // ⚠️ Waited for since 2026-10-13. The session hands its close to the store's thread
        // and ends the browser server without waiting for the write (82 b), so a read at the
        // stop can come before it: the release run of 2.0.0 at bed09d9e read no close here,
        // and the store held it afterwards, BrowserCrashed with the crash's exit code.
        var path = SessionPath.For(directory);

        await WaitUntilAsync(
            async () => (await SessionLock.ReadRecordAsync(path))?.ClosedHistory.Count > 0,
            "the session never recorded the close of its browser");

        var said = EndsDuringACall(rig);
        var why = code is 0 ? "a clean exit beside a tab close" : "a crash beside a tab close";

        await Assert.That((await SessionLock.ReadRecordAsync(path))!.ClosedHistory.Single().Value.Cause).IsEqualTo(closedFor).Because(why);

        if (code is 0)
        {
            await Assert.That(said).IsEmpty().Because(why);
            return;
        }

        await Assert.That(said.Count).IsEqualTo(1).Because(why);
        await Assert.That(said[0].Message)
            .IsEqualTo($"The browser of {SessionPath.For(directory).FullPath} ended with exit code -1073741819 (0xC0000005) while '{HeldTool}' was running; BrowserAI had not asked it to close.")
            .Because(why);
    }

    /// <summary>
    /// The agent's own close is recorded with the client that sent it and the reason
    /// it gave, and the refusal after it says it was this client's.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The close is <c>browserai_close</c> since 2026-10-08, F1 a</b> (previously
    /// Playwright's <c>browser_close</c>, forwarded, which is denied since that day).
    /// Planted red against the tree with the tool listed and not answered.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCallersOwnCloseIsRecordedWithItsClientAndItsReason()
    {
        await using var sessions = await SessionsAsync();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "closed-by-the-caller");

        await InitAsync(rig, directory, headed: false);
        await NavigateAsync(rig, directory, "the call that brings the browser up");

        var closed = await CallAsync(rig, SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "done with the checkout page",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(TextOf(closed));

        var refused = await NavigateAsync(rig, directory, "the call after the caller's own close");

        await Assert.That(TextOf(refused)).Contains($"by a {SessionToolSurface.Close} call from this conversation, which gave the reason \"done with the checkout page\".");

        var close = (await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!.ClosedHistory.Single().Value;

        await Assert.That(close.Cause).IsEqualTo(SessionCloseCause.Caller);
        await Assert.That(close.Why).IsEqualTo("done with the checkout page");

        // ⚠️ Since 2026-10-10, the texts polish, page #27 (previously the record held
        // "client 'name' (its BrowserAI server is pid N)"): what a model reads back is
        // the conversation's client and folder, and the rig's client names no folder.
        await Assert.That(close.By).IsEqualTo("BrowserAI.RawPipeClient");
    }

    /// <summary>
    /// Another client's close is named as that client's to the next one that names
    /// the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Through the session host</b>, which is where two clients meet one session:
    /// the first closes the browser and goes, the host lets the closed session go, and
    /// the second is sent to <c>browserai_resume</c> with the first's close as the reason.
    /// </para>
    /// <para>
    /// ⚠️ <b>The close is <c>browserai_close</c> since 2026-10-08, F1 a</b> (previously
    /// <c>browser_close</c>). Planted red against the tree with the tool listed and not
    /// answered.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnotherClientsCloseIsNamedToTheNextClient()
    {
        await using var sessions = await SessionsAsync();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var first = await rig.ConnectAsync(clientName: "agent-one");
        var directory = Path.Combine(sessions.Root, "closed-by-another-client");

        _ = await first.CallAsync(SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session two clients meet",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        _ = await first.CallAsync("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the first client bringing the browser up",
            ["url"] = "data:text/html,<h1>one</h1>",
        });

        _ = await first.CallAsync(SessionToolSurface.Close, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the first client is finished with it",
        });

        await first.EndAsync();

        await WaitUntilAsync(() => rig.Host.Sessions.Find(directory) is null, "the closed session was never let go after its client went");

        var second = await rig.ConnectAsync(clientName: "agent-two");

        var refused = await second.CallAsync("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the second client arriving at the session",
            ["url"] = "data:text/html,<h1>two</h1>",
        });

        var text = HostConnection.TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(text).Contains("Its last close: This session was closed at");
        await Assert.That(text).Contains($"by a {SessionToolSurface.Close} call from a conversation (agent-one)");
        await Assert.That(text).DoesNotContain("pid");
        await Assert.That(text).Contains("which gave the reason \"the first client is finished with it\"");
    }

    /// <summary>
    /// A shutdown records why it closed every session, and the next BrowserAI to open
    /// one says so: an update, a stop through the pipe, a failure, or, told nothing, a
    /// client that went away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-10 (previously two causes, the second marked through the
    /// proxy's <c>StoppingThroughThePipe</c>)</b>, for the texts review's #24: nothing in
    /// the product called that mark, so every session an update or a stop closed was
    /// recorded as shut down when its client went away. The background now says why it
    /// ends through <see cref="SessionManager.ShuttingDownBecause"/>, and this arm holds
    /// each cause's record and its sentence.
    /// </para>
    /// <para>
    /// <b>Through the session host's rig</b>, whose host the arm ends itself while the
    /// client is still connected, so the session is closed by the shutdown and never
    /// judged by a detach.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a <c>ShuttingDownBecause</c> that kept
    /// nothing: the three declared causes were each recorded as
    /// <see cref="SessionCloseCause.ServerShutDown"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AShutdownRecordsItsCauseAndTheNextResumeSaysIt()
    {
        var causes = new (SessionCloseCause? Declared, SessionCloseCause Recorded, string Said)[]
        {
            (null, SessionCloseCause.ServerShutDown, "because the BrowserAI holding it shut down when its client, agent-one, went away"),
            (SessionCloseCause.Updating, SessionCloseCause.Updating, "to install an update."),
            (SessionCloseCause.Stopped, SessionCloseCause.Stopped, "because it was asked to stop."),
            (SessionCloseCause.Failed, SessionCloseCause.Failed, "because it ended on a failure, which its log names."),
        };

        foreach (var (declared, recorded, said) in causes)
        {
            var directory = Path.Combine(await ScratchRoot.PathAsync(), $"shut-down-{Guid.NewGuid():N}");

            await using (var sessions = await SessionsAsync())
            {
                await using var rig = await SessionHostRig.StartAsync(sessions);

                var client = await rig.ConnectAsync(clientName: "agent-one");

                await OpenWithABrowserAsync(client, directory);

                if (declared is { } cause)
                {
                    rig.Host.Sessions.ShuttingDownBecause(cause);
                }

                await rig.Host.DisposeAsync();
            }

            var close = (await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!.ClosedHistory.Single().Value;

            await Assert.That(close.Cause).IsEqualTo(recorded);

#pragma warning disable CA2000 // Disposed on every path by the await using; the rule does not follow an awaited factory inside a loop.
            await using (var sessions = await SessionsAsync())
#pragma warning restore CA2000
            {
                await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

                var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
                {
                    ["directory"] = directory,
                    ["why"] = "the suite resuming after the BrowserAI that held it ended",
                    ["headed"] = false,
                    ["transcript"] = false,
                    ["captureNetwork"] = false,
                    ["idleMinutes"] = 10,
                });

                await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(TextOf(resumed));
                await Assert.That(TextOf(resumed)).Contains("why this session was last closed: BrowserAI closed this session");
                await Assert.That(TextOf(resumed)).Contains(said).Because($"a shutdown told {declared?.ToString() ?? "nothing"} says so");
            }
        }
    }

    /// <summary>
    /// The update's close of every session, the person's <i>Install now</i>, records the
    /// update as each session's reason, and the sentence every later answer quotes says
    /// so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-10 for the texts review's #24</b>: the update core closes the
    /// sessions through <see cref="Background.BackgroundSessions"/>, which disposed the
    /// host with no reason given, so each read as shut down when its client went away.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against <c>BackgroundSessions.CloseAllAsync</c>
    /// without its <c>ShuttingDownBecause</c>: the record held
    /// <see cref="SessionCloseCause.ServerShutDown"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdatesCloseOfEverySessionRecordsTheUpdateAsItsReason()
    {
        await using var sessions = await SessionsAsync();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var client = await rig.ConnectAsync(clientName: "agent-one");
        var directory = Path.Combine(sessions.Root, "closed-for-an-update");

        await OpenWithABrowserAsync(client, directory);

        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        await new Background.BackgroundSessions(rig.Host).CloseAllAsync(hang.Token);

        var close = (await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!.LastClose!;

        await Assert.That(close.Value.Cause).IsEqualTo(SessionCloseCause.Updating);
        await Assert.That(CloseReasons.Of(close)).IsEqualTo($"BrowserAI closed this session at {SessionErrors.When(close.At)} to install an update.");
    }

    /// <summary>
    /// The background's own end closes every session for the state it ends in: an
    /// update's hand-over, a stop through the pipe, and anything else, which is a serve
    /// that threw.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-10 for the texts review's #24, and planted red</b> against a
    /// mapping that answered <see cref="SessionCloseCause.ServerShutDown"/> for every
    /// state, the reason the background gave by giving none.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheBackgroundsEndClosesEverySessionForTheStateItEndsIn()
    {
        await Assert.That(Program.WhySessionsClose(Background.BackgroundState.Updating)).IsEqualTo(SessionCloseCause.Updating);
        await Assert.That(Program.WhySessionsClose(Background.BackgroundState.Stopping)).IsEqualTo(SessionCloseCause.Stopped);
        await Assert.That(Program.WhySessionsClose(Background.BackgroundState.Serving)).IsEqualTo(SessionCloseCause.Failed);
    }

    /// <summary>
    /// An opening no close followed reads as a BrowserAI that ended without closing
    /// the session -- killed, crashed or stopped with the machine -- in the refusal
    /// that sends an agent to resume, in the resume, and in the record from then on.
    /// </summary>
    /// <remarks>
    /// <b>What a killed holder leaves is planted by hand</b>: an <c>opened</c>
    /// statement and no close after it, written through the session's own lock the
    /// way an opening writes it, because no arm can kill the process it runs in.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnOpeningNoCloseFollowedIsReadAsAKillOrACrash()
    {
        var directory = Path.Combine(await ScratchRoot.PathAsync(), $"killed-holder-{Guid.NewGuid():N}");

        await using (var sessions = await SessionsAsync())
        {
            await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

            await InitAsync(rig, directory, headed: false);
        }

        var location = SessionPath.For(directory);
        var taken = await SessionLock.TryAcquireAsync(location, new SessionLockRequest { Browser = ProvisionedBrowsers.Chromium, Purpose = "a holder the suite plays dead" }, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        await using (var held = taken.Acquired!)
        {
            await held.AppendLifecycleAsync(RecordFields.Opened, SessionToolSurface.Resume);
        }

        await using (var sessions = await SessionsAsync())
        {
            await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

            var refused = await NavigateAsync(rig, directory, "a call to a session nobody drives here");

            await Assert.That(TextOf(refused)).Contains("Its last close: No close was recorded after this session was last opened, at");
            await Assert.That(TextOf(refused)).Contains("the BrowserAI holding it ended without closing it, which a kill, a crash, a sign-out or the machine stopping does.");

            var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
            {
                ["directory"] = directory,
                ["why"] = "the suite resuming after a holder that died",
                ["headed"] = false,
                ["transcript"] = false,
                ["captureNetwork"] = false,
                ["idleMinutes"] = 10,
            });

            await Assert.That(TextOf(resumed)).Contains("why this session was last closed: No close was recorded after this session was last opened");
        }

        // Written down by the resume that found it, so it no longer has to be inferred.
        await Assert.That((await SessionLock.ReadRecordAsync(location))!.ClosedHistory.Any(close => close.Value.Cause is SessionCloseCause.Unrecorded)).IsTrue();
    }

    /// <summary>
    /// A kept headed session whose window a person closes is recorded as theirs and let
    /// go, so the client that comes back is told why.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AKeptWindowAPersonClosesIsRecordedAsTheirsAndLetGo()
    {
        await using var sessions = await SessionsAsync();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var client = await rig.ConnectAsync();
        var directory = Path.Combine(sessions.Root, "kept-window-closed");

        _ = await client.CallAsync(SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a headed session kept while its client is away",
            ["headed"] = true,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 60,
        });

        _ = await client.CallAsync("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "bringing the window up",
            ["url"] = "data:text/html,<h1>kept</h1>",
        });

        var child = sessions.SessionChildren.Single();

        await client.EndAsync();

        await Assert.That(rig.Host.Sessions.Find(directory)).IsNotNull().Because("a headed session is kept while its window is open");

        child.CloseTheWindow();

        await WaitUntilAsync(() => rig.Host.Sessions.Find(directory) is null, "a kept session whose window was closed was never let go");

        await Assert.That((await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!.ClosedHistory.Single().Value.Cause).IsEqualTo(SessionCloseCause.WindowClosed);

        var back = await rig.ConnectAsync();

        var resumed = await back.CallAsync(SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the client coming back to its session",

            // The last run's own settings, as in the arm above.
            ["headed"] = true,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 60,
        });

        await Assert.That(HostConnection.TextOf(resumed)).Contains("why this session was last closed: A person closed this session's browser window at");
    }

    /// <summary>
    /// A close made by another conversation is said with that conversation named by its
    /// client and its folder, never by a title and never by a process id, for
    /// <c>browserai_close</c> and for a resume's switch alike; to the conversation that
    /// closed it, it is "this conversation", and read back from the record "a
    /// conversation".
    /// </summary>
    /// <remarks>
    /// <b>Rewritten 2026-10-10, the texts polish, pages #19 and #27</b> (previously
    /// <c>AnotherClientsCloseNamesThatClientOnce</c>, added the same day from the texts
    /// review, which read "from another client, client 'claude-code' (its BrowserAI server
    /// is pid ...)", and held "from another client 'claude-code' (its BrowserAI server is
    /// pid 4242)"). The record keeps the same words, so a close read back later carries no
    /// pid either. Planted red against <see cref="CallerConnection.Conversation"/> standing
    /// in for <see cref="CallerConnection.Describe"/> and the old sentences.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnotherConversationsCloseNamesItsClientAndFolderAndNeverAPid()
    {
        var closer = new CallerConnection(clientProcessId: 4242) { Folder = @"C:\Source\BrowserAI" };
        var reader = new CallerConnection(clientProcessId: 4343) { Folder = @"C:\Source\Elsewhere" };

        closer.Introduced("claude-code");
        reader.Introduced("codex-mcp-client");

        foreach (var cause in new[] { SessionCloseCause.Caller, SessionCloseCause.SettingsChanged })
        {
            var closure = new SessionClosure(cause, DateTimeOffset.UnixEpoch, IdlePeriod: null)
            {
                ClosedBy = closer,
                By = closer.Conversation(),
                Why = "the suite closing it",
            };

            var toAnother = CloseReasons.Of(closure, reader);
            var toItsOwn = CloseReasons.Of(closure, closer);
            var forTheRecord = CloseReasons.Of(closure, asking: null);

            await Assert.That(toAnother).Contains("call from another conversation (Claude Code in BrowserAI), which gave the reason \"the suite closing it\".").Because(toAnother);
            await Assert.That(toItsOwn).Contains("call from this conversation, which gave the reason \"the suite closing it\".").Because(toItsOwn);
            await Assert.That(forTheRecord).Contains("call from a conversation (Claude Code in BrowserAI), which gave the reason \"the suite closing it\".").Because(forTheRecord);

            foreach (var said in new[] { toAnother, toItsOwn, forTheRecord })
            {
                await Assert.That(said).DoesNotContain("pid").Because(said);
                await Assert.That(said).DoesNotContain("4242").Because(said);
            }
        }
    }

    /// <summary>
    /// Against a <b>real</b> Chromium: a browser killed from outside closes the session
    /// the moment it ends, with its exit code as the reason, and nothing starts a new one.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against the tree before 8 b</b>: the call after the kill was
    /// forwarded, and <c>@playwright/mcp</c> started a new browser on its own.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AgainstARealChromiumAKilledBrowserClosesTheSessionWithItsExitCode()
    {
        await SuiteEnvironment.RequireProvisionedChromiumAsync();

        await using var sessions = await RigSessionEnvironment.CreateAsync(opensDefaultSession: false, realSessionChildren: true);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "real-killed");

        await InitAsync(rig, directory, headed: false);

        var navigated = await NavigateAsync(rig, directory, "the call that starts the real browser");

        await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true).Because(TextOf(navigated));

        var child = sessions.RealSessionChildren.Single();
        var executable = Path.Combine(BrowserAiPaths.BrowsersDirectory, $"chromium-{BrowserAiPaths.ChromiumRevision}", ProvisionedBrowsers.ExecutableWithin(ProvisionedBrowsers.Chromium)!);

        int processId;
        long created;

        using (var browser = BrowserProcesses.HoldTheEarliest(child.JobProcessIds(), executable)
            ?? throw new InvalidOperationException($"No process in the session's job runs '{executable}'."))
        {
            (processId, created) = (browser.ProcessId, browser.CreatedFileTime);
        }

        ProcessIdentity.Terminate(processId, created);

        await WaitUntilAsync(
            async () => (await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!.ClosedHistory.Count is not 0,
            "the session never recorded its killed browser",
            TestDefaults.ProcessHang);

        var close = (await SessionLock.ReadRecordAsync(SessionPath.For(directory)))!.ClosedHistory.Single().Value;

        await Assert.That(close.Cause).IsEqualTo(SessionCloseCause.BrowserCrashed);
        await Assert.That(close.ExitCode).IsEqualTo(ProcessIdentity.TerminationExitCode);

        var refused = await NavigateAsync(rig, directory, "the call after the browser was killed");

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains("which a crash or a kill leaves");
    }

    /// <summary>
    /// A rig whose doubles answer a navigation and a close, and hold a call of
    /// <see cref="HeldTool"/> open until an arm lets it go.
    /// </summary>
    /// <param name="holdUntil">What lets a held call answer, or <see langword="null"/> for a rig that holds none.</param>
    /// <param name="holdTheTabsUntil">What lets a held <see cref="TabsTool"/> call answer, or <see langword="null"/> for a rig that holds none.</param>
    /// <returns>The environment, which the arm disposes.</returns>
    private static async Task<RigSessionEnvironment> SessionsAsync(Task? holdUntil = null, Task? holdTheTabsUntil = null) =>
        await RigSessionEnvironment.CreateAsync(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();

                if (holdUntil is not null)
                {
                    child.Tools[HeldTool] = new FakeToolBehaviour { HoldUntil = holdUntil };
                }

                if (holdTheTabsUntil is not null)
                {
                    child.Tools[TabsTool] = new FakeToolBehaviour { HoldUntil = holdTheTabsUntil };
                }
            },
            opensDefaultSession: false);

    /// <summary>The tool whose call the double holds open while its browser ends.</summary>
    private const string HeldTool = "browser_snapshot";

    /// <summary>Upstream's tab tool, whose <c>close</c> ends Firefox when it closes the last tab.</summary>
    private const string TabsTool = "browser_tabs";

    /// <summary>The code the double's browser ends with under a call: an access violation's.</summary>
    private const int CrashCode = unchecked((int)0xC0000005);

    /// <summary>The event id of <c>SessionLog.BrowserEndedDuringACall</c>.</summary>
    private const int EndedDuringACallEvent = 14;

    /// <summary>
    /// Starts the call the double holds open, and does not wait for its answer, which
    /// may be a JSON-RPC error.
    /// </summary>
    /// <param name="rig">The rig.</param>
    /// <param name="directory">The session.</param>
    /// <returns>The call's answer, once it has one.</returns>
    private static Task<RawResponse> HeldCallAsync(McpTestHarness rig, string directory) =>
        rig.Client.SendAsync("tools/call", HeldCall(directory));

    /// <summary>The parameters of a call of <see cref="HeldTool"/> on a session.</summary>
    /// <param name="directory">The session.</param>
    /// <returns>The <c>tools/call</c> parameters.</returns>
    private static JsonObject HeldCall(string directory) => new()
    {
        ["name"] = HeldTool,
        ["arguments"] = new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the call the browser ends under",
        },
    };

    /// <summary>The answer the rig's client has read to one request, or <see langword="null"/> before it has one.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="id">The request's id.</param>
    /// <returns>The answer's envelope.</returns>
    private static JsonObject? AnswerTo(McpTestHarness rig, int id) =>
        rig.Client.FramesReceived
            .Where(static frame => frame.Length is not 0)
            .Select(static frame => JsonNode.Parse(FrameChannel.TextOf(frame)) as JsonObject)
            .FirstOrDefault(envelope => envelope?["id"] is JsonValue answered && answered.TryGetValue<int>(out var number) && number == id);

    /// <summary>
    /// Waits until a held call's row in the session's record is settled. The proxy
    /// settles it after the call's scope has ended, so whatever the scope writes is
    /// written by then.
    /// </summary>
    /// <param name="directory">The session.</param>
    /// <param name="tool">The held call's tool.</param>
    /// <returns>The wait.</returns>
    private static Task WaitUntilSettledAsync(string directory, string tool = HeldTool) =>
        WaitUntilAsync(
            async () => (await RecordedSession.LogOfAsync(directory)).Any(row => row.Tool == tool && row.Outcome != SessionStore.InFlight),
            $"the row of the held '{tool}' call in the session's record was never settled");

    /// <summary>Every line a session of this rig wrote saying its browser ended during a call.</summary>
    /// <param name="rig">The rig.</param>
    /// <returns>The lines, in the order they were written.</returns>
    private static List<LogRecord> EndsDuringACall(McpTestHarness rig) =>
        [.. rig.Logs.Records.Where(record => record.EventId.Id is EndedDuringACallEvent && record.Category == typeof(LiveSession).FullName)];

    private static async Task InitAsync(McpTestHarness rig, string directory, bool headed)
    {
        var answer = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose closes the suite reads",
            ["headed"] = headed,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = headed ? 60 : 10,
        });

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not open '{directory}': {TextOf(answer)}");
        }
    }

    /// <summary>Opens a session through a host connection and brings its browser up.</summary>
    /// <param name="client">The connection.</param>
    /// <param name="directory">The session.</param>
    /// <returns>A task that completes once the browser is up.</returns>
    private static async Task OpenWithABrowserAsync(HostConnection client, string directory)
    {
        var opened = await client.CallAsync(SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose closes the suite reads",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        if ((bool?)opened["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not open '{directory}': {HostConnection.TextOf(opened)}");
        }

        _ = await client.CallAsync("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the call that brings the browser up",
            ["url"] = "data:text/html,<h1>ok</h1>",
        });
    }

    private static Task<JsonObject> NavigateAsync(McpTestHarness rig, string directory, string why) =>
        CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = why,
            ["url"] = "data:text/html,<h1>ok</h1>",
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

    /// <summary>Waits for an event, bounded by a hang detector and by no number written here.</summary>
    /// <param name="condition">The event.</param>
    /// <param name="whatWentWrong">What it means when it never comes.</param>
    /// <param name="patience">The hang detector, the suite's in-process one unless a real process is involved.</param>
    /// <returns>A task that completes once the event has happened.</returns>
    private static Task WaitUntilAsync(Func<bool> condition, string whatWentWrong, TimeSpan? patience = null) =>
        WaitUntilAsync(() => Task.FromResult(condition()), whatWentWrong, patience);

    /// <summary>Waits until an awaited condition holds, or throws naming what never happened.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="whatWentWrong">What a timeout means.</param>
    /// <param name="patience">The hang detector, or <see langword="null"/> for the suite's in-process one.</param>
    /// <returns>The wait.</returns>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string whatWentWrong, TimeSpan? patience = null)
    {
        var bound = patience ?? TestDefaults.InProcessHang;
        var waited = Stopwatch.StartNew();

        while (!await condition())
        {
            if (waited.Elapsed > bound)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
