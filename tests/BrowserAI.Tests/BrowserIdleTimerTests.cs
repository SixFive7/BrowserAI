// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Sessions;
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
/// hundreds of milliseconds — but a test-friendly value leaking into the product
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
    /// <see cref="TimeProvider.System"/> — neither matches, because both have
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
        // assignment scan below cannot see an initialiser — `{ get; init; } =`
        // has something other than whitespace before the `=` and never matches,
        // which is exactly what keeps the scan from flagging the declaration.
        var seam = RepositoryLayout.ProductSourceFiles
            .Single(file => file.Name is "SessionEnvironment.cs");

        await Assert.That(await File.ReadAllTextAsync(seam.FullName))
            .Contains("public TimeProvider Clock { get; init; } = TimeProvider.System;");

        var offenders = RepositoryLayout.ProductSourceFiles
            .Where(file => ClockAssignment().IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName))
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    /// <summary>
    /// The tool the timer calls is upstream's, by the name upstream publishes.
    /// </summary>
    /// <remarks>
    /// <b>An upstream rename must turn the build red rather than turning the
    /// timer into a no-op.</b> A <c>tools/call</c> naming a tool that no longer
    /// exists is answered with an error the timer logs and nothing else notices —
    /// and the browser then stays open forever, which is exactly the defect this
    /// step exists to remove, restored silently by a version bump.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCloseToolIsUpstreamsOwnAndIsCallableInEveryMode()
    {
        await Assert.That(UpstreamSurface.DefaultSurface()).Contains(LiveSession.BrowserCloseTool);

        // Callable at all: the timer bypasses the decision anyway — it is
        // BrowserAI calling its own child rather than a caller calling a tool —
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
        // asking `Decide(tool, mode)`). `Decide` no longer takes a mode: the one
        // tool it refuses is refused everywhere, so a per-mode loop here would
        // ask the same question three times and read as coverage it is not.
        await Assert.That(SessionToolPolicy.Decide(LiveSession.BrowserCloseTool).IsAllowed).IsTrue();

        // And it is in the surface a caller sees, which is the other half of
        // "callable": the timer's own tool must not be the withheld one.
        await Assert.That(SessionToolPolicy.IsWithheldFromTheSurface(LiveSession.BrowserCloseTool)).IsFalse();
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
    /// never fires. The pair is the behaviour. The child is a double here — what
    /// is under test is the decision, and the decision is the same code against a
    /// real browser, which the next test drives.
    /// </para>
    /// <para>
    /// ⚠️ <b>Rewritten 2026-08-17 against a <see cref="ManualClock"/> (previously
    /// a wall-clock driving loop with a starvation detector and four retries).</b>
    /// The old shape asserted that it had achieved a rate — every gap between two
    /// in-process round trips under the 800 ms period — and on a machine running
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
    /// period, is <b>three periods of elapsed time with no close</b> — which is
    /// impossible unless every call re-armed the timer. Then the clock is moved
    /// past the deadline and the close must arrive.
    /// </para>
    /// <para>
    /// <b>The one thing that is not observable from here</b> is the instant the
    /// proxy releases its in-flight scope: <c>BrowserProxy</c> holds it across the
    /// answer, so it may still be open when this test's round trip returns. A
    /// timer that fires then re-arms for a whole period, correctly, so the quiet
    /// half advances the clock until the close lands rather than assuming one
    /// advance is enough. That is not a retry against flakiness — every advance
    /// that meets an outstanding call is the product keeping its promise, and the
    /// count assertion at the end is what makes it a bounded claim.
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

            await Assert.That(child.ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool);
        }

        // And now nothing at all.
        await WaitUntilAsync(
            () =>
            {
                clock.Advance(ShortPeriod);
                return child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool);
            },
            TestDefaults.InProcessHang,
            "the idle close never reached the child, however far the clock was moved");

        // One close, and only one, however long it is left: the timer is one-shot
        // and stays disarmed until the next call. Twenty periods is twenty
        // chances for a timer that wrongly re-armed, and the round trip after
        // them is a real exchange through the same server and the same child —
        // so anything the close path had queued has been through the child's loop
        // by the time the count is read.
        clock.Advance(ShortPeriod * 20);

        _ = await harness.Client.RoundTripAsync("tools/list");

        await Assert.That(child.ToolCallsReceived.Count(tool => tool == LiveSession.BrowserCloseTool)).IsEqualTo(1);

        // The evidence a caller would see, which is none: the close is
        // BrowserAI's own call to its own child and no frame about it reaches
        // the client.
        var toTheCaller = string.Concat(harness.Client.FramesReceived.Select(Encoding.UTF8.GetString));

        await Assert.That(toTheCaller).DoesNotContain(LiveSession.BrowserCloseTool);
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
    /// hoped the request had reached the child inside them — which is a guess at
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
        // would then be correct — which is a test that can only fail for the
        // wrong reason.
        await WaitUntilAsync(
            () => harness.Child.ToolCallsReceived.Contains("browser_navigate"),
            TestDefaults.InProcessHang,
            "the held call never reached the child, so nothing was outstanding to protect");

        // Outstanding for three whole periods — exactly three, because this is
        // the only thing that moves the clock — and the only thing ending it is
        // the line below.
        clock.Advance(ShortPeriod * 3);

        await Assert.That(harness.Child.ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool);

        held.SetResult();

        var answer = await harness.Client.AwaitAsync(id, "browser_navigate");

        await Assert.That(answer.Error).IsNull();

        // And the period restarts from the moment the call was answered, so the
        // close still comes — a suppressed timer that never re-armed would be
        // the same defect this step exists to remove. Advanced repeatedly rather
        // than once: the proxy releases its in-flight scope after the caller's
        // answer is on the wire, so an advance that lands while the release is
        // still in flight re-arms for a whole period, correctly.
        await WaitUntilAsync(
            () =>
            {
                clock.Advance(ShortPeriod);
                return harness.Child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool);
            },
            TestDefaults.InProcessHang,
            "the timer never re-armed after the held call was answered, however far the clock was moved");
    }

    /// <summary>
    /// Against a <b>real</b> browser: idle past the period leaves no browser
    /// process and the node child still running, and the next call succeeds
    /// without ever saying the browser is closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both facts are asserted because either alone is the wrong outcome.</b>
    /// A session with no browser and no node child has been torn down, not idled;
    /// a session with both is one where the timer did nothing. The pair is what
    /// §C asks for.
    /// </para>
    /// <para>
    /// <b>The relaunch is upstream's, and this test is what establishes that</b> —
    /// there is no relaunch code in this repository. Playwright creates the
    /// browser lazily on first use, so the call after an idle close simply works.
    /// Measured separately at ~0.41 s
    /// ([kb](../../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)).
    /// </para>
    /// <para>
    /// <b>Every question about processes is asked of this session's own job</b>,
    /// never of the machine: the suite runs several browsers in parallel and an
    /// image-path scan cannot tell one session's Chromium from another's.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnIdleSessionLosesItsBrowserKeepsItsNodeChildAndTheNextCallStillWorks()
    {
        // A machine that has never been provisioned proves nothing here, so this
        // reports as SKIPPED rather than as a pass -- and as a failure under
        // BROWSERAI_RELEASE_RUN, because a release run that never started a
        // browser is the batteries-included premise being silently dead code.
        SuiteEnvironment.RequireProvisionedChromium();

        // ⚠️ A clock this test moves by hand, so the close happens exactly when
        // this test asks for it and at no other moment.
        //
        // Corrected 2026-08-17 (previously a real 3 s period). Every census
        // below is a question about a live browser, and with a real period the
        // timer was racing them: at full parallelism the relaunch on the last
        // line took longer than the period, so the browser was closed again
        // before it could be counted and the test failed reporting zero
        // browsers — with the product having done exactly what it promises,
        // twice. The period is nominal now; nothing waits for it.
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

        // ⚠️ **There is deliberately no assertion here that the close came no
        // sooner than a period after this answer**, and two full-suite runs are
        // why. A client-side stopwatch measures from the moment the *test* is
        // scheduled to observe the answer, not from the moment the product sent
        // it — and under this machine's known starvation those differ by
        // seconds: the first version failed at 1.71 s and then at 0.55 s against
        // a 3 s period while the timer was behaving correctly. The reset
        // property is asserted where both clocks are the product's, in the
        // in-process tests above; what this test owns is the pair of facts a
        // real browser is needed for.
        await Assert.That((bool?)navigate["isError"]).IsNotEqualTo(true);

        var child = rig.RealSessionChildren.Single();
        var node = child.ProcessId!.Value;
        var nodeCreated = ProcessIdentity.CreationTimeOf(node);

        // A real browser really is up. Anything below this that finds zero
        // browsers would otherwise pass vacuously.
        await Assert.That(BrowsersIn(child, rig).Count).IsGreaterThan(0);

        // Now, and only now, the session goes idle. The clock is advanced until
        // the browser has actually gone rather than once: the proxy releases its
        // in-flight scope after the caller's answer is on the wire, so an advance
        // that lands while a call is still outstanding re-arms for a whole
        // period — correctly — and the wait is what absorbs that. What is being
        // waited for afterwards is a real process tree dying, which is real time
        // and is bounded by the teardown patience.
        await WaitUntilAsync(
            () =>
            {
                clock.Advance(period);
                return BrowsersIn(child, rig).Count is 0;
            },
            TeardownPatience,
            "the browser was still running long after the session went idle");

        // ⚠️ Confirmed a second time rather than believed the first. The scan
        // behind it opens ~600 processes, and one transient failure to open the
        // browser's own would read as "the browser is gone" while it was
        // running — the same class of false answer that makes an image-NAME
        // match unacceptable, arriving through a legitimate one.
        await Task.Delay(250);

        await Assert.That(BrowsersIn(child, rig).Count).IsEqualTo(0);

        // The half a browser count cannot make: the node child is still there,
        // so this was an idle close rather than a teardown.
        await Assert.That(ProcessIdentity.IsAlive(node, nodeCreated)).IsTrue();
        await Assert.That(child.JobProcessIds()).Contains(node);

        // And the whole reason the timer is safe to have. Not "it did not
        // throw": the answer must carry no closed-browser wording on any path,
        // because a model reads this text and would give up on the session.
        var again = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = harness.Session!, ["why"] = "the suite exercising this call" },
        });

        var text = string.Join("\n", (again["content"]?.AsArray() ?? []).Select(block => (string?)block!["text"] ?? string.Empty));

        await Assert.That((bool?)again["isError"]).IsNotEqualTo(true);
        await Assert.That(text.ToUpperInvariant()).DoesNotContain("BROWSER IS CLOSED");
        await Assert.That(text.ToUpperInvariant()).DoesNotContain("HAS BEEN CLOSED");
        await Assert.That(BrowsersIn(child, rig).Count).IsGreaterThan(0);
    }

    /// <summary>
    /// stdin EOF reaps everything: no node child, no browser, and every member
    /// of the job gone.
    /// </summary>
    /// <remarks>
    /// <b>This is the graceful path and it is not the same claim
    /// <c>VerticalSliceTests</c> makes.</b> That one terminates BrowserAI from
    /// outside, so the kernel closing the last job handle is what cleans up. Here
    /// BrowserAI runs its own shutdown — the session's child gets its stdin
    /// closed, which trips upstream's <c>setupExitWatchdog</c> — and the
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
            [],
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
    /// open — a Windows pipe signals EOF only when its <i>last</i> write handle
    /// closes — and the only remaining route to a teardown is the
    /// <c>OpenProcess</c> handle BrowserAI holds on its client.
    /// </para>
    /// <para>
    /// That the handle is still ours when BrowserAI exits is asserted rather than
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

        // ⚠️ Read through ProbeReport rather than File.Exists plus
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

            // ⚠️ The wrapper really is BrowserAI's parent, asserted rather than
            // assumed. A watcher pointed at the wrong process fires at the wrong
            // moment and looks identical in every other signal — which is
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
            // this line, a BrowserAI that had already died — of a crash, of a
            // watcher pointed at the wrong process, of anything — would satisfy
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
    /// The watch is a handle on the client, signalled by its exit — never a ping
    /// and never a poll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The prohibition is asserted as well as the behaviour, because the
    /// behaviour cannot distinguish them.</b> A poll every 200 ms and a kernel
    /// wait both look like "it noticed"; what tells them apart is that there is
    /// no <c>ping</c> anywhere in the product — and there could not be, since MCP
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
        var pinging = RepositoryLayout.ProductSourceFiles
            .Where(file => File.ReadAllText(file.FullName) is var text
                && (text.Contains("\"ping\"", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("RequestMethods.Ping", StringComparison.Ordinal)
                    || text.Contains("PingAsync", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName))
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join(Environment.NewLine, pinging)).IsEmpty();

        using var scope = new JobObjectScope();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => _ = builder.AddProvider(logs));

        // cmd.exe with no arguments reads its stdin forever, and the scope holds
        // the write end — so it stays alive without a timer or a script, exactly
        // as PlantedProcess relies on.
        var client = scope.Launch(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Path.GetTempPath());

        var created = ProcessIdentity.CreationTimeOf(client.Id);
        var fires = 0;

        // The creation time is passed rather than left to be assumed: since
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

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan patience, string whatWentWrong)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > patience)
            {
                throw new TimeoutException($"{whatWentWrong} — after {waited.Elapsed.TotalSeconds:F1} s.");
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
    [System.Text.RegularExpressions.GeneratedRegex(@"Clock\s*=[^=]")]
    private static partial System.Text.RegularExpressions.Regex ClockAssignment();

}
