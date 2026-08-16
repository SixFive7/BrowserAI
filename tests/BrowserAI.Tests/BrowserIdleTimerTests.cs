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
/// [§C](../../plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever)'s
/// whole lifetime section: one timer, no expiry, and two teardown mechanisms
/// neither of which is a close tool.
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
    /// Short enough that the suite never waits, long enough that a rig which
    /// opens a session, hands shakes and forwards a call is not closed while it
    /// is still starting.
    /// </summary>
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromMilliseconds(800);

    /// <summary>How long a real browser tree gets to go away once it has been asked to.</summary>
    private static readonly TimeSpan TeardownPatience = TimeSpan.FromSeconds(60);

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

        // Classified, and permitted everywhere: the timer bypasses the policy —
        // it is BrowserAI calling its own child rather than a caller calling a
        // tool — but a close that a mode forbids would mean an `interactive`
        // session whose browser can never be closed, and the policy is where
        // that would be decided.
        await Assert.That(SessionToolPolicy.Classification.ContainsKey(LiveSession.BrowserCloseTool)).IsTrue();

        foreach (var mode in SessionModes.All)
        {
            await Assert.That(SessionToolPolicy.Decide(LiveSession.BrowserCloseTool, mode).IsAllowed).IsTrue();
        }
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
    /// ⚠️ <b>"A timer did not fire during this window" is a wall-clock claim, and
    /// on this machine the harness itself stalls.</b> Measured twice over full
    /// suite runs, 2026-08-16: a version pausing a quarter of a period between
    /// calls saw the session go genuinely idle and the product close the browser
    /// <i>correctly</i>, and a version driving flat out still recorded a
    /// <b>1.65 s</b> gap between two in-process round trips against an 800 ms
    /// period. Both times the product was right and the test was wrong.
    /// </para>
    /// <para>
    /// So each attempt <b>measures the longest gap it actually achieved</b> and
    /// only makes the claim when that gap stayed under the period; a starved
    /// attempt is retried against a fresh session rather than failing, and
    /// running out of attempts fails naming starvation. That is deliberately not
    /// the same as skipping: an attempt that was not starved always asserts, and
    /// the failure mode this replaces was a red build that named the wrong
    /// cause. The starvation it works around predates this step and is the same
    /// one <see cref="TestDefaults.RigBudget"/> records — 2.57 s, 2.89 s and
    /// 4.20 s against a rig that normally answers in milliseconds.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryToolCallResetsTheTimerAndOnlyASessionThatGoesQuietIsClosed()
    {
        await using var rig = RigSessionEnvironment.Create(
            configure: child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            browserIdlePeriod: ShortPeriod);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        const int Attempts = 4;

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            // A fresh session per attempt, so a starved one leaves nothing
            // behind for the next: its own child, its own timer, its own record
            // of what was called.
            var session = Path.Combine(rig.Root, $"driven-{attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
            {
                ["name"] = "browserai_init",
                ["arguments"] = new JsonObject
                {
                    ["directory"] = session,
                    ["purpose"] = "the session driven continuously",
                    ["mode"] = "persistent",
                },
            });

            var child = rig.SessionChildren[^1];
            var driving = Stopwatch.StartNew();
            var sinceLastCall = Stopwatch.StartNew();
            var longestGap = TimeSpan.Zero;
            var calls = 0;

            while (driving.Elapsed < ShortPeriod * 3)
            {
                _ = await harness.Client.RoundTripAsync("tools/call", new JsonObject
                {
                    ["name"] = "browser_navigate",
                    ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = session },
                });

                calls++;

                if (sinceLastCall.Elapsed > longestGap)
                {
                    longestGap = sinceLastCall.Elapsed;
                }

                sinceLastCall.Restart();
            }

            if (longestGap >= ShortPeriod)
            {
                // This attempt let the session go genuinely idle, so it can say
                // nothing about whether a call resets the timer. Never treated
                // as a pass.
                if (attempt == Attempts)
                {
                    throw new TimeoutException(
                        $"All {Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture)} attempts were starved: the longest gap between two in-process round trips was "
                        + $"{longestGap.TotalSeconds:F2} s against a {ShortPeriod.TotalSeconds:F2} s period, so the harness — not the timer — let the session go idle.");
                }

                continue;
            }

            await Assert.That(calls).IsGreaterThan(10);
            await Assert.That(child.ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool);

            // And now nothing at all. One close, and only one, however long it is
            // left: the timer is one-shot and stays disarmed until the next call.
            await WaitUntilAsync(
                () => child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool),
                ShortPeriod * 20,
                "the idle close never reached the child");

            await Task.Delay(ShortPeriod * 3);

            await Assert.That(child.ToolCallsReceived.Count(tool => tool == LiveSession.BrowserCloseTool)).IsEqualTo(1);

            // The evidence a caller would see, which is none: the close is
            // BrowserAI's own call to its own child and no frame about it reaches
            // the client.
            var toTheCaller = string.Concat(harness.Client.FramesReceived.Select(Encoding.UTF8.GetString));

            await Assert.That(toTheCaller).DoesNotContain(LiveSession.BrowserCloseTool);
            return;
        }
    }

    /// <summary>
    /// A single call that outlives the whole period does not have the browser
    /// closed underneath it.
    /// </summary>
    /// <remarks>
    /// <b>This is the half of "reset by any tool call" that no wall clock can
    /// make flaky</b>, and it is the case that actually matters in production: a
    /// navigation to a slow site, a download, a <c>browser_wait_for</c>. The call
    /// is held open by the double until this test releases it, so the session is
    /// outstanding for three periods by construction rather than by timing, and a
    /// timer that ignored in-flight calls would close the browser under a caller
    /// that was mid-request.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallThatOutlivesThePeriodIsNotClosedUnderneath()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var rig = RigSessionEnvironment.Create(
            configure: child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour { HoldUntil = held.Task };
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            browserIdlePeriod: ShortPeriod);

        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var id = await harness.Client.BeginAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>", ["session"] = harness.Session! },
        });

        // Outstanding for three whole periods, and the only thing ending it is
        // the line below.
        await Task.Delay(ShortPeriod * 3);

        await Assert.That(harness.Child.ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool);

        held.SetResult();

        var answer = await harness.Client.AwaitAsync(id, "browser_navigate");

        await Assert.That(answer.Error).IsNull();

        // And the period restarts from the moment the call was answered, so the
        // close still comes — a suppressed timer that never re-armed would be
        // the same defect this step exists to remove.
        await WaitUntilAsync(
            () => harness.Child.ToolCallsReceived.Contains(LiveSession.BrowserCloseTool),
            ShortPeriod * 20,
            "the timer never re-armed after the held call was answered");
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

        // Long enough that bringing a cold Chromium up cannot outlast it, short
        // enough that the test costs seconds. The call scope is what makes that
        // safe: a navigation in flight holds the timer off however long it takes.
        var period = TimeSpan.FromSeconds(3);

        await using var rig = RigSessionEnvironment.Create(browserIdlePeriod: period, realSessionChildren: true);
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var navigate = await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = harness.Session! },
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

        await WaitUntilAsync(
            () => BrowsersIn(child, rig).Count is 0,
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
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = harness.Session! },
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
                ["mode"] = "headless",
            },
        });

        _ = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = session },
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
        var exited = await client.CloseAndWaitForExitAsync(TimeSpan.FromSeconds(60));

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
        var failures = new List<string>();
        await DeleteWhenReleasedAsync(Path.Combine(session, SessionLayout.ProfileFolderName), failures);

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

        await WaitUntilAsync(
            () => File.Exists(reportPath),
            TimeSpan.FromSeconds(180),
            $"the wrapper never reported. Scratch tree: {scratch.Path}");

        var report = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(reportPath))!;

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

        using var watcher = ClientLivenessWatcher.ForProcess(
            client.Id,
            () => Interlocked.Increment(ref fires),
            factory.CreateLogger("watch"))!;

        await Assert.That(watcher).IsNotNull();
        await Assert.That(watcher.ProcessId).IsEqualTo(client.Id);
        await Assert.That(watcher.HasFired).IsFalse();

        ProcessIdentity.Terminate(client.Id, created);

        await WaitUntilAsync(
            () => Volatile.Read(ref fires) > 0,
            TimeSpan.FromSeconds(10),
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

    private static async Task DeleteWhenReleasedAsync(string directory, List<string> failures)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            failures.Clear();
            BrowserAI.Runtime.TreeDelete.Remove(directory, failures);

            if (failures.Count is 0 || waited.Elapsed > TeardownPatience)
            {
                return;
            }

            await Task.Delay(200);
        }
    }
}
