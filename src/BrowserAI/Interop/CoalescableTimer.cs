// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// A periodic timer Windows may run late by a stated tolerance, so that its wake-up
/// can share one with other timers: a waitable timer set through
/// <c>SetWaitableTimerEx</c> with a tolerable delay, and waited on by the thread pool.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the framework's timers.</b> Microsoft's idle-energy assessment names
/// <c>SetTimer</c>, <c>CreateTimerQueueTimer</c> and <c>Sleep</c> as timers that do
/// not coalesce, and <c>SetWaitableTimerEx</c> with a tolerable delay as one that
/// does
/// (<see href="https://learn.microsoft.com/windows-hardware/test/assessments/results-for-the-idle-energy-efficiency-assessment#issues">Idle Energy Efficiency Assessment</see>);
/// its power guidance says a periodic timer should <i>"set the interval to a value
/// greater than one second"</i>
/// (<see href="https://learn.microsoft.com/windows/win32/sync/waitable-timer-objects">Waitable Timer Objects</see>).
/// The visible-input check is the one periodic wake-up BrowserAI has, so it is the
/// one place this matters.
/// </para>
/// <para>
/// <b>A synchronization timer, never a notification timer.</b> It is created without
/// <c>CREATE_WAITABLE_TIMER_MANUAL_RESET</c>, so a completed wait resets it and a
/// wait registered for every signal fires once a period. A manual-reset periodic
/// timer <i>"is set to the signaled state when the initial due time arrives and
/// remains signaled until it is reset"</i>
/// (<see href="https://learn.microsoft.com/windows/win32/api/synchapi/nf-synchapi-setwaitabletimerex">SetWaitableTimerEx</see>),
/// and a wait registered on one fires without pause from then on: planted on
/// 2026-10-08, the second firing came 0.39 ms after the first.
/// <c>VisibleInputWatchTests</c> holds the spacing of two real ticks.
/// </para>
/// <para>
/// <b>No completion routine, so the thread that set it may end.</b> Windows cancels
/// a timer whose setting thread exits only when a completion routine is attached:
/// <i>"If there is no completion routine, then terminating the thread has no effect
/// on the timer"</i> (the same page). The watch starts this from whatever thread
/// registered the first visible session, and thread-pool threads come and go.
/// ⚠️ The same page says the opposite further down, <i>"If the thread that called
/// SetWaitableTimerEx exits, the timer is canceled"</i>, so it was measured: on
/// 2026-10-08 a timer set from a thread that ended before its first tick went on
/// firing for twelve minutes, in each of three processes
/// (<see href="../../../kb/windows/processes.md">kb/windows/processes.md</see>).
/// </para>
/// </remarks>
internal sealed partial class CoalescableTimer : IDisposable
{
    /// <summary>Needed by <c>SetWaitableTimerEx</c> and <c>CancelWaitableTimer</c>.</summary>
    private const uint TimerModifyState = 0x0002;

    /// <summary>Needed to wait on the timer at all.</summary>
    private const uint Synchronize = 0x00100000;

    private readonly TimerHandle _timer;
    private readonly Action _tick;
    private readonly RegisteredWaitHandle _registration;
    private int _disposed;

    private CoalescableTimer(TimerHandle timer, Action tick)
    {
        _timer = timer;
        _tick = tick;

        // Registered last, after every field the callback reads is set.
        _registration = ThreadPool.RegisterWaitForSingleObject(
            timer,
            static (state, _) => ((CoalescableTimer)state!).Fire(),
            this,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// Starts a timer that calls <paramref name="tick"/> once a period, the first time
    /// one period from now.
    /// </summary>
    /// <param name="period">How often it fires.</param>
    /// <param name="tolerance">How late Windows may run each firing to share a wake-up with another timer.</param>
    /// <param name="tick">What it calls, on a thread-pool thread. It must not throw.</param>
    /// <returns>The timer, which the caller disposes to stop it.</returns>
    /// <exception cref="Win32Exception">Windows would not create or set the timer.</exception>
    public static CoalescableTimer Start(TimeSpan period, TimeSpan tolerance, Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentOutOfRangeException.ThrowIfLessThan(period, ProcessBounds.ShortestTimerPeriod);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(period, TimeSpan.FromMilliseconds(int.MaxValue));
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tolerance, TimeSpan.FromMilliseconds(int.MaxValue));

        var handle = CreateWaitableTimerExW(nint.Zero, null, 0, TimerModifyState | Synchronize);

        if (handle.IsInvalid)
        {
            // Read first, before Dispose can replace it with its own.
            var error = Marshal.GetLastPInvokeError();

            handle.Dispose();
            throw new Win32Exception(error, "Windows would not create the timer for the check of a person's input in visible sessions.");
        }

        var timer = new TimerHandle(handle);

        // Relative, so negative, in 100 ns units: one period from now.
        var due = -period.Ticks;

        if (!SetWaitableTimerEx(handle, in due, (int)period.TotalMilliseconds, nint.Zero, nint.Zero, nint.Zero, (uint)tolerance.TotalMilliseconds))
        {
            var error = Marshal.GetLastPInvokeError();

            timer.Dispose();
            throw new Win32Exception(error, "Windows would not set the timer for the check of a person's input in visible sessions.");
        }

        try
        {
            return new CoalescableTimer(timer, tick);
        }
        catch
        {
            _ = CancelWaitableTimer(handle);
            timer.Dispose();
            throw;
        }
    }

    /// <summary>Stops the timer: no call starts after this returns, and the handle is closed.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // The wait first, so the pool dispatches nothing new; then the timer, so
        // Windows stops waking for it; then the handle.
        _ = _registration.Unregister(null);
        _ = CancelWaitableTimer(_timer.SafeWaitHandle);
        _timer.Dispose();
    }

    private void Fire()
    {
        if (Volatile.Read(ref _disposed) is not 0)
        {
            return;
        }

        _tick();
    }

    /// <summary>
    /// The timer as a <see cref="WaitHandle"/> the thread pool can wait on, owning the
    /// handle, so its raw value never leaves a <see cref="SafeHandle"/>.
    /// </summary>
    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    // System32 only, on every P/Invoke in this repository (CA5392). kernel32 is a
    // KnownDLL, so the attribute cannot change these three; it is here because the
    // rule is every declaration.
    //
    // No security attributes, so the handle is not inherited, and no name, so no
    // other process can open it. Flags 0: a synchronization timer, low resolution.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeWaitHandle CreateWaitableTimerExW(
        nint lpTimerAttributes,
        string? lpTimerName,
        uint dwFlags,
        uint dwDesiredAccess);

    // No completion routine, no argument for one, and no wake context: the timer
    // never wakes a sleeping machine, and only the tolerable delay is used.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimerEx(
        SafeWaitHandle hTimer,
        in long lpDueTime,
        int lPeriod,
        nint pfnCompletionRoutine,
        nint lpArgToCompletionRoutine,
        nint wakeContext,
        uint tolerableDelay);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelWaitableTimer(SafeWaitHandle hTimer);
}
