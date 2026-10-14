// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test moves it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because the one timer in the product makes claims about
/// <i>when</i>, and a test that establishes one by letting real time pass is
/// measuring the machine.</b> Measured 2026-08-17 with every test in this suite
/// running at once: a single in-process round trip took <b>1.51 s</b> and
/// <b>2.27 s</b> against an 800 ms idle period, so
/// <c>BrowserIdleTimerTests</c>'s driving loop could not achieve its own
/// premise and failed five times in twenty runs -- every time with the product
/// behaving correctly. Four retries and a budget already raised from two
/// seconds to ten had been spent on that, which is two of the three moves this
/// repository forbids.
/// </para>
/// <para>
/// <b>Hand-written and not taken from
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>.</b> What is needed is
/// forty lines of it -- advance, and fire whatever is due -- and the package
/// would be a floating dependency, a licence entry and an upstream review
/// surface for a type the suite can own outright. That trade is the same one
/// this suite already made for <c>RawStdioClient</c> and
/// <see cref="RawPipeClient"/>.
/// </para>
/// <para>
/// <b>Callbacks fire on the advancing thread, in due-time order, and a callback
/// that re-arms inside its own callback is honoured.</b> That last property is
/// not incidental: <c>BrowserIdleTimer.OnIdle</c> re-arms for the remaining
/// time whenever a call has moved the deadline on, so a clock that ignored a
/// re-arm made during dispatch would silently change the behaviour under test.
/// </para>
/// <para>
/// <b>What it deliberately does not model.</b> There is no automatic advance,
/// no wall-clock component and no thread of its own -- a test that forgets to
/// advance sees a timer that never fires, which is a visible failure and
/// not a flaky one.
/// </para>
/// <para>
/// ⚠️ <b>A timer armed on another thread meets the moves made on this one</b>, added
/// 2026-10-14 by addition. A timer's <c>Change</c> adds its delay to the clock as it
/// reads at that moment, so code that took the delay from an earlier reading, on a
/// thread of its own while a test moved the clock, has its timer late by the move. The
/// relay's rig moves the clock only while its relay is settled for that reason
/// (<c>RelayRig.StepAsync</c>).
/// </para>
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<ManualTimer> _disposed = [];

    private long _now;
    private int _created;
    private ManualTimer? _newest;
    private Action? _atTheNextReading;
    private Action<ITimer>? _atTheNextChoice;

    /// <summary>One tick, in this clock's units, for "just short of" arithmetic.</summary>
    /// <remarks>
    /// The unit is deliberately the same as <see cref="TimestampFrequency"/>'s,
    /// so a test can say <i>one tick short of the period</i> and mean exactly
    /// that, not approximately that.
    /// </remarks>
    public const long OneTick = 1;

    /// <inheritdoc />
    /// <remarks>
    /// Ticks, so a <see cref="TimeSpan"/> converts without rounding: one tick is
    /// 100 ns, and every duration this suite uses is a whole number of them.
    /// </remarks>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The Unix epoch plus the clock's ticks, so it moves with the clock. ⚠️ <i>Corrected
    /// 2026-10-14 (previously "A fixed instant. Nothing under test reads it; it is
    /// implemented and not thrown from so that a future reader of the clock gets a coherent
    /// answer instead of an exception.")</i>: the relay engine reads its every <c>Now</c>
    /// here, as other product code a rig hands this clock to does.
    /// </para>
    /// <para>
    /// An action handed to <see cref="AtTheNextReading"/> runs here once, after the reading
    /// and before it is returned.
    /// </para>
    /// </remarks>
    public override DateTimeOffset GetUtcNow()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp());
        Interlocked.Exchange(ref _atTheNextReading, null)?.Invoke();
        return now;
    }

    /// <summary>
    /// Runs <paramref name="action"/> once, on the thread that next reads
    /// <see cref="GetUtcNow"/>, after that reading is taken and before it is returned.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-14 to force an order no timing can</b>: what code under test does
    /// between two readings of this clock. The relay engine reads it for the delay of a
    /// timer it arms, and the timer reads <see cref="GetTimestamp"/> for the moment the
    /// delay is added to, so an action that moves the clock here moves it between the two,
    /// as a step the arm takes on its own thread can.
    /// </remarks>
    /// <param name="action">What to run.</param>
    public void AtTheNextReading(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Interlocked.CompareExchange(ref _atTheNextReading, action, null) is not null)
        {
            throw new InvalidOperationException("An action is already waiting for the next reading of this clock.");
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> once, on the thread that steps the clock, with the timer
    /// <see cref="FireTheNextDueBy"/> has chosen to fire next, after the choice and before the
    /// clock moves to that timer's due.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-14 to force an order no timing can</b>, the maintainer's 170 a: what
    /// code under test does to a timer of this clock on a thread of its own while a step is
    /// under way. A thread that disarms a timer, or arms one from a reading the step has
    /// already left behind, can do it at any moment of a step, and this is the one moment of
    /// it an arm can name.
    /// </para>
    /// <para>
    /// <b>It runs with the clock's lock held</b>, which a timer of this clock takes again on
    /// the same thread without waiting, so the action may arm, disarm or dispose one; it must
    /// not wait for another thread.
    /// </para>
    /// </remarks>
    /// <param name="action">What to run, handed the chosen timer.</param>
    public void AtTheNextChoice(Action<ITimer> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Interlocked.CompareExchange(ref _atTheNextChoice, action, null) is not null)
        {
            throw new InvalidOperationException("An action is already waiting for the next timer this clock chooses.");
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new ManualTimer(this, callback, state);

        lock (_gate)
        {
            _timers.Add(timer);
            _created++;
            _newest = timer;
        }

        _ = timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>How many timers have been created on this clock, disposed ones included.</summary>
    /// <remarks>
    /// <b>Added 2026-10-03 for the idle close's cap, Q367 a.</b> The close arms a
    /// timer of its own on the session's clock when it asks the browser to close,
    /// so a count that grows is the one synchronous sign an arm can read that the
    /// close has got that far -- and an arm that went on moving the clock after it
    /// would run the cap down by however far it moved.
    /// </remarks>
    public int TimersCreated
    {
        get
        {
            lock (_gate)
            {
                return _created;
            }
        }
    }

    /// <summary>
    /// How far this clock has to move before the timer created last fires, or
    /// <see langword="null"/> when that timer is not armed.
    /// </summary>
    /// <remarks>
    /// What lets an arm stop <i>one tick short</i> of a deadline the product
    /// set by itself, which is the only reading of "not before" that cannot be
    /// satisfied by a clock that happened not to move far enough.
    /// </remarks>
    /// <returns>The time left, or <see langword="null"/>.</returns>
    public TimeSpan? UntilTheNewestTimerFires()
    {
        lock (_gate)
        {
            return _newest?.DueAt is { } due ? TimeSpan.FromTicks(due - _now) : null;
        }
    }

    /// <summary>
    /// Fires, once each and without moving the clock, every timer due within
    /// <paramref name="window"/> of now: a real timer that runs its callback a little
    /// before its moment by the clock the product reads.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-09 for the relay's look timer</b>, which stopped looking for
    /// good the first time that happened. Measured that day on .NET 10.0.12 and Windows
    /// 10.0.26300: 146 of 200 two-second timers armed through
    /// <see cref="TimeProvider.System"/> ran their callback while
    /// <see cref="TimeProvider.GetUtcNow"/> was still before the moment. A callback that
    /// finds its moment not yet come has to arm itself again, and this is how an arm
    /// holds that one does. A callback that re-arms is not fired a second time here.
    /// </remarks>
    /// <param name="window">How far ahead of now a due timer is fired.</param>
    public void FireEarly(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);

        var horizon = GetTimestamp() + window.Ticks;
        List<ManualTimer> due;

        lock (_gate)
        {
            due = [.. _timers.Where(timer => timer.DueAt is { } at && at <= horizon)];
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    /// <summary>
    /// Runs, once each, the callback of every timer disposed since the last call: the
    /// tick a system timer had already queued when its <c>Dispose</c> was called.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-11</b>, after the second PowerShell run of the release gate at
    /// <c>d1bae44f</c> ended on such a tick: <c>BrowserProvisioner.PollTicker</c>'s timer
    /// called <c>Set</c> on the event its <c>Dispose</c> had already closed, on a pool
    /// thread, and the exception ended the test host. A system timer's <c>Dispose</c>
    /// does not wait for a tick already handed to the thread pool, and the
    /// documentation of <c>Timer.Dispose</c> says so: <i>"Callbacks can occur after the
    /// Dispose() method overload has been called, because the timer queues callbacks for
    /// execution by thread pool threads."</i> A timer of this clock never fires once it is
    /// disposed, so this is the one way an arm can deliver that tick.
    /// </remarks>
    /// <returns>How many callbacks ran.</returns>
    public int FireAfterDispose()
    {
        List<ManualTimer> late;

        lock (_gate)
        {
            late = [.. _disposed];
            _disposed.Clear();
        }

        foreach (var timer in late)
        {
            timer.FireLate();
        }

        return late.Count;
    }

    /// <summary>
    /// Moves the clock forward and fires everything that becomes due, in order.
    /// </summary>
    /// <param name="by">How far to move.</param>
    public void Advance(TimeSpan by) => AdvanceTicks(by.Ticks);

    /// <summary>Moves the clock forward by a raw tick count.</summary>
    /// <param name="ticks">How many ticks. Never negative: the clock is monotonic.</param>
    public void AdvanceTicks(long ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        var target = GetTimestamp() + ticks;

        // One due timer at a time, re-reading the set after every callback: a
        // callback may arm a new deadline inside this window, and the whole
        // point of the class under test is that it does exactly that.
        while (FireTheNextDueBy(target))
        {
        }
    }

    /// <summary>
    /// Moves the clock to the earliest moment at or before <paramref name="target"/> at
    /// which a timer is due and fires that one timer; or, when none is due by then, moves
    /// the clock to <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// <b>One step of <see cref="AdvanceTicks"/>, added 2026-10-14</b> for a caller that
    /// must let what a callback set off finish before the clock moves again:
    /// <c>RelayRig.StepAsync</c>, whose relay arms its next timers on a thread of its
    /// own, from readings of this clock.
    /// </remarks>
    /// <param name="target">The tick the clock is being moved to.</param>
    /// <returns><see langword="true"/> when a timer fired; <see langword="false"/> once the clock reads <paramref name="target"/> with nothing due by it.</returns>
    public bool FireTheNextDueBy(long target)
    {
        ManualTimer? next = null;

        lock (_gate)
        {
            foreach (var timer in _timers)
            {
                if (timer.DueAt is { } due && due <= target && (next?.DueAt is not { } best || due < best))
                {
                    next = timer;
                }
            }

            if (next is null)
            {
                _now = target;
                return false;
            }

            Interlocked.Exchange(ref _atTheNextChoice, null)?.Invoke(next);

            _now = next.DueAt!.Value;
        }

        next.Fire();
        return true;
    }

    private void Forget(ManualTimer timer)
    {
        lock (_gate)
        {
            // Kept for FireAfterDispose, once: a second Dispose finds it gone.
            if (_timers.Remove(timer))
            {
                _disposed.Add(timer);
            }
        }
    }

    /// <summary>One timer on a <see cref="ManualClock"/>.</summary>
    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private readonly Lock _gate = new();
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _disposed;

        /// <summary>When this timer next fires, or null when it is disarmed.</summary>
        /// <remarks>
        /// Read under the same lock the arming path writes it under, so a
        /// dispatch can never act on a due time a concurrent
        /// <see cref="Change(TimeSpan, TimeSpan)"/> has half-written.
        /// </remarks>
        public long? DueAt
        {
            get
            {
                lock (_gate)
                {
                    return _disposed ? null : Due;
                }
            }
        }

        /// <summary>The raw due time. Guarded by <see cref="_gate"/>.</summary>
        private long? Due { get; set; }

        /// <inheritdoc />
        /// <remarks>
        /// ⚠️ <b>The clock is read BEFORE this timer's lock is taken, and that
        /// ordering is the whole of what keeps this class from deadlocking.</b>
        /// <see cref="ManualClock.AdvanceTicks"/> holds the clock's lock and asks
        /// every timer for its due time, so a timer that took its own lock and
        /// then reached for the clock's would close the cycle -- and it would
        /// close it exactly when a callback re-armed during a dispatch, which is
        /// the one path this clock exists to model.
        /// </remarks>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            var now = clock.GetTimestamp();

            lock (_gate)
            {
                if (_disposed)
                {
                    // The product catches ObjectDisposedException on this path
                    // and treats it as "teardown already stopped everything", so
                    // answering false instead of throwing keeps the manual
                    // clock from exercising a path the real timer would not.
                    return false;
                }

                _period = period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime.Ticks;
                return true;
            }
        }

        /// <summary>Runs the callback and re-arms if this timer is periodic.</summary>
        public void Fire()
        {
            // Read before the lock, for the reason on Change.
            var now = clock.GetTimestamp();

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                Due = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero
                    ? null
                    : now + _period.Ticks;
            }

            callback(state);
        }

        /// <summary>Runs the callback once, disposed or not, for <see cref="ManualClock.FireAfterDispose"/>.</summary>
        public void FireLate() => callback(state);

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                Due = null;
            }

            clock.Forget(this);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
