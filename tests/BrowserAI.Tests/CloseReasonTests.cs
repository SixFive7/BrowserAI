// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

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
        await using var sessions = Sessions();
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

        var record = SessionLock.ReadRecord(SessionPath.For(directory))!;

        await Assert.That(record.ClosedHistory.Select(close => close.Value.Cause).ToArray()).IsEquivalentTo([SessionCloseCause.WindowClosed]);
        await Assert.That(record.ClosedHistory[^1].Value.ExitCode).IsEqualTo(0);
        await Assert.That(RecordedSession.LogOf(directory).Any(row =>
            row.Tool == CloseReasons.LogRowTool && row.Why.StartsWith("A person closed this session's browser window", StringComparison.Ordinal))).IsTrue();

        var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite resuming after a person closed the window",
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
            await using var sessions = Sessions();
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

            var record = SessionLock.ReadRecord(SessionPath.For(directory))!;

            await Assert.That(record.ClosedHistory[^1].Value.Cause)
                .IsEqualTo(code is 0 ? SessionCloseCause.BrowserEnded : SessionCloseCause.BrowserCrashed);
            await Assert.That(record.ClosedHistory[^1].Value.ExitCode).IsEqualTo(code);
        }
    }

    /// <summary>
    /// The agent's own <c>browser_close</c> is recorded with the client that sent it
    /// and the reason it gave, and the refusal after it says it was this client's.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCallersOwnCloseIsRecordedWithItsClientAndItsReason()
    {
        await using var sessions = Sessions();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "closed-by-the-caller");

        await InitAsync(rig, directory, headed: false);
        await NavigateAsync(rig, directory, "the call that brings the browser up");

        var closed = await CallAsync(rig, LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "done with the checkout page",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(TextOf(closed));

        var refused = await NavigateAsync(rig, directory, "the call after the caller's own close");

        await Assert.That(TextOf(refused)).Contains($"by a {LiveSession.BrowserCloseTool} call from this client, which gave the reason \"done with the checkout page\".");

        var close = SessionLock.ReadRecord(SessionPath.For(directory))!.ClosedHistory.Single().Value;

        await Assert.That(close.Cause).IsEqualTo(SessionCloseCause.Caller);
        await Assert.That(close.Why).IsEqualTo("done with the checkout page");
        await Assert.That(close.By).StartsWith("client ");
    }

    /// <summary>
    /// Another client's <c>browser_close</c> is named as that client's to the next one
    /// that names the session.
    /// </summary>
    /// <remarks>
    /// <b>Through the session host</b>, which is where two clients meet one session:
    /// the first closes the browser and goes, the host lets the closed session go, and
    /// the second is sent to <c>browserai_resume</c> with the first's close as the reason.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnotherClientsCloseIsNamedToTheNextClient()
    {
        await using var sessions = Sessions();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var first = await rig.ConnectAsync(clientName: "agent-one");
        var directory = Path.Combine(sessions.Root, "closed-by-another-client");

        _ = await first.CallAsync(SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session two clients meet",
        });

        _ = await first.CallAsync("browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the first client bringing the browser up",
            ["url"] = "data:text/html,<h1>one</h1>",
        });

        _ = await first.CallAsync(LiveSession.BrowserCloseTool, new JsonObject
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
        await Assert.That(text).Contains("Its last close: This session's browser was closed at");
        await Assert.That(text).Contains($"by a {LiveSession.BrowserCloseTool} call from client 'agent-one'");
        await Assert.That(text).Contains("which gave the reason \"the first client is finished with it\"");
    }

    /// <summary>
    /// A shutdown records why it closed every session, and the next BrowserAI to open
    /// one says so: a client that went away, or a stop through the pipe.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AShutdownRecordsItsCauseAndTheNextResumeSaysIt()
    {
        foreach (var throughThePipe in new[] { false, true })
        {
            var directory = Path.Combine(ScratchRoot.Path, $"shut-down-{Guid.NewGuid():N}");

            await using (var sessions = Sessions())
            {
                await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

                await InitAsync(rig, directory, headed: false);
                await NavigateAsync(rig, directory, "the call that brings the browser up");

                if (throughThePipe)
                {
                    rig.Proxy.StoppingThroughThePipe();
                }
            }

            var close = SessionLock.ReadRecord(SessionPath.For(directory))!.ClosedHistory.Single().Value;

            await Assert.That(close.Cause).IsEqualTo(throughThePipe ? SessionCloseCause.Stopped : SessionCloseCause.ServerShutDown);

            await using (var sessions = Sessions())
            {
                await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

                var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
                {
                    ["directory"] = directory,
                    ["why"] = "the suite resuming after the BrowserAI that held it ended",
                });

                await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(TextOf(resumed));
                await Assert.That(TextOf(resumed)).Contains(throughThePipe
                    ? "why this session was last closed: BrowserAI closed this session's browser at"
                    : "why this session was last closed: BrowserAI closed this session's browser at");
                await Assert.That(TextOf(resumed)).Contains(throughThePipe
                    ? "because BrowserAI was stopped, which it is to install an update"
                    : "because the BrowserAI holding it shut down when its client");
            }
        }
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
        var directory = Path.Combine(ScratchRoot.Path, $"killed-holder-{Guid.NewGuid():N}");

        await using (var sessions = Sessions())
        {
            await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

            await InitAsync(rig, directory, headed: false);
        }

        var location = SessionPath.For(directory);
        var taken = SessionLock.TryAcquire(location, new SessionLockRequest { Browser = ProvisionedBrowsers.Chromium, Purpose = "a holder the suite plays dead" }, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        using (var held = taken.Acquired!)
        {
            held.AppendLifecycle(RecordFields.Opened, SessionToolSurface.Resume);
        }

        await using (var sessions = Sessions())
        {
            await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

            var refused = await NavigateAsync(rig, directory, "a call to a session nobody drives here");

            await Assert.That(TextOf(refused)).Contains("Its last close: No close was recorded after this session was last opened, at");
            await Assert.That(TextOf(refused)).Contains("the BrowserAI holding it ended without closing it, which a kill, a crash, a sign-out or the machine stopping does.");

            var resumed = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
            {
                ["directory"] = directory,
                ["why"] = "the suite resuming after a holder that died",
            });

            await Assert.That(TextOf(resumed)).Contains("why this session was last closed: No close was recorded after this session was last opened");
        }

        // Written down by the resume that found it, so it no longer has to be inferred.
        await Assert.That(SessionLock.ReadRecord(location)!.ClosedHistory.Any(close => close.Value.Cause is SessionCloseCause.Unrecorded)).IsTrue();
    }

    /// <summary>
    /// A kept headed session whose window a person closes is recorded as theirs and let
    /// go, so the client that comes back is told why.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AKeptWindowAPersonClosesIsRecordedAsTheirsAndLetGo()
    {
        await using var sessions = Sessions();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var client = await rig.ConnectAsync();
        var directory = Path.Combine(sessions.Root, "kept-window-closed");

        _ = await client.CallAsync(SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a headed session kept while its client is away",
            ["headed"] = true,
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

        await Assert.That(SessionLock.ReadRecord(SessionPath.For(directory))!.ClosedHistory.Single().Value.Cause).IsEqualTo(SessionCloseCause.WindowClosed);

        var back = await rig.ConnectAsync();

        var resumed = await back.CallAsync(SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the client coming back to its session",
        });

        await Assert.That(HostConnection.TextOf(resumed)).Contains("why this session was last closed: A person closed this session's browser window at");
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
        SuiteEnvironment.RequireProvisionedChromium();

        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false, realSessionChildren: true);
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
            () => SessionLock.ReadRecord(SessionPath.For(directory))!.ClosedHistory.Count is not 0,
            "the session never recorded its killed browser",
            TestDefaults.ProcessHang);

        var close = SessionLock.ReadRecord(SessionPath.For(directory))!.ClosedHistory.Single().Value;

        await Assert.That(close.Cause).IsEqualTo(SessionCloseCause.BrowserCrashed);
        await Assert.That(close.ExitCode).IsEqualTo(ProcessIdentity.TerminationExitCode);

        var refused = await NavigateAsync(rig, directory, "the call after the browser was killed");

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains("which a crash or a kill leaves");
    }

    /// <summary>A rig whose doubles answer a navigation and a close.</summary>
    /// <returns>The environment, which the arm disposes.</returns>
    private static RigSessionEnvironment Sessions() =>
        RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            opensDefaultSession: false);

    private static async Task InitAsync(McpTestHarness rig, string directory, bool headed)
    {
        var answer = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose closes the suite reads",
            ["headed"] = headed,
        });

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not open '{directory}': {TextOf(answer)}");
        }
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
