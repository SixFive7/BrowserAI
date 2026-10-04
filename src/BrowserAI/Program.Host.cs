// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI;

/// <summary>
/// The two modes Q366 b adds to the server binary: the session host the coordinator
/// starts, and the front a client starts, which relays to it.
/// </summary>
/// <remarks>
/// <b>Q366 b, the maintainer's words of 2026-10-03, verbatim:</b> <i>"Q366 b - lets go
/// with a fully build option c. If the server crashes and the coordinator loses the
/// pipe, keep the browser around with the already running activity timeout timer
/// active. This allows restarting vscode, the claude code plugin or soemthing without
/// losing the state."</i> The design and what it was chosen from are in
/// <c>docs/design/coordinator-owned-browsers</c>.
/// </remarks>
internal static partial class Program
{
    /// <summary>
    /// Starts this binary as the session host, serving the pipe named after it.
    /// </summary>
    /// <remarks>
    /// <b>Not a supported interface</b>: the coordinator passes it, inside its own
    /// kill-on-close job, and nothing else does. A client starting the server with it
    /// gets a process that serves no stdio.
    /// </remarks>
    public const string HostArgument = SessionHostProtocol.HostArgument;

    /// <summary>
    /// Relays this server's stdio to the session host on the pipe named after it, and
    /// serves nothing itself.
    /// </summary>
    /// <remarks>
    /// <b>The seam the suite drives the relay through</b>, with a host it started. An
    /// installed server finds its host through the coordinator and is given no
    /// argument at all.
    /// </remarks>
    public const string RelayArgument = SessionHostProtocol.RelayArgument;

    /// <summary>The pipe name that follows an argument, or <see langword="null"/>.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The value after it.</returns>
    internal static string? ValueOf(string[]? args, string argument)
    {
        if (args is null)
        {
            return null;
        }

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], argument, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// The session host: every session of every client, served over a pipe, for as
    /// long as it holds one or a client is connected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What it shares with a client's server is everything below the stdio</b>:
    /// the census, its own pipe, the stray sweep, the instance directory, the payload,
    /// the verdicts and provisioning. What it does not have is a client to watch, a
    /// stdout to own and an update lane: the coordinator decides when an update
    /// applies, and a stop through this process's own pipe closes every browser first.
    /// </para>
    /// <para>
    /// <b>It joins the live census</b> because it runs from the install root: an apply
    /// ends it, so a client's server that saw nothing else running would apply out from
    /// under every session on the machine.
    /// </para>
    /// </remarks>
    /// <param name="paths">The data root.</param>
    /// <param name="log">The process log.</param>
    /// <param name="logger">Startup's logger.</param>
    /// <param name="updateLogger">The update lane's logger, for the census.</param>
    /// <param name="pipeName">The pipe to serve.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> RunTheSessionHostAsync(
        LocalAppDataPaths paths,
        ProcessLog log,
        ILogger logger,
        ILogger updateLogger,
        string pipeName)
    {
        var scope = InstallRootScope.Judge(paths.RootAppDir, InstallLocation.RootAppDir);

        if (scope.Unestablished is { } unestablished)
        {
            StartupLog.AppRootScopeUnestablished(logger, unestablished);
        }

        if (!scope.MayServe)
        {
            StartupLog.AppRootIsShared(logger, scope.Refusal!);
            return 1;
        }

        var installRoot = InstallLocation.RootAppDir ?? paths.RootAppDir;

        StraySweep.StartInBackground(() => CreateSweep(paths, installRoot, log.Factory), logger);

        using var live = LiveInstances.Join(installRoot, updateLogger);
        LiveInstances.StartReclaimInBackground(installRoot, updateLogger);

        using var stopping = new CancellationTokenSource();

        var activity = new ServerActivity(TimeProvider.System, Environment.CurrentDirectory);
        SessionHostServer? serving = null;

        var responder = new ServerPipeResponder(activity, () => Volatile.Read(ref serving)?.Stop())
        {
            Role = ServerDescription.Roles.Host,
        };

        using var pipe = OpenPipe(live, responder, log.Factory.CreateLogger("BrowserAI.Pipe"));

        var run = InstanceDirectory.CreateFresh(paths, logger);
        var instance = run.Directory;

        try
        {
            var payload = new PayloadLayout();
            var verdicts = ToolVerdicts.Read(payload.ToolVerdicts);

            var options = ChildLaunch.Create(
                payload,
                paths.BrowsersDirectory,
                instance,
                Path.Combine(instance, "playwright-mcp.config.json"),
                BrowserConfiguration.ForSurface(instance),
                name: "playwright-mcp[surface]");

            using var provisioner = new BrowserProvisioner(payload, paths.BrowsersDirectory, log.Factory);

            var environment = new SessionEnvironment
            {
                Paths = paths,
                Payload = payload,
                Verdicts = verdicts,
                Provisioner = provisioner,
                InstanceDirectory = instance,
                OpenSessionLog = ProcessLog.OpenSessionLog,

                // Nobody kills the host on a clock, so its browsers get the idle
                // close's generous cap at shutdown and not a client's one second.
                ShutdownCloseBudget = SessionHostProtocol.ShutdownCloseBudget,
            };

            var host = await SessionHost.ConnectAsync(
                new DirectStdioClientTransport(options, log.Factory),
                log.Factory,
                environment).ConfigureAwait(false);

            await using var hostScope = host.ConfigureAwait(false);

            responder.AttachSessions(host.Sessions.Held);

            var server = new SessionHostServer(host, pipeName, log.Factory, TimeProvider.System, SessionHostServer.Linger);

            await using var serverScope = server.ConfigureAwait(false);

            Volatile.Write(ref serving, server);
            server.Start();
            activity.Serving();

            HostLog.Serving(logger, pipeName);

            await server.Finished.ConfigureAwait(false);

            HostLog.Ending(logger, host.Sessions.HeldCount);

            return 0;
        }
#pragma warning disable CA1031 // The process boundary reports every failure the same way: a log record and a non-zero exit code.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            StartupLog.Failed(logger, failure);
            return 1;
        }
        finally
        {
            run.Dispose();
            InstanceDirectory.Delete(instance, logger);
        }
    }

    /// <summary>
    /// The front: relays this server's stdio to the session host, byte for byte, until
    /// either end closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It parses nothing.</b> The host speaks MCP to the client through it, so the
    /// handshake, the tool list, every refusal and every answer are the host's, and a
    /// frame reaches the client as the host wrote it.
    /// </para>
    /// <para>
    /// <b>Either end ending ends both.</b> The client closing stdin, or going away,
    /// closes the pipe, and the host detaches what this connection drove. The host
    /// going away closes stdout, and the client sees its server end, as it would have
    /// if this process had died.
    /// </para>
    /// </remarks>
    /// <param name="host">The connected pipe. Owned from here.</param>
    /// <param name="logger">Where the relay reports.</param>
    /// <param name="stopping">Cancelled when the client goes away or a stop arrives.</param>
    /// <returns>Zero.</returns>
    private static async Task<int> RelayAsync(FileStream host, ILogger logger, CancellationToken stopping)
    {
        await using var hostScope = host.ConfigureAwait(false);

        // Last, and only once everything that could fail loudly has: from here
        // stdout is the relay's, through the one owner it has.
        using var channel = StdioChannel.OpenStandardStreams();

        var up = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var down = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Two threads of their own, each in a blocking copy: stdin's asynchronous
        // read is the same blocking call on a pool thread, and a read parked on a
        // console handle is ended by nothing, so the pump that reads it is a
        // background thread the process does not wait for.
        StartPump("BrowserAI relay, client to host", () => PumpUp(channel.Input, host), up);
        StartPump("BrowserAI relay, host to client", () => PumpDown(host, channel), down);

        var client = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var departure = stopping.Register(() => client.TrySetResult()).ConfigureAwait(false);

        var first = await Task.WhenAny(up.Task, down.Task, client.Task).ConfigureAwait(false);

        HostLog.RelayEnded(logger, first == up.Task ? "the client's input ended" : first == down.Task ? "the session host's pipe ended" : "the client went away");

        // Closing the pipe is what tells the host this connection ended; closing the
        // channel is what tells the client this server has.
        await host.DisposeAsync().ConfigureAwait(false);
        channel.Dispose();

        return 0;
    }

    /// <summary>Runs one copy on a background thread and says when it has ended.</summary>
    /// <param name="name">The thread's name.</param>
    /// <param name="copy">The copy.</param>
    /// <param name="ended">Completed when the copy returns.</param>
    private static void StartPump(string name, Action copy, TaskCompletionSource ended) =>
        new Thread(() =>
        {
            try
            {
                copy();
            }
            finally
            {
                _ = ended.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = name,
        }.Start();

    /// <summary>Copies the client's stdin to the host's pipe until either ends.</summary>
    /// <param name="input">This process's stdin.</param>
    /// <param name="host">The pipe.</param>
    private static void PumpUp(Stream input, Stream host)
    {
        var buffer = new byte[NamedPipes.StreamBufferBytes];

        try
        {
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);

                if (read is 0)
                {
                    return;
                }

                host.Write(buffer, 0, read);
                host.Flush();
            }
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Either end going away mid-copy is how a relay ends.
        }
    }

    /// <summary>Copies the host's pipe to the client's stdout until either ends.</summary>
    /// <param name="host">The pipe.</param>
    /// <param name="channel">This process's stdio, the one owner of stdout.</param>
    private static void PumpDown(Stream host, StdioChannel channel)
    {
        var buffer = new byte[NamedPipes.StreamBufferBytes];

        try
        {
            while (true)
            {
                var read = host.Read(buffer, 0, buffer.Length);

                if (read is 0)
                {
                    return;
                }

                channel.Output.Write(buffer, 0, read);
                channel.Output.Flush();
            }
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Either end going away mid-copy is how a relay ends.
        }
    }

    /// <summary>
    /// The session host this server relays to, or <see langword="null"/> when it serves
    /// its client itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three answers.</b> A pipe named on the command line is the suite's seam, and
    /// nothing else is tried. An install looks for its own host, and has the
    /// coordinator start one when none serves, starting the coordinator itself through
    /// the logon task when none runs (<see cref="SessionHostAccess"/>). A build that is
    /// not installed has no logon task and no coordinator to start one, and serves its
    /// client itself, which is every developer run and every slice the suite drives
    /// without asking for a host.
    /// </para>
    /// <para>
    /// <b>Not finding one is not a failure</b>: the server serves its client in-process,
    /// as every server did before Q366 b, and the log says so.
    /// </para>
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <param name="installRoot">The install root, which names the host.</param>
    /// <param name="logger">Where the search is recorded.</param>
    /// <param name="pipe">The pipe that was connected to, for the log.</param>
    /// <returns>The connected pipe, or <see langword="null"/>.</returns>
    private static FileStream? FindTheSessionHost(string[]? args, string installRoot, ILogger logger, out string pipe)
    {
        if (ValueOf(args, RelayArgument) is { Length: > 0 } named)
        {
            pipe = named;
            // The suite's seam gets the wait an installed front gets.
            return ConnectToTheHost(named, SessionHostAccess.StartBound, logger);
        }

        pipe = SessionHostProtocol.NameFor(installRoot);

        return InstallLocation.IsInstalled
            ? SessionHostAccess.Reach(installRoot, InstallLocation.AppId, Registration.ScheduledTasks.Instance, logger, out _)
            : null;
    }

    /// <summary>
    /// Connects to a session host's pipe, waiting out a moment in which every instance
    /// is busy, or answers <see langword="null"/> when no host serves the name.
    /// </summary>
    /// <param name="name">The full pipe name.</param>
    /// <param name="bound">How long to keep trying while the name exists and is busy.</param>
    /// <param name="logger">Where a failure is reported.</param>
    /// <returns>The connected pipe, opened for asynchronous I/O, or <see langword="null"/>.</returns>
    private static FileStream? ConnectToTheHost(string name, TimeSpan bound, ILogger logger)
    {
        var deadline = Environment.TickCount64 + (long)bound.TotalMilliseconds;

        while (true)
        {
            var handle = NamedPipes.OpenClient(name, out var error);

            try
            {
                if (!handle.IsInvalid)
                {
                    var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);

                    // The stream owns the handle from here.
                    handle = null;

                    return stream;
                }
            }
            finally
            {
                handle?.Dispose();
            }

            var left = deadline - Environment.TickCount64;

            if (error is not NamedPipes.ErrorPipeBusy || left <= 0)
            {
                HostLog.NoHost(logger, name, new Win32Exception(error).Message);
                return null;
            }

            // Returns as soon as an instance is free; a name that has gone fails the
            // next open with something other than busy, which ends the loop above.
            _ = NamedPipes.WaitForFreeInstance(name, (uint)left);
        }
    }
}

/// <summary>Source-generated log messages for the host and relay modes.</summary>
internal static partial class HostLog
{
    /// <summary>This process is the session host.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="pipe">The pipe it serves.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "BrowserAI is the session host, serving {Pipe}.")]
    public static partial void Serving(ILogger logger, string pipe);

    /// <summary>The session host is ending.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="sessions">How many sessions it still held, which its shutdown closes.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "The session host is ending with {Sessions} session(s), each asked to close its browser first.")]
    public static partial void Ending(ILogger logger, int sessions);

    /// <summary>This server relays to the session host.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="pipe">The pipe it relays to.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "BrowserAI relays its client to the session host on {Pipe}; the host holds the sessions and keeps them if this process ends.")]
    public static partial void Relaying(ILogger logger, string pipe);

    /// <summary>The relay ended.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="why">Which end ended it.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "The relay to the session host ended: {Why}.")]
    public static partial void RelayEnded(ILogger logger, string why);

    /// <summary>No session host could be reached.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="pipe">The pipe that was tried.</param>
    /// <param name="why">What Windows said.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "No session host answered on {Pipe} ({Why}).")]
    public static partial void NoHost(ILogger logger, string pipe, string why);
}
