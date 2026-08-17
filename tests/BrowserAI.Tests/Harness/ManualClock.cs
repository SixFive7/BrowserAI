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
/// premise and failed five times in twenty runs — every time with the product
/// behaving correctly. Four retries and a budget already raised from two
/// seconds to ten had been spent on that, which is two of the three moves this
/// repository forbids.
/// </para>
/// <para>
/// <b>Hand-written rather than taken from
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>.</b> What is needed is
/// forty lines of it — advance, and fire whatever is due — and the package
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
/// no wall-clock component and no thread of its own — a test that forgets to
/// advance sees a timer that never fires, which is a visible failure rather
/// than a flaky one.
/// </para>
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];

    private long _now;

    /// <summary>One tick, in this clock's units, for "just short of" arithmetic.</summary>
    /// <remarks>
    /// The unit is deliberately the same as <see cref="TimestampFrequency"/>'s,
    /// so a test can say <i>one tick short of the period</i> and mean exactly
    /// that rather than approximately that.
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
    /// A fixed instant. Nothing under test reads it; it is implemented rather
    /// than thrown from so that a future reader of the clock gets a coherent
    /// answer instead of an exception.
    /// </remarks>
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp());

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new ManualTimer(this, callback, state);

        lock (_gate)
        {
            _timers.Add(timer);
        }

        _ = timer.Change(dueTime, period);
        return timer;
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
        while (true)
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
                    return;
                }

                _now = next.DueAt!.Value;
            }

            next.Fire();
        }
    }

    private void Forget(ManualTimer timer)
    {
        lock (_gate)
        {
            _ = _timers.Remove(timer);
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
        /// then reached for the clock's would close the cycle — and it would
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
                    // answering false rather than throwing keeps the manual
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
