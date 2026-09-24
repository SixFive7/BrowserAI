// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What a published server does about an update: the calls in flight when it
/// is stopped for one, and every call it gets when it started during one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q286 b, the maintainer's words verbatim: <i>"Q286 b"</i>.</b> A tool result
/// is the only channel that reaches a model, so both moments are answered with
/// <see cref="SessionErrors.UpdateIsBeingInstalled"/>: a call still running when
/// a stop arrives through the pipe, and every call made to a server whose own
/// install's <c>Update.exe</c> was running when it started.
/// </para>
/// <para>
/// <b>The updater here is a stand-in, found by its path and nothing else.</b> A
/// copy of <c>cmd.exe</c>, placed at <c>&lt;scratch install root&gt;\Update.exe</c>
/// and started with <c>/d /q /k</c> -- no AutoRun, no echo, waiting on a standard
/// input nothing ever writes to -- inside a kill-on-close job the arm owns and
/// ends by closing that job's handle. The server under test knows it only as a
/// process whose full image path is its install root's <c>Update.exe</c>, which is
/// exactly how it would know Velopack's.
/// </para>
/// </remarks>
internal sealed class UpdateInProgressTests
{
    /// <summary>
    /// A call still running when a stop arrives through the pipe is answered with
    /// the update refusal, and not with a connection that closed under it.
    /// </summary>
    /// <remarks>
    /// <b>The call is a thirty-second <c>browser_wait_for</c></b>, and the stop is
    /// sent only once the server's own description says a call is in flight --
    /// so the call is known to be running when the stop arrives, from the server
    /// itself, and no timing is guessed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStopWithACallInFlightAnswersThatCallWithTheUpdateRefusal()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("update-in-flight");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var session = Path.Combine(scratch.Path, "cut-off-session");

        _ = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Init,
            ["arguments"] = new JsonObject { ["directory"] = session, ["purpose"] = "the session whose call a stop cuts off" },
        });

        _ = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = session, ["why"] = "the suite exercising this call" },
        });

        var marker = await ServerPipeRig.MarkerOfAsync(client.ProcessId, BrowserAiPaths.Real.RootAppDir, TestDefaults.ProcessHang);

        var inFlight = client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_wait_for",
            ["arguments"] = new JsonObject { ["time"] = 30, ["session"] = session, ["why"] = "the suite holding a call open across a stop" },
        });

        // Until the server itself says the call is running: an event read off
        // the server, with a hang detector behind it.
        var waited = Stopwatch.StartNew();

        while (true)
        {
            var described = await ServerPipeClient.DescribeAsync(marker, TestDefaults.ProcessHang);

            if (described.Description?.CallsInFlight is 1)
            {
                break;
            }

            if (waited.Elapsed > TestDefaults.ProcessHang)
            {
                throw new TimeoutException($"The server never described a call in flight. Last answer: {described.Why}");
            }

            await Task.Delay(20);
        }

        var stop = await ServerPipeClient.StopAsync(marker, TestDefaults.ProcessHang);

        await Assert.That(stop.Outcome).IsEqualTo(ServerPipeOutcome.Answered).Because(stop.Why);

        var cutOff = await inFlight;

        await Assert.That((bool?)cutOff["result"]?["isError"]).IsTrue();
        await Assert.That(TextOf(cutOff)).IsEqualTo(
            SessionErrors.UpdateIsBeingInstalled("browser_wait_for", wasRunning: true, RawStdioClient.DefaultClientName));

        await Assert.That(await client.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await Assert.That(client.ExitCode).IsEqualTo(0);
    }

    /// <summary>
    /// A server started while its own install's updater runs answers the
    /// handshake, refuses a call with the update refusal, starts no child, says
    /// so on its pipe -- and ends its conversation when the updater goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No child is read off the job the suite put the server in</b>: nothing in
    /// it runs an image from the published payload, where the node child lives.
    /// The control is the next arm, the same server without the stand-in, whose
    /// job does hold one.
    /// </para>
    /// <para>
    /// <b>The last half is an addition to Q286 b's text, and it is measured.</b>
    /// Four servers in the 2026-09-24 kill run started after Velopack's kill pass
    /// and before its updater exited; nothing would ever have ended them, so a
    /// server in this state ends itself when the updater it found has gone.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AServerStartedWhileItsInstallsUpdaterRunsRefusesCallsStartsNoChildAndEndsWithTheUpdater()
    {
        SuiteEnvironment.RequirePublishedSlice();
        PublishedSlice.EnsureFresh();

        using var root = ScratchDirectory.CreateUnderProfile("update-in-progress");

        using var updaterJob = JobObject.CreateKillOnClose();
        using var updater = StartTheStandIn(updaterJob, root.Path);

        var environment = PublishedSlice.InheritedEnvironment();
        environment[BrowserAiPaths.AppRootOverride] = root.Path;

        await using var client = RawStdioClient.Start(PublishedSlice.Executable, [], root.Path, environment);

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var refused = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.List,
            ["arguments"] = new JsonObject { ["directory"] = root.Path },
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOfResult(refused)).IsEqualTo(
            SessionErrors.UpdateIsBeingInstalled(SessionToolSurface.List, wasRunning: false, RawStdioClient.DefaultClientName));

        await Assert.That(PayloadChildrenIn(client).Count).IsEqualTo(0);

        var marker = await ServerPipeRig.MarkerOfAsync(client.ProcessId, root.Path, TestDefaults.ProcessHang);
        var described = await ServerPipeRig.DescribeWhenListeningAsync(marker, TestDefaults.ProcessHang);

        await Assert.That(described.Description?.State).IsEqualTo(ServerDescription.States.Updating).Because(described.Why);

        // ---- The updater goes, by its handle: the job the arm owns is closed.
        updaterJob.Dispose();

        await Assert.That(await client.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await Assert.That(client.ExitCode).IsEqualTo(0);
    }

    /// <summary>
    /// The control: the same server, the same root, and no updater running --
    /// it serves the call and starts its child.
    /// </summary>
    /// <remarks>
    /// Without this, an arm above that passed because detection answered
    /// <i>updating</i> for every server would be indistinguishable from one that
    /// passed because it found the stand-in.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSameServerWithNoUpdaterRunningServesTheCallAndStartsItsChild()
    {
        SuiteEnvironment.RequirePublishedSlice();
        PublishedSlice.EnsureFresh();

        using var root = ScratchDirectory.CreateUnderProfile("update-not-in-progress");

        // The same file, not running: detection is by a running process, never
        // by what is on disk.
        File.Copy(StandInSource, Path.Combine(root.Path, Program.UpdaterFileName));

        var environment = PublishedSlice.InheritedEnvironment();
        environment[BrowserAiPaths.AppRootOverride] = root.Path;

        await using var client = RawStdioClient.Start(PublishedSlice.Executable, [], root.Path, environment);

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var served = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.List,
            ["arguments"] = new JsonObject { ["directory"] = root.Path },
        });

        await Assert.That((bool?)served["isError"]).IsNotEqualTo(true).Because(TextOfResult(served));
        await Assert.That(PayloadChildrenIn(client).Count).IsGreaterThan(0);
    }

    /// <summary>The harmless executable the stand-in is a copy of.</summary>
    private static string StandInSource { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    /// <summary>
    /// Copies <c>cmd.exe</c> to the root's <c>Update.exe</c> and starts it waiting
    /// on its own standard input, in the arm's job.
    /// </summary>
    private static LaunchedProcess StartTheStandIn(JobObject job, string root)
    {
        var standIn = Path.Combine(root, Program.UpdaterFileName);

        File.Copy(StandInSource, standIn);

        // /d: no AutoRun commands; /q: no echo; /k: stay, reading stdin, which is
        // a pipe the launcher holds and nothing writes to.
        return JobLauncher.Start(job, standIn, ["/d", "/q", "/k"], root, PublishedSlice.InheritedEnvironment());
    }

    /// <summary>Every process in the client's job running an image from the published payload.</summary>
    private static List<int> PayloadChildrenIn(RawStdioClient client)
    {
        var payload = Path.Combine(PublishedSlice.Directory, "payload") + Path.DirectorySeparatorChar;

        return
        [
            .. client.JobProcessIds().Where(pid =>
                pid != client.ProcessId
                && ProcessCommandLine.ImagePathOf(pid) is { } image
                && image.StartsWith(payload, StringComparison.OrdinalIgnoreCase)),
        ];
    }

    private static string TextOf(JsonObject envelope) =>
        string.Concat((envelope["result"]?["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

    private static string TextOfResult(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
