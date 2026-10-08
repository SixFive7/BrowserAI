// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;
using BrowserAI.Interop;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// A person's keyboard or mouse input in a visible session's browser window counts as
/// activity for that session, through one timer for the whole process and four
/// Windows reads a check (F4 a, 2026-10-08).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every arm but the last four drives a fake desktop and a fake timer</b>, so a
/// check runs when the arm fires it and reads exactly what the arm set, and every
/// read is counted. The last four hold the shipped numbers, the desktop's real reads,
/// the real timer and the product's own watch. Nothing here touches the keyboard, the
/// mouse or the window in front: the suite may not, and a check that could only be
/// tested by typing would not be tested at all.
/// </para>
/// <para>
/// <b>The tick counts are in milliseconds, the unit of <c>GetTickCount</c>, and the
/// arms step them by the shipped interval</b>, so the arithmetic reads as checks
/// two seconds apart. Nothing waits for any of it.
/// </para>
/// <para>
/// <b>Each arm was planted red before the code it holds went in</b>, and its remarks
/// say against what: the arms that expect a session to be told were red against a
/// skeleton whose check did nothing, and the arms that expect nobody to be told were
/// red against a check that told too eagerly in the one way that arm is about.
/// </para>
/// </remarks>
internal sealed class VisibleInputWatchTests
{
    /// <summary>The pid that owns the browser window of the session under test.</summary>
    private const int Browser = 4242;

    /// <summary>The pid that owns some other application's window.</summary>
    private const int Stranger = 9000;

    /// <summary>The tick count at which the first session registers: the baseline.</summary>
    private const uint Baseline = 1_000_000;

    /// <summary>A window handle the fake desktop hands out.</summary>
    private static readonly nint BrowserWindow = 0x10010;

    /// <summary>One shipped interval, in the tick count's milliseconds.</summary>
    private static readonly uint Step = (uint)SessionTimes.VisibleInputCheckInterval.TotalMilliseconds;

    /// <summary>
    /// Input made while the session's window is in front tells that session, once.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against the skeleton, whose <see cref="VisibleInputWatch.Tick"/>
    /// did nothing: the session was told 0 times where 1 was expected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InputWhileTheSessionsWindowIsInFrontTellsThatSessionOnce()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var session = new Session(Browser);

        using var registration = watch.Watch(session);

        desktop.Show(BrowserWindow, Browser);
        desktop.LastInputTick = Baseline + (Step / 2);
        desktop.Now = Baseline + Step;
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(1);

        // The same input, still the newest, is not news at the next check.
        desktop.Now += Step;
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(1);
    }

    /// <summary>
    /// Input made while another application's window is in front tells nobody.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against a check that told every session about any new input,
    /// whatever window was in front: the session was told once where 0 was expected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InputWhileAnotherWindowIsInFrontTellsNobody()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var session = new Session(Browser);

        using var registration = watch.Watch(session);

        desktop.Show(BrowserWindow, Stranger);
        desktop.LastInputTick = Baseline + (Step / 2);
        desktop.Now = Baseline + Step;
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(0);
    }

    /// <summary>
    /// A NULL window in front attributes nothing, and the check still reads both tick
    /// counts, so input made then is not attributed later to a window the person only
    /// switched to.
    /// </summary>
    /// <remarks>
    /// <b>Planted red twice.</b> Against a check that told every session about any new
    /// input whatever was in front, the first check told the session: once where 0 was
    /// expected. Against a check that returned before the tick reads whenever the window
    /// was unknown, the first check made 0 last-input reads where 1 was expected, which
    /// leaves the baseline at the registration's for the second check to read input
    /// from.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ANullWindowInFrontAttributesNothingAndStillMovesTheBaseline()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var session = new Session(Browser);

        using var registration = watch.Watch(session);

        desktop.Show(nint.Zero, Browser);
        desktop.LastInputTick = Baseline + (Step / 2);
        desktop.Now = Baseline + Step;
        desktop.ForgetReads();
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(0);
        await Assert.That(desktop.LastInputReads).IsEqualTo(1);
        await Assert.That(desktop.TickReads).IsEqualTo(1);

        // The person switches to the browser window and types nothing.
        desktop.Show(BrowserWindow, Browser);
        desktop.Now += Step;
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(0);
    }

    /// <summary>
    /// A window whose thread reads as zero attributes nothing, even when the pid beside
    /// it is the session's, and the check still reads both tick counts.
    /// </summary>
    /// <remarks>
    /// <b>Planted red twice</b>, against the same two checks as the NULL-window arm:
    /// one told the session at the first check, once where 0 was expected, and the
    /// other made 0 last-input reads there where 1 was expected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AZeroThreadAttributesNothingAndStillMovesTheBaseline()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var session = new Session(Browser);

        using var registration = watch.Watch(session);

        desktop.Show(BrowserWindow, Browser);
        desktop.Thread = 0;
        desktop.LastInputTick = Baseline + (Step / 2);
        desktop.Now = Baseline + Step;
        desktop.ForgetReads();
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(0);
        await Assert.That(desktop.LastInputReads).IsEqualTo(1);
        await Assert.That(desktop.TickReads).IsEqualTo(1);

        desktop.Thread = 7;
        desktop.Now += Step;
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(0);
    }

    /// <summary>
    /// The session's window in front with no input since the previous check tells
    /// nobody: being in front is not activity.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against a check that counted the session's window being in
    /// front as activity, input or not: the session was told once where 0 was
    /// expected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoNewInputTellsNobody()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var session = new Session(Browser);

        using var registration = watch.Watch(session);

        desktop.Show(BrowserWindow, Browser);
        desktop.LastInputTick = Baseline - (Step / 2);
        desktop.Now = Baseline + Step;
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(0);
    }

    /// <summary>
    /// Input made across <c>GetTickCount</c>'s 49.7-day wrap is new input, and the
    /// same input is not news at the next check.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against a check that compared the last-input tick with the
    /// baseline directly: the input carries tick 500 after the wrap and the baseline
    /// 4,294,966,296 before it, so it read as older and the session was told 0 times
    /// where 1 was expected. The skeleton was red here too.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheComparisonHoldsAcrossTheTickCountWrappingToZero()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var session = new Session(Browser);
        var beforeTheWrap = unchecked(0u - (Step / 2));

        desktop.Now = beforeTheWrap;

        using var registration = watch.Watch(session);

        desktop.Show(BrowserWindow, Browser);
        desktop.LastInputTick = unchecked(beforeTheWrap + (Step * 3 / 4));
        desktop.Now = unchecked(beforeTheWrap + Step);
        timers.Last.Fire();

        // Both readings are past the wrap and the baseline is not.
        await Assert.That(desktop.LastInputTick).IsLessThan(beforeTheWrap);
        await Assert.That(desktop.Now).IsLessThan(beforeTheWrap);
        await Assert.That(session.Told).IsEqualTo(1);

        desktop.Now += Step;
        timers.Last.Fire();

        await Assert.That(session.Told).IsEqualTo(1);
    }

    /// <summary>
    /// A session whose browser is not known yet is never told, whatever window is in
    /// front.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against a check that told every session about any new input,
    /// whatever window was in front, which tells a session with no known browser too:
    /// told once where 0 was expected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionWithNoKnownBrowserIsNeverTold()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var unknown = new Session(null);

        using var registration = watch.Watch(unknown);

        desktop.Show(BrowserWindow, Stranger);
        desktop.LastInputTick = Baseline + (Step / 2);
        desktop.Now = Baseline + Step;
        timers.Last.Fire();

        await Assert.That(unknown.Told).IsEqualTo(0);
    }

    /// <summary>
    /// The timer starts at the first registration and stops at the last disposal, and
    /// no timer runs and nothing is read while nothing is registered.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against the skeleton, which registered sessions and never
    /// started a timer: 0 timers started where 1 was expected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OneTimerRunsOnlyWhileASessionIsRegistered()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);

        await Assert.That(timers.Started).IsEqualTo(0);
        await Assert.That(watch.IsRunning).IsFalse();

        var first = watch.Watch(new Session(Browser));
        var second = watch.Watch(new Session(Browser + 4));

        await Assert.That(timers.Started).IsEqualTo(1);
        await Assert.That(watch.IsRunning).IsTrue();

        first.Dispose();

        await Assert.That(timers.Last.Stopped).IsFalse();
        await Assert.That(watch.IsRunning).IsTrue();

        second.Dispose();
        second.Dispose();

        await Assert.That(timers.Last.Stopped).IsTrue();
        await Assert.That(watch.IsRunning).IsFalse();

        // Nothing registered: a check reads nothing at all.
        desktop.ForgetReads();
        watch.Tick();

        await Assert.That(desktop.ForegroundReads + desktop.ThreadReads + desktop.LastInputReads + desktop.TickReads).IsEqualTo(0);

        // And the next session starts a new one.
        using (watch.Watch(new Session(Browser)))
        {
            await Assert.That(timers.Started).IsEqualTo(2);
            await Assert.That(watch.IsRunning).IsTrue();
        }

        await Assert.That(timers.Last.Stopped).IsTrue();
    }

    /// <summary>
    /// <b>One pace.</b> With a hundred sessions registered, one check makes one read of
    /// each of the four, exactly as with one.
    /// </summary>
    /// <remarks>
    /// The maintainer's condition of 2026-10-08, verbatim: <i>"No 100x fold checks just
    /// because there are 100 windows open."</i> <b>Planted red</b> against a check that
    /// read the foreground window and its owner once per session: 100 foreground reads
    /// where 1 was expected. The skeleton, which read nothing, was red here too.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OneCheckMakesTheSameFourReadsForAHundredSessionsAsForOne()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var sessions = Enumerable.Range(0, 100).Select(index => new Session(Browser + (index * 4))).ToList();
        var registrations = sessions.Select(watch.Watch).ToList();

        desktop.Show(BrowserWindow, Browser);
        desktop.LastInputTick = Baseline + (Step / 2);
        desktop.Now = Baseline + Step;
        desktop.ForgetReads();
        timers.Last.Fire();

        await Assert.That(desktop.ForegroundReads).IsEqualTo(1);
        await Assert.That(desktop.ThreadReads).IsEqualTo(1);
        await Assert.That(desktop.LastInputReads).IsEqualTo(1);
        await Assert.That(desktop.TickReads).IsEqualTo(1);

        // And only the session whose browser is in front was told.
        await Assert.That(sessions[0].Told).IsEqualTo(1);
        await Assert.That(sessions.Skip(1).Sum(session => session.Told)).IsEqualTo(0);

        // Down to one session, the same four.
        foreach (var registration in registrations.Skip(1))
        {
            registration.Dispose();
        }

        desktop.ForgetReads();
        desktop.Now += Step;
        timers.Last.Fire();

        await Assert.That((desktop.ForegroundReads, desktop.ThreadReads, desktop.LastInputReads, desktop.TickReads))
            .IsEqualTo((1, 1, 1, 1));

        registrations[0].Dispose();
    }

    /// <summary>
    /// A session that throws when it is told does not stop the others being told, and
    /// does not stop the next check.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against a check with no catch around a session's callback: the
    /// throw ended the loop, so the second session was told 0 times where 1 was
    /// expected. Two sessions never share a browser in the product; the second stands
    /// for the rest of the loop.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionThatThrowsStopsNeitherTheOthersNorTheNextCheck()
    {
        using var log = new CapturingLoggerProvider();
        var (watch, desktop, timers) = Rig(log);
        var failing = new FailingSession(Browser);
        var other = new Session(Browser);

        using var first = watch.Watch(failing);
        using var second = watch.Watch(other);

        desktop.Show(BrowserWindow, Browser);
        desktop.LastInputTick = Baseline + (Step / 2);
        desktop.Now = Baseline + Step;
        timers.Last.Fire();

        await Assert.That(failing.Told).IsEqualTo(1);
        await Assert.That(other.Told).IsEqualTo(1);
        await Assert.That(log.Records.Count(record => record.EventId.Id is 103 && record.Exception is InvalidOperationException)).IsEqualTo(1);

        desktop.LastInputTick = desktop.Now + (Step / 2);
        desktop.Now += Step;
        timers.Last.Fire();

        await Assert.That(failing.Told).IsEqualTo(2);
        await Assert.That(other.Told).IsEqualTo(2);
    }

    /// <summary>
    /// A timer Windows will not start refuses no session: the registration stands, the
    /// failure is logged, a check called directly still works, and the next
    /// registration tries the timer again.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against a watch with no catch around the timer's start: the
    /// <see cref="Win32Exception"/> reached the session's registration.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATimerThatCannotStartRefusesNoSession()
    {
        using var log = new CapturingLoggerProvider();
        var desktop = new FakeDesktop { Now = Baseline };
        var attempts = 0;
        using var watch = new VisibleInputWatch(
            desktop,
            _ =>
            {
                attempts++;
                throw new Win32Exception(1450);
            },
            log.CreateLogger(nameof(VisibleInputWatch)));
        var session = new Session(Browser);

        using var registration = watch.Watch(session);

        await Assert.That(watch.IsRunning).IsFalse();
        await Assert.That(log.Records.Count(record => record.EventId.Id is 102 && record.Level is LogLevel.Warning)).IsEqualTo(1);

        desktop.Show(BrowserWindow, Browser);
        desktop.LastInputTick = Baseline + (Step / 2);
        desktop.Now = Baseline + Step;
        watch.Tick();

        await Assert.That(session.Told).IsEqualTo(1);

        using var another = watch.Watch(new Session(Browser + 4));

        await Assert.That(attempts).IsEqualTo(2);
    }

    /// <summary>
    /// The shipped check runs every two seconds, and Windows may run it up to a second
    /// late.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against the skeleton, which declared both as zero. Both are
    /// chosen numbers, and <see cref="SessionTimes"/> says why each is what it is.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheShippedCheckIsEveryTwoSecondsWithASecondOfRoom()
    {
        await Assert.That(SessionTimes.VisibleInputCheckInterval).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(SessionTimes.VisibleInputCheckTolerance).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// The product's last-input read is answered by Windows, which it is only when the
    /// structure's size is set before the call.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against a read that left <c>cbSize</c> at zero: Windows
    /// answered <see langword="false"/>. The other three reads are made too, so a
    /// declaration that does not bind fails here; what they answer depends on the
    /// desktop the suite runs on and is not asserted.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDesktopsLastInputReadIsAnswered()
    {
        var desktop = new VisibleInputWatch.DesktopInput();

        var window = desktop.ForegroundWindow();
        _ = desktop.WindowThread(window, out _);
        _ = desktop.TickCount();

        await Assert.That(desktop.LastInput(out _)).IsTrue();
    }

    /// <summary>
    /// The shipped timer fires once an interval, never sooner than an interval less its
    /// tolerance after the one before, and not at all once it is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A real timer for a little over two intervals.</b> The wait for each firing is
    /// a hang detector, <see cref="TestDefaults.InProcessHang"/>; the spacing is a lower
    /// bound, which a loaded machine can only make easier to meet; and the quiet after
    /// disposal is one interval and its tolerance, the longest the timer's own terms
    /// allow between two firings.
    /// </para>
    /// <para>
    /// <b>Planted red</b> against a manual-reset timer, created with
    /// <c>CREATE_WAITABLE_TIMER_MANUAL_RESET</c>: once its first due time had passed it
    /// stayed signalled, the pool's wait fired without pause, and the second firing came
    /// 0.39 ms after the first where at least one second was expected.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheShippedTimerFiresOnceAnIntervalAndStopsWhenDisposed()
    {
        using var fired = new SemaphoreSlim(0);
        var stamps = new List<long>();
        var count = 0;

        var timer = CoalescableTimer.Start(
            SessionTimes.VisibleInputCheckInterval,
            SessionTimes.VisibleInputCheckTolerance,
            () =>
            {
                lock (stamps)
                {
                    stamps.Add(Stopwatch.GetTimestamp());
                    count++;
                }

                _ = fired.Release();
            });

        try
        {
            await Assert.That(await fired.WaitAsync(TestDefaults.InProcessHang)).IsTrue();
            await Assert.That(await fired.WaitAsync(TestDefaults.InProcessHang)).IsTrue();
        }
        finally
        {
            timer.Dispose();
        }

        long first;
        long second;
        int atDisposal;

        lock (stamps)
        {
            first = stamps[0];
            second = stamps[1];
            atDisposal = count;
        }

        await Assert.That(Stopwatch.GetElapsedTime(first, second))
            .IsGreaterThanOrEqualTo(SessionTimes.VisibleInputCheckInterval - SessionTimes.VisibleInputCheckTolerance);

        await Task.Delay(SessionTimes.VisibleInputCheckInterval + SessionTimes.VisibleInputCheckTolerance);

        lock (stamps)
        {
            atDisposal = count - atDisposal;
        }

        await Assert.That(atDisposal).IsEqualTo(0);
    }

    /// <summary>
    /// The product's watch runs a real timer while a session is registered, and none
    /// once the last registration is gone.
    /// </summary>
    /// <remarks>
    /// <b>Planted red</b> against the skeleton, whose registration never started the
    /// timer: not running where running was expected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheProductsWatchRunsATimerOnlyWhileASessionIsRegistered()
    {
        using var log = new CapturingLoggerProvider();
        using var watch = VisibleInputWatch.ForThisDesktop(log.CreateLogger(nameof(VisibleInputWatch)));

        await Assert.That(watch.IsRunning).IsFalse();

        using (watch.Watch(new Session(Browser)))
        {
            await Assert.That(watch.IsRunning).IsTrue();
        }

        await Assert.That(watch.IsRunning).IsFalse();
        await Assert.That(log.Records.Count(record => record.EventId.Id is 100)).IsEqualTo(1);
        await Assert.That(log.Records.Count(record => record.EventId.Id is 101)).IsEqualTo(1);
    }

    /// <summary>A watch over a fake desktop and fake timers, with the clock at <see cref="Baseline"/>.</summary>
    /// <param name="log">Where the watch reports.</param>
    /// <returns>The watch, its desktop and its timers.</returns>
    private static (VisibleInputWatch Watch, FakeDesktop Desktop, FakeTimers Timers) Rig(CapturingLoggerProvider log)
    {
        var desktop = new FakeDesktop { Now = Baseline };
        var timers = new FakeTimers();

        return (new VisibleInputWatch(desktop, timers.Start, log.CreateLogger(nameof(VisibleInputWatch))), desktop, timers);
    }

    /// <summary>
    /// The desktop as an arm sets it: the window in front, its thread and owner, and the
    /// two tick counts. Every read is counted.
    /// </summary>
    private sealed class FakeDesktop : IInputProbe
    {
        public nint Foreground { get; private set; }

        public uint Thread { get; set; } = 7;

        public int Owner { get; private set; }

        public uint LastInputTick { get; set; }

        public uint Now { get; set; }

        public int ForegroundReads { get; private set; }

        public int ThreadReads { get; private set; }

        public int LastInputReads { get; private set; }

        public int TickReads { get; private set; }

        public void Show(nint window, int owner)
        {
            Foreground = window;
            Owner = owner;
        }

        public void ForgetReads()
        {
            ForegroundReads = 0;
            ThreadReads = 0;
            LastInputReads = 0;
            TickReads = 0;
        }

        public nint ForegroundWindow()
        {
            ForegroundReads++;
            return Foreground;
        }

        // The pid comes back whatever the thread says, so an arm can hand a zero
        // thread a pid that would otherwise match.
        public uint WindowThread(nint window, out int processId)
        {
            ThreadReads++;
            processId = Owner;
            return Thread;
        }

        public bool LastInput(out uint tick)
        {
            LastInputReads++;
            tick = LastInputTick;
            return true;
        }

        public uint TickCount()
        {
            TickReads++;
            return Now;
        }
    }

    /// <summary>Every timer the watch started, newest last.</summary>
    private sealed class FakeTimers
    {
        private readonly List<FakeTimer> _started = [];

        public int Started => _started.Count;

        public FakeTimer Last => _started[^1];

        public FakeTimer Start(Action tick)
        {
            var timer = new FakeTimer(tick);

            _started.Add(timer);
            return timer;
        }
    }

    /// <summary>A timer that fires when the arm says so.</summary>
    private sealed class FakeTimer(Action tick) : IDisposable
    {
        public bool Stopped { get; private set; }

        public void Fire() => tick();

        public void Dispose() => Stopped = true;
    }

    /// <summary>A visible session that counts how often it was told.</summary>
    private sealed class Session(int? browser) : IVisibleWindowOwner
    {
        public int? WindowProcessId { get; } = browser;

        public int Told { get; private set; }

        public void PersonWasActive() => Told++;
    }

    /// <summary>A visible session that throws every time it is told.</summary>
    private sealed class FailingSession(int? browser) : IVisibleWindowOwner
    {
        public int? WindowProcessId { get; } = browser;

        public int Told { get; private set; }

        public void PersonWasActive()
        {
            Told++;
            throw new InvalidOperationException("A planted failure in a session's own answer to input.");
        }
    }
}
