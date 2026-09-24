// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using BrowserAI.Coordination;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// One server's named pipe, end to end: what it describes, how it is created,
/// what a client makes of a server that fails, and what a stop does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q284 a, the maintainer's words verbatim: <i>"Q284 a"</i>.</b> One raw named
/// pipe per server, named after its live marker, answering <c>describe</c> from
/// memory and <c>stop</c> by acknowledging and then ending the conversation.
/// </para>
/// <para>
/// <b>Two layers, chosen per claim.</b> What a published server says and does
/// is asked of the published binary. What the pipe itself is -- its DACL, its
/// flags, its refusal of a second instance -- is asked of the product's own
/// <see cref="ServerPipe"/> hosted in this process, read back with Windows' own
/// calls declared in the suite. And what a client makes of a server that
/// misbehaves is asked of a server the suite writes with the framework's pipe
/// classes, which share no code with the product.
/// </para>
/// </remarks>
internal sealed class ServerPipeTests
{
    /// <summary>
    /// A published server describes itself with what the harness gave it: the
    /// client's name, title and version, its working directory, and then the
    /// session it opens and the browser that session starts.
    /// </summary>
    /// <remarks>
    /// <b>Every member is compared with something the suite knows
    /// independently</b> -- the pid and creation time off the process handle, the
    /// version baked into the binary, the directory it was started in -- and not
    /// with a second reading of the description.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APublishedServerDescribesItselfWithWhatTheHarnessGaveIt()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("pipe-describe");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(
            SliceRun.OfferedProtocolVersion,
            clientInfo: new JsonObject
            {
                ["name"] = "pipe-describe-client",
                ["title"] = "The pipe describe arm",
                ["version"] = "7.3.1",
            });

        var created = ProcessIdentity.CreationTimeOf(client.ProcessId);
        var marker = await ServerPipeRig.MarkerOfAsync(client.ProcessId, BrowserAiPaths.Real.RootAppDir, TestDefaults.ProcessHang);

        var first = await ServerPipeRig.DescribeWhenListeningAsync(marker, TestDefaults.ProcessHang);

        await Assert.That(first.Outcome).IsEqualTo(ServerPipeOutcome.Answered).Because(first.Why);

        var before = first.Description!;

        await Assert.That(before.Protocol).IsEqualTo(ServerPipeProtocol.Version);
        await Assert.That(before.ProcessId).IsEqualTo(client.ProcessId);
        await Assert.That(before.CreatedFileTime).IsEqualTo(created);
        await Assert.That(before.Version).IsEqualTo(PublishedSlice.BakedVersion());
        await Assert.That(before.ImagePath).IsEqualTo(PublishedSlice.Executable, StringComparison.OrdinalIgnoreCase);
        await Assert.That(before.State).IsEqualTo(ServerDescription.States.Serving);
        await Assert.That(before.Client).IsEqualTo(new ClientIdentity("pipe-describe-client", "The pipe describe arm", "7.3.1"));
        await Assert.That(before.WorkingDirectory).IsEqualTo(scratch.Path, StringComparison.OrdinalIgnoreCase);
        await Assert.That(before.Started).IsNotNull();
        await Assert.That(before.LastToolCall).IsNull();
        await Assert.That(before.CallsInFlight).IsEqualTo(0);
        await Assert.That(before.Sessions.Count).IsEqualTo(0);

        // A session, and the browser that session's first navigation starts.
        var session = Path.Combine(scratch.Path, "described-session");
        const string Purpose = "the session the pipe describe arm opens";

        _ = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Init,
            ["arguments"] = new JsonObject { ["directory"] = session, ["purpose"] = Purpose },
        });

        _ = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = session, ["why"] = "the suite exercising this call" },
        });

        var second = await ServerPipeClient.DescribeAsync(marker, TestDefaults.ProcessHang);

        await Assert.That(second.Outcome).IsEqualTo(ServerPipeOutcome.Answered).Because(second.Why);

        var after = second.Description!;

        await Assert.That(after.LastToolCall).IsNotNull();
        await Assert.That(after.LastToolCall!.Value).IsGreaterThanOrEqualTo(after.Started!.Value);
        await Assert.That(after.CallsInFlight).IsEqualTo(0);
        await Assert.That(after.Sessions.Count).IsEqualTo(1);
        await Assert.That(after.Sessions[0].Directory).IsEqualTo(session, StringComparison.OrdinalIgnoreCase);
        await Assert.That(after.Sessions[0].Purpose).IsEqualTo(Purpose);
        await Assert.That(after.Sessions[0].BrowserOpen).IsTrue();
    }

    /// <summary>
    /// The pipe's DACL has exactly one allow entry, and it is the current user;
    /// a pipe created with Windows' default DACL has more.
    /// </summary>
    /// <remarks>
    /// <b>The default is the positive control, and it is the reason the product
    /// writes a DACL at all.</b> Measured 2026-09-24, a pipe created with no
    /// security attributes grants SYSTEM, the administrators, the owner,
    /// <c>Everyone</c> and <c>ANONYMOUS LOGON</c>. A reading that could only
    /// ever count one entry would be indistinguishable from this arm passing, so
    /// the same reader is shown counting five.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePipesDaclHasOneAllowEntryForTheCurrentUserAndTheDefaultHasMore()
    {
        using var root = ScratchDirectory.Create("pipe-dacl");
        using var live = LiveInstances.Join(root.Path, NullLogger.Instance)!;
        using var responder = new ScriptedResponder();
        using var pipe = ServerPipe.Open(live.OwnFile, responder, NullLogger.Instance);

        using (var client = new NamedPipeClientStream(".", ServerPipeRig.ShortName(pipe.Name), PipeDirection.InOut))
        {
            await client.ConnectAsync(TestDefaults.InProcessHang, CancellationToken.None);

            var dacl = ServerPipeRig.DaclOf(client.SafePipeHandle);
            var aces = Enumerable.Range(0, dacl.Count).Select(index => dacl[index]).OfType<CommonAce>().ToList();

            using var identity = WindowsIdentity.GetCurrent();

            await Assert.That(dacl.Count).IsEqualTo(1);
            await Assert.That(aces.Count).IsEqualTo(1);
            await Assert.That(aces[0].AceQualifier).IsEqualTo(AceQualifier.AccessAllowed);
            await Assert.That(aces[0].SecurityIdentifier).IsEqualTo(identity.User!);
        }

        // ---- The positive control: the same reader, over a pipe created the
        // default way.
        var control = $"BrowserAI-dacl-control-{Guid.NewGuid():N}";

        using var server = new NamedPipeServerStream(control, PipeDirection.InOut, 1);
        using var reader = new NamedPipeClientStream(".", control, PipeDirection.InOut);

        var accepted = server.WaitForConnectionAsync();

        await reader.ConnectAsync(TestDefaults.InProcessHang, CancellationToken.None);
        await accepted;

        await Assert.That(ServerPipeRig.DaclOf(reader.SafePipeHandle).Count).IsGreaterThan(1);
    }

    /// <summary>
    /// A client of the pipe reads <c>PIPE_REJECT_REMOTE_CLIENTS</c>, 0x8, off its
    /// own end; a pipe created without it reads nothing there.
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-24: the flag is visible from the client end, which is
    /// what makes it assertable at all -- the server end reports it too, but a
    /// test that read the server's own handle would be the product reading
    /// itself.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientOfThePipeReadsTheRejectRemoteFlagAndAPipeWithoutItReadsNone()
    {
        const uint RejectRemote = 0x8;

        using var root = ScratchDirectory.Create("pipe-reject-remote");
        using var live = LiveInstances.Join(root.Path, NullLogger.Instance)!;
        using var responder = new ScriptedResponder();
        using var pipe = ServerPipe.Open(live.OwnFile, responder, NullLogger.Instance);

        using (var client = new NamedPipeClientStream(".", ServerPipeRig.ShortName(pipe.Name), PipeDirection.InOut))
        {
            await client.ConnectAsync(TestDefaults.InProcessHang, CancellationToken.None);

            await Assert.That(ServerPipeRig.FlagsOf(client.SafePipeHandle) & RejectRemote).IsEqualTo(RejectRemote);
        }

        // ---- The positive control: the framework cannot set the flag, so a
        // pipe it creates is the pipe without it.
        var control = $"BrowserAI-reject-control-{Guid.NewGuid():N}";

        using var server = new NamedPipeServerStream(control, PipeDirection.InOut, 1);
        using var reader = new NamedPipeClientStream(".", control, PipeDirection.InOut);

        var accepted = server.WaitForConnectionAsync();

        await reader.ConnectAsync(TestDefaults.InProcessHang, CancellationToken.None);
        await accepted;

        await Assert.That(ServerPipeRig.FlagsOf(reader.SafePipeHandle) & RejectRemote).IsEqualTo(0u);
    }

    /// <summary>
    /// A second instance under a name this pipe already serves is refused with
    /// <c>0x800700E7</c>; a name somebody else created first is refused too,
    /// with <c>0x80070005</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two arms, because they are two different defences.</b> One instance
    /// per name is what keeps a second server from answering for the first. The
    /// second arm is what <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> is for, and it
    /// was measured before it was asserted, 2026-09-24: against a name another
    /// process had created with unlimited instances, a creation carrying the
    /// flag was refused with <c>ERROR_ACCESS_DENIED</c>, and the same creation
    /// without it SUCCEEDED -- it would have joined somebody else's pipe as its
    /// second instance, and a client could then have reached either.
    /// </para>
    /// <para>
    /// <b>The released name opens again</b>, which is what shows the refusal was
    /// about the name being taken and not about the name.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASecondInstanceIsRefusedAndSoIsANameSomebodyElseCreatedFirst()
    {
        const int PipeBusy = unchecked((int)0x800700E7);
        const int AccessDenied = unchecked((int)0x80070005);

        using var responder = new ScriptedResponder();

        var name = $"{ServerPipeRig.PipeNamespace}BrowserAI-second-{Guid.NewGuid():N}";

        using (ServerPipe.OpenNamed(name, responder, NullLogger.Instance))
        {
            var second = Assert.Throws<IOException>(() => ServerPipe.OpenNamed(name, responder, NullLogger.Instance).Dispose());

            await Assert.That(second.HResult).IsEqualTo(PipeBusy);
        }

        // Released: the same name opens again.
        await Assert.That(await OpensAgainAsync(name, responder)).IsTrue();

        // ---- Somebody else's name: created first, by the framework, with
        // unlimited instances and its default DACL.
        var squatted = $"BrowserAI-squatted-{Guid.NewGuid():N}";

        using var squatter = new NamedPipeServerStream(squatted, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances);

        var refused = Assert.Throws<IOException>(
            () => ServerPipe.OpenNamed(ServerPipeRig.PipeNamespace + squatted, responder, NullLogger.Instance).Dispose());

        await Assert.That(refused.HResult).IsEqualTo(AccessDenied);
    }

    /// <summary>
    /// A server that dies part-way through its answer is <i>no answer</i>,
    /// through the length prefix, and never a short answer read as a whole one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The server here is the suite's own</b>, written with the framework's
    /// pipe class so that it shares no code with the client under test: it
    /// reads the request, writes a length prefix and half the bytes it
    /// promised, and closes its handle -- which is what Windows does with every
    /// handle a process holds when that process dies.
    /// </para>
    /// <para>
    /// <b>The same server writing its whole answer is the positive control</b>:
    /// a client that could only ever report no answer would pass the first half.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AServerThatDiesMidAnswerIsNoAnswerThroughTheLengthPrefix()
    {
        using var root = ScratchDirectory.Create("pipe-dies");
        using var live = LiveInstances.Join(root.Path, NullLogger.Instance)!;

        using var scripted = new ScriptedResponder();

        var body = scripted.Describe().Body;
        var name = ServerPipeRig.ShortName(ServerPipeProtocol.NameFor(live.OwnFile));

        // ---- Half the promised bytes, then the handle goes.
        var truncated = await AgainstASuiteServerAsync(live.OwnFile, name, body, body.Length / 2);

        await Assert.That(truncated.Outcome).IsEqualTo(ServerPipeOutcome.NoAnswer).Because(truncated.Why);
        await Assert.That(truncated.Description).IsNull();
        await Assert.That(truncated.Why).Contains($"closed after {body.Length / 2} of the {body.Length} bytes");

        // ---- The positive control: the whole answer is read as one.
        var whole = await AgainstASuiteServerAsync(live.OwnFile, name, body, body.Length);

        await Assert.That(whole.Outcome).IsEqualTo(ServerPipeOutcome.Answered).Because(whole.Why);
        await Assert.That(whole.Description!.ProcessId).IsEqualTo(Environment.ProcessId);
    }

    /// <summary>
    /// A listener that has stopped answering costs a caller the whole bound and
    /// no more, while the census still says its holder is alive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the case the census cannot see, and the bound is why it
    /// exists.</b> The holder is alive, so its marker is held and the census
    /// counts it; only the pipe can tell that it is not answering, and only the
    /// bound stops that costing the caller for ever.
    /// </para>
    /// <para>
    /// <b>Both bounds are derived and neither is a promptness claim.</b> The
    /// lower one is the product's own <see cref="ServerPipeProtocol.CallBound"/> --
    /// the client waited the whole of it, because its deadline is read off a
    /// stopwatch and a wake that came early waits out the rest. The upper one is
    /// <see cref="TestDefaults.InProcessHang"/>, a hang detector.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHungListenerCostsTheWholeBoundWhileTheCensusStillSaysHeld()
    {
        using var root = ScratchDirectory.Create("pipe-hung");
        using var live = LiveInstances.Join(root.Path, NullLogger.Instance)!;
        using var responder = new ScriptedResponder();
        using var pipe = ServerPipe.Open(live.OwnFile, responder, NullLogger.Instance);

        await Assert.That(LiveInstances.IsMarkerHeld(live.OwnFile)).IsTrue();

        responder.Hang();

        try
        {
            var clock = Stopwatch.StartNew();
            var answer = await ServerPipeClient.DescribeAsync(live.OwnFile);
            clock.Stop();

            await Assert.That(answer.Outcome).IsEqualTo(ServerPipeOutcome.NoAnswer).Because(answer.Why);
            await Assert.That(clock.Elapsed).IsGreaterThanOrEqualTo(ServerPipeProtocol.CallBound);
            await Assert.That(clock.Elapsed).IsLessThan(TestDefaults.InProcessHang);

            // And the census, asked during the hang, still counts the holder.
            await Assert.That(LiveInstances.IsMarkerHeld(live.OwnFile)).IsTrue();
        }
        finally
        {
            responder.Release();
        }
    }

    /// <summary>
    /// The census is read before any pipe is opened: a marker nobody holds is
    /// <i>not running</i>, and a marker whose name carries another pid is a pipe
    /// the client refuses to talk to.
    /// </summary>
    /// <remarks>
    /// <b>The pid check is planted against a marker the suite holds under a pid
    /// that is not its own</b>, so the census says held and the pipe answers, and
    /// the only thing that can refuse is the comparison with
    /// <c>GetNamedPipeServerProcessId</c>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFreeMarkerIsNotRunningAndAPipeServedByAnotherPidIsNotAsked()
    {
        using var root = ScratchDirectory.Create("pipe-census");

        var liveDirectory = Directory.CreateDirectory(LiveInstances.DirectoryUnder(root.Path)).FullName;

        // ---- Free: written and let go, the shape a dead server leaves.
        var free = Path.Combine(liveDirectory, $"{Environment.ProcessId}-{Guid.NewGuid():N}.live");
        await File.WriteAllBytesAsync(free, []);

        var gone = await ServerPipeClient.DescribeAsync(free);

        await Assert.That(gone.Outcome).IsEqualTo(ServerPipeOutcome.NotRunning).Because(gone.Why);

        // ---- Held, with no pipe behind it: a server from before the pipe, or
        // the configuration app. Its own outcome, and never a wait: the
        // framework's client waits out its whole timeout on exactly this, and a
        // wait that ran out would answer NoAnswer, which is the red.
        var pipeless = Path.Combine(liveDirectory, $"{Environment.ProcessId}-{Guid.NewGuid():N}.live");

        using (new FileStream(pipeless, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1))
        {
            var none = await ServerPipeClient.DescribeAsync(pipeless);

            await Assert.That(none.Outcome).IsEqualTo(ServerPipeOutcome.NoPipe).Because(none.Why);
        }

        // ---- Held, named for a pid that is not the pipe's server.
        // Pids are multiples of four, so this is never this process's own.
        var stranger = Environment.ProcessId + 4;
        var foreign = Path.Combine(liveDirectory, $"{stranger}-{Guid.NewGuid():N}.live");

        using var held = new FileStream(foreign, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
        using var responder = new ScriptedResponder();
        using var pipe = ServerPipe.Open(foreign, responder, NullLogger.Instance);

        var refused = await ServerPipeClient.DescribeAsync(foreign, TestDefaults.InProcessHang);

        await Assert.That(refused.Outcome).IsEqualTo(ServerPipeOutcome.NotItsServer).Because(refused.Why);
        await Assert.That(refused.Description).IsNull();
    }

    /// <summary>
    /// <c>stop</c> through the pipe ends a published server the graceful way:
    /// exit 0, its instance directory gone, its session's lock released and its
    /// headless browser gone.
    /// </summary>
    /// <remarks>
    /// <b>Every one of the four is something only the graceful path does.</b> A
    /// process that simply ended would release its locks and lose its browser to
    /// the job object -- the kernel does both -- and would leave its instance
    /// directory behind for the next run's sweep. So the directory is the
    /// witness that the stop went through <c>Program.Main</c>'s own teardown.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStopThroughThePipeEndsAPublishedServerTheGracefulWay()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("pipe-stop");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var session = Path.Combine(scratch.Path, "stopped-session");

        _ = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Init,
            ["arguments"] = new JsonObject { ["directory"] = session, ["purpose"] = "the session a stop through the pipe ends" },
        });

        _ = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl, ["session"] = session, ["why"] = "the suite exercising this call" },
        });

        var server = client.ProcessId;
        var browsers = BrowsersIn(client, server);

        await Assert.That(browsers.Count).IsGreaterThan(0);

        var instance = Directory.EnumerateDirectories(BrowserAiPaths.Real.InstanceRoot, $"{server}-*").Single();
        var lockFile = Path.Combine(session, SessionLayout.LockFileName);

        await Assert.That(IsReleased(lockFile)).IsFalse();

        var marker = await ServerPipeRig.MarkerOfAsync(server, BrowserAiPaths.Real.RootAppDir, TestDefaults.ProcessHang);
        var stop = await ServerPipeClient.StopAsync(marker, TestDefaults.ProcessHang);

        await Assert.That(stop.Outcome).IsEqualTo(ServerPipeOutcome.Answered).Because(stop.Why);

        await Assert.That(await client.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await Assert.That(client.ExitCode).IsEqualTo(0);
        await Assert.That(Directory.Exists(instance)).IsFalse();
        await Assert.That(IsReleased(lockFile)).IsTrue();

        foreach (var (pid, created) in browsers)
        {
            await Assert.That(ProcessIdentity.IsAlive(pid, created)).IsFalse();
        }
    }

    /// <summary>Every process in the client's job but the server itself, with its identity.</summary>
    private static List<(int ProcessId, long Created)> BrowsersIn(RawStdioClient client, int server)
    {
        var found = new List<(int, long)>();

        foreach (var pid in client.JobProcessIds().Where(pid => pid != server))
        {
            if (ProcessCommandLine.ImagePathOf(pid) is { } image
                && image.StartsWith(BrowserAiPaths.Real.BrowsersDirectory, StringComparison.OrdinalIgnoreCase))
            {
                found.Add((pid, ProcessIdentity.CreationTimeOf(pid)));
            }
        }

        return found;
    }

    /// <summary>Whether nobody holds a session's lock file.</summary>
    private static bool IsReleased(string lockFile)
    {
        try
        {
            using var probe = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Whether a name opens again once its previous pipe is gone.</summary>
    private static async Task<bool> OpensAgainAsync(string name, ScriptedResponder responder)
    {
        // The released instance's thread closes its handle on its own way out,
        // a moment after Dispose: a hang detector, not a budget.
        var waited = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                ServerPipe.OpenNamed(name, responder, NullLogger.Instance).Dispose();
                return true;
            }
            catch (IOException) when (waited.Elapsed < TestDefaults.InProcessHang)
            {
                await Task.Delay(20);
            }
        }
    }

    /// <summary>
    /// Serves one request from a pipe the suite writes itself, answering with
    /// the first <paramref name="sent"/> bytes of a framed answer and then
    /// closing, and returns what the product's client made of it.
    /// </summary>
    private static async Task<ServerPipeAnswer> AgainstASuiteServerAsync(string marker, string name, byte[] body, int sent)
    {
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        // ⚠️ CANCELLED ONCE THE CLIENT HAS ANSWERED, so a client that never
        // connects -- one that decided from the census alone -- costs this arm a
        // red and not a wait for a connection nobody will make. Watched: without
        // it, a planted census defect hung the whole filtered run for eleven
        // minutes.
        using var clientDone = new CancellationTokenSource();

        var served = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(clientDone.Token);

            var request = new byte[ServerPipeProtocol.MaximumRequestBytes];
            _ = await server.ReadAsync(request);

            var prefix = new byte[ServerPipeProtocol.LengthPrefixBytes];
            BinaryPrimitives.WriteInt32LittleEndian(prefix, body.Length);

            await server.WriteAsync(prefix);
            await server.WriteAsync(body.AsMemory(0, sent));
            await server.FlushAsync();

            // What a process's death does to every handle it holds.
            await server.DisposeAsync();
        });

        var answer = await ServerPipeClient.DescribeAsync(marker, TestDefaults.InProcessHang);

        await clientDone.CancelAsync();

        try
        {
            await served;
        }
        catch (OperationCanceledException)
        {
            // The client never connected; its answer above says why.
        }

        return answer;
    }
}
