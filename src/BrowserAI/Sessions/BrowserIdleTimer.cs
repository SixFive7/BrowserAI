// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// The <b>only</b> timer in BrowserAI: a session's browser is closed once it has
/// gone unused for <see cref="DefaultIdlePeriod"/>, and the node child is kept.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is exactly one timer, and this is it.</b> No handle-expiry timer, no
/// session TTL and no reclaim window — <b>reclaim is forever</b>, because the
/// durable thing is the profile rather than the process: a resume after killing
/// the node child preserves cookies, localStorage, IndexedDB, service workers and
/// CacheStorage, losing only <c>sessionStorage</c>, in ~515 ms
/// ([kb](../../../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)).
/// Every expiry timer that was considered was a cliff that deleted work in
/// exchange for nothing: an agent thinking for 61 minutes came back to a dead
/// handle, and the recovery was a <c>resume</c> it could have done anyway. The
/// cost is honest — directories accumulate forever — and it is why explicit
/// <c>browserai_list</c> and <c>browserai_destroy</c> matter here more, not less.
/// </para>
/// <para>
/// <b>The relaunch is implicit, and that is what makes the timer safe to have at
/// all.</b> Measured 2026-08-16 twice against <c>@playwright/mcp</c> 0.0.79 with
/// <c>chromium-1237</c>: <c>browser_close</c> takes the whole browser tree down —
/// 8 then 7 processes to zero, 378.3 MB then 369.4 MB of browser working set to
/// zero — leaves the node child running, and the <i>next</i> tool call brings the
/// browser back in 416 ms then 409 ms with no error and no
/// <i>"browser is closed"</i> anywhere. Nothing in BrowserAI relaunches
/// anything: Playwright creates the browser lazily on first use, so the recovery
/// is upstream's own behaviour rather than a thing this product had to build
/// ([kb: timings](../../../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)).
/// </para>
/// <para>
/// <b>It starts disarmed.</b> A session that has been opened and never driven has
/// no browser — Playwright has not launched one — so arming at <c>init</c> would
/// buy one pointless round trip per session and a log line saying nothing was
/// closed. The first forwarded tool call arms it.
/// </para>
/// <para>
/// <b>A call in flight is a session being driven, however long the call takes.</b>
/// <see cref="Call"/> both resets the period and marks the call outstanding, so a
/// navigation that outlives the whole period cannot have the browser closed
/// underneath it. The residual window — a call that arrives in the microseconds
/// between the decision to close and the close being sent — is <i>narrowed</i> by
/// a second check rather than eliminated, and it is harmless for the reason above:
/// the caller's next call relaunches the browser and answers normally.
/// </para>
/// </remarks>
internal sealed class BrowserIdleTimer : IAsyncDisposable
{
    /// <summary>
    /// The shipped period: <b>ten minutes</b>, reset by any tool call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It closes the browser and keeps the node child. Re-measured 2026-08-16,
    /// that is ~496 MB → ~118 MB, with the next call bringing the browser back in
    /// ~0.41 s
    /// ([kb](../../../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)) —
    /// so the period is long enough that ordinary think-time between calls never
    /// closes a browser, and the cost of being wrong is under half a second on a
    /// relaunch the caller cannot see.
    /// </para>
    /// <para>
    /// The suite drives the timer in milliseconds through
    /// <see cref="SessionEnvironment.BrowserIdlePeriod"/>, which is why this
    /// constant is asserted on directly: a test-friendly value that leaked into
    /// the product would show up nowhere else, because a browser closing too
    /// eagerly is invisible — the next call silently relaunches it.
    /// </para>
    /// </remarks>
    public static TimeSpan DefaultIdlePeriod { get; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a close is given before teardown stops waiting for it.</summary>
    private static readonly TimeSpan CloseBudget = TimeSpan.FromSeconds(20);

    private readonly Lock _gate = new();
    private readonly string _session;
    private readonly Func<CancellationToken, Task<BrowserCloseResult>> _closeBrowser;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Timer _timer;

    private int _inFlight;
    private bool _closing;
    private Task? _close;
    private int _closes;
    private int _disposed;

    /// <summary>
    /// When this session becomes idle, as a monotonic tick count. Guarded by
    /// <see cref="_gate"/>.
    /// </summary>
    private long Deadline { get; set; }

    /// <summary>Creates a session's timer, disarmed.</summary>
    /// <param name="session">The session directory, for the log.</param>
    /// <param name="period">How long idle is. The shipped value is <see cref="DefaultIdlePeriod"/>.</param>
    /// <param name="closeBrowser">Closes the browser and leaves the node child. Reports what went away.</param>
    /// <param name="logger">This session's own logger.</param>
    public BrowserIdleTimer(
        string session,
        TimeSpan period,
        Func<CancellationToken, Task<BrowserCloseResult>> closeBrowser,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(closeBrowser);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);

        _session = session;
        Period = period;
        _closeBrowser = closeBrowser;
        _logger = logger;

        _timer = new Timer(static state => ((BrowserIdleTimer)state!).OnIdle(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The period this session's browser may sit idle for.</summary>
    public TimeSpan Period { get; }

    /// <summary>How many idle closes have completed on this session.</summary>
    /// <remarks>
    /// Exposed because it is the only observable difference between a browser
    /// that was closed and one that never started: both leave zero processes.
    /// </remarks>
    public int Closes => Volatile.Read(ref _closes);

    /// <summary>
    /// Marks one tool call as driving this session, and resets the period at both
    /// ends of it.
    /// </summary>
    /// <returns>A scope to dispose when the call is answered.</returns>
    public IDisposable Call()
    {
        lock (_gate)
        {
            _inFlight++;
            Arm();
        }

        return new CallScope(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _timer.DisposeAsync().ConfigureAwait(false);

        Task? close;

        lock (_gate)
        {
            close = _close;
        }

        if (close is not null)
        {
            try
            {
                // Bounded: a close that will not finish must not turn a session
                // teardown into a hang. The job object is what guarantees the
                // browser goes either way.
                await close.WaitAsync(CloseBudget).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A close that was cancelled or failed on the way down is the ordinary path here, and it is already logged.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        _stopping.Dispose();
    }

    /// <summary>Restarts the period. The caller holds <see cref="_gate"/>.</summary>
    private void Arm() => ArmFor(Period);

    /// <summary>Sets the deadline and the timer together. The caller holds <see cref="_gate"/>.</summary>
    /// <remarks>
    /// <b>The deadline is what decides, and the timer is only a wake-up.</b>
    /// <see cref="Timer.Change(TimeSpan, TimeSpan)"/> cannot recall a callback
    /// the pool has already dispatched, so a re-arm that lands in that window
    /// leaves an <see cref="OnIdle"/> in flight against the <i>old</i> deadline.
    /// Keeping the deadline separately makes that callback harmless: it sees
    /// time left and re-arms for the remainder.
    /// </remarks>
    private void ArmFor(TimeSpan delay)
    {
        if (Volatile.Read(ref _disposed) is not 0)
        {
            return;
        }

        // From `delay`, never from the period: re-arming a stale callback for
        // what is LEFT must reproduce the existing deadline rather than push it
        // out by another whole period.
        Deadline = Environment.TickCount64 + (long)delay.TotalMilliseconds;

        try
        {
            // One-shot: fired once, it stays disarmed until the next call. A
            // periodic timer would re-close a browser that is already closed
            // every ten minutes for as long as the session lives.
            _ = _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Raced with teardown, which has already stopped everything.
        }
    }

    private void OnIdle()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) is not 0 || _closing)
            {
                return;
            }

            if (_inFlight > 0)
            {
                // Driven, not idle. Re-armed rather than closed.
                Arm();
                return;
            }

            // A callback from an EARLIER arming that Change could not recall,
            // arriving after a call has already moved the deadline on. Timer
            // rearms and dispatched callbacks race by construction, and the
            // window is a few milliseconds wide -- narrow enough that this
            // suite could not provoke it on demand, which is exactly why it is
            // guarded rather than tested: the symptom would be a ten-minute
            // timer behaving like a two-second one, and nothing would show it,
            // because the caller's next call silently relaunches the browser.
            var remaining = Deadline - Environment.TickCount64;

            if (remaining > 0)
            {
                ArmFor(TimeSpan.FromMilliseconds(remaining));
                return;
            }

            _closing = true;
            _close = Task.Run(CloseAsync, CancellationToken.None);
        }
    }

    private async Task CloseAsync()
    {
        try
        {
            lock (_gate)
            {
                // The second look. A call that arrived between the decision and
                // this line is still driving the session, so the close is
                // abandoned rather than raced.
                if (_inFlight > 0)
                {
                    Arm();
                    return;
                }
            }

            var result = await _closeBrowser(_stopping.Token).ConfigureAwait(false);

            if (result.Failure is { } failure)
            {
                IdleLog.CloseRefused(_logger, _session, failure);
                return;
            }

            _ = Interlocked.Increment(ref _closes);
            IdleLog.BrowserClosed(_logger, _session, Period, result.ProcessesBefore, result.ProcessesAfter);
        }
        catch (OperationCanceledException)
        {
            // Teardown cancelled it. The job object takes the browser instead.
        }
#pragma warning disable CA1031 // A browser that will not close is a log line: the session stays usable, and the next call relaunches whatever did go.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            IdleLog.CloseFailed(_logger, _session, failure);
        }
        finally
        {
            lock (_gate)
            {
                _closing = false;
                _close = null;
            }
        }
    }

    private void Finished()
    {
        lock (_gate)
        {
            _inFlight--;
            Arm();
        }
    }

    private sealed class CallScope(BrowserIdleTimer owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is 0)
            {
                owner.Finished();
            }
        }
    }
}

/// <summary>What one idle close did, as evidence rather than as an assumption.</summary>
/// <remarks>
/// <b>The two counts are the whole point.</b> "The browser was closed" is a claim
/// no log line can support on its own; <c>11 → 1</c> processes left in the child's
/// job says the browser tree went and the node child stayed, which is exactly the
/// pair of facts this timer promises and either alone is the wrong outcome.
/// </remarks>
/// <param name="ProcessesBefore">Processes in the child's job before the close.</param>
/// <param name="ProcessesAfter">Processes left in it afterwards, which should be the node child alone.</param>
/// <param name="Failure">Why nothing was closed, when nothing was.</param>
internal readonly record struct BrowserCloseResult(int ProcessesBefore, int ProcessesAfter, string? Failure);

/// <summary>Source-generated log messages for the browser-idle timer.</summary>
/// <remarks>Event ids start at 60, after <see cref="SessionToolLog"/>'s 40s.</remarks>
internal static partial class IdleLog
{
    /// <summary>A session's browser was closed because nothing had driven it.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="period">How long it had been idle.</param>
    /// <param name="before">Processes in the child's job before.</param>
    /// <param name="after">Processes in it afterwards.</param>
    [LoggerMessage(
        EventId = 60,
        Level = LogLevel.Information,
        Message = "Browser closed after {Period} idle on the session at {Session}; its node child is still running. Processes in that child's job: {Before} → {After}. The next tool call relaunches the browser, which is upstream's own behaviour and costs ~0.4 s.")]
    public static partial void BrowserClosed(ILogger logger, string session, TimeSpan period, int before, int after);

    /// <summary>The child answered the close with an error.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="reason">What the child said.</param>
    [LoggerMessage(
        EventId = 61,
        Level = LogLevel.Warning,
        Message = "The browser on the session at {Session} was not closed after going idle: {Reason}. The session is unaffected and the browser stays open until something else takes it down.")]
    public static partial void CloseRefused(ILogger logger, string session, string reason);

    /// <summary>The close could not be sent at all.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 62,
        Level = LogLevel.Warning,
        Message = "The idle close on the session at {Session} failed. The session is unaffected; its browser stays open until the job object takes it down.")]
    public static partial void CloseFailed(ILogger logger, string session, Exception failure);
}
