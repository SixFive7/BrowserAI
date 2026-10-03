// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The session host: one set of sessions served to several connections, a session
/// that outlives the connection that opened it, and the rules that let one go.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b, the maintainer's words of 2026-10-03, verbatim:</b> <i>"Q366 b - lets go
/// with a fully build option c. If the server crashes and the coordinator loses the
/// pipe, keep the browser around with the already running activity timeout timer
/// active. This allows restarting vscode, the claude code plugin or soemthing without
/// losing the state. And the timer logic for cleaning up inactive sessions already
/// exsits. Of course if a close or destroy is called explicitly then do clean it
/// up."</i> And from P7: <i>"it all needs to be done in a super safe way so we don't
/// permanently leak stuff."</i>
/// </para>
/// <para>
/// <b>Every arm runs in process</b>, over <see cref="SessionHostRig"/>: a connection
/// ends the way a front that dies ends it, and the session doubles say whether a
/// browser is up. The real processes are the host-mode arms' business.
/// </para>
/// </remarks>
internal sealed class SessionHostTests
{
    /// <summary>An idle period the manual clock is advanced past; it never passes by itself.</summary>
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A session outlives the connection that opened it, and the next connection that
    /// names it drives the same child, with its browser as it was.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionOutlivesTheConnectionThatOpenedItAndTheNextConnectionDrivesItsBrowserAsItWas()
    {
        await using var sessions = NewSessions();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var first = await rig.ConnectAsync("the client before its restart");
        var directory = await OpenAsync(first, sessions, "kept-across-a-restart");

        await NavigateAsync(first, directory);

        var child = sessions.SessionChildren.Single();

        await Assert.That(child.BrowserIsOpen).IsTrue();

        await first.EndAsync();

        await Assert.That(rig.Host.Sessions.Find(directory)).IsNotNull()
            .Because("a session whose client went with its browser up is kept, not torn down");
        await Assert.That(child.HasStopped).IsFalse();

        var second = await rig.ConnectAsync("the same client after its restart");

        var snapshot = await second.CallAsync("browser_snapshot", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite's client after a restart, picking its session up again",
        });

        await Assert.That((bool?)snapshot["isError"]).IsNotEqualTo(true).Because(HostConnection.TextOf(snapshot));
        await Assert.That(child.ToolCallsReceived).Contains("browser_snapshot");
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1)
            .Because("the call went to the child the session already had, and none was started");
        await Assert.That(rig.Host.Sessions.Find(directory)!.AttachedTo).IsEqualTo(second.Proxy.Connection);
    }

    /// <summary>
    /// A client that re-dials the session host and calls before it lists is refused
    /// once, in words that are true of a host which may have served its list, and its
    /// next call reaches the session it kept.
    /// </summary>
    /// <remarks>
    /// <b>Found by the research of 2026-10-03 into the client at startup.</b> A Claude
    /// Code that re-dials lists nothing, measured for Q261, and the host outlives the
    /// front the client re-dials through, so the server's own sentence, that the list
    /// came from a different BrowserAI, would be false here as often as not.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientThatRedialsTheHostWithoutListingIsRefusedOnceInWordsTrueOfTheHost()
    {
        await using var sessions = NewSessions();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        const string ClientName = "claude-code";

        var first = await rig.ConnectAsync(ClientName);
        var directory = await OpenAsync(first, sessions, "redialled-without-a-list");

        await NavigateAsync(first, directory);
        await first.EndAsync();

        var redialled = await rig.ConnectAsync(ClientName, listTools: false);

        var refused = await redialled.CallAsync("browser_snapshot", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite's client after a re-dial, calling before it lists",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(HostConnection.TextOf(refused)).IsEqualTo(
            SessionErrors.ToolListPredatesThisServer("browser_snapshot", BuildVersion.Current, ClientName, throughTheSessionHost: true));
        await Assert.That(HostConnection.TextOf(refused)).DoesNotContain("it started after the tool list you are calling from was read")
            .Because("the host may well be the BrowserAI the client's list came from");
        await Assert.That(sessions.SessionChildren.Single().ToolCallsReceived).DoesNotContain("browser_snapshot");

        var served = await redialled.CallAsync("browser_snapshot", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite's client calling again, which is forwarded",
        });

        await Assert.That((bool?)served["isError"]).IsNotEqualTo(true).Because(HostConnection.TextOf(served));
        await Assert.That(sessions.SessionChildren.Single().ToolCallsReceived).Contains("browser_snapshot");
    }

    /// <summary>
    /// A session another open connection drives is refused, naming that client, and
    /// nothing reaches its child.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionAnotherOpenConnectionDrivesIsRefusedAndNothingReachesItsChild()
    {
        await using var sessions = NewSessions();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var driving = await rig.ConnectAsync("the client that opened it");
        var directory = await OpenAsync(driving, sessions, "driven-elsewhere");

        await NavigateAsync(driving, directory);

        var other = await rig.ConnectAsync("another client");

        var refused = await other.CallAsync("browser_snapshot", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite naming a session another client drives",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(HostConnection.TextOf(refused)).IsEqualTo(
            SessionErrors.SessionDrivenByAnotherClient("browser_snapshot", directory, driving.Proxy.Connection.Describe()));
        await Assert.That(sessions.SessionChildren.Single().ToolCallsReceived).DoesNotContain("browser_snapshot");

        // And the session goes on serving the client that drives it.
        var served = await driving.CallAsync("browser_snapshot", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite's driving client, still served",
        });

        await Assert.That((bool?)served["isError"]).IsNotEqualTo(true).Because(HostConnection.TextOf(served));
    }

    /// <summary>
    /// A session whose client went with no browser up is let go at once: its child
    /// ends and its directory is free.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionWhoseClientWentWithNoBrowserUpIsLetGoAtOnce()
    {
        await using var sessions = NewSessions();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var client = await rig.ConnectAsync();
        var directory = await OpenAsync(client, sessions, "nothing-to-keep");
        var child = sessions.SessionChildren.Single();

        await Assert.That(child.BrowserIsOpen).IsFalse();

        await client.EndAsync();

        await AssertLetGoAsync(rig, directory, child);
    }

    /// <summary>
    /// A session whose browser the caller closed is let go when its client goes:
    /// the close was the caller's own, and nothing is left to keep.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionTheCallerClosedIsLetGoWhenItsClientGoes()
    {
        await using var sessions = NewSessions();

        await using var rig = await SessionHostRig.StartAsync(sessions);

        var client = await rig.ConnectAsync();
        var directory = await OpenAsync(client, sessions, "closed-then-left");

        await NavigateAsync(client, directory);

        var closed = await client.CallAsync(LiveSession.BrowserCloseTool, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite closing its own browser before it goes",
        });

        await Assert.That((bool?)closed["isError"]).IsNotEqualTo(true).Because(HostConnection.TextOf(closed));

        await client.EndAsync();

        await AssertLetGoAsync(rig, directory, sessions.SessionChildren.Single());
    }

    /// <summary>
    /// A headless session whose client went is kept while its idle timer runs, and is
    /// let go by the idle close and not a tick before.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHeadlessSessionWhoseClientWentIsLetGoByItsIdleCloseAndNotBefore()
    {
        var clock = new ManualClock();

        await using var sessions = NewSessions(browserIdlePeriod: ShortPeriod, clock: clock);
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var client = await rig.ConnectAsync();
        var directory = await OpenAsync(client, sessions, "kept-until-idle");

        await NavigateAsync(client, directory);

        var child = sessions.SessionChildren.Single();

        await client.EndAsync();

        // One tick short of the period since the last call: kept.
        clock.AdvanceTicks(ShortPeriod.Ticks - ManualClock.OneTick);

        await Assert.That(rig.Host.Sessions.Find(directory)).IsNotNull();
        await Assert.That(child.HasStopped).IsFalse();

        // Past it: the idle close ends the child and the host lets the session go.
        // Advanced in a loop, because the call's in-flight scope is released after
        // its answer is on the wire, and an advance that lands before that re-arms
        // the timer for a whole period, correctly.
        await WaitUntilAsync(
            () =>
            {
                clock.Advance(ShortPeriod);
                return rig.Host.Sessions.Find(directory) is null;
            },
            "the idle close of a session whose client went never let it go");

        await AssertLetGoAsync(rig, directory, child);
    }

    /// <summary>
    /// A headed session whose client went is kept for as long as its window is open,
    /// and let go at the first look after the person has closed it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHeadedSessionWhoseClientWentIsKeptUntilItsWindowClosesThenLetGo()
    {
        var clock = new ManualClock();

        await using var sessions = NewSessions(clock: clock);
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var client = await rig.ConnectAsync();
        var directory = await OpenAsync(client, sessions, "kept-while-its-window-is-open", headed: true);

        await NavigateAsync(client, directory);

        var child = sessions.SessionChildren.Single();

        await client.EndAsync();

        // Many looks with the window open: kept, and no timer closes it (Q326 a).
        for (var look = 0; look < 10; look++)
        {
            clock.Advance(LiveSession.DetachedWindowLook);
        }

        await Assert.That(rig.Host.Sessions.Find(directory)).IsNotNull()
            .Because("a headed session is not closed for idleness, and its window is still open");
        await Assert.That(child.HasStopped).IsFalse();

        child.CloseTheWindow();

        await WaitUntilAsync(
            () =>
            {
                clock.Advance(LiveSession.DetachedWindowLook);
                return rig.Host.Sessions.Find(directory) is null;
            },
            "a headed session whose window was closed was never let go");

        await AssertLetGoAsync(rig, directory, child);
    }

    /// <summary>
    /// A resume from the next connection takes a kept session over and says that its
    /// browser was kept and nothing needed restoring.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeFromTheNextConnectionTakesOverAKeptSessionAndSaysItsBrowserWasKept()
    {
        await using var sessions = NewSessions();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var first = await rig.ConnectAsync();
        var directory = await OpenAsync(first, sessions, "resumed-after-a-restart");

        await NavigateAsync(first, directory);
        await first.EndAsync();

        var second = await rig.ConnectAsync();

        var resumed = await second.CallAsync("browserai_resume", new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite's client after a restart, resuming what it had",
        });

        var text = HostConnection.TextOf(resumed);

        await Assert.That((bool?)resumed["isError"]).IsNotEqualTo(true).Because(text);
        await Assert.That(text).Contains(SessionManager.KeptWhileItsClientWasAway);
        await Assert.That(text).Contains(SessionManager.BrowserIsUpSoNothingWasApplied);
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1).Because("the resume started no child");
        await Assert.That(rig.Host.Sessions.Find(directory)!.AttachedTo).IsEqualTo(second.Proxy.Connection);
    }

    /// <summary>
    /// A destroy from a connection that does not drive the session is refused while
    /// the client that does is still connected, and the session goes on.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADestroyFromAnotherOpenConnectionIsRefusedAndTheSessionGoesOn()
    {
        await using var sessions = NewSessions();
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var driving = await rig.ConnectAsync("the client that opened it");
        var directory = await OpenAsync(driving, sessions, "not-yours-to-destroy");
        var other = await rig.ConnectAsync("another client");

        var refused = await other.CallAsync("browserai_destroy", new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite destroying a session another client drives",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(HostConnection.TextOf(refused)).IsEqualTo(
            SessionErrors.SessionDrivenByAnotherClient("browserai_destroy", directory, driving.Proxy.Connection.Describe()));
        await Assert.That(Directory.Exists(directory)).IsTrue();
        await Assert.That(rig.Host.Sessions.Find(directory)).IsNotNull();
    }

    /// <summary>
    /// A child's progress reaches the connection that drives its session now, after a
    /// takeover, and not the connection that opened it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AChildsProgressReachesTheConnectionThatDrivesItsSessionNow()
    {
        await using var sessions = NewSessions(
            child => child.Tools["browser_snapshot"] = new FakeToolBehaviour { ProgressUpdates = 2 });

        await using var rig = await SessionHostRig.StartAsync(sessions);

        var first = await rig.ConnectAsync();
        var directory = await OpenAsync(first, sessions, "progress-after-a-takeover");

        await NavigateAsync(first, directory);
        await first.EndAsync();

        var second = await rig.ConnectAsync();

        var answered = await second.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_snapshot",
            ["arguments"] = new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite asking for progress on a session it took over",
            },
            ["_meta"] = new JsonObject { ["progressToken"] = "takeover-progress" },
        });

        await Assert.That((bool?)answered["isError"]).IsNotEqualTo(true).Because(HostConnection.TextOf(answered));

        // The relay is asynchronous, so a notification may land after the result
        // of the call that provoked it, and a client sees only what it reads.
        int progress() => second.Client.FramesReceived
            .Select(System.Text.Encoding.UTF8.GetString)
            .Count(frame => frame.Contains("notifications/progress", StringComparison.Ordinal)
                && frame.Contains("takeover-progress", StringComparison.Ordinal));

        await second.Client.ReadUntilAsync(() => progress() >= 2);

        await Assert.That(progress()).IsEqualTo(2);
    }

    /// <summary>
    /// A rig's sessions whose children answer every tool these arms call, and nothing
    /// opened up front.
    /// </summary>
    /// <remarks>
    /// <b>The fake child answers only tools an arm programmed</b>, and an unanswered
    /// <c>browser_close</c> is an error the shutdown logs and goes past, so the close
    /// is programmed too: the arms are about which sessions are kept, and a close that
    /// fails would be a second thing for a reader of the log to account for.
    /// </remarks>
    /// <param name="also">Programs anything an arm needs beyond the three tools.</param>
    /// <param name="browserIdlePeriod">The idle period, when an arm drives it.</param>
    /// <param name="clock">The clock, when an arm drives it.</param>
    /// <returns>The environment, which the arm disposes.</returns>
    private static RigSessionEnvironment NewSessions(
        Action<FakePlaywrightChild>? also = null,
        TimeSpan? browserIdlePeriod = null,
        ManualClock? clock = null) =>
        RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools["browser_snapshot"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
                also?.Invoke(child);
            },
            opensDefaultSession: false,
            browserIdlePeriod: browserIdlePeriod,
            clock: clock);

    /// <summary>Opens a session from a connection and returns its directory.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="sessions">The rig, whose scratch root the session goes under.</param>
    /// <param name="name">The directory's leaf name.</param>
    /// <param name="headed">Whether the session's browser has a window.</param>
    /// <returns>The session directory.</returns>
    private static async Task<string> OpenAsync(HostConnection connection, RigSessionEnvironment sessions, string name, bool headed = false)
    {
        var directory = Path.Combine(sessions.Root, name);

        var answer = await connection.CallAsync("browserai_init", new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session the session host's arms open",
            ["headed"] = headed,
        });

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The rig could not open '{directory}': {HostConnection.TextOf(answer)}");
        }

        return directory;
    }

    /// <summary>Navigates a session, which brings its double's browser up.</summary>
    /// <param name="connection">The connection that drives it.</param>
    /// <param name="directory">The session.</param>
    /// <returns>The navigation.</returns>
    private static async Task NavigateAsync(HostConnection connection, string directory)
    {
        var answer = await connection.CallAsync("browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,<h1>ok</h1>",
            ["session"] = directory,
            ["why"] = "the suite bringing the session's browser up",
        });

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The navigation on '{directory}' failed: {HostConnection.TextOf(answer)}");
        }
    }

    /// <summary>Asserts that the host let a session go: not held, its child ended, its directory free.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="directory">The session.</param>
    /// <param name="child">The session's double.</param>
    /// <returns>The assertion task.</returns>
    private static async Task AssertLetGoAsync(SessionHostRig rig, string directory, FakePlaywrightChild child)
    {
        await WaitUntilAsync(
            () => rig.Host.Sessions.Find(directory) is null && child.HasStopped,
            "the session was not let go: the host still holds it or its child is still running");

        var guard = SessionLock.ProbeLiveness(SessionPath.For(directory));

        await Assert.That(guard.State).IsNotEqualTo(SessionLiveness.Held)
            .Because("a session the host let go must leave its directory free");
    }

    /// <summary>Waits for a condition, failing only on a hang.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="whatWentWrong">What a hang means here.</param>
    /// <returns>The wait.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition, string whatWentWrong)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            // A hang detector, never a promptness claim: the bound is the suite's
            // own and nothing here asserts on how long the wait took.
            if (waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds:F1} s.");
            }

            await Task.Delay(50);
        }
    }
}
