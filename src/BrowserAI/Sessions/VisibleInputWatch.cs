// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using BrowserAI.Interop;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// A visible session as the input watch sees it: which process owns its browser's
/// window, and what to do when a person used that window.
/// </summary>
internal interface IVisibleWindowOwner
{
    /// <summary>
    /// The pid of the session's browser main process, or <see langword="null"/> while
    /// none is known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A bare pid, and safe only because it is pinned.</b> The session found that
    /// process inside its own job by full image path and holds it open
    /// (<see cref="BrowserExitWatch.Browser"/>), so Windows cannot hand the number to
    /// another process while the session is registered, and a window whose owner
    /// answers it is the browser's. A <see langword="null"/> is never matched, by
    /// any window.
    /// </para>
    /// <para>
    /// Read outside the watch's lock, and only on a check that saw new input, so it
    /// must be a plain read that takes no lock of its own.
    /// </para>
    /// </remarks>
    int? WindowProcessId { get; }

    /// <summary>A person used this session's window since the previous check.</summary>
    /// <remarks>
    /// Called on the thread that ran the check, outside the watch's lock: the timer's
    /// thread-pool thread, or the caller of <see cref="VisibleInputWatch.Tick"/>.
    /// An exception is logged and goes no further.
    /// </remarks>
    void PersonWasActive();
}

/// <summary>
/// The four Windows reads one check makes, behind a seam the suite can count.
/// </summary>
internal interface IInputProbe
{
    /// <summary><c>GetForegroundWindow</c>.</summary>
    /// <returns>The window in front, or zero.</returns>
    nint ForegroundWindow();

    /// <summary><c>GetWindowThreadProcessId</c>.</summary>
    /// <param name="window">The window.</param>
    /// <param name="processId">The owning pid.</param>
    /// <returns>The creating thread's id, or zero when Windows did not answer.</returns>
    uint WindowThread(nint window, out int processId);

    /// <summary><c>GetLastInputInfo</c>'s <c>dwTime</c>.</summary>
    /// <param name="tick">The tick count of the last input this session received.</param>
    /// <returns>Whether Windows answered.</returns>
    bool LastInput(out uint tick);

    /// <summary><c>GetTickCount</c>.</summary>
    /// <returns>The tick count now.</returns>
    uint TickCount();
}

/// <summary>
/// Counts a person's keyboard or mouse input in a visible session's browser window as
/// activity for that session: one shared timer for the whole process, running only
/// while a visible session is registered, and four Windows reads a check whatever the
/// number of sessions.
/// </summary>
/// <remarks>
/// <para>
/// <b>F4 a, decided 2026-10-08 by the maintainer, his conditions verbatim:</b>
/// <i>"f4 a - but only if this is easy"</i>, <i>"Make sure the keyboard and mouse
/// input check does not lag the system."</i>, and <i>"Make sure these reads happen
/// at the same pace regardless of how many visible windows there are. No 100x fold
/// checks just because there are 100 windows open."</i> So there are no input hooks
/// of any kind (<see cref="InputActivity"/> says why), there is one timer however
/// many sessions are visible, and a check never asks Windows anything per session.
/// </para>
/// <para>
/// <b>One check, in this order.</b> The window in front, and the process that owns
/// it; a NULL window or a zero thread is <i>unknown</i>, and unknown attributes
/// nothing. Then the tick count and the last-input tick, <b>on every check</b>,
/// whatever the first two said, so the baseline never goes stale: input made in
/// another window, or behind a lock screen, is not attributed later to a browser
/// window a person merely switched to. New input since the previous check is
/// <c>now - lastInput &lt; now - previousCheck</c> in unsigned 32-bit arithmetic,
/// which holds across <c>GetTickCount</c>'s 49.7-day wrap and refuses a
/// <c>SendInput</c> tick from the past or the future. The tick count is read
/// before the last-input tick, so input that lands between the two reads carries a
/// tick no older than this check's and is counted by this check or the next; read
/// the other way round, it would carry a tick no newer than the new baseline and be
/// lost. A session whose browser owns the window in front is told, through
/// <see cref="IVisibleWindowOwner.PersonWasActive"/>, once per check.
/// </para>
/// <para>
/// <b>What it cannot see, stated and not implied.</b> Input made in a session's
/// window and followed, before the next check, by a switch to another window is
/// not attributed, because the window in front is read at the check and not at the
/// input. And input in the same millisecond tick as the previous check's tick count
/// but after it is not attributed either, when nothing follows it before the next
/// check, because the comparison is strict; <c>GetTickCount</c> moves in steps of
/// the system timer, so that is a window of about one step after each check.
/// </para>
/// <para>
/// <b>The sessions are told outside the lock.</b> The lock serializes the reads, the
/// baseline and the registrations; the sessions are told after it is released, on the
/// thread that ran the check, so a session that takes a lock of its own in
/// <see cref="IVisibleWindowOwner.PersonWasActive"/>, and calls
/// <see cref="Tick"/> while holding it, cannot deadlock against the timer.
/// </para>
/// </remarks>
internal sealed class VisibleInputWatch : IDisposable
{
    private readonly Lock _gate = new();
    private readonly IInputProbe _probe;
    private readonly Func<Action, IDisposable> _startPeriodic;
    private readonly ILogger _logger;
    private readonly List<Registration> _registrations = [];

    private IDisposable? _periodic;
    private uint _previousCheck;
    private bool _disposed;

    /// <summary>Creates a watch with nothing registered and no timer.</summary>
    /// <param name="probe">The four reads.</param>
    /// <param name="startPeriodic">
    /// Starts the periodic timer that calls the given action, and answers what stops
    /// it. Called when the first session registers and whenever no timer is running
    /// at a registration, never otherwise.
    /// </param>
    /// <param name="logger">Where the watch reports.</param>
    public VisibleInputWatch(IInputProbe probe, Func<Action, IDisposable> startPeriodic, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(startPeriodic);
        ArgumentNullException.ThrowIfNull(logger);

        _probe = probe;
        _startPeriodic = startPeriodic;
        _logger = logger;
    }

    /// <summary>Whether the periodic timer is running.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _periodic is not null;
            }
        }
    }

    /// <summary>
    /// The product's watch: the reads of this desktop, and a coalescable timer at
    /// <see cref="SessionTimes.VisibleInputCheckInterval"/> that Windows may run up to
    /// <see cref="SessionTimes.VisibleInputCheckTolerance"/> late.
    /// </summary>
    /// <param name="logger">Where the watch reports.</param>
    /// <returns>The watch, with nothing registered and no timer.</returns>
    public static VisibleInputWatch ForThisDesktop(ILogger logger) =>
        new(
            new DesktopInput(),
            static tick => CoalescableTimer.Start(SessionTimes.VisibleInputCheckInterval, SessionTimes.VisibleInputCheckTolerance, tick),
            logger);

    /// <summary>
    /// Registers a visible session. The first registration starts the timer, and
    /// disposing the last one stops it.
    /// </summary>
    /// <param name="owner">The session.</param>
    /// <returns>The registration, which the session disposes when it stops being visible.</returns>
    public IDisposable Watch(IVisibleWindowOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var registration = new Registration(this, owner);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _registrations.Add(registration);

            if (_registrations.Count is 1)
            {
                // The baseline: input older than the first visible session is nobody's.
                _previousCheck = _probe.TickCount();
            }

            if (_periodic is null)
            {
                try
                {
                    _periodic = _startPeriodic(OnTimer);
                    VisibleInputLog.Started(_logger);
                }
                catch (Win32Exception failure)
                {
                    // An addition to a visible session, never a condition of one: the
                    // session is registered and runs, and the next registration tries
                    // again.
                    VisibleInputLog.CouldNotStart(_logger, failure);
                }
            }
        }

        return registration;
    }

    /// <summary>
    /// One check, now. The timer calls this once an interval, and a session's countdown
    /// may call it just before it would close a visible session, so input in the last
    /// seconds before the deadline is not missed.
    /// </summary>
    /// <remarks>
    /// Does nothing at all, and reads nothing, while no session is registered.
    /// </remarks>
    public void Tick()
    {
        IVisibleWindowOwner[] owners;
        int front;

        lock (_gate)
        {
            if (_disposed || _registrations.Count is 0)
            {
                return;
            }

            var window = _probe.ForegroundWindow();

            front = window == nint.Zero || _probe.WindowThread(window, out var process) is 0 ? 0 : process;

            var now = _probe.TickCount();
            var answered = _probe.LastInput(out var lastInput);
            var sincePrevious = unchecked(now - _previousCheck);

            _previousCheck = now;

            if (front is 0 || !answered || unchecked(now - lastInput) >= sincePrevious)
            {
                return;
            }

            owners = [.. _registrations.Select(registration => registration.Owner)];
        }

        foreach (var owner in owners)
        {
            try
            {
                if (owner.WindowProcessId == front)
                {
                    owner.PersonWasActive();
                }
            }
#pragma warning disable CA1031 // One session's failure must not stop the others being told, or the timer that comes next.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                VisibleInputLog.SessionFailed(_logger, failure);
            }
        }
    }

    /// <summary>Stops the timer and forgets every registration.</summary>
    public void Dispose()
    {
        IDisposable? periodic;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _registrations.Clear();
            periodic = _periodic;
            _periodic = null;
        }

        periodic?.Dispose();
    }

    private void OnTimer()
    {
        try
        {
            Tick();
        }
#pragma warning disable CA1031 // A thread-pool callback: an exception escaping it would end the process, where a failed check costs one check.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            VisibleInputLog.CheckFailed(_logger, failure);
        }
    }

    private void Release(Registration registration)
    {
        IDisposable? periodic;

        lock (_gate)
        {
            if (!_registrations.Remove(registration) || _registrations.Count is not 0 || _periodic is null)
            {
                return;
            }

            periodic = _periodic;
            _periodic = null;
        }

        periodic.Dispose();
        VisibleInputLog.Stopped(_logger);
    }

    /// <summary>The reads of the desktop BrowserAI runs on.</summary>
    internal sealed class DesktopInput : IInputProbe
    {
        /// <inheritdoc />
        public nint ForegroundWindow() => InputActivity.ForegroundWindow();

        /// <inheritdoc />
        public uint WindowThread(nint window, out int processId) => InputActivity.WindowThread(window, out processId);

        /// <inheritdoc />
        public bool LastInput(out uint tick) => InputActivity.LastInput(out tick);

        /// <inheritdoc />
        public uint TickCount() => InputActivity.TickCount();
    }

    private sealed class Registration(VisibleInputWatch watch, IVisibleWindowOwner owner) : IDisposable
    {
        private int _disposed;

        public IVisibleWindowOwner Owner { get; } = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is 0)
            {
                watch.Release(this);
            }
        }
    }
}

/// <summary>Source-generated log messages for the input watch.</summary>
/// <remarks>
/// <b>Event ids start at 100</b>, a range of their own after <c>ReapLog</c>'s 90s, for
/// the reason that one gives: a person scanning <c>[nn]</c> in the one machine-wide
/// file should not meet two meanings under one number.
/// </remarks>
internal static partial class VisibleInputLog
{
    /// <summary>The first visible session registered, and the timer started.</summary>
    /// <param name="logger">Where it goes.</param>
    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Information,
        Message = "A visible session is open, so the check for a person's keyboard or mouse input in its window has started: one timer for every visible session in this process.")]
    public static partial void Started(ILogger logger);

    /// <summary>The last visible session went, and the timer stopped.</summary>
    /// <param name="logger">Where it goes.</param>
    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Information,
        Message = "No visible session is left, so the check for a person's input has stopped and its timer is gone.")]
    public static partial void Stopped(ILogger logger);

    /// <summary>Windows would not create or set the timer.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="failure">What Windows said.</param>
    [LoggerMessage(
        EventId = 102,
        Level = LogLevel.Warning,
        Message = "The check for a person's input in visible sessions could not start its timer, so that input does not count as activity until the next visible session opens and the timer is tried again. The sessions themselves run as before.")]
    public static partial void CouldNotStart(ILogger logger, Exception failure);

    /// <summary>A session threw when it was told a person had used its window.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="failure">What it threw.</param>
    [LoggerMessage(
        EventId = 103,
        Level = LogLevel.Warning,
        Message = "A visible session threw when it was told a person had used its window. The other sessions were still told, and the next check runs at its interval.")]
    public static partial void SessionFailed(ILogger logger, Exception failure);

    /// <summary>A check failed before any session was told.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="failure">What it threw.</param>
    [LoggerMessage(
        EventId = 104,
        Level = LogLevel.Warning,
        Message = "One check for a person's input in visible sessions failed. The next runs at its interval.")]
    public static partial void CheckFailed(ILogger logger, Exception failure);
}
