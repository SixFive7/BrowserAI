// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Globalization;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Sessions;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Coordination;

/// <summary>
/// The names the session host is reached by and started with, spelled once for the
/// coordinator that starts it, the server that is it, and the front that relays to it.
/// </summary>
internal static class SessionHostProtocol
{
    /// <summary>What every session host pipe's name starts with.</summary>
    public const string PipePrefix = @"\\.\pipe\BrowserAI-Host-";

    /// <summary>
    /// The server's argument that makes it the session host, followed by the pipe it
    /// serves. The coordinator passes it, and nothing else does.
    /// </summary>
    public const string HostArgument = "--host";

    /// <summary>
    /// The server's argument that makes it relay to a session host, followed by the
    /// host's pipe: the seam the suite drives the relay through.
    /// </summary>
    public const string RelayArgument = "--relay";

    // ⚠️ RETIRED 2026-10-04: `ShutdownCloseBudget` stood here, thirty seconds, "the
    // idle close's own cap". D4.1 and D4.2, the maintainer's words verbatim: "Make it
    // a roomy 1 min." and "Same 1 min. under option d (lane c)". The host's close
    // before an update is `SessionTimes.BrowserCloseCap` since, the one cap every
    // close takes, declared in this library so the coordinator and the server read
    // the same value.

    /// <summary>
    /// How long the coordinator waits for the host to close everything and end before
    /// an update: twice <see cref="SessionTimes.BrowserCloseCap"/>, <b>two minutes</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Twice the close, because the host's shutdown is two steps.</b> First every
    /// browser's own close, all at once and each capped at
    /// <see cref="SessionTimes.BrowserCloseCap"/>, or a close already in flight waited
    /// for under the same cap; then each session's child and the tool list's own child
    /// ended through their stdin, each given its transport's shutdown timeout, five
    /// seconds, before its job is closed. The second cap covers the second step with
    /// room, and <c>SessionHostBoundsTests</c> holds that it does.
    /// </para>
    /// <para>
    /// ⚠️ <b>Two minutes since 2026-10-04</b> (previously sixty seconds, twice the
    /// thirty-second close it followed), when the cap became a minute.
    /// </para>
    /// <para>
    /// <b>A hang detector, and past it nothing is left running</b>: the coordinator
    /// exits to let the update in, which closes its job, and the kernel ends the host
    /// and everything it started.
    /// </para>
    /// </remarks>
    public static TimeSpan StopBound { get; } = SessionTimes.BrowserCloseCap * 2;

    /// <summary>The session host pipe for an install root.</summary>
    /// <remarks>
    /// <b>Named for the install root the way the census gate, the coordinator's pipe
    /// and the logon task are</b>, with the same key, so one install has one host.
    /// </remarks>
    /// <param name="installRoot">The install root, or the data root of a process that is not installed.</param>
    /// <returns>The full pipe name.</returns>
    public static string NameFor(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        return PipePrefix + LiveInstances.RootKeyFor(installRoot);
    }
}

/// <summary>The session host, as the coordinator's loop sees it.</summary>
/// <remarks>
/// <b>A seam, so the suite drives the loop with a host it scripts</b>, the way it
/// drives it with a scripted scan. The product's is <see cref="SessionHostKeeper"/>.
/// </remarks>
internal interface ISessionHostHold
{
    /// <summary>
    /// The running host as a handle to wait on, signalled when it exits, or
    /// <see langword="null"/> when none runs.
    /// </summary>
    WaitHandle? Running { get; }

    /// <summary>
    /// Has the host close every browser and end, before an update, and refuses to
    /// start another until <see cref="Reopen"/>.
    /// </summary>
    /// <returns>Whether no host runs once this returns.</returns>
    bool StopForUpdate();

    /// <summary>Lets a host be started again, after an update that did not happen.</summary>
    void Reopen();
}

/// <summary>
/// The coordinator's hold on the session host: a kill-on-close job of the
/// coordinator's own, and the host process inside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b, the maintainer's words of 2026-10-03, verbatim:</b> <i>"Q366 b - lets go
/// with a fully build option c. If the server crashes and the coordinator loses the
/// pipe, keep the browser around with the already running activity timeout timer
/// active."</i> And the brief's first rule: the coordinator launches and owns each
/// session's child and browser in a kill-on-close job of its own, and every process
/// stays contained.
/// </para>
/// <para>
/// <b>The job is the containment, and the coordinator holds its only handle.</b> The
/// host is created inside it; every child the host starts is created inside a job of
/// its own nested in this one; so the coordinator ending, however it ends, closes this
/// handle and the kernel ends the host, every <c>node</c> and every browser. Measured
/// with stand-ins before this was written: a coordinator started by the Task Scheduler
/// and then killed took its two browsers with it, both exiting as the kill landed
/// (<c>docs/evidence/2026-10-03-coordinator-survival</c>).
/// </para>
/// <para>
/// <b>It starts nothing until a front asks</b>, and starts at most one host at a time:
/// a host that exits, on its own linger or by a crash, is started again by the next
/// front that asks.
/// </para>
/// </remarks>
internal sealed class SessionHostKeeper : ISessionHostHold, IDisposable
{
    private readonly Lock _gate = new();
    private readonly JobObject _job;
    private readonly string _server;
    private readonly IReadOnlyList<string> _arguments;
    private readonly string _workingDirectory;
    private readonly Func<int, bool> _askToStop;
    private readonly TimeSpan _stopBound;
    private readonly ILogger _logger;

    private JobMember? _host;
    private HeldProcess? _held;
    private bool _closing;
    private int _disposed;

    /// <summary>Creates the job; nothing is started yet.</summary>
    /// <param name="installRoot">The install root, which names the host's pipe and holds its census marker.</param>
    /// <param name="server">The server binary to start as the host: <c>current\BrowserAI.Server.exe</c> beside this app.</param>
    /// <param name="workingDirectory">The host's working directory.</param>
    /// <param name="logger">Where starts and exits are recorded.</param>
    public SessionHostKeeper(string installRoot, string server, string workingDirectory, ILogger logger)
        : this(
            installRoot,
            server,
            [SessionHostProtocol.HostArgument, SessionHostProtocol.NameFor(installRoot)],
            workingDirectory,
            processId => AskTheHostToStop(installRoot, processId),
            SessionHostProtocol.StopBound,
            logger)
    {
    }

    /// <summary>Creates the job with the suite's seams; nothing is started yet.</summary>
    /// <param name="installRoot">The install root, which names the host's pipe.</param>
    /// <param name="server">The binary to start as the host.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="workingDirectory">Its working directory.</param>
    /// <param name="askToStop">Asks the host, by its pid, to close everything and end; whether it took the request.</param>
    /// <param name="stopBound">How long a stop for an update waits for the host to end.</param>
    /// <param name="logger">Where starts and exits are recorded.</param>
    internal SessionHostKeeper(
        string installRoot,
        string server,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Func<int, bool> askToStop,
        TimeSpan stopBound,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(askToStop);
        ArgumentNullException.ThrowIfNull(logger);

        PipeName = SessionHostProtocol.NameFor(installRoot);
        _server = server;
        _arguments = arguments;
        _workingDirectory = workingDirectory;
        _askToStop = askToStop;
        _stopBound = stopBound;
        _logger = logger;
        _job = JobObject.CreateKillOnClose();
    }

    /// <summary>
    /// The keeper for the install this app runs from, or <see langword="null"/> when it
    /// is not installed and so has no server of its own to start.
    /// </summary>
    /// <remarks>
    /// <b>The server is the one a client is registered with</b>, resolved exactly the
    /// way registration resolves it, so the host is the same binary every client's
    /// server is and the two can never be two builds.
    /// </remarks>
    /// <param name="imagePath">This app's own image, <see cref="Environment.ProcessPath"/>.</param>
    /// <param name="workingDirectory">The host's working directory: the data root, so no handle on <c>current</c> stands in an update's way.</param>
    /// <param name="logger">Where starts and exits are recorded.</param>
    /// <returns>The keeper, or <see langword="null"/>.</returns>
    public static SessionHostKeeper? ForThisInstall(string? imagePath, string workingDirectory, ILogger logger) =>
        RegistrationTarget.TryResolve(imagePath, out var target, out _)
            ? new SessionHostKeeper(target!.InstallRoot, target.Command, workingDirectory, logger)
            : null;

    /// <summary>The pipe the host serves.</summary>
    public string PipeName { get; }

    /// <summary>Whether a host this keeper started is still running.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _held is { } held && !held.WaitOne(0);
            }
        }
    }

    /// <summary>The pid of the host this keeper started last, or <see langword="null"/> before the first.</summary>
    public int? ProcessId
    {
        get
        {
            lock (_gate)
            {
                return _host?.ProcessId;
            }
        }
    }

    /// <inheritdoc />
    public WaitHandle? Running
    {
        get
        {
            lock (_gate)
            {
                return _held is { } held && !held.WaitOne(0) ? held : null;
            }
        }
    }

    /// <summary>Every process in the coordinator's job: the host and everything it started.</summary>
    /// <returns>The pids, or none once the job cannot be read.</returns>
    public IReadOnlyList<int> Members()
    {
        try
        {
            return _job.ProcessIds();
        }
        catch (Exception failure) when (failure is Win32Exception or ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>Starts the host when none runs; does nothing when one does.</summary>
    /// <remarks>
    /// <b>Refused while an update is closing the host</b> (<see cref="StopForUpdate"/>),
    /// so a server that asks in that window serves its client itself and is not handed
    /// a host the update is about to end.
    /// </remarks>
    /// <returns>Whether a host runs once this returns.</returns>
    public bool EnsureStarted()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) is not 0 || _closing)
            {
                return false;
            }

            if (_held is { } held && !held.WaitOne(0))
            {
                return true;
            }

            _held?.Dispose();
            _host?.Dispose();
            _held = null;
            _host = null;

            try
            {
                var environment = Environment.GetEnvironmentVariables()
                    .Cast<System.Collections.DictionaryEntry>()
                    .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                var host = JobLauncher.StartInJob(_job, _server, _arguments, _workingDirectory, environment);

                _host = host;
                _held = BrowserProcesses.Hold(host.ProcessId, host.CreatedFileTime);

                KeeperLog.Started(_logger, host.ProcessId, PipeName);

                return _held is not null;
            }
            catch (Win32Exception failure)
            {
                KeeperLog.NotStarted(_logger, _server, failure);
                return false;
            }
        }
    }

    /// <summary>
    /// Leaves the coordinator's own processes out of a scan of the install root, so
    /// the apply loop sees only what it does not own.
    /// </summary>
    /// <remarks>
    /// <b>The host and its <c>node</c> children run from the install root</b>, so the
    /// scan finds them, and an apply would end them. They are the coordinator's to
    /// close before an apply (<see cref="StopForUpdate"/>), not processes it waits for.
    /// </remarks>
    /// <param name="scan">The scan. Owned from here: what is left out is let go.</param>
    /// <returns>The scan without this keeper's processes.</returns>
    public RootScan LeaveOutMine(RootScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var mine = Members().ToHashSet();

        if (mine.Count is 0)
        {
            return scan;
        }

        var kept = new List<HeldProcess>();

        foreach (var process in scan.Held)
        {
            if (mine.Contains(process.ProcessId))
            {
                process.Dispose();
            }
            else
            {
                kept.Add(process);
            }
        }

        return new RootScan(kept, scan.Unresolved);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>The brief's rule for updates</b>: the host and its children run from the
    /// install root, so an apply must stop them; browsers are closed cleanly first,
    /// generously capped (<see cref="SessionTimes.BrowserCloseCap"/>), and
    /// sessions come back through <c>browserai_resume</c> and the session restore. The
    /// host's own shutdown is the clean close; its stop is asked through its pipe and
    /// its census marker, the way anything stops a server.
    /// </para>
    /// <para>
    /// <b>A host that does not end inside <see cref="SessionHostProtocol.StopBound"/>
    /// is not left behind</b>: the coordinator exits to let the update in, and its job
    /// takes the host with it. This returns <see langword="false"/> then, and the log
    /// says the stop was not clean.
    /// </para>
    /// </remarks>
    public bool StopForUpdate()
    {
        HeldProcess? held;
        int processId;

        lock (_gate)
        {
            _closing = true;
            held = _held;
            processId = _host?.ProcessId ?? 0;
        }

        if (held is null || held.WaitOne(0))
        {
            return true;
        }

        var asked = _askToStop(processId);
        var ended = held.WaitOne(asked ? _stopBound : TimeSpan.Zero);

        KeeperLog.StoppedForUpdate(_logger, processId, asked, ended);

        return ended;
    }

    /// <inheritdoc />
    public void Reopen()
    {
        lock (_gate)
        {
            _closing = false;
        }
    }

    /// <summary>Asks a host, by its pid, to stop through its own pipe, found by its census marker.</summary>
    /// <param name="installRoot">The install root, under which the census lives.</param>
    /// <param name="processId">The host's pid.</param>
    /// <returns>Whether its pipe took the stop.</returns>
    private static bool AskTheHostToStop(string installRoot, int processId)
    {
        string? marker;

        try
        {
            marker = Directory
                .EnumerateFiles(LiveInstances.DirectoryUnder(installRoot), string.Create(CultureInfo.InvariantCulture, $"{processId}-*.live"))
                .FirstOrDefault();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (marker is null)
        {
            return false;
        }

        // Off this thread, which is the dialog's apartment: nothing the call awaits
        // may come back to it.
        var answer = Task.Run(() => ServerPipeClient.StopAsync(marker)).GetAwaiter().GetResult();

        return answer.Outcome is ServerPipeOutcome.Answered;
    }

    /// <summary>
    /// Closes the job, which ends the host and everything it started that is still
    /// running: the coordinator's own way out.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        lock (_gate)
        {
            _job.Dispose();
            _held?.Dispose();
            _host?.Dispose();
        }
    }
}

/// <summary>Source-generated log messages for the coordinator's hold on the session host.</summary>
internal static partial class KeeperLog
{
    /// <summary>The host was started.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="processId">Its pid.</param>
    /// <param name="pipe">The pipe it serves.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Started the session host, pid {ProcessId}, serving {Pipe}, in the coordinator's own kill-on-close job.")]
    public static partial void Started(ILogger logger, int processId, string pipe);

    /// <summary>The host could not be started.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The binary that was tried.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "The session host could not be started from {Server}; a front that asked serves its client itself.")]
    public static partial void NotStarted(ILogger logger, string server, Exception failure);

    /// <summary>The host was asked to stop for an update.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="processId">Its pid.</param>
    /// <param name="asked">Whether its pipe took the stop.</param>
    /// <param name="ended">Whether it ended inside the bound.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Asked the session host, pid {ProcessId}, to close every browser and end for an update: stop taken {Asked}, ended {Ended}.")]
    public static partial void StoppedForUpdate(ILogger logger, int processId, bool asked, bool ended);
}
