// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Protocol;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// The server's one timer: a headless session's browser server is ended once it
/// has gone unused for <see cref="DefaultIdlePeriod"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-10-03 (previously "The <b>only</b> timer in
/// BrowserAI").</b> The configuration app's coordinator has one too since that
/// day: the minute it waits after the last browser tab closes before it stops
/// (Q336 a). This is still the server's only one.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-10-03 (previously "a session's browser is closed once it
/// has gone unused for <see cref="DefaultIdlePeriod"/>, and the node child is
/// kept").</b> Under the maintainer's P4 b the close ends the whole child, under
/// his P2 a every call after it is refused until <c>browserai_resume</c>, and
/// under his Q326 a a headed session has no timer at all. What the close does is
/// <c>LiveSession</c>'s; this type decides only <i>when</i>.
/// </para>
/// <para>
/// <b>There is exactly one timer, and this is it.</b> No handle-expiry timer, no
/// session TTL and no reclaim window -- <b>reclaim is forever</b>, because the
/// durable thing is the profile and not the process: a resume after killing
/// the node child preserves cookies, localStorage, IndexedDB, service workers and
/// CacheStorage, losing only <c>sessionStorage</c>, in ~515 ms
/// ([kb](../../../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)).
/// Every expiry timer that was considered was a cliff that deleted work in
/// exchange for nothing: an agent thinking for 61 minutes came back to a dead
/// handle, and the recovery was a <c>resume</c> it could have done anyway. The
/// cost is honest -- directories accumulate forever -- and it is why explicit
/// <c>browserai_list</c> and <c>browserai_destroy</c> matter here more, not less.
/// </para>
/// <para>
/// ⚠️ <b>The relaunch is no longer implicit, and that is the field report's
/// finding acted on.</b> <i>Corrected 2026-10-03 (previously "The relaunch is
/// implicit, and that is what makes the timer safe to have at all ... the next
/// tool call brings the browser back in 416 ms then 409 ms with no error and no
/// browser is closed anywhere").</i> The measurement of 2026-08-16 stands -- a
/// <c>browser_close</c> took 8 then 7 processes and 378.3 then 369.4 MB to zero,
/// and the next call relaunched in 416 then 409 ms -- and it was taken with a
/// navigation as the next call, which works from any page. A field report of
/// 2026-10-01 met the case it did not cover: a call that depended on the page it
/// left ran on <c>about:blank</c>, and nothing said why. So a closed session now
/// refuses every call with a sentence naming <c>browserai_resume</c>, and the
/// browser's own session restore brings the tabs back at the resume.
/// </para>
/// <para>
/// <b>It starts disarmed.</b> A session that has been opened and never driven has
/// no browser -- Playwright has not launched one -- so arming at <c>init</c> would
/// buy one pointless round trip per session and a log line saying nothing was
/// closed. The first forwarded tool call arms it.
/// </para>
/// <para>
/// <b>A call in flight is a session being driven, however long the call takes.</b>
/// <see cref="Call"/> both resets the period and marks the call outstanding, so a
/// navigation that outlives the whole period cannot have the browser closed
/// underneath it. The residual window -- a call that arrives in the microseconds
/// between the decision to close and the close being sent -- is <i>narrowed</i> by
/// a second check and not eliminated. <i>Corrected 2026-10-03 (previously "and it
/// is harmless for the reason above: the caller's next call relaunches the
/// browser and answers normally").</i> That call is now answered with the
/// transport's own failure, and the one after it is refused with the sentence
/// naming <c>browserai_resume</c>.
/// </para>
/// <para>
/// <b>The clock is a constructor parameter, and that is the only reason this
/// class can be tested at all.</b> Every property above is a statement about
/// <i>when</i> something happens, and a test that establishes one by letting real
/// time pass is not testing the timer -- it is testing the machine's scheduler.
/// Measured 2026-08-17 with the suite running every test at once: a single
/// in-process round trip through the rig took <b>1.51 s and 2.27 s</b> against an
/// 800 ms period, so the test that drove this timer <i>correctly concluded</i>
/// that the session had gone idle and failed, five times in twenty runs. The
/// product was right every time. Four retries and a raised budget had already
/// been spent on it, which is two of the three things this repository forbids.
/// </para>
/// <para>
/// With <see cref="TimeProvider"/> handed in, the suite advances the clock
/// itself: <i>one tick short of the period, no close; a call; one tick short
/// again, still no close; one tick past, exactly one close.</i> None of that can
/// be falsified by a loaded machine, because none of it reads a wall clock. The
/// product passes <see cref="TimeProvider.System"/> and behaves exactly as it
/// did -- <c>SessionEnvironment.Clock</c> carries the same guard
/// <c>BrowserIdlePeriod</c> carries, so a test clock cannot leak into a shipped
/// build without a test going red.
/// </para>
/// </remarks>
internal sealed class BrowserIdleTimer : IAsyncDisposable
{
    /// <summary>
    /// The shipped period: <b>ten minutes</b>, reset by any tool call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It ends the browser server, node child included. Re-measured 2026-08-16,
    /// the browser alone was ~496 MB → ~118 MB
    /// ([kb](../../../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)),
    /// and on 2026-10-03 the node child was another ~124 MB of working set
    /// (176 MB private) that the close now frees as well. The period is long
    /// enough that ordinary think-time between calls never closes a browser.
    /// <i>Corrected 2026-10-03 (previously "and the cost of being wrong is under
    /// half a second on a relaunch the caller cannot see").</i> The cost of being
    /// wrong is now a refused call and a <c>browserai_resume</c>, which starts a
    /// new child in 0.3 to 0.5 s more than the relaunch used to take.
    /// </para>
    /// <para>
    /// The suite drives the timer in milliseconds through
    /// <see cref="SessionEnvironment.BrowserIdlePeriod"/>, which is why this
    /// constant is asserted on directly: a test-friendly value that leaked into
    /// the product would show up nowhere else, because a browser closing too
    /// eagerly is invisible -- the next call silently relaunches it.
    /// </para>
    /// </remarks>
    public static TimeSpan DefaultIdlePeriod => SessionTimes.BrowserIdlePeriod;

    /// <summary>
    /// How long a teardown waits for an idle close in flight before it goes on without
    /// it: the close's own cap and then the child's end through its stdin,
    /// <see cref="SessionTimes.BrowserCloseCap"/> plus
    /// <see cref="ChildProcessOptions.DefaultShutdownTimeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Derived since 2026-10-04</b> (previously twenty seconds, chosen and
    /// pinned by nothing, and shorter than the thirty-second cap because a teardown
    /// cancelled the close's wait first). Nothing but a destroy cuts an idle close
    /// short any more, so a teardown sits the close out: its wait for the browser's
    /// answer, at most the cap, and then the child ended through its stdin, at most
    /// the child's own shutdown timeout before its job is closed.
    /// </para>
    /// <para>
    /// <b>A hang detector and nothing else.</b> The close bounds itself on the
    /// session's clock; this bound, on the real clock, is what stops a close that went
    /// wrong some other way from holding a teardown for ever, and past it the job
    /// object still takes the browser.
    /// </para>
    /// </remarks>
    internal static TimeSpan CloseBudget { get; } = SessionTimes.BrowserCloseCap + ChildProcessOptions.DefaultShutdownTimeout;

    private readonly Lock _gate = new();
    private readonly string _session;
    private readonly Func<Task<BrowserCloseResult?>> _closeBrowser;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;

    private int _inFlight;
    private bool _closing;
    private Task? _close;
    private int _closes;
    private int _disposed;

    /// <summary>
    /// When this session becomes idle, as a <see cref="TimeProvider.GetTimestamp"/>
    /// reading. Guarded by <see cref="_gate"/>.
    /// </summary>
    private long Deadline { get; set; }

    /// <summary>Creates a session's timer, disarmed.</summary>
    /// <param name="session">The session directory, for the log.</param>
    /// <param name="period">How long idle is. The shipped value is <see cref="DefaultIdlePeriod"/>.</param>
    /// <param name="closeBrowser">
    /// Ends the browser server and reports what went away, or answers
    /// <see langword="null"/> when there was no browser to close.
    /// </param>
    /// <param name="logger">This session's own logger.</param>
    /// <param name="time">
    /// The clock this timer reads and schedules against.
    /// <see cref="TimeProvider.System"/> in the product; a manual one in the
    /// suite. See the remarks on the type.
    /// </param>
    public BrowserIdleTimer(
        string session,
        TimeSpan period,
        Func<Task<BrowserCloseResult?>> closeBrowser,
        ILogger logger,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(closeBrowser);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);

        _session = session;
        Period = period;
        _closeBrowser = closeBrowser;
        _logger = logger;
        _time = time;

        _timer = time.CreateTimer(static state => ((BrowserIdleTimer)state!).OnIdle(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
    /// When the idle close fires if no call comes first, or <see langword="null"/>
    /// while a call runs, while a close runs, and when no deadline lies ahead.
    /// </summary>
    /// <remarks>
    /// <b>Read for a description, so a person can see when a session nobody drives
    /// will end</b> (Q366 b). The deadline is the one the timer itself fires on,
    /// read under the same lock, and turned into a time on the timer's own clock.
    /// </remarks>
    public DateTimeOffset? ClosesAt
    {
        get
        {
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) is not 0 || _closing || _inFlight > 0)
                {
                    return null;
                }

                var remaining = _time.GetElapsedTime(_time.GetTimestamp(), Deadline);

                return remaining > TimeSpan.Zero ? _time.GetUtcNow() + remaining : null;
            }
        }
    }

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

        // ⚠️ THE TIMER STOPS AND THE CLOSE IN FLIGHT IS LEFT TO FINISH, since
        // 2026-10-04 (previously this cancelled the close's wait before it waited,
        // so a resume, a release or a shutdown cut an idle close short). Nothing
        // but a destroy may do that, and a destroy says so to the session itself.
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
#pragma warning disable CA1031 // A close that failed on the way down is already logged, and the teardown goes on either way.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
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
        // what is LEFT must reproduce the existing deadline instead of pushing it
        // out by another whole period.
        Deadline = _time.GetTimestamp() + (long)(delay.TotalSeconds * _time.TimestampFrequency);

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
                // Driven, not idle. Re-armed, not closed.
                Arm();
                return;
            }

            // A callback from an EARLIER arming that Change could not recall,
            // arriving after a call has already moved the deadline on. Timer
            // rearms and dispatched callbacks race by construction, and the
            // window is a few milliseconds wide -- narrow enough that this
            // suite could not provoke it on demand, which is exactly why it is
            // guarded and not tested: the symptom would be a ten-minute
            // timer behaving like a two-second one, and nothing would show it,
            // because the caller's next call silently relaunches the browser.
            var remaining = _time.GetElapsedTime(_time.GetTimestamp(), Deadline);

            if (remaining > TimeSpan.Zero)
            {
                ArmFor(remaining);
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
                // abandoned and not raced.
                if (_inFlight > 0)
                {
                    Arm();
                    return;
                }

                // And a teardown that has begun ends the child itself.
                if (Volatile.Read(ref _disposed) is not 0)
                {
                    return;
                }
            }

            // Null is Q327 a: only the node child was left, so nothing was
            // closed, no row was written and nothing is counted.
            if (await _closeBrowser().ConfigureAwait(false) is not { } result)
            {
                IdleLog.NothingToClose(_logger, _session, Period);
                return;
            }

            _ = Interlocked.Increment(ref _closes);
            IdleLog.BrowserClosed(_logger, _session, Period, result.ProcessesBefore, result.ProcessesAfter);
        }
#pragma warning disable CA1031 // A close that fails is a log line; the teardown that follows it ends the job either way.
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

/// <summary>What one idle close did, as evidence and not as an assumption.</summary>
/// <remarks>
/// <b>The two counts are the whole point.</b> "The browser was closed" is a claim
/// no log line can support on its own; <c>11 → 0</c> processes left in the
/// child's job says the browser tree went and the node child with it, which is
/// what the close promises since 2026-10-03. <i>Corrected 2026-10-03 (previously
/// "<c>11 → 1</c> ... the browser tree went and the node child stayed"), with
/// the close.</i>
/// </remarks>
/// <param name="ProcessesBefore">Processes in the child's job before the close.</param>
/// <param name="ProcessesAfter">Processes left in it afterwards, which should be none.</param>
internal readonly record struct BrowserCloseResult(int ProcessesBefore, int ProcessesAfter);

/// <summary>Source-generated log messages for the browser-idle timer.</summary>
/// <remarks>Event ids start at 60, after <see cref="SessionToolLog"/>'s 40s.</remarks>
internal static partial class IdleLog
{
    /// <summary>A session's browser server was ended because nothing had driven it.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="period">How long it had been idle.</param>
    /// <param name="before">Processes in the child's job before.</param>
    /// <param name="after">Processes in it afterwards.</param>
    [LoggerMessage(
        EventId = 60,
        Level = LogLevel.Information,
        Message = "Browser server ended after {Period} idle on the session at {Session}, node child included. Processes in that child's job: {Before} → {After}. Browser calls are refused until browserai_resume starts a new one.")]
    public static partial void BrowserClosed(ILogger logger, string session, TimeSpan period, int before, int after);

    /// <summary>The close could not be sent at all.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 62,
        Level = LogLevel.Warning,
        Message = "The idle close on the session at {Session} failed. Its child is ended by the session's own teardown and its job object either way.")]
    public static partial void CloseFailed(ILogger logger, string session, Exception failure);

    /// <summary>The timer fired and found only the node child: nothing to close.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="period">How long it had been idle.</param>
    [LoggerMessage(
        EventId = 63,
        Level = LogLevel.Debug,
        Message = "The session at {Session} was idle for {Period} with no browser up, so nothing was closed and no row was written.")]
    public static partial void NothingToClose(ILogger logger, string session, TimeSpan period);

    /// <summary>A shutdown's own <c>browser_close</c> did not answer within its bound.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="budget">How long it was given.</param>
    [LoggerMessage(
        EventId = 64,
        Level = LogLevel.Warning,
        Message = "The browser on the session at {Session} did not answer its close within {Budget} at shutdown; its child is ended through its stdin and its job anyway.")]
    public static partial void ShutdownCloseUnanswered(ILogger logger, string session, TimeSpan budget);

    /// <summary>The idle close's own <c>browser_close</c> did not answer within its cap.</summary>
    /// <remarks>
    /// <b>The armed-pause case, Q367 a.</b> Warning, because the browser was ended
    /// without the flush the close was asked for, and that is what a reader of the
    /// log looking for a lost cookie needs to find.
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="budget">How long it was given.</param>
    [LoggerMessage(
        EventId = 65,
        Level = LogLevel.Warning,
        Message = "The browser on the session at {Session} did not answer its idle close within {Budget}; its child is ended through its stdin and its job anyway.")]
    public static partial void IdleCloseUnanswered(ILogger logger, string session, TimeSpan budget);

    /// <summary>A resume met a close in flight and waits for it before it opens the session again.</summary>
    /// <remarks>
    /// <b>The ordering rule of 2026-10-04.</b> Information, because the resume's
    /// caller waits for as long as the close takes, up to the cap, and a reader of the
    /// log looking at a slow resume needs to find why.
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="cap">The longest it can wait.</param>
    [LoggerMessage(
        EventId = 66,
        Level = LogLevel.Information,
        Message = "A resume of the session at {Session} waits up to {Cap} for its browser to finish the close in flight, and opens the session again once it has.")]
    public static partial void ReopenWaitsForTheClose(ILogger logger, string session, TimeSpan cap);

    /// <summary>A teardown met a close in flight and waits for it before it ends the child.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="cap">The longest it can wait.</param>
    [LoggerMessage(
        EventId = 67,
        Level = LogLevel.Information,
        Message = "The session at {Session} is being torn down and waits up to {Cap} for its browser to finish the close in flight before its child is ended.")]
    public static partial void TeardownWaitsForTheClose(ILogger logger, string session, TimeSpan cap);

    /// <summary>The caller of <c>browserai_close</c> stopped waiting, and the close goes on without it.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-08 (previously "The caller of browser_close on the session at
    /// {Session} stopped waiting..."): the agent's close is <c>browserai_close</c> since
    /// F1 a.</i>
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="cap">The longest the close is given.</param>
    [LoggerMessage(
        EventId = 68,
        Level = LogLevel.Information,
        Message = "The caller of browserai_close on the session at {Session} stopped waiting for its answer; the close goes on, up to {Cap}, and the child is ended once it is over.")]
    public static partial void CallerLeftItsClose(ILogger logger, string session, TimeSpan cap);

    /// <summary>The agent's own close did not answer within the cap.</summary>
    /// <remarks>
    /// <para>
    /// <b>Warning, for the reason the idle close's own is one</b>: whatever was waiting
    /// for the close goes ahead and ends the child without the flush the close was
    /// for.
    /// </para>
    /// <para>
    /// <i>Corrected 2026-10-08 (previously "did not answer the caller's browser_close
    /// within {Cap}"): the agent's close is <c>browserai_close</c> since F1 a, and the
    /// close it sends the browser is BrowserAI's own.</i>
    /// </para>
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="cap">How long it was given.</param>
    [LoggerMessage(
        EventId = 69,
        Level = LogLevel.Warning,
        Message = "The browser on the session at {Session} did not answer the agent's close within {Cap}; whatever waits for that close goes ahead, and the child is ended through its stdin.")]
    public static partial void CallersCloseUnanswered(ILogger logger, string session, TimeSpan cap);

    /// <summary>A destroy cut a close in flight short.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    [LoggerMessage(
        EventId = 70,
        Level = LogLevel.Information,
        Message = "browserai_destroy cut the close in flight on the session at {Session} short: the session's data is deleted, so nothing the close would have saved is kept.")]
    public static partial void CloseCutShortByDestroy(ILogger logger, string session);

    // Id 61 was `CloseRefused`, the child answering the idle close's
    // `browser_close` with an error. The idle close sends no `browser_close`
    // since 2026-10-03, so nothing can produce it, and the id is not reused for
    // anything else.
    //
    // ⚠️ Corrected the same day, Q367 a (previously "The idle close sends no
    // `browser_close` since 2026-10-03, so nothing can produce it"): it sends one
    // again, and an error answer is still not reported, because the child is
    // ended next whatever the answer says. 61 stays retired; the cap running out
    // is 65.
    //
    // RETIRED-EVENT-IDS: 61
}
