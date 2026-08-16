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

    private ClientLivenessWatcher(int processId, ManualResetEvent signal, Action onExit, ILogger logger)
    {
        ProcessId = processId;
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

    /// <summary>Whether the watched process has been observed to exit.</summary>
    public bool HasFired => Volatile.Read(ref _fired) is not 0;

    /// <summary>Watches the process that started this one.</summary>
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

        return ForProcess(parent, onExit, logger);
    }

    /// <summary>Watches one named process.</summary>
    /// <param name="processId">The client's pid.</param>
    /// <param name="onExit">What to do when it goes. Runs on a thread-pool thread.</param>
    /// <param name="logger">Where the watcher reports.</param>
    /// <returns>The watcher, or <see langword="null"/> when the process cannot be watched.</returns>
    public static ClientLivenessWatcher? ForProcess(int processId, Action onExit, ILogger logger)
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

            var watcher = new ClientLivenessWatcher(processId, signal, onExit, logger);
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
}
