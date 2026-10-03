// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Interop;
using BrowserAI.Registration;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Coordination;

/// <summary>How a server's search for the session host went.</summary>
internal enum HostReach
{
    /// <summary>A host was serving, and this server is connected to it.</summary>
    Connected,

    /// <summary>A coordinator was asked to start one, and it came up inside the bound.</summary>
    StartedByTheCoordinator,

    /// <summary>The logon task was run to start a coordinator, which started one inside the bound.</summary>
    StartedThroughTheTask,

    /// <summary>No host came up, and the log says why; the server serves its client itself.</summary>
    None,
}

/// <summary>
/// How a server a client started reaches the session host: connect when one serves,
/// and otherwise have the coordinator start one, starting the coordinator itself
/// through the logon task when none runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b, the maintainer's words of 2026-10-03, verbatim:</b> <i>"Q366 b - lets go
/// with a fully build option c."</i> And the brief: the server starts the coordinator
/// through the logon task if it is not running, which is Q283 a's path. The host is the
/// coordinator's child and not this server's, so it is outside the client's tree and
/// the client's job: a client's kill of this server reaches nothing it holds, measured
/// with stand-ins under every client exit, 21 of 21
/// (<c>docs/evidence/2026-10-03-coordinator-survival</c>).
/// </para>
/// <para>
/// <b>The wait is bounded by the clients' own patience, and past it the server serves
/// its client itself</b>, exactly as before this design (<see cref="StartBound"/>).
/// </para>
/// </remarks>
internal static class SessionHostAccess
{
    /// <summary>
    /// How long a client gives a stdio server to start before it gives up on it:
    /// <b>30 s</b>, the same in both clients BrowserAI registers with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured for Codex, read for Claude Code, both on 2026-10-03.</b> codex-cli
    /// 0.155.0-alpha.9.2 and 0.160.0, with a configuration that sets no timeout, reported
    /// a server ready that answered after 15 s and failed one that answered after 35 s at
    /// 30.0 s, with its own <i>"timed out after 30 seconds"</i>
    /// (<c>docs/evidence/2026-10-03-codex-default-timeouts</c>; the kb's earlier ten
    /// seconds is corrected there). Claude Code 2.1.288 reads <c>MCP_TIMEOUT</c> or
    /// 30000 ms in its binary, and its debug log says <i>"Starting connection with timeout
    /// of 30000ms"</i>; that was read and not timed.
    /// </para>
    /// <para>
    /// <b>What no bound here can meet</b>, measured the same day: <c>codex exec</c> sent
    /// its first turn about 1.2 s after a slow server started, without that server's
    /// tools, 4 of 4. So a front that has to wait for a cold host misses a Codex thread's
    /// first turn whatever this bound is. The hazard index carries it.
    /// </para>
    /// </remarks>
    public static TimeSpan ClientStartupAllowance { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a server waits for a host to come up before it serves its client
    /// itself: half of <see cref="ClientStartupAllowance"/>, <b>15 s</b>.
    /// </summary>
    /// <remarks>
    /// <b>Half for the wait and half for what follows it.</b> A client's
    /// <c>initialize</c> waits through both: this bound, and then, when no host came,
    /// the in-process start the server falls back to, which is the same work the
    /// host's own start does, a surface child spawned and handshaken. Splitting the
    /// client's allowance evenly gives each the same room. <i>Corrected 2026-10-03,
    /// before any release carried it (previously 6 s, "under Codex's own: Codex gives
    /// a stdio server ten seconds to start by default"), when that default was read in
    /// Codex's source as 30 s.</i>
    /// </remarks>
    public static TimeSpan StartBound { get; } = ClientStartupAllowance / 2;

    /// <summary>Connects to the session host for an install, starting one when none serves.</summary>
    /// <param name="installRoot">The install root, which names the host's pipe and the coordinator's.</param>
    /// <param name="appId">The pack id the logon task is named for, or <see langword="null"/> when this process is not installed.</param>
    /// <param name="tasks">The task scheduler.</param>
    /// <param name="logger">Where the search is recorded.</param>
    /// <param name="how">How it went.</param>
    /// <returns>The connected pipe, opened for asynchronous I/O, or <see langword="null"/>.</returns>
    public static FileStream? Reach(string installRoot, string? appId, ILogonTasks tasks, ILogger logger, out HostReach how)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(logger);

        var name = SessionHostProtocol.NameFor(installRoot);

#pragma warning disable CA2000 // The caller owns the connected pipe.
        if (TryConnect(name) is { } serving)
#pragma warning restore CA2000
        {
            how = HostReach.Connected;
            return serving;
        }

        var clock = Stopwatch.StartNew();
        var asked = CoordinatorClient.SendAsync(installRoot, CoordinatorVerb.Host, grant: null).GetAwaiter().GetResult();

        if (asked.Outcome is HandOverOutcome.Answered)
        {
            how = HostReach.StartedByTheCoordinator;
        }
        else if (asked.Outcome is HandOverOutcome.NoCoordinator && appId is { Length: > 0 })
        {
            var run = tasks.Run(SignInTask.NameFor(appId, installRoot), CoordinatorProtocol.StartHostArgument);

            if (run.Change is not TaskChange.Started)
            {
                AccessLog.NotReached(logger, name, $"no coordinator serves this install and the logon task did not start: {run.Detail}");
                how = HostReach.None;
                return null;
            }

            how = HostReach.StartedThroughTheTask;
        }
        else
        {
            AccessLog.NotReached(logger, name, $"the coordinator was not asked to start one: {asked.Why}");
            how = HostReach.None;
            return null;
        }

        while (clock.Elapsed < StartBound)
        {
            // The pause every wait on a pipe of ours takes between two looks: a
            // polling interval and not a bound, which StartBound is.
            Thread.Sleep(CoordinatorStart.StoppingPause);

#pragma warning disable CA2000 // The caller owns the connected pipe.
            if (TryConnect(name) is { } started)
#pragma warning restore CA2000
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var who = how.ToString();

                    AccessLog.Reached(logger, name, who, clock.Elapsed.TotalMilliseconds);
                }

                return started;
            }
        }

        AccessLog.NotReached(logger, name, $"no host served it within {StartBound.TotalSeconds:F0} s of asking ({how})");
        how = HostReach.None;
        return null;
    }

    /// <summary>Connects to a host's pipe when it exists and has a free instance.</summary>
    /// <param name="name">The full pipe name.</param>
    /// <returns>The connected pipe, or <see langword="null"/>.</returns>
    public static FileStream? TryConnect(string name)
    {
        var handle = NamedPipes.OpenClient(name, out _);

        try
        {
            if (handle.IsInvalid)
            {
                return null;
            }

            var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);

            // The stream owns the handle from here.
            handle = null;

            return stream;
        }
        finally
        {
            handle?.Dispose();
        }
    }
}

/// <summary>Source-generated log messages for a server's search for the session host.</summary>
internal static partial class AccessLog
{
    /// <summary>A host came up after this server asked for one.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="pipe">The host's pipe.</param>
    /// <param name="how">Who started it.</param>
    /// <param name="milliseconds">How long it took.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "The session host on {Pipe} came up {Milliseconds:F0} ms after this server asked ({How}).")]
    public static partial void Reached(ILogger logger, string pipe, string how, double milliseconds);

    /// <summary>No host could be reached, and this server serves its client itself.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="pipe">The host's pipe.</param>
    /// <param name="why">Why.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "No session host on {Pipe}: {Why}. This server serves its client itself, and its sessions end with it.")]
    public static partial void NotReached(ILogger logger, string pipe, string why);
}
