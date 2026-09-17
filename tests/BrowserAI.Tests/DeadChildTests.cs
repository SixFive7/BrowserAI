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
/// be answered is failed rather than left outstanding, and a resume that meets a
/// dead child relaunches it. Each is asserted on its own, because either alone
/// is an improvement and the second is what makes the first's advice work.
/// </para>
/// <para>
/// ⚠️ <b>The layer is the in-process rig, and what it is not evidence about is
/// stated rather than implied.</b> A session child here is a
/// <c>FakePlaywrightChild</c> over a pipe, so nothing below says anything about
/// <c>ChildProcessSession</c> — its launcher, its job object or its process
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
    /// and says so, rather than answering that nothing was changed.
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
    /// the product's own rule — so the arm waits for the transport to report
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
    /// working child on every resume — throwing away the browser, the page and
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
    /// Waits until the child's transport has reported end-of-stream, which is
    /// what makes the child dead as far as this process is concerned.
    /// </summary>
    /// <remarks>
    /// The session's own transport logs into <see cref="McpTestHarness.Logs"/>,
    /// so this reads the product's own record of the close rather than guessing
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
