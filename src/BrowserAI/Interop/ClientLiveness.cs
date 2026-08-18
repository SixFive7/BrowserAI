// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// The client-liveness half of teardown: an <c>OpenProcess</c> handle on the
/// process that started BrowserAI, signalled the moment it exits.
/// </summary>
/// <remarks>
/// <para>
/// <b>A handle, never a ping.</b> There is no <c>ping</c> to send —
/// <see href="https://modelcontextprotocol.io">MCP</see> removed it at protocol
/// revision <c>2026-07-28</c> — and a poll would be the wrong shape even if
/// there were one: a kernel object is an <i>event</i>, so this costs one thread
/// pool registration and nothing per second. Holding the handle is also what
/// stops Windows recycling the pid underneath the watch, which is the same
/// guarantee <see cref="BrowserProcesses.ScanFor"/> takes for the sweep.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-18 (previously the paragraph above stood alone).</b>
/// The guarantee it claims begins at the instant the handle is opened and says
/// nothing whatever about the interval before it — which is the interval that
/// matters, because the pid comes from a field the kernel never invalidates. So
/// the handle is necessary and was not sufficient, and
/// <see cref="ForProcess"/> now proves the identity on that handle before it is
/// held. Read its remarks: a watch pointed at a recycled pid is not a missing
/// mechanism, it is an <i>active</i> one aimed at a stranger, and firing it
/// tears down every session on the machine.
/// </para>
/// <para>
/// <b>It is the second of two teardown mechanisms, and neither is a close
/// tool.</b> stdin EOF is the backstop and fires instantly when the parent
/// holding the pipe is <c>TerminateProcess</c>d; this covers the case EOF cannot
/// — a client that started BrowserAI through a wrapper, so that the pipe outlives
/// the process that owns the conversation. The job object remains the guarantee
/// underneath both: nothing here has to run for a browser to go.
/// </para>
/// <para>
/// <b>It degrades rather than refusing to start.</b> A parent that cannot be
/// opened — elevated, or already gone — produces a warning and a null watcher,
/// and BrowserAI serves normally with EOF alone. A BrowserAI that would not start
/// because it could not watch its client would be worse than one that watches
/// nothing.
/// </para>
/// </remarks>
internal sealed partial class ClientLivenessWatcher : IDisposable
{
    private const uint ProcessQueryLimitedInformation = 0x00001000;

    /// <summary>
    /// Required to wait on a process handle, and <b>not</b> implied by
    /// <see cref="ProcessQueryLimitedInformation"/>.
    /// </summary>
    private const uint Synchronize = 0x00100000;

    private readonly ManualResetEvent _signal;
    private readonly RegisteredWaitHandle _registration;
    private readonly Action _onExit;
    private readonly ILogger _logger;

    private int _fired;
    private int _disposed;

    private ClientLivenessWatcher(int processId, long createdFileTime, ManualResetEvent signal, Action onExit, ILogger logger)
    {
        ProcessId = processId;
        CreatedFileTime = createdFileTime;
        _signal = signal;
        _onExit = onExit;
        _logger = logger;

        // Registered last, after every field the callback reads is set. An
        // already-dead client makes this fire on this very line, which is the
        // correct outcome and the reason the ordering matters.
        _registration = ThreadPool.RegisterWaitForSingleObject(
            signal,
            static (state, _) => ((ClientLivenessWatcher)state!).Fire(),
            this,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: true);
    }

    /// <summary>The client process this watcher holds a handle on.</summary>
    public int ProcessId { get; }

    /// <summary>
    /// Its creation time, read off the handle this watcher holds — the other
    /// half of the identity, because a pid on its own is not one.
    /// </summary>
    public long CreatedFileTime { get; }

    /// <summary>Whether the watched process has been observed to exit.</summary>
    public bool HasFired => Volatile.Read(ref _fired) is not 0;

    /// <summary>Watches the process that started this one.</summary>
    /// <remarks>
    /// The parent's pid arrives from <c>InheritedFromUniqueProcessId</c> with no
    /// creation time beside it anywhere, so this passes
    /// <see langword="null"/> for the recorded time and
    /// <see cref="ForProcess"/> falls back to the weaker — and, for this one
    /// question, equally exact — test that the process did not start after us.
    /// </remarks>
    /// <param name="onExit">What to do when it goes. Runs on a thread-pool thread.</param>
    /// <param name="logger">Where the watcher reports.</param>
    /// <returns>The watcher, or <see langword="null"/> when there is nothing to watch.</returns>
    public static ClientLivenessWatcher? ForParentProcess(Action onExit, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var parent = ProcessLiveness.ParentProcessId();

        if (parent <= 0)
        {
            ClientLivenessLog.NoClientToWatch(logger);
            return null;
        }

        return ForProcess(parent, recordedCreation: null, onExit, logger);
    }

    /// <summary>Watches one named process, after proving it is that process.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The identity check was added 2026-08-18; before it this opened a
    /// bare pid and held whatever answered.</b> The pid comes from
    /// <c>InheritedFromUniqueProcessId</c>, which the kernel writes once at
    /// creation and never invalidates. A client that started BrowserAI through a
    /// wrapper — the arrangement this whole mechanism exists for — leaves that
    /// number pointing at an exited process, and Windows reuses pids in seconds.
    /// The watch then fires when an <b>unrelated</b> process exits, and firing it
    /// takes every session's child, its browser and its job down while the log
    /// asserts a cause that is false. The mirror is as bad: if the recycler is
    /// long-lived the watch never fires at all, in precisely the case it was
    /// built for. Found by
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-processes.md),
    /// finding 1; <c>Interop\CLAUDE.md</c> had already stated the rule it broke.
    /// </para>
    /// <para>
    /// <b>The check runs on the handle that will be held, never on a re-opened
    /// one.</b> A second open would put a second recycling window between the
    /// proof and the use, which is the window being closed.
    /// </para>
    /// </remarks>
    /// <param name="processId">The client's pid.</param>
    /// <param name="recordedCreation">
    /// The creation time recorded beside that pid, when the caller has one.
    /// <see langword="null"/> means there is no record — the pid came from the
    /// parent field — and the pairing is then
    /// <see cref="ProcessLiveness.StartedNoLaterThanThisProcess"/>.
    /// </param>
    /// <param name="onExit">What to do when it goes. Runs on a thread-pool thread.</param>
    /// <param name="logger">Where the watcher reports.</param>
    /// <returns>The watcher, or <see langword="null"/> when the process cannot be watched.</returns>
    public static ClientLivenessWatcher? ForProcess(int processId, long? recordedCreation, Action onExit, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(onExit);
        ArgumentNullException.ThrowIfNull(logger);

        if (processId <= 0)
        {
            ClientLivenessLog.NoClientToWatch(logger);
            return null;
        }

        var handle = OpenProcess(Synchronize | ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (handle == nint.Zero)
        {
            ClientLivenessLog.ClientCannotBeWatched(
                logger,
                processId,
                new Win32Exception(Marshal.GetLastPInvokeError()).Message);

            return null;
        }

        // THE PAIRING, on the handle that is about to be held rather than on a
        // second open of the same number.
        if (!GetProcessTimes(handle, out var created, out _, out _, out _))
        {
            // Read first, before CloseHandle can replace it with its own.
            var reason = new Win32Exception(Marshal.GetLastPInvokeError()).Message;

            _ = CloseHandle(handle);
            ClientLivenessLog.ClientCannotBeWatched(logger, processId, reason);

            return null;
        }

        if (recordedCreation is { } recorded
            ? created != recorded
            : !ProcessLiveness.StartedNoLaterThanThisProcess(handle, out _))
        {
            _ = CloseHandle(handle);
            ClientLivenessLog.ClientPidIsNotTheClient(logger, processId);
            return null;
        }

        ManualResetEvent? signal = null;

        try
        {
            // The same handle swap LaunchedProcess uses, and for the same
            // reason: RegisterWaitForSingleObject needs a WaitHandle and a
            // process handle is a perfectly good waitable object. ownsHandle
            // is true here -- disposing this watcher is what closes it, which
            // is also what releases the pid.
            signal = new ManualResetEvent(initialState: false);
            var placeholder = signal.SafeWaitHandle;
            signal.SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
            placeholder.Dispose();

            var watcher = new ClientLivenessWatcher(processId, created, signal, onExit, logger);
            ClientLivenessLog.WatchingClient(logger, processId);

            return watcher;
        }
        catch
        {
            signal?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // Before the event is disposed, which is what closes the process handle
        // underneath it.
        _ = _registration.Unregister(null);
        _signal.Dispose();
    }

    private void Fire()
    {
        if (Interlocked.Exchange(ref _fired, 1) is not 0)
        {
            return;
        }

        ClientLivenessLog.ClientExited(_logger, ProcessId);

        try
        {
            _onExit();
        }
#pragma warning disable CA1031 // This runs on a thread-pool callback; an exception escaping it would take the process down instead of shutting it down.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            ClientLivenessLog.TeardownFailed(_logger, ProcessId, failure);
        }
    }

    // System32 only, on every P/Invoke in this repository (CA5392). The raw
    // nint rather than SafeProcessHandle is deliberate: ownership moves to the
    // SafeWaitHandle above, and two wrappers over one handle is a double close.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    // Declared here rather than reached through ProcessLiveness so that the
    // pairing is visible in the same twenty lines as the OpenProcess it pairs
    // with -- which is what ProcessLivenessTests.EveryProcessHandleOpenedInThe
    // ProductIsPairedWithACreationTimeRead reads for.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        nint hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}

/// <summary>Source-generated log messages for the client-liveness watcher.</summary>
/// <remarks>Event ids start at 70, after <see cref="Sessions.IdleLog"/>'s 60s.</remarks>
internal static partial class ClientLivenessLog
{
    /// <summary>A handle is held on the client, and the watch is live.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="processId">The client's pid.</param>
    [LoggerMessage(
        EventId = 70,
        Level = LogLevel.Information,
        Message = "Watching the MCP client, pid {ProcessId}, through an open process handle. It is signalled when the client exits; nothing polls and nothing is pinged.")]
    public static partial void WatchingClient(ILogger logger, int processId);

    /// <summary>There is no parent process to watch.</summary>
    /// <param name="logger">Where it goes.</param>
    [LoggerMessage(
        EventId = 71,
        Level = LogLevel.Warning,
        Message = "BrowserAI could not identify the process that started it, so there is no client-liveness watch. Teardown falls back to stdin EOF alone, which is the ordinary path anyway.")]
    public static partial void NoClientToWatch(ILogger logger);

    /// <summary>The client exists and cannot be opened.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="processId">The client's pid.</param>
    /// <param name="reason">What Windows said.</param>
    [LoggerMessage(
        EventId = 72,
        Level = LogLevel.Warning,
        Message = "A handle could not be opened on the MCP client, pid {ProcessId} ({Reason}), so there is no client-liveness watch. Teardown falls back to stdin EOF alone.")]
    public static partial void ClientCannotBeWatched(ILogger logger, int processId, string reason);

    /// <summary>The client exited and teardown was asked for.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="processId">The client's pid.</param>
    [LoggerMessage(
        EventId = 73,
        Level = LogLevel.Information,
        Message = "The MCP client, pid {ProcessId}, has exited, so BrowserAI is closing its own protocol channel — which ends the conversation exactly as stdin EOF would, without waiting for it. Every session's child, its browser and its job go down with this process.")]
    public static partial void ClientExited(ILogger logger, int processId);

    /// <summary>Asking for teardown threw.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="processId">The client's pid.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 74,
        Level = LogLevel.Error,
        Message = "The MCP client, pid {ProcessId}, exited and asking BrowserAI to stop threw. stdin EOF and the job object are what remain.")]
    public static partial void TeardownFailed(ILogger logger, int processId, Exception failure);

    /// <summary>The pid opened is not the process it was supposed to be.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="processId">The pid that answered.</param>
    [LoggerMessage(
        EventId = 75,
        Level = LogLevel.Warning,
        Message = "Pid {ProcessId} was opened as the MCP client and is a different process from the one that number named — it started after this one, so it cannot be the process that launched BrowserAI. Windows had reused the number. Nothing is watched: teardown falls back to stdin EOF alone, which is correct, where firing this watch on a stranger's exit would have taken every session's browser down.")]
    public static partial void ClientPidIsNotTheClient(ILogger logger, int processId);
}
