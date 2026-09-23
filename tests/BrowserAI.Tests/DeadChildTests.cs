// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What BrowserAI does once a session's browser server has died underneath it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure these arms exist for is a hang, not an error.</b> Measured
/// 2026-09-17 against the published slice
/// (<see href="../../docs/evidence/2026-09-17-resume-wedge/README.md">evidence</see>):
/// with a session's <c>node</c> child killed under a live BrowserAI, the next
/// <c>browser_navigate</c> had not returned after 900,000 ms and
/// <c>browserai_resume</c> answered <i>"already open in this BrowserAI; nothing
/// was changed"</i> in 7.68 ms. Nothing in the product bounded either.
/// </para>
/// <para>
/// <b>Two halves, and they are separate behaviours.</b> A forward that can never
/// be answered is failed and not left outstanding, and a resume that meets a
/// dead child relaunches it. Each is asserted on its own, because either alone
/// is an improvement and the second is what makes the first's advice work.
/// </para>
/// <para>
/// ⚠️ <b>The layer is the in-process rig, and what it is not evidence about is
/// stated and not implied.</b> A session child here is a
/// <c>FakePlaywrightChild</c> over a pipe, so nothing below says anything about
/// <c>ChildProcessSession</c> -- its launcher, its job object or its process
/// handle. <see cref="DirectStdioClientTransportTests"/> carries the real-process
/// half against a child this suite really starts and really kills.
/// </para>
/// </remarks>
internal sealed class DeadChildTests
{
    /// <summary>What the double answers a navigation with, so a relaunched child can be seen to work.</summary>
    private const string NavigateResult =
        """{"content":[{"type":"text","text":"Page URL: data:text/html,<h1>ok</h1>"}]}""";

    /// <summary>
    /// A resume that meets a session whose child has died relaunches the child
    /// and says so, instead of answering that nothing was changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The 7.68 ms no-op is the measured behaviour this replaces.</b>
    /// <c>browserai_resume</c>'s question was <i>do I already own this
    /// directory</i>, and the answer is still yes when the child behind it has
    /// gone: the session is in this process's own index and the live marker is
    /// this process's. Whether the child is alive is a different question and
    /// nothing asked it.
    /// </para>
    /// <para>
    /// <b>The wait before the resume is not a sleep and not a timeout.</b> A
    /// child mid-exit counts as dead only once the transport says so, which is
    /// the product's own rule -- so the arm waits for the transport to report
    /// end-of-stream before asking, and a resume issued earlier is entitled to
    /// answer that nothing changed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeRelaunchesAChildThatHasDiedAndSaysSo()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "relaunch");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose browser server is about to die",
        });

        // The child dies the way a killed node child dies: its end of the pipe
        // closes with no answer and no farewell.
        await sessions.SessionChildren[0].DisposeAsync();
        await WaitUntilTheTransportNoticedAsync(rig);

        var resumed = TextOf(await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite exercising a resume against a session whose child has gone",
        }));

        await Assert.That(resumed).Contains(SessionManager.ChildWasRelaunched);
        await Assert.That(resumed).DoesNotContain("nothing was changed");

        // A second child, started by the resume, and it is the one the next call
        // reaches: the relaunch is what makes the advice in the fail-fast
        // refusal below true.
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(2);

        var after = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite proving the relaunched child answers",
            ["url"] = "data:text/html,<h1>ok</h1>",
        });

        await Assert.That((bool?)after["isError"]).IsNotEqualTo(true);
        await Assert.That(sessions.SessionChildren[1].ToolCallsReceived).Contains("browser_navigate");
    }

    /// <summary>
    /// A resume against a session whose child is <b>healthy</b> is still the
    /// no-op it always was.
    /// </summary>
    /// <remarks>
    /// <b>The control for the arm above, and it is not decoration.</b> A
    /// liveness check that answered <i>dead</i> too readily would relaunch a
    /// working child on every resume -- throwing away the browser, the page and
    /// the tab the caller was about to pick up, while reporting a repair.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeOfALiveSessionStillChangesNothing()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "healthy");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session nothing is wrong with",
        });

        var resumed = TextOf(await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite exercising a resume against a session that is fine",
        }));

        await Assert.That(resumed).Contains("nothing was changed");
        await Assert.That(resumed).DoesNotContain(SessionManager.ChildWasRelaunched);
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A call forwarded to a session whose child has gone comes back as a
    /// refusal, instead of never coming back at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The thing this replaces is silence.</b> A request handed to the SDK
    /// after its transport's channel has completed is registered in a
    /// pending-request table that nothing will ever walk again -- the walk that
    /// faults it runs once, as the channel completes
    /// (<see href="../../kb/mcp/sdk.md">kb</see>) -- so it waits on the caller's
    /// token and on nothing else. Measured 2026-09-17 against the published
    /// slice at 900,000 ms.
    /// </para>
    /// <para>
    /// <b>The bound here is a hang detector and not a promptness claim.</b>
    /// Nothing asserts how long the refusal takes; what the arm asserts is that
    /// one arrives at all, and <see cref="TestDefaults.InProcessHang"/> inside
    /// <c>RawPipeClient</c> is what turns *never* into a failure instead of a
    /// suite that stops.
    /// </para>
    /// <para>
    /// <b>A call ALREADY IN FLIGHT when the child dies is a different path and
    /// is not this arm.</b> The SDK faults those itself, which
    /// <c>LosslessPassthroughTests.AChildThatDiesMidCallProducesANamedErrorRatherThanASuccess</c>
    /// asserts.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallForwardedAfterTheChildDiedComesBackRatherThanWaitingForever()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour { RawResult = NavigateResult },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "refused");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose browser server dies before the next call",
        });

        await sessions.SessionChildren[0].DisposeAsync();
        await WaitUntilTheTransportNoticedAsync(rig);

        var answer = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite calling into a session whose browser server has gone",
            ["url"] = "data:text/html,<h1>ok</h1>",
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();

        await Assert.That(TextOf(answer))
            .Contains(SessionErrors.BrowserServerHasGone("browser_navigate", SessionPath.For(directory).FullPath));
    }

    /// <summary>
    /// A slow call on a <b>healthy</b> session is not cut short by the check
    /// that refuses a dead one.
    /// </summary>
    /// <remarks>
    /// <b>The control, and the failure it guards against is the obvious way to
    /// get this wrong.</b> A liveness question answered with a clock, or asked
    /// of a child that is merely busy, would turn every long page action into a
    /// refusal -- and a refusal is what a model would act on. The call below is
    /// held open while the child goes on listening, exactly as a navigation that
    /// takes a while does, and it has to come back with the child's own result.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASlowCallOnAHealthySessionStillGetsTheChildsOwnAnswer()
    {
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                RawResult = NavigateResult,

                // Holds the call open WITHOUT blocking the child's read loop, so
                // the child is demonstrably alive and listening for the whole
                // time the call is outstanding.
                HoldUntil = holding.Task,
            },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "slow");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session whose page action takes a while",
        });

        var slow = CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite holding a call open against a healthy child",
            ["url"] = "data:text/html,<h1>ok</h1>",
        });

        // Released only once the call is demonstrably outstanding: the child has
        // the request and has not answered it.
        await WaitUntilTheChildIsHoldingAsync(sessions);
        holding.SetResult();

        var answer = await slow;

        await Assert.That((bool?)answer["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(answer)).IsEqualTo("Page URL: data:text/html,<h1>ok</h1>");
    }

    /// <summary>
    /// Waits until the session's child has actually received the held call.
    /// </summary>
    /// <remarks>
    /// The double's own record of what arrived, and not a duration:
    /// releasing the hold before the call reached the child would make the arm
    /// a test of two round trips instead of one outstanding call.
    /// <see cref="TestDefaults.InProcessHang"/> bounds it as a hang detector.
    /// </remarks>
    /// <param name="sessions">The rig whose session child is holding a call.</param>
    /// <returns>A task that completes once the child has the call.</returns>
    private static async Task WaitUntilTheChildIsHoldingAsync(RigSessionEnvironment sessions)
    {
        using var patience = new CancellationTokenSource(TestDefaults.InProcessHang);

        while (!sessions.SessionChildren[0].ToolCallsReceived.Contains("browser_navigate"))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), patience.Token);
        }
    }

    /// <summary>
    /// Waits until the child's transport has reported end-of-stream, which is
    /// what makes the child dead as far as this process is concerned.
    /// </summary>
    /// <remarks>
    /// The session's own transport logs into <see cref="McpTestHarness.Logs"/>,
    /// so this reads the product's own record of the close instead of guessing
    /// at a duration. <see cref="TestDefaults.InProcessHang"/> bounds it as a
    /// hang detector: both ends are in this process.
    /// </remarks>
    /// <param name="rig">The rig whose session child died.</param>
    /// <returns>A task that completes once the transport has noticed.</returns>
    private static async Task WaitUntilTheTransportNoticedAsync(McpTestHarness rig)
    {
        using var patience = new CancellationTokenSource(TestDefaults.InProcessHang);

        while (!rig.Logs.Logged("the peer closed its end of the connection"))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), patience.Token);
        }
    }

    /// <summary>
    /// The relaunch note distinguishes <b>the profile is on disk</b> from
    /// <b>your writes are in it</b>, because measurement says those are not the
    /// same sentence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a wording guard and it is deliberate.</b> Every other arm in
    /// this file asserts against <c>SessionManager.ChildWasRelaunched</c> the
    /// constant, so all of them stay green through any rewrite of the text the
    /// constant holds -- which is exactly how the sentence below shipped for five
    /// days saying something measurement contradicts.
    /// </para>
    /// <para>
    /// <b>What it is guarding, measured.</b> 2026-09-22 at chromium 1246 and
    /// firefox 1549, eight runs over two sittings: a relaunch after the child
    /// was <i>killed</i> lost at least one persistent store beyond
    /// <c>sessionStorage</c> every time -- the cookie on both Chromium runs,
    /// <c>localStorage</c> on one Chromium and both Firefox runs -- while the
    /// clean handover through the identical probe lost <c>sessionStorage</c> and
    /// nothing else on 4 of 4. The string nevertheless read <i>"so cookies and
    /// stored state are still there"</i>.
    /// </para>
    /// <para>
    /// ⚠️ <b>And it does NOT say "killed", which is the half Q223 c settled.</b>
    /// The obvious narrowing -- blame the <c>kill</c> -- was measured and refused:
    /// a child that ends ITSELF with <c>process.exit(0)</c>, a death nobody
    /// caused, loses the same stores as one that is terminated
    /// (<see href="../../docs/probes/2026-09-16-resume/README.md">the rig</see>).
    /// So the note is true of any death that was not a clean shutdown, and a
    /// wording that named the kill would be false on the case a caller is most
    /// likely to meet.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheRelaunchNoteDoesNotPromiseThatStoredStateSurvived()
    {
        var note = SessionManager.ChildWasRelaunched;

        // The claim the measurement contradicts. Quoted here in the shape it
        // shipped in, so that re-introducing it is red and not reviewed.
        await Assert.That(note)
            .DoesNotContain("stored state are still there")
            .Because("a relaunch follows a browser that did not shut down cleanly, and measurement says the cookie"
                + " and localStorage writes are exactly what it may not have flushed");

        // The true half has to survive the correction: the DIRECTORY really is
        // intact, and a caller that concluded otherwise would destroy a session
        // it could still use.
        await Assert.That(note).Contains("profile");

        // And the caller has to be told to go and look, not to assume,
        // because which store is lost varies by family and nothing predicts it.
        //
        // The predicate is the instruction's SHAPE and not one spelling of
        // it -- read-it-back, or verify, or check -- because a guard that
        // demanded an exact phrase would be a guard on the phrase. It was
        // written as `Contains("read them back")` first and went red against a
        // note that said "Read any stored value back", which is the same
        // instruction and a different sentence.
        var tellsTheCallerToLook =
            (note.Contains("read", StringComparison.OrdinalIgnoreCase)
                && note.Contains("back", StringComparison.OrdinalIgnoreCase))
            || note.Contains("verify", StringComparison.OrdinalIgnoreCase)
            || note.Contains("check", StringComparison.OrdinalIgnoreCase);

        await Assert.That(tellsTheCallerToLook)
            .IsTrue()
            .Because("the note names a risk; without an instruction the model has no action to take from it");

        // ⚠️ NOT "killed". The self-death arm of the probe lost the same stores,
        // so a note scoped to termination would be false on a crash.
        await Assert.That(note)
            .DoesNotContain("was killed")
            .Because("Q223 c measured a child that ended itself and it lost the same stores; scoping the warning to a kill would understate it");
    }

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
