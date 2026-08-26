// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The three lock scopes under real concurrency — the decision
/// [DECISIONS → Still open](../../DECISIONS.md#still-open) named as settled on paper
/// and unexercised.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every property here is a property of two or more processes, so every
/// property here is measured across processes.</b> A named mutex is a kernel
/// object shared between them; a sharing violation is what one process's open
/// does to another's; an abandoned mutex needs a holder that really died. A
/// single-threaded stub can be made to produce all three answers and would prove
/// none of them.
/// </para>
/// <para>
/// <b>Nothing may leak.</b> Every probe runs inside a <see cref="JobObjectScope"/>,
/// so a failed assertion unwinding past the scope takes the whole tree with it,
/// and every probe additionally exits on its own after two minutes. The
/// directories are under the suite's scratch root, which is reclaimed at the
/// start of every run. The one piece of machine-wide state these tests create —
/// a named mutex — is named from a directory that carries a fresh GUID, so a run
/// can never inherit one from a previous run and quietly test nothing.
/// </para>
/// </remarks>
internal sealed class SessionLockTests
{
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// How long a probe gets to start a runtime and report. Generous on purpose:
    /// a slow machine starting sixteen processes is the ordinary reason this is
    /// slow, and a tight deadline reports as a locking failure.
    /// </summary>
    private static readonly TimeSpan Patience = TestDefaults.ProcessHang;

    /// <summary>
    /// How many processes race one directory.
    /// </summary>
    /// <remarks>
    /// The design target is ~100 concurrent BrowserAI processes. Sixteen is what
    /// the suite pays for on every run; the same test was also run at
    /// <b>64</b> on 2026-08-16, twice, with the same result — one acquired,
    /// sixty-three told who had it, the slowest refusal at 1.40 s against the
    /// five-second gate. Re-establish by raising this constant, rebuilding and
    /// running this test alone. The number recorded in
    /// [kb](../../kb/windows/detection.md) is that run, not an extrapolation
    /// from this one.
    /// </remarks>
    private const int Contenders = 16;

    /// <summary>
    /// How long the three tests here that watch a call which <b>must not
    /// return</b> watch it for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a hang detector and not a promptness assertion — the inverse of
    /// both.</b> Everything else in this suite bounds how long something may
    /// take, and must be unreachable by a slow machine. This bounds how long a
    /// call is <i>observed failing to return</i>, so a slow, starved or paging
    /// machine can only make it pass. The number therefore has to be sized
    /// against the <b>defect</b> rather than against the product: a free
    /// directory acquired without the gate costs tens of milliseconds, so three
    /// seconds is roughly two orders of magnitude of headroom on the behaviour
    /// being excluded, and it is three seconds of a suite that is otherwise
    /// event-driven.
    /// </para>
    /// <para>
    /// <b>Its three users are sized against three different defects and the
    /// argument is the same for each.</b>
    /// <see cref="AProbeThatFindsTheDirectoryFreeStillProvesItAtTheGate"/> is the
    /// original;
    /// <see cref="ADisposalArrivingDuringAReleaseAndDeleteWaitsForItInsteadOfLeakingTheHandle"/>
    /// and
    /// <see cref="AMutationArrivingDuringAReleaseAndDeleteWaitsForItRatherThanReadingAFieldItIsWriting"/>
    /// were added 2026-08-24 for adversarial review B4, where the excluded
    /// behaviour is a disposal that contends with nothing and returns in
    /// microseconds, and the required behaviour is a wait that <b>cannot</b> end
    /// until the thread doing the watching lets it — so there is no load under
    /// which the fixed code reaches this bound at all.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan StillBlocked = TimeSpan.FromSeconds(3);

    [Test]
    public async Task UnderConcurrentProcessesExactlyOneAcquiresAndEveryOtherIsToldWho()
    {
        using var scratch = ScratchDirectory.Create("session-race");
        var (directory, path) = NewSession(scratch, "race");

        var release = Path.Combine(scratch.Path, "release.flag");
        var startName = $@"Local\BrowserAI-Test-Start-{Guid.NewGuid():N}";

        // Local\ deliberately, and it is not an exception to the product's rule:
        // this is the rig's start gate, not one of BrowserAI's locks, and it
        // must NOT be visible to another logon session running this same suite.
        using var start = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, startName, out var createdNew);
        await Assert.That(createdNew).IsTrue();

        var reports = Enumerable.Range(0, Contenders)
            .Select(index => Path.Combine(scratch.Path, $"report-{index.ToString(CultureInfo.InvariantCulture)}.json"))
            .ToList();

        int winner;

        using (var scope = new JobObjectScope())
        {
            foreach (var report in reports)
            {
                _ = scope.Launch(ProbePath, AppContext.BaseDirectory, "session-race", directory, startName, report, release);
            }

            await WaitForAllAsync(reports.Select(report => $"{report}.ready"));

            // Everybody is parked on the event. This is the instant the race
            // starts, rather than "whenever each process happened to get here".
            _ = start.Set();

            var outcomes = await ProbeReport.ReadAllAsync(reports, Patience);

            var taken = outcomes.Where(report => (bool)report["taken"]!).ToList();
            var refused = outcomes.Where(report => !(bool)report["taken"]!).ToList();

            // ⚠️ EVERY ASSERTION BELOW CARRIES THE WHOLE DOSSIER, and that is the
            // 2026-08-18 correction rather than decoration. This test failed once
            // in eighteen full-suite runs with a loser reporting an outcome other
            // than `Held`, and the run that caught it recorded only that much: not
            // which outcome, not the holder it named, not what was on disk. The
            // property under test is a property of sixteen processes, so a
            // failure that shows one of them cannot be diagnosed — it took twenty
            // further runs to not reproduce. The dossier is the whole set, and it
            // is read before the release flag is written, so the winner is still
            // holding while it is taken.
            var dossier = Dossier(outcomes, path);

            await Assert.That(taken.Count).IsEqualTo(1).Because(dossier);
            await Assert.That(refused.Count).IsEqualTo(Contenders - 1).Because(dossier);

            winner = (int)taken[0]["pid"]!;
            await Assert.That((string?)taken[0]["outcome"]).IsEqualTo(nameof(SessionLockOutcome.Acquired)).Because(dossier);

            foreach (var loser in refused)
            {
                // Not "busy", not a timeout, not a wait: the holder, named.
                await Assert.That((string?)loser["outcome"]).IsEqualTo(nameof(SessionLockOutcome.Held)).Because(dossier);
                await Assert.That((int?)loser["holderPid"]).IsEqualTo(winner).Because(dossier);

                var message = (string)loser["message"]!;
                await Assert.That(message).Contains($"PID {winner.ToString(CultureInfo.InvariantCulture)}").Because(dossier);
                await Assert.That(message).Contains("running since").Because(dossier);
                await Assert.That(message).Contains("took the lock at").Because(dossier);
                await Assert.That(message).Contains("race contender").Because(dossier);
                await Assert.That(message).Contains("Nothing was changed.").Because(dossier);

                // Acquisition never waits. The only bounded wait in the design is
                // the per-directory gate, and a loser that had waited on it to
                // the limit would be sitting at the gate's own timeout.
                //
                // ⚠️ This is NOT a promptness assertion, and the distinction is
                // the whole reason the gate was re-sized on 2026-08-18. What it
                // asserts is that no contender reached the gate's timeout, which
                // is the same statement as "every refusal named the holder" —
                // reaching it produces `Busy`, which the outcome assertion above
                // catches first. It is kept because a build that changed `Busy`
                // into something else would slip past that one.
                await Assert.That((double)loser["elapsedMilliseconds"]!)
                    .IsLessThan(LockScopes.PerDirectoryGate.TotalMilliseconds)
                    .Because(dossier);
            }

            await File.WriteAllTextAsync(release, "go");
        }

        // One record, written once, by the winner -- and no temp file left over
        // from the durable write.
        var record = SessionLock.ReadRecord(path);

        await Assert.That(record).IsNotNull();
        await Assert.That(record!.Holder!.ProcessId).IsEqualTo(winner);

        // The guard says the same thing the record's newest holder statement
        // does, which is what makes a probe and a history two views of one
        // acquisition rather than two answers.
        await Assert.That(LockFile.Read(path.LockFile)!.ProcessId).IsEqualTo(winner);
    }

    /// <summary>
    /// A contender that can name the holder is refused <b>in front of</b> the
    /// per-directory gate, so the answer does not queue behind everything else
    /// naming the same directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gate is held by a third process for the whole call, and that is the
    /// assertion rather than the setup.</b> A <c>TryAcquire</c> that still went
    /// through the gate could only come back <c>Busy</c>, at
    /// <see cref="LockScopes.PerDirectoryGate"/>; one that comes back
    /// <c>Held</c> naming the holder's pid provably never entered it. No clock is
    /// read, because the outcome is the discriminator.
    /// </para>
    /// <para>
    /// <b>Why this exists.</b> Every process that wanted to know who held a
    /// session took the gate, losers included — so a refusal waited behind the
    /// whole queue rather than behind one critical section, and the cost was
    /// super-linear: 367 ms at 16 contenders, 3,349 ms at the charter's design
    /// point of 100, and at 200 the then-five-second gate was reached by queueing
    /// alone. The sharing violation on <c>browserai.lock</c> already proves ownership,
    /// so the gate was being taken to answer a question the kernel had answered.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AContenderThatCanNameTheHolderIsRefusedInFrontOfTheGate()
    {
        using var scratch = ScratchDirectory.Create("session-probe-held");
        var (directory, path) = NewSession(scratch, "probe-held");

        var heldReady = Path.Combine(scratch.Path, "held.json");
        var gateReady = Path.Combine(scratch.Path, "gate.json");

        using var scope = new JobObjectScope();

        // Ordered, and the order is load-bearing: the holder needs the gate to
        // take the directory, so it has to be finished with it before the gate
        // is taken away from everybody.
        _ = scope.Launch(ProbePath, AppContext.BaseDirectory, "session-hold", directory, heldReady, "reading the customer portal");

        var holder = await ProbeReport.ReadAsync(heldReady, Patience);
        await Assert.That((bool)holder["taken"]!).IsTrue();

        var holderPid = (int)holder["pid"]!;

        _ = scope.Launch(ProbePath, AppContext.BaseDirectory, "session-hold-gate", directory, gateReady);

        var gate = await ProbeReport.ReadAsync(gateReady, Patience);
        await Assert.That((string?)gate["acquisition"]).IsEqualTo(nameof(MutexAcquisition.Acquired));
        await Assert.That((string?)gate["mutexName"]).IsEqualTo(path.MutexName);

        var refused = SessionLock.TryAcquire(path, Request("a peer that only wants a name"), NullLogger.Instance);

        try
        {
            // Not Busy. The gate is provably held by a process that will not let
            // go until this scope is disposed, so reaching it at all could only
            // end at its timeout.
            await Assert.That(refused.Outcome).IsEqualTo(SessionLockOutcome.Held);
            await Assert.That(refused.Taken).IsFalse();
            await Assert.That(refused.Guard?.ProcessId).IsEqualTo(holderPid);
            await Assert.That(refused.HolderRunning).IsTrue();

            // The same sentence a gated refusal produced, because both routes go
            // through one place. A model must not be able to tell which path
            // answered it.
            await Assert.That(refused.Message).Contains($"PID {holderPid.ToString(CultureInfo.InvariantCulture)}");
            await Assert.That(refused.Message).Contains("reading the customer portal");
            await Assert.That(refused.Message).Contains("Nothing was changed.");
        }
        finally
        {
            refused.Acquired?.Dispose();
        }
    }

    /// <summary>
    /// A probe that finds the directory <b>free</b> proves nothing, and still has
    /// to prove it at the gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the test that stops the optimisation being taken one step
    /// too far, and the step was attacked before it was built</b> —
    /// [the adversarial review](../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// D. With the gate skipped on the free path, two contenders both probe and
    /// both see "free"; A writes and holds its record; B's rename is refused
    /// because A's handle is open, and B <i>retries</i>. The moment anything
    /// closes A's handle — a rewrite, a teardown, a destroy — B's next retry
    /// lands, and <b>B holds <c>browserai.lock</c> while A holds a valid handle to a
    /// now-nameless file</b>. Both report ownership. The retry loop becomes the
    /// serialiser, and a retry loop is not a lock.
    /// </para>
    /// <para>
    /// <b>The check that the call is still running is safe in the only direction
    /// that matters, and it is not a promptness assertion.</b> A machine that is
    /// slow, starved or paging can only make the call take <i>longer</i>, which
    /// makes this pass. The one thing that makes it fail is the call returning
    /// while a foreign process holds the gate — which is precisely the defect.
    /// Against the product as written, a free directory is taken in tens of
    /// milliseconds, so the window below is more than two orders of magnitude of
    /// headroom on the failing behaviour.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProbeThatFindsTheDirectoryFreeStillProvesItAtTheGate()
    {
        using var scratch = ScratchDirectory.Create("session-probe-free");
        var (directory, path) = NewSession(scratch, "probe-free");

        // No browserai.lock at all, so the probe's open fails with "not found" rather
        // than with a sharing violation -- the "looks free" answer, which is the
        // one it is not allowed to act on.
        await Assert.That(File.Exists(path.LockFile)).IsFalse();

        var gateReady = Path.Combine(scratch.Path, "gate.json");
        Task<SessionLockResult> call;

        using (var scope = new JobObjectScope())
        {
            _ = scope.Launch(ProbePath, AppContext.BaseDirectory, "session-hold-gate", directory, gateReady);

            var gate = await ProbeReport.ReadAsync(gateReady, Patience);
            await Assert.That((string?)gate["acquisition"]).IsEqualTo(nameof(MutexAcquisition.Acquired));

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            call = Task.Run(() =>
            {
                entered.SetResult();
                return SessionLock.TryAcquire(path, Request("a peer that found nothing"), NullLogger.Instance);
            });

            await entered.Task;

            var blocked = await Task.WhenAny(call, Task.Delay(StillBlocked));

            await Assert.That(blocked).IsNotEqualTo((Task)call).Because(
                "the per-directory gate is held by another process, so a TryAcquire that reached it cannot have returned; "
                + "one that returned took the probe's 'looks free' as an answer and skipped the gate, which is how two "
                + "processes end up owning one directory");
        }

        // The gate holder is gone with the job object, so the wait clears and the
        // directory is taken -- by the route it was always meant to be taken by.
        var taken = await call.WaitAsync(TestDefaults.InProcessHang);

        try
        {
            await Assert.That(taken.Outcome).IsEqualTo(SessionLockOutcome.Acquired);
            await Assert.That(taken.Taken).IsTrue();
        }
        finally
        {
            taken.Acquired?.Dispose();
        }
    }

    [Test]
    public async Task AKilledHolderIsReclaimedAndTheReasonNamesThePidAndWhen()
    {
        using var scratch = ScratchDirectory.Create("session-reclaim");
        var (directory, path) = NewSession(scratch, "reclaim");

        var ready = Path.Combine(scratch.Path, "ready.json");
        int holderPid;
        long holderCreated;

        using (var scope = new JobObjectScope())
        {
            _ = scope.Launch(ProbePath, AppContext.BaseDirectory, "session-hold", directory, ready, "reading the customer portal");

            var report = await ProbeReport.ReadAsync(ready, Patience);
            await Assert.That((bool)report["taken"]!).IsTrue();

            // The pid the child reports about itself, never the one the launcher
            // recorded: an interposed process would make the second true and the
            // first false.
            holderPid = (int)report["pid"]!;
            holderCreated = ProcessIdentity.CreationTimeOf(holderPid);

            // The reclaim below is worth nothing unless the lock was real while
            // the holder lived, so that is asserted first.
            var whileAlive = SessionLock.TryAcquire(path, Request("second opinion"), NullLogger.Instance);

            try
            {
                await Assert.That(whileAlive.Outcome).IsEqualTo(SessionLockOutcome.Held);
                await Assert.That(whileAlive.Message).Contains($"PID {holderPid.ToString(CultureInfo.InvariantCulture)}");
            }
            finally
            {
                whileAlive.Acquired?.Dispose();
            }

            // TerminateProcess from outside: no finally, no shutdown hook, no
            // code of the holder's runs. Only the kernel releases the handle.
            ProcessIdentity.Terminate(holderPid, holderCreated);
        }

        await WaitUntilGoneAsync(holderPid, holderCreated);

        var reclaimed = SessionLock.TryAcquire(path, Request("taking over"), NullLogger.Instance);

        try
        {
            await Assert.That(reclaimed.Outcome).IsEqualTo(SessionLockOutcome.Reclaimed);
            await Assert.That(reclaimed.Taken).IsTrue();
            await Assert.That(reclaimed.HolderRunning).IsFalse();
            await Assert.That(reclaimed.Message).Contains($"was locked by PID {holderPid.ToString(CultureInfo.InvariantCulture)} since ");
            await Assert.That(reclaimed.Message).Contains("no longer running");
            await Assert.That(reclaimed.Message).Contains("Reclaiming it.");

            // Row 9 is not an error: the purpose the dead session recorded is
            // handed on rather than lost, which is what a resume is for.
            await Assert.That(reclaimed.Message).Contains("reading the customer portal");
            await Assert.That(reclaimed.Acquired!.Record.PurposeHistory.Count).IsEqualTo(2);
            await Assert.That(reclaimed.Acquired.Record.PurposeHistory[0].Value).IsEqualTo("reading the customer portal");

            // Schema 2: the dead session's purpose is not merely kept, it is
            // DATED -- and dated before the purpose that replaced it, so a reader
            // can tell which agent said which.
            await Assert.That(reclaimed.Acquired.Record.PurposeHistory[0].At)
                .IsLessThan(reclaimed.Acquired.Record.PurposeHistory[^1].At);

            // And the dead holder is still in the record beside the live one,
            // which is the statement `Reclaimed` is making.
            await Assert.That(reclaimed.Acquired.Record.HolderHistory.Count).IsEqualTo(2);
            await Assert.That(reclaimed.Acquired.Record.HolderHistory[0].Value.ProcessId).IsEqualTo(holderPid);
            await Assert.That(reclaimed.Acquired.Record.Holder!.ProcessId).IsEqualTo(Environment.ProcessId);
        }
        finally
        {
            reclaimed.Acquired?.Dispose();
        }
    }

    [Test]
    public async Task AnAbandonedMutexIsAcquiredAndTheAcquisitionSaysSo()
    {
        using var scratch = ScratchDirectory.Create("session-abandoned");
        var (directory, path) = NewSession(scratch, "abandoned");
        var ready = Path.Combine(scratch.Path, "gate.json");

        // THE HANDLE IS OPENED BEFORE THE HOLDER IS KILLED, AND THAT ORDER IS
        // THE WHOLE TEST. A named mutex is reference-counted by its handles: if
        // the process that dies holding it was the last one with a handle, the
        // kernel object is destroyed with it and the next CreateMutexW makes a
        // brand new, unabandoned object. Measured 2026-08-16 -- written the
        // other way round, this test observed `Acquired` and would have passed
        // for a build that swallowed AbandonedMutexException entirely.
        using var mutex = MachineMutex.Create(path.MutexName);

        await KillAGateHolderAsync(directory, ready);

        // R3. The wait SUCCEEDS -- what the exception reports is that the
        // previous holder died without releasing. Unhandled it disables locking
        // permanently after the first crash, and nothing reports it.
        var first = mutex.Acquire(LockScopes.PerDirectoryGate);
        await Assert.That(first).IsEqualTo(MutexAcquisition.AcquiredAbandoned);

        // Releasing is the proof that it really was acquired: releasing a mutex
        // this thread does not own throws.
        mutex.Release();

        // And the abandonment is consumed rather than sticky, so the next
        // acquire is ordinary.
        var second = mutex.Acquire(LockScopes.PerDirectoryGate);
        await Assert.That(second).IsEqualTo(MutexAcquisition.Acquired);
        mutex.Release();
    }

    [Test]
    public async Task AnAbandonedGateDoesNotStopASessionBeingTaken()
    {
        using var scratch = ScratchDirectory.Create("session-abandoned-take");
        var (directory, path) = NewSession(scratch, "abandoned-take");
        var ready = Path.Combine(scratch.Path, "gate.json");

        // Held open across the kill, so the abandoned object survives to be
        // met -- see the note in the test above.
        using var keepAlive = MachineMutex.Create(path.MutexName);

        await KillAGateHolderAsync(directory, ready);

        var result = SessionLock.TryAcquire(path, Request("after a crash"), NullLogger.Instance);

        try
        {
            await Assert.That(result.Outcome).IsEqualTo(SessionLockOutcome.Acquired);
            await Assert.That(result.Acquired!.GateWasAbandoned).IsTrue();
        }
        finally
        {
            result.Acquired?.Dispose();
        }
    }

    [Test]
    public async Task TheSweepScopeIsTryAcquireAndSkipAtZeroTimeout()
    {
        using var scratch = ScratchDirectory.Create("session-sweep");

        // The product's own sweep name is asserted separately, below. The
        // cross-process arm uses a per-run name deliberately: a suite that
        // contends for the real Global\BrowserAI-Sweep would fight a running
        // BrowserAI on the machine, and would report that as a product defect.
        var mutexName = $@"Global\BrowserAI-Test-Sweep-{Guid.NewGuid():N}";
        var startName = $@"Local\BrowserAI-Test-Start-{Guid.NewGuid():N}";
        var release = Path.Combine(scratch.Path, "release.flag");

        using var start = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, startName, out _);

        const int Sweepers = 8;

        var reports = Enumerable.Range(0, Sweepers)
            .Select(index => Path.Combine(scratch.Path, $"sweep-{index.ToString(CultureInfo.InvariantCulture)}.json"))
            .ToList();

        using var scope = new JobObjectScope();

        foreach (var report in reports)
        {
            _ = scope.Launch(ProbePath, AppContext.BaseDirectory, "session-sweep", mutexName, startName, report, release);
        }

        await WaitForAllAsync(reports.Select(report => $"{report}.ready"));
        _ = start.Set();

        var outcomes = await ProbeReport.ReadAllAsync(reports, Patience);

        var swept = outcomes.Where(report => (string?)report["acquisition"] != nameof(MutexAcquisition.NotAcquired)).ToList();

        // One sweeps and the rest pay a mutex acquire. A skipped sweep is not a
        // missed sweep: whoever holds it is scanning the same store.
        await Assert.That(swept.Count).IsEqualTo(1);

        foreach (var report in outcomes)
        {
            // ⚠️ THE ARGUMENT, READ BACK OFF THE GATE — never the wall-clock
            // time the call took. Every contender asked for
            // LockScopes.NeverWaits, and that is the whole of "try-acquire-and-
            // skip at zero timeout": the acquire records what it was handed, so
            // a build that started waiting is caught by the record moving.
            //
            // This replaced `elapsedMilliseconds < 1000` on 2026-08-23, which
            // was the same defect the live-marker reclaim had shed three days
            // earlier with five times MORE headroom — and this one measured
            // across a process boundary, in each of eight processes launched
            // together, where process creation is the most contended thing on a
            // loaded box. It also invented its own 1000 rather than deriving
            // anything. What the record proves and what it does not is stated on
            // MachineMutex.LastAcquireTimeout;
            // AGateRecordsTheWaitItWasHandedRatherThanAConstant is the control
            // that it follows the argument rather than sitting on a constant.
            //
            // Asserted over EVERY outcome rather than only the skippers: the
            // winner asked for zero too, and a version that only checked the
            // losers would pass a sweep whose first contender blocked.
            await Assert.That((long?)report["acquireTimeoutTicks"]).IsEqualTo(LockScopes.NeverWaits.Ticks);
        }

        await File.WriteAllTextAsync(release, "go");
    }

    [Test]
    public async Task TheThreeScopesAreNamedInOnePlaceAndAllOfThemAreGlobal()
    {
        // R4: the scheduled task and BrowserAI using different mutexes is closed
        // by there being one name in one place, not by two files agreeing.
        await Assert.That(LockScopes.Sweep).IsEqualTo(@"Global\BrowserAI-Sweep");
        await Assert.That(LockScopes.PerDirectoryPrefix).IsEqualTo(@"Global\BrowserAI-");
        await Assert.That(LockScopes.NeverWaits).IsEqualTo(TimeSpan.Zero);
        await Assert.That(LockScopes.PerDirectoryGate).IsGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// The gate must outlast every wait taken while holding it, or one entitled
    /// reader converts every peer's correct answer into a wrong one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is an ordering constraint between two numbers in two files, and
    /// on 2026-08-18 it was violated in shipped code.</b>
    /// <see cref="RenameWindow.Budget"/> was raised to 30 s that day for good
    /// measured reasons; <see cref="LockScopes.PerDirectoryGate"/> was 5 s and
    /// nobody noticed the pair. <c>SessionLock.OpenHeld</c> and
    /// <c>SessionLock.ReadRecord</c> both run <i>inside</i> the gate and both go
    /// through <see cref="RenameWindow"/> — so a reader legitimately waiting out
    /// a rename could hold the gate six times longer than the gate's own
    /// timeout, and every other contender would be told <c>Busy</c>: <i>"something
    /// is wrong"</i>, about a machine where nothing was.
    /// </para>
    /// <para>
    /// <b>Asserted rather than commented, because the two values are three
    /// directories apart and each has its own long justification.</b> Either can
    /// be re-tuned on its own evidence; what may not happen is the two crossing,
    /// and the only thing that can notice that is a build.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheGateOutlastsEveryWaitTakenInsideIt()
    {
        // ⚠️ Corrected 2026-08-18 (previously `IsGreaterThan(RenameWindow.Budget)`,
        // i.e. the PAIR). It is the SUM that has to fit: one hold of the gate
        // contains three of those waits in series, so a 60 s gate was being
        // outlasted by 90 s of legitimate waiting inside it, and this test read
        // green throughout. The pair was the relationship the comment reasoned
        // about and the sum is the one the code has.
        var inside = LockScopes.RenameWindowWaitsInsideTheGate * RenameWindow.Budget;

        await Assert.That(LockScopes.PerDirectoryGate).IsGreaterThan(inside)
            .Because(
                "every open performed under the per-directory gate goes through RenameWindow, and one hold contains "
                + $"{LockScopes.RenameWindowWaitsInsideTheGate.ToString(CultureInfo.InvariantCulture)} of them in series — so a gate that expires "
                + "before that total does turns a peer's correct 'held by PID n' into a wrong 'Busy', on a holder that "
                + "is doing exactly what the design tells it to");

        // The count is not decoration: it is what a fourth wait has to be added
        // to, and this asserts it still describes something.
        await Assert.That(LockScopes.RenameWindowWaitsInsideTheGate).IsGreaterThan(0);
    }

    [Test]
    public async Task AMachineMutexRefusesAnyNameThatIsNotGlobal()
    {
        // There is no Local\ fallback, and the type will not be talked into one.
        var local = Assert.Throws<ArgumentException>(() => _ = MachineMutex.Create(@"Local\BrowserAI-anything"));
        await Assert.That(local!.Message).Contains("Global");

        _ = Assert.Throws<ArgumentException>(() => _ = MachineMutex.Create("BrowserAI-anything"));
    }

    /// <summary>
    /// The gate records the wait it was <b>handed</b>, and the record follows the
    /// argument rather than sitting on a constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the positive control underneath
    /// <c>UpdateTests.AReclaimWhosePeerHoldsTheGateSkipsAtOnceAndRemovesNothing</c></b>,
    /// which asserts that the live-marker reclaim skipped <i>at once</i> by
    /// reading <see cref="MachineMutex.LastAcquireTimeout"/> back off the gate
    /// instead of timing the call. A property that always answered
    /// <see cref="LockScopes.NeverWaits"/> would make that assertion vacuous and
    /// would be indistinguishable from a working one when read from there — so
    /// both values are exercised here, on one object, in order.
    /// </para>
    /// <para>
    /// <b>Unasked is a third state and is kept separate from zero.</b> A reclaim
    /// that never reached an acquire — no directory, or a gate that could not be
    /// created — must not report that it asked not to wait.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AGateRecordsTheWaitItWasHandedRatherThanAConstant()
    {
        using var gate = MachineMutex.Create(
            $"{LockScopes.PerDirectoryPrefix}acquire-record-{Guid.NewGuid():N}");

        // Nothing has been asked of it yet, which is a different fact from
        // "it was asked for zero".
        await Assert.That(gate.LastAcquireTimeout).IsNull();

        await Assert.That(gate.Acquire(LockScopes.NeverWaits)).IsEqualTo(MutexAcquisition.Acquired);
        await Assert.That(gate.LastAcquireTimeout).IsEqualTo(LockScopes.NeverWaits);
        gate.Release();

        // ⚠️ THE CONTROL. A different value, on the same object: the record moved
        // with the argument, so it is not a constant wearing a property's name.
        await Assert.That(gate.Acquire(LockScopes.LiveInstanceGate)).IsEqualTo(MutexAcquisition.Acquired);
        await Assert.That(gate.LastAcquireTimeout).IsEqualTo(LockScopes.LiveInstanceGate);
        gate.Release();

        await Assert.That(LockScopes.NeverWaits).IsNotEqualTo(LockScopes.LiveInstanceGate);
    }

    [Test]
    public async Task TheProductCreatesNamedKernelObjectsInExactlyOnePlace()
    {
        // A string scan for "Local\" would match this project's own prose about
        // why there is no fallback. What actually has to be true is narrower and
        // checkable: exactly one file in the product constructs a named waitable
        // object at all, and that file refuses anything but Global\.
        var creators = new List<string>();

        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(IsProductCode))
        {
            var text = await RepositoryLayout.ReadCodeAsync(file);

            if (text.Contains("new Mutex(", StringComparison.Ordinal)
                || text.Contains("new EventWaitHandle(", StringComparison.Ordinal)
                || text.Contains("new Semaphore(", StringComparison.Ordinal)
                || text.Contains("OpenExisting(", StringComparison.Ordinal))
            {
                creators.Add(Path.GetFileName(file.FullName));
            }
        }

        await Assert.That(string.Join(", ", creators)).IsEqualTo("MachineMutex.cs");
    }

    /// <summary>
    /// The liveness probe takes <b>no</b> lock, opens <b>no</b> database, and is
    /// one <c>CreateFile</c> on <c>browserai.lock</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously "the listing's probe takes the
    /// per-directory gate without waiting for it").</b> The gate was there
    /// because the record was durably rewritten and renamed on every forwarded
    /// call, so a bare probe could catch a busy session in the instant its
    /// ownership handle was dropped and read it as free — which is what
    /// <c>browserai_list</c> printed between 2026-08-20 and 2026-08-24. Nothing
    /// rewrites the guard now, so the gate buys nothing and costs a
    /// <c>CreateMutexW</c> per listed entry.
    /// </para>
    /// <para>
    /// <b>Two things it must not do, and the second is the new one.</b> It must
    /// not queue — <c>browserai_list</c> runs it once per session on the machine,
    /// and 100 contenders on one gate was measured at 3,349 ms. And it must not
    /// open the store: a database open is orders of magnitude dearer than a
    /// <c>CreateFile</c>, it can leave a <c>-shm</c> in a directory nobody asked
    /// it to touch, and the newest holder statement cannot answer the question
    /// anyway.
    /// </para>
    /// <para>
    /// <b>This is weaker than a red test and could not have been planted as one
    /// — say so rather than implying otherwise.</b> What it holds is the one
    /// property the behavioural tests beside it cannot: those would all still
    /// pass with a gate, or with a database open, in the answer.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLivenessProbeTakesNoLockAndOpensNoDatabase()
    {
        var file = RepositoryLayout.SourceAndScriptFiles
            .Single(candidate => string.Equals(candidate.Name, "SessionLock.cs", StringComparison.OrdinalIgnoreCase));

        var body = MethodBody(await RepositoryLayout.ReadCodeAsync(file), "ProbeLiveness");

        // Not vacuous: a scan that failed to find the method would otherwise
        // report every half clean.
        await Assert.That(body).IsNotNull();
        await Assert.That(body!).Contains("LockFile.Probe");
        await Assert.That(body!).DoesNotContain("MachineMutex");
        await Assert.That(body!).DoesNotContain("LockScopes.");
        await Assert.That(body!).DoesNotContain("SessionStore");
        await Assert.That(body!).DoesNotContain("ReadRecord");
    }

    /// <summary>One method's body, brace-matched from its signature down.</summary>
    /// <param name="code">The file, comment-only lines already removed.</param>
    /// <param name="name">The method name to find.</param>
    /// <returns>The body, or <see langword="null"/> when the method is not there.</returns>
    private static string? MethodBody(string code, string name)
    {
        var lines = code.Split('\n');

        for (var at = 0; at < lines.Length; at++)
        {
            if (!lines[at].Contains(name + "(", StringComparison.Ordinal)
                || !lines[at].Contains("static", StringComparison.Ordinal))
            {
                continue;
            }

            var body = new List<string>();
            var depth = 0;
            var opened = false;

            for (var line = at; line < lines.Length; line++)
            {
                foreach (var character in lines[line])
                {
                    if (character is '{')
                    {
                        depth++;
                        opened = true;
                    }
                    else if (character is '}')
                    {
                        depth--;
                    }
                }

                if (opened)
                {
                    body.Add(lines[line]);
                }

                if (opened && depth <= 0)
                {
                    return string.Join('\n', body);
                }
            }
        }

        return null;
    }

    [Test]
    public async Task AHeldLockRefusesASecondWriterAndStillAnswersAReader()
    {
        using var scratch = ScratchDirectory.Create("session-share");
        var (_, path) = NewSession(scratch, "share");

        var first = SessionLock.TryAcquire(path, Request("the first"), NullLogger.Instance);

        try
        {
            await Assert.That(first.Taken).IsTrue();

            // That is the lock: write access is refused.
            _ = Assert.Throws<IOException>(() =>
            {
                using var stranger = new FileStream(path.LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            });

            // And this is why the refusal can name the holder: a reader still
            // gets in. Note the share mode -- FileShare.Read alone FAILS here,
            // because the holder has the file open for WRITE and a reader that
            // does not share write is refused outright. A reader written the
            // obvious way would turn "somebody owns this" into "this file cannot
            // be read", which is the wrong answer in the dangerous direction.
            _ = Assert.Throws<IOException>(() =>
            {
                using var narrow = new FileStream(path.LockFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            });

            // The guard names the holder; the store says what the session is
            // for. Two files, two questions, and a reader gets both while the
            // holder still has the directory.
            await Assert.That(LockFile.Read(path.LockFile)!.ProcessId).IsEqualTo(Environment.ProcessId);
            await Assert.That(SessionLock.ReadRecord(path)!.Purpose).IsEqualTo("the first");
        }
        finally
        {
            first.Acquired?.Dispose();
        }

        // Released, and the record stays: that is what makes a stale lock a
        // sentence instead of a refusal.
        await Assert.That(File.Exists(path.LockFile)).IsTrue();

        var second = SessionLock.TryAcquire(path, Request("the second"), NullLogger.Instance);

        try
        {
            await Assert.That(second.Outcome).IsEqualTo(SessionLockOutcome.Reclaimed);
            await Assert.That(second.HolderRunning).IsTrue();
        }
        finally
        {
            second.Acquired?.Dispose();
        }
    }

    [Test]
    public async Task ARenameCannotReplaceALockFileWhoseOwnHandleIsStillOpen()
    {
        using var scratch = ScratchDirectory.Create("session-rename");
        var (directory, path) = NewSession(scratch, "rename");

        var temp = Path.Combine(directory, "candidate.json");
        const int ErrorAccessDenied = unchecked((int)0x80070005);

        // The naive combination of the two requirements -- hold the handle AND
        // rename the record into place -- does not work under ANY share mode,
        // and this is the measurement that says so rather than an inference.
        // MoveFileEx with MOVEFILE_REPLACE_EXISTING wants DELETE on the
        // destination and is refused ERROR_ACCESS_DENIED, not the sharing
        // violation one would expect -- so a rename retry that caught only
        // IOException would not retry at all.
        FileShare[] everyShareMode =
        [
            FileShare.Read,
            FileShare.Read | FileShare.Delete,
            FileShare.ReadWrite | FileShare.Delete,
        ];

        foreach (var share in everyShareMode)
        {
            await File.WriteAllTextAsync(temp, "{}");

            using var held = new FileStream(path.LockFile, FileMode.Create, FileAccess.ReadWrite, share);
            var blocked = Assert.Throws<UnauthorizedAccessException>(() => File.Move(temp, path.LockFile, overwrite: true));

            await Assert.That(blocked!.HResult).IsEqualTo(ErrorAccessDenied);
            await Assert.That(held.CanWrite).IsTrue();
        }

        // Closing first is the only thing that works, which is what makes the
        // gap real and the per-directory mutex the thing that covers it.
        //
        // ⚠️ The rename below is retried, and the reason is a live-system
        // condition rather than a defect in either side of it. A file this
        // process has just closed is briefly held by something OUTSIDE this
        // repository — the same scanner's handle that
        // `SessionLock`'s two-second move budget, `InstallationMarker` and the
        // first-run cache's commit all exist for — and `MOVEFILE_REPLACE_EXISTING`
        // wants DELETE on the destination, so it is refused ACCESS_DENIED rather
        // than as a sharing violation. Measured 2026-08-18: one run in twenty of
        // the full suite at `SuiteParallelism.Unbounded`, 47 ms into this test,
        // as a bare `UnauthorizedAccessException` naming neither the holder nor
        // the fact that a retry would have worked.
        //
        // The assertion is unchanged and is not weakened by this: what this test
        // establishes is that closing the handle is what makes the rename
        // POSSIBLE, and the arms above still prove it is refused while the handle
        // is open, on a single attempt, under every share mode.
        await File.WriteAllTextAsync(temp, "{}");

        using (var held = new FileStream(path.LockFile, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            await Assert.That(held.CanWrite).IsTrue();
        }

        await MoveOnceNothingElseHoldsItAsync(temp, path.LockFile);
        await Assert.That(File.Exists(temp)).IsFalse();

        // The `{}` that landed there is now a lock file this build refuses,
        // which is the strictness rule catching the experiment's own litter.
        var refused = SessionLock.TryAcquire(path, Request("after the experiment"), NullLogger.Instance);
        refused.Acquired?.Dispose();
        await Assert.That(refused.Outcome).IsEqualTo(SessionLockOutcome.Unreadable);

        File.Delete(path.LockFile);

        // Which is why acquisition closes, renames and re-opens inside the
        // per-directory gate, and why that gate exists at all.
        var result = SessionLock.TryAcquire(path, Request("after the experiment"), NullLogger.Instance);

        try
        {
            await Assert.That(result.Taken).IsTrue();

            _ = Assert.Throws<IOException>(() =>
            {
                using var stranger = new FileStream(path.LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            });
        }
        finally
        {
            result.Acquired?.Dispose();
        }
    }

    /// <summary>
    /// A reader in another process never sees the record half-written, and —
    /// unlike before — never sees it <b>absent</b> either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously
    /// <c>ARewriteIsNeverObservedTorn</c>, whose whole apparatus was a
    /// discriminator for an absence it had to tolerate).</b> The record was one
    /// JSON file, rewritten whole and renamed into place on every append, and
    /// the measurement that test carried is what this one replaces: at the
    /// instant of a null read, the name genuinely resolved to <b>nothing</b>
    /// while the writer's own <c>browserai.json.new-&lt;guid&gt;</c> sat beside
    /// it, and on one run it was absent on two consecutive reads. So
    /// <c>MoveFileEx</c> with <c>MOVEFILE_REPLACE_EXISTING</c> does not keep the
    /// name bound throughout on this machine, and the old test could only assert
    /// <i>an absence is acceptable when a rewrite is demonstrably in flight</i>.
    /// </para>
    /// <para>
    /// <b>An append is an <c>INSERT</c> now, so the assertion is the strong
    /// one.</b> Nothing renames, nothing unbinds a name, and the store's own
    /// write-ahead log is what makes a reader's view consistent — so a null read
    /// is a defect with no tolerance attached to it, and it is asserted as one.
    /// That is the property the two superseded attempts were both approximating.
    /// </para>
    /// <para>
    /// <b>The reader has to have been looking WHILE the writer wrote</b>, or
    /// "never torn" is a claim about an empty observation — so the assertion is
    /// on <i>distinct records observed</i> rather than on a read count. Two
    /// different purposes cannot both be seen unless the record changed under
    /// the reader.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnAppendIsNeverObservedTornAndNeverAsAbsent()
    {
        using var scratch = ScratchDirectory.Create("session-append");
        var (directory, path) = NewSession(scratch, "append");

        var ready = Path.Combine(scratch.Path, "ready.json");
        var done = Path.Combine(scratch.Path, "done.json");

        const int Appends = 100;

        var damaged = new List<string>();
        var reads = 0;

        // Every distinct record the reader actually saw. This, not a read count,
        // is what proves the reader was looking WHILE the writer was writing.
        var observed = new HashSet<string>(StringComparer.Ordinal);

        using (var scope = new JobObjectScope())
        {
            _ = scope.Launch(
                ProbePath,
                AppContext.BaseDirectory,
                "session-rewrite",
                directory,
                ready,
                Appends.ToString(CultureInfo.InvariantCulture),
                done);

            var report = await ProbeReport.ReadAsync(ready, Patience);
            await Assert.That((bool)report["taken"]!).IsTrue();

            var clock = Stopwatch.StartNew();

            // The handshake is released from INSIDE the loop, after a read has
            // demonstrably happened. Written just before the loop it guarded the
            // wrong side: `WriteAllTextAsync` yields, and the continuation can be
            // descheduled past the writer's entire run, after which the loop
            // finds `done` already set and reads nothing.
            var released = false;

            while (!File.Exists(done) && clock.Elapsed < Patience)
            {
                try
                {
                    var record = SessionLock.ReadRecord(path);
                    reads++;

                    if (record is null)
                    {
                        // ⚠️ NO TOLERANCE, AND THAT IS THE CHANGE. A reader that
                        // sees no record while another process is appending to it
                        // is a reader that would report an owned session as
                        // unowned -- which is the precondition for two BrowserAI
                        // processes driving one directory.
                        damaged.Add($"'{path.DataFile}' read as no record at all while a writer was appending to it");
                    }
                    else
                    {
                        _ = observed.Add(record.Purpose);
                    }

                    if (!released)
                    {
#pragma warning disable CA1849 // Synchronous on purpose: awaiting here yields, and a yield at this exact point is the defect being fixed -- the reader must not leave the loop between reading and saying go.
                        File.WriteAllText($"{ready}.go", "go");
#pragma warning restore CA1849
                        released = true;
                    }
                }
#pragma warning disable CA1031 // Anything at all that a concurrent reader sees is the finding; narrowing the catch would hide the interesting half.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    damaged.Add($"{failure.GetType().Name}: {failure.Message}");
                }
            }

            var finished = await ProbeReport.ReadAsync(done, Patience);

            // A writer that died is invisible otherwise: its streams are drained
            // into the void, so the failure presents as the host waiting out its
            // own patience and naming the wrong thing.
            await Assert.That((string?)finished["failure"]).IsNull();
            await Assert.That((int?)finished["rewrites"]).IsEqualTo(Appends);
        }

        await Assert.That(string.Join(Environment.NewLine, damaged.Distinct(StringComparer.Ordinal))).IsEmpty();

        // And it is still there afterwards, readable, with the writer finished —
        // so a run that somehow passed the line above while having actually lost
        // the record still fails here.
        await Assert.That(SessionLock.ReadRecord(path)).IsNotNull();

        // The reader has to have been looking, and looking WHILE the writer
        // wrote, or "never torn" is a claim about an empty observation.
        await Assert.That(reads).IsGreaterThan(0);
        await Assert.That(observed.Count).IsGreaterThanOrEqualTo(2)
            .Because(
                $"the reader made {reads.ToString(CultureInfo.InvariantCulture)} read(s) and saw "
                + $"{observed.Count.ToString(CultureInfo.InvariantCulture)} distinct record(s), so it was not looking while the writer wrote and this test asserted nothing");
    }

    [Test]
    public async Task ADirectoryThatDoesNotExistIsRefusedRatherThanCreated()
    {
        using var scratch = ScratchDirectory.Create("session-missing");
        var path = SessionPath.For(Path.Combine(scratch.Path, "never-created"));

        var result = SessionLock.TryAcquire(path, Request("nowhere"), NullLogger.Instance);

        await Assert.That(result.Outcome).IsEqualTo(SessionLockOutcome.DirectoryMissing);
        await Assert.That(result.Taken).IsFalse();
        await Assert.That(Directory.Exists(path.FullPath)).IsFalse();
    }

    [Test]
    public async Task ALockFileWithAnUnknownKeyIsRefusedAndTheDirectoryIsLeftAlone()
    {
        using var scratch = ScratchDirectory.Create("session-strict");
        var (_, path) = NewSession(scratch, "strict");

        var first = SessionLock.TryAcquire(path, Request("the original"), NullLogger.Instance);
        first.Acquired!.Dispose();

        var original = await File.ReadAllTextAsync(path.LockFile);
        var files = Directory.GetFiles(path.FullPath).Length;

        await File.WriteAllTextAsync(path.LockFile, original.Replace(@"""processId""", @"""processld""", StringComparison.Ordinal));

        var refused = SessionLock.TryAcquire(path, Request("after the edit"), NullLogger.Instance);

        try
        {
            await Assert.That(refused.Outcome).IsEqualTo(SessionLockOutcome.Unreadable);
            await Assert.That(refused.Taken).IsFalse();
            await Assert.That(refused.Message).Contains("processld");
            await Assert.That(refused.Message).Contains("is not a BrowserAI lock file");
            await Assert.That(refused.Message).Contains("will not guess at ownership");
        }
        finally
        {
            refused.Acquired?.Dispose();
        }

        // The refusal changed nothing, which is what makes the recovery the
        // caller is offered actually available.
        await Assert.That(await File.ReadAllTextAsync(path.LockFile)).Contains("processld");
        await Assert.That(Directory.GetFiles(path.FullPath).Length).IsEqualTo(files);
    }

    /// <summary>
    /// A record that landed and could not then be re-opened is reported as
    /// written, and a write that never landed is still reported as nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both arms in one test because the defect was that they were one
    /// arm.</b> Until 2026-08-19 <c>WriteDurably</c> and the re-open shared a
    /// single <c>catch</c>, so a failure <i>after</i> the rename answered
    /// <i>"the directory was not taken and nothing was changed"</i> — about a
    /// machine where <c>browserai.lock</c> had just been replaced with a record
    /// naming this process as the holder. Asserting the honest sentence without
    /// also asserting that the other arm still says <i>nothing was changed</i>
    /// would let the fix be "stop claiming that anywhere", which is not a fix.
    /// </para>
    /// <para>
    /// <b>The seam is an ACL, and it is the one right the two operations do not
    /// share.</b> <c>WriteDurably</c> creates its temp file <c>FileAccess.Write</c>
    /// and renames it into place; neither needs <c>FILE_READ_DATA</c>. The
    /// re-open asks for <c>FileAccess.ReadWrite</c> and does. So an inheritable
    /// <b>deny</b> of <c>ReadData</c> on the session directory lets the record be
    /// written and refuses the handle over it, deterministically and with no
    /// fault injection in the product. The control arm denies <c>CreateFiles</c>
    /// on the directory itself instead, which stops the temp file ever existing.
    /// <i>The seam itself moved to <see cref="DirectoryDenial"/> on 2026-08-19,
    /// when a second test needed it.</i>
    /// </para>
    /// <para>
    /// ⚠️ <b>This test costs <see cref="RenameWindow.Budget"/> — thirty seconds —
    /// and that is coverage rather than waste.</b> The denied open runs through
    /// <c>RenameWindow.WaitOut</c>, which exists to wait out a rename in flight;
    /// a permanent denial is a different fault and must still be <i>reported</i>
    /// rather than waited on forever. <i>Corrected 2026-08-19 (previously "This
    /// is the only test in the suite that reaches the end of that budget, so it
    /// is also the only one that proves the wait is bounded at all")</i> — there
    /// are two now. <c>ErrorCatalogueTests.TheLockRowsAreEmittedByRealLockConditions</c>
    /// denies the same right over a <c>browserai.lock</c> that already exists, which
    /// reaches the <b>first</b> open in <c>TakeOrReport</c> rather than the
    /// re-open this test reaches, and that open had no arm for it at all until
    /// the same day.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWriteThatLandedSaysSoAndOnlyAWriteThatDidNotSaysNothingChanged()
    {
        using var scratch = ScratchDirectory.Create("session-write-landed");
        var (_, landed) = NewSession(scratch, "reopen-refused");

        SessionLockResult refused;

        // Disposed before anything reads the file, and before the scratch
        // teardown has to delete a tree it cannot enumerate.
        using (DirectoryDenial.Apply(landed.FullPath, FileSystemRights.ReadData, InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly))
        {
            refused = SessionLock.TryAcquire(landed, Request("the record lands and the re-open is refused"), NullLogger.Instance);
        }

        refused.Acquired?.Dispose();

        await Assert.That(refused.Taken).IsFalse();
        await Assert.That(refused.Message.Contains("nothing was changed", StringComparison.OrdinalIgnoreCase))
            .IsFalse()
            .Because(refused.Message);

        await Assert.That(refused.Message).Contains("the record WAS written");

        // ⚠️ THE HALF THAT MAKES THE SENTENCE A CLAIM RATHER THAN A PHRASING.
        // The answer says the guard was written and names this process as its
        // holder, so the file is read back and asked both questions. An answer
        // that merely stopped saying "nothing was changed" would pass every
        // assertion above and prove nothing about the disk.
        var written = LockFile.Read(landed.LockFile);

        await Assert.That(written).IsNotNull();
        await Assert.That(written!.ProcessId).IsEqualTo(Environment.ProcessId);

        // The other arm, and it is the reason this is one test. A write that
        // never landed leaves no record, and still says so.
        var (_, never) = NewSession(scratch, "write-refused");

        SessionLockResult unwritten;

        using (DirectoryDenial.Apply(never.FullPath, FileSystemRights.CreateFiles, InheritanceFlags.None, PropagationFlags.None))
        {
            unwritten = SessionLock.TryAcquire(never, Request("the write never lands"), NullLogger.Instance);
        }

        unwritten.Acquired?.Dispose();

        await Assert.That(unwritten.Taken).IsFalse();
        await Assert.That(unwritten.Message).Contains("nothing was changed");
        await Assert.That(File.Exists(never.LockFile)).IsFalse();
    }

    /// <summary>
    /// The ownership handle is held continuously across every write, so a write
    /// that fails cannot also release the directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously
    /// <c>ARewriteThatFailsKeepsTheDirectoryAndOneThatCannotTakeItBackSaysSo</c>).</b>
    /// That test existed because <c>Rewrite</c> <i>dropped</i> the ownership
    /// handle before every replacement — Windows will not rename over a file
    /// this process is holding — so an exception anywhere between the drop and
    /// the re-open left the session silently unowned while the caller was told
    /// only that a write had failed. It shipped broken once, which is what
    /// earned it a regression test.
    /// </para>
    /// <para>
    /// <b>The defect is now structurally impossible and this is what replaces
    /// the regression test.</b> A write is an <c>INSERT</c> into a second file;
    /// the guard's handle is opened once at acquisition and closed once at
    /// disposal, and nothing between those two points touches it. So the
    /// assertion is the property directly: a stranger's <c>FileAccess.ReadWrite</c>
    /// open of <c>browserai.lock</c> — the same test a competing BrowserAI's
    /// <c>TryAcquire</c> performs — is refused before a write, after a write,
    /// and after a write that <b>threw</b>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Coverage bounded, and named rather than implied.</b> The old test's
    /// second arm — a replacement that failed <i>and</i> a name that could not be
    /// taken back — has no counterpart, because there is no window in which the
    /// name is not held. The failing write here is provoked by disposing the
    /// session's store out from under it, which is the one deterministic write
    /// failure this suite can construct without injecting a fault into the
    /// product; a denied ACL cannot reach a connection that is already open.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheOwnershipHandleIsHeldAcrossEveryWriteIncludingOneThatFails()
    {
        using var scratch = ScratchDirectory.Create("session-write-continuity");
        var (_, path) = NewSession(scratch, "write-continuity");

        var taken = SessionLock.TryAcquire(path, Request("about to write"), NullLogger.Instance);
        var held = taken.Acquired!;

        try
        {
            await Assert.That(NothingHoldsTheLockFile(path.LockFile)).IsFalse();

            held.AppendPurpose("a purpose written while the directory is held");

            // THE PROPERTY, mid-session: the guard was never let go.
            await Assert.That(NothingHoldsTheLockFile(path.LockFile)).IsFalse();
            await Assert.That(held.Record.Purpose).IsEqualTo("a purpose written while the directory is held");

            // A write that throws. `ReleaseAndDelete` and `Dispose` are the only
            // things that close the store, and both take the directory with them
            // -- so the failing write is provoked from outside, by denying the
            // directory the store would have to create its next journal file in.
            using (DirectoryDenial.Apply(path.FullPath, FileSystemRights.CreateFiles | FileSystemRights.WriteData, InheritanceFlags.ObjectInherit, PropagationFlags.None))
            {
                try
                {
                    for (var i = 0; i < 200; i++)
                    {
                        held.AppendPurpose($"a purpose the store may refuse {i.ToString(CultureInfo.InvariantCulture)}");
                    }
                }
#pragma warning disable CA1031 // Whether the store refuses at all is the machine's business; what this test asserts is what happens to the guard either way.
                catch (Exception)
#pragma warning restore CA1031
                {
                    // Swallowed on purpose. The claim below is about the handle,
                    // and it has to hold whether the write failed or not.
                }
            }

            // ⚠️ THE CLAIM. However that ended, the directory is still ours.
            await Assert.That(NothingHoldsTheLockFile(path.LockFile)).IsFalse()
                .Because("a write that failed must not also release the directory: the ownership handle is opened once at acquisition and closed once at disposal, and nothing in between may touch it");
        }
        finally
        {
            held.Dispose();
        }

        // And disposal really does release it, so the assertion above is not
        // vacuously true of a handle nothing could ever take.
        await Assert.That(NothingHoldsTheLockFile(path.LockFile)).IsTrue();
    }

    /// <summary>
    /// A second disposal arriving while <c>ReleaseAndDelete</c> is mid-delete
    /// waits for it, rather than disposing the gate out from under it and
    /// leaving <c>browserai.lock</c> held by a <c>FileStream</c> nothing will
    /// ever close.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is finding B4 of
    /// [the 2026-08-18 adversarial review](../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// and it is the one whose failure does not heal.</b> <c>SessionManager</c>
    /// serialises nothing — <c>_live</c> is a <c>ConcurrentDictionary</c> and is
    /// the only synchronisation there is — so two tool calls naming one session
    /// run concurrently by design. A <c>_disposed</c> check taken <i>outside</i>
    /// the exclusion is a decision made by reading a field another thread is
    /// writing, and the disposal disposes the very object a blocked caller would
    /// wake holding: after that, every <c>TryAcquire</c> on the directory
    /// answers <c>Held</c> naming a pid with no session, for the life of the
    /// process, while the destroy reports a partial failure blaming
    /// <i>"something still has them open"</i>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously
    /// <c>ADisposalArrivingDuringAReleaseAndDeleteWaitsForItInsteadOfLeakingTheHandle</c>,
    /// which forced the interleaving through <c>Rewrite</c>'s <c>update</c>
    /// delegate).</b> There is no <c>Rewrite</c> and no <c>update</c> delegate:
    /// a mutation is a single <c>INSERT</c> with no caller code inside it. The
    /// one seam left in a path that holds the exclusion is
    /// <c>ReleaseAndDelete</c>'s own <c>delete</c>, so what this now forces is
    /// the other pairing — <c>Dispose</c> against <c>ReleaseAndDelete</c> — and
    /// its sibling below forces a mutation against the same delete.
    /// </para>
    /// <para>
    /// ⚠️ <b>Coverage bounded, and stated rather than implied.</b> The third
    /// pairing — a <i>disposal</i> arriving during a <i>mutation</i> — has no
    /// deterministic seam left, because the mutation no longer runs any caller's
    /// code. What holds it is the same private <c>Lock</c> these two prove is
    /// taken by both disposal paths and by every writing path, and nothing here
    /// can watch that pairing interleave.
    /// </para>
    /// <para>
    /// <b>Which way the join goes is the assertion, and load can only make it
    /// safer.</b> Against the defect the second disposal contends with nothing
    /// and returns in microseconds; against the fix it <i>cannot</i> return,
    /// because it takes the same per-session lock the delete is holding. There
    /// is no load under which the fixed code reaches this bound at all.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADisposalArrivingDuringAReleaseAndDeleteWaitsForItInsteadOfLeakingTheHandle()
    {
        using var scratch = ScratchDirectory.Create("session-destroy-races-disposal");
        var (_, path) = NewSession(scratch, "destroy-races-disposal");

        var taken = SessionLock.TryAcquire(path, Request("about to be destroyed twice at once"), NullLogger.Instance);
        await Assert.That(taken.Taken).IsTrue();

        var held = taken.Acquired!;

        using var disposalReached = new ManualResetEventSlim(false);
        var disposal = new Thread(() =>
        {
            // Set BEFORE the call, so "the thread never ran" is a different
            // observation from "the disposal returned", and the join below
            // cannot be satisfied by a scheduler that ignored this thread.
            disposalReached.Set();
            held.Dispose();
        })
        {
            IsBackground = true,
            Name = "b4-second-disposal",
        };

        var disposalReturnedMidDelete = false;

        held.ReleaseAndDelete(() =>
        {
            disposal.Start();
            _ = disposalReached.Wait(TestDefaults.InProcessHang);

            // True is the defect: the second disposal ran to completion -- and
            // therefore disposed the gate -- while this one was between its own
            // check and its own release.
            disposalReturnedMidDelete = disposal.Join(StillBlocked);

            TreeDelete.Remove(path.FullPath, []);
        });

        // Unbounded would be a hang; this is the suite's in-process hang
        // detector and nothing asserts on it beyond "it finished".
        await Assert.That(disposal.Join(TestDefaults.InProcessHang)).IsTrue();

        await Assert.That(disposalReturnedMidDelete).IsFalse()
            .Because(
                "a disposal that runs while browserai_destroy's release-and-delete is between its own check and its own "
                + "release disposes the gate underneath it -- after which the release throws, the handle on browserai.lock "
                + "is never closed, and every later TryAcquire on this directory answers Held naming a pid with no session "
                + "(adversarial review B4)");

        // THE LEAK ITSELF. Both threads are done and the session is released, so
        // nothing may still be holding the guard -- and a stranger's exclusive
        // open is the same question a competing BrowserAI's TryAcquire asks.
        await Assert.That(NothingHoldsTheLockFile(path.LockFile)).IsTrue()
            .Because($"'{path.LockFile}' is still held after the session that owned it was disposed, which is the end state finding B4 describes");
    }

    /// <summary>
    /// A mutation arriving while <c>ReleaseAndDelete</c> is mid-delete waits for
    /// it, rather than racing the disposal it is being torn down by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other disposal path, and the fix is worth nothing if it only
    /// covers one.</b> <c>browserai_destroy</c> reaches <c>Dispose</c> through
    /// <c>LiveSession.DisposeAsync</c> for a session this process is driving and
    /// <c>ReleaseAndDelete</c> for the record it then re-takes, so a per-session
    /// lock that only <c>Dispose</c> takes closes half of B4 and leaves the half
    /// that unlinks the directory.
    /// </para>
    /// <para>
    /// <b>The seam is again the product's own delegate</b> — <c>delete</c> runs
    /// after the handles are closed and before the gate is released — and the
    /// direction of the join is again the whole assertion. Against the defect
    /// the mutation is refused <i>at once</i>, because <c>_disposed</c> was set at
    /// the top of <c>ReleaseAndDelete</c> and the mutation read it without
    /// synchronisation; against the fix it cannot even reach that check until the
    /// disposal has finished. <b>Both end in an
    /// <see cref="ObjectDisposedException"/>, and that is the point:</b> the same
    /// answer arrived at by reading a field another thread is writing is a race
    /// whose <i>other</i> outcome is the leak above. The exclusion is the
    /// property; the refusal is what it then reports.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AMutationArrivingDuringAReleaseAndDeleteWaitsForItRatherThanReadingAFieldItIsWriting()
    {
        using var scratch = ScratchDirectory.Create("session-rewrite-races-destroy");
        var (_, path) = NewSession(scratch, "rewrite-races-destroy");

        var taken = SessionLock.TryAcquire(path, Request("about to be released and deleted"), NullLogger.Instance);
        await Assert.That(taken.Taken).IsTrue();

        var held = taken.Acquired!;

        using var rewriteReached = new ManualResetEventSlim(false);
        Exception? rewriteFailure = null;

        var rewrite = new Thread(() =>
        {
            rewriteReached.Set();

            try
            {
                held.AppendPurpose("a purpose set while the session was being destroyed");
            }
#pragma warning disable CA1031 // Whatever came out is evidence; the assertion below says which failure is the contract.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                rewriteFailure = failure;
            }
        })
        {
            IsBackground = true,
            Name = "b4-set-purpose",
        };

        var rewriteReturnedMidDelete = false;

        held.ReleaseAndDelete(() =>
        {
            rewrite.Start();
            _ = rewriteReached.Wait(TestDefaults.InProcessHang);

            // True is the defect: the mutation reached a decision about a
            // half-torn-down lock, on a field the thread running this delete is
            // in the middle of writing.
            rewriteReturnedMidDelete = rewrite.Join(StillBlocked);

            TreeDelete.Remove(path.FullPath, []);
        });

        await Assert.That(rewrite.Join(TestDefaults.InProcessHang)).IsTrue();

        await Assert.That(rewriteReturnedMidDelete).IsFalse()
            .Because(
                "browserai_destroy's release-and-delete and browserai_set_purpose's rewrite are the two halves of "
                + "adversarial review B4, and a rewrite that answers while the delete is still running answered by "
                + "reading _disposed unsynchronised -- the same read whose other outcome leaks the handle");

        // What it reports once it is allowed to look: the session is gone.
        await Assert.That(rewriteFailure).IsTypeOf<ObjectDisposedException>();
    }

    /// <summary>
    /// The hold this class takes after its own write waits a transient handle
    /// out, and a handle nothing releases is still reported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The handle it waits out is one the product itself opens and cannot
    /// stop opening.</b> To be refused by a holder's <c>FileShare.Read</c> a
    /// liveness probe must ask for access outside <c>Read</c>, and a handle
    /// whose granted access is outside <c>Read</c> is exactly what an open
    /// sharing only <c>Read</c> is refused by — so detecting an owner and
    /// blocking one are the same capability, and no share mode dissolves it.
    /// <c>browserai_list</c> opens exactly that handle, for the length of one
    /// <c>FileStream</c> construction, once per session on the machine.
    /// </para>
    /// <para>
    /// <b>Observed rather than predicted: CI run 32203064556 attempt 1.</b> A
    /// contender wrote its record, was refused on the re-open by a peer's
    /// pre-gate probe, and answered that it had not taken the directory; a second
    /// contender reclaimed the same directory 61 ms later, and the record carried
    /// two processes' holder statements.
    /// </para>
    /// <para>
    /// <b>The licence for waiting is a precondition, not a guess.</b> The caller
    /// holds the per-directory gate and the file on disk names this process, so
    /// no second owner can exist — becoming one means passing through the gate.
    /// An ownership test has no such precondition and must never wait, which is
    /// what <see cref="TheOnlyOpenThatWaitsAHandleOutIsTheOneThatFollowsOurOwnWrite"/>
    /// holds.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATransientHandleIsWaitedOutByTheHoldAndAPermanentOneIsStillReported()
    {
        using var scratch = ScratchDirectory.Create("session-transient");
        var (_, path) = NewSession(scratch, "transient");

        // A real guard, written by the product, then released -- so the file on
        // disk is exactly what a hold meets.
        SessionLock.TryAcquire(path, Request("the guard the hold is for"), NullLogger.Instance).Acquired!.Dispose();

        var holder = LockFileHolder.ForThisProcess();

        // ⚠️ THE WINDOW IS BETWEEN THE WRITE AND THE HOLD, AND ONLY THERE. A
        // probe handle taken BEFORE the rename is harmless: it shares Delete, so
        // the rename succeeds and leaves that handle pointing at a file with no
        // name while the new one has no handles at all. What cannot be dodged is
        // a probe that lands in the instant after this process's own rename and
        // before its own hold -- so the guard is written first, and the handle
        // is taken on the file that is now on disk.
        LockFile.Write(path.LockFile, holder);

        // The liveness probe's open, mode for mode: ReadWrite access so a holder
        // refuses it, ReadWrite | Delete sharing so it does not refuse a
        // concurrent rename. The granted ReadWrite is what refuses a holder's
        // FileShare.Read open, and no share mode can take that away.
        var transient = new FileStream(path.LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);

        try
        {
            // The mechanism, asserted before the behaviour that absorbs it: while
            // that handle lives, a holder's own open really is refused.
            _ = Assert.Throws<IOException>(() =>
            {
                using var refused = new FileStream(path.LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
            });

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var take = Task.Run(() =>
            {
                entered.SetResult();
                return RenameWindow.WaitOutWhereNoOwnerIsPossible(() => LockFile.Hold(path.LockFile));
            });

            await entered.Task;

            var blocked = await Task.WhenAny(take, Task.Delay(StillBlocked));

            await Assert.That(blocked).IsNotEqualTo((Task)take).Because(
                "the hold taken after this process's own write must wait a transient handle out rather than answer it: "
                + "under the per-directory gate nothing else can own the file, so a sharing violation is a peer looking, "
                + "and answering it hands the directory to whichever contender arrives next");

            // Released, and the wait clears -- which is the whole property. It is
            // released here rather than on a timer so that nothing in this test
            // depends on how long a machine takes.
            await transient.DisposeAsync();

            using var held = await take.WaitAsync(TestDefaults.InProcessHang);
            await Assert.That(held.IsHeld).IsTrue();
        }
        finally
        {
            await transient.DisposeAsync();
        }

        // The other arm. A handle nothing releases is not a peer passing over the
        // file, and the wait is bounded so that it is still reported.
        using var permanent = new FileStream(path.LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);

        var reported = Assert.Throws<IOException>(() =>
            RenameWindow.WaitOutWhereNoOwnerIsPossible(() => LockFile.Hold(path.LockFile)).Dispose());

        await Assert.That(RenameWindow.IsSharingViolation(reported!)).IsTrue().Because(reported!.Message);
    }

    /// <summary>
    /// A request that refuses an existing record is answered <b>under the
    /// gate</b>, changing nothing — and the same directory is still reclaimed by
    /// a request that does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the question is asked twice.</b> `browserai_init`'s own look at
    /// the record is ungated, which it has to be — it runs before the
    /// directory is created — so it can land in the instant in which the guard's
    /// name is unbound while a peer is acquiring the directory, read <see langword="null"/> as
    /// <i>free, proceed</i>, and reach the reclaim below. The reclaim appends a
    /// <c>mode</c> and a <c>browser</c> statement, so what the window costs is a
    /// Firefox session's record gaining a `chromium` statement, or the reverse.
    /// Under the gate the record has already been read and a peer replacing one
    /// is holding this gate, so the same question cannot be asked at the wrong
    /// instant.
    /// </para>
    /// <para>
    /// <b>Both arms, because a refusal that refuses everything is not a fix.</b>
    /// `resume`, `destroy` and `set_purpose` all take a directory that already
    /// has a record and must keep doing so; only `init` sets the flag.
    /// </para>
    /// <para>
    /// <b>And the record is read back.</b> "Nothing was taken" is the cheap half;
    /// the half that matters is that nothing was <i>written</i>, because the
    /// defect this closes is a rebinding rather than a bad acquisition.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARequestThatRefusesAnExistingRecordIsAnsweredUnderTheGateAndChangesNothing()
    {
        using var scratch = ScratchDirectory.Create("session-already");
        var (_, path) = NewSession(scratch, "already");

        var first = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Browser = "firefox", Purpose = "the session that is already here" },
            NullLogger.Instance);

        first.Acquired!.Dispose();

        var before = await File.ReadAllTextAsync(path.LockFile);

        var refused = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Browser = "chromium", Purpose = "an init that should have been a resume", RefuseAnExistingRecord = true },
            NullLogger.Instance);

        try
        {
            await Assert.That(refused.Outcome).IsEqualTo(SessionLockOutcome.AlreadyASession);
            await Assert.That(refused.Taken).IsFalse();
            await Assert.That(refused.Holder!.Browser).IsEqualTo("firefox");
            await Assert.That(refused.Message).Contains("the session that is already here");
        }
        finally
        {
            refused.Acquired?.Dispose();
        }

        // Byte for byte. A refusal that appended a `browser` statement would
        // have rebound a Firefox session to Chromium and still passed every
        // assertion above.
        await Assert.That(await File.ReadAllTextAsync(path.LockFile)).IsEqualTo(before);

        // The other arm: without the flag the same directory is reclaimed, which
        // is what `resume`, `destroy` and `set_purpose` all depend on.
        var reclaimed = SessionLock.TryAcquire(path, Request("a resume"), NullLogger.Instance);

        try
        {
            await Assert.That(reclaimed.Outcome).IsEqualTo(SessionLockOutcome.Reclaimed);
            await Assert.That(reclaimed.Taken).IsTrue();
        }
        finally
        {
            reclaimed.Acquired?.Dispose();
        }
    }

    /// <summary>
    /// The only open in the product that waits a sharing violation out is the
    /// one that follows this process's own write of the guard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An invariant, because the interleaving cannot be reproduced.</b> The
    /// behaviour of the tolerant open is measured by the test above; what nothing
    /// else can hold is <i>which</i> opens use it, because reaching a post-write
    /// hold with a peer's handle really open needs a photo finish this suite will
    /// not reliably win. So this reads the files as text, which is what
    /// <c>ProcessLogTests.EveryTimedWaitForExitIsFollowedByABareOne</c> does for
    /// the same class of pairing.
    /// </para>
    /// <para>
    /// <b>Getting it backwards in either direction is a defect.</b> An ownership
    /// test routed through the tolerant open waits a <i>live owner</i> out for
    /// thirty seconds and then treats it as a peer looking — the mechanism
    /// inverted, which is the failure <see cref="RenameWindow"/>'s own table
    /// exists to prevent. A post-write hold routed through the bare open is the
    /// CI failure of 2026-08-19, restored.
    /// </para>
    /// <para>
    /// ⚠️ <b>Retargeted 2026-08-26 (previously <c>OpenHeld</c> against
    /// <c>ReopenHeld</c>, both inside <c>SessionLock</c>).</b> Neither exists:
    /// the guard's open is <c>LockFile.Hold</c>, and there are two call sites
    /// that follow a write — <c>LockFile.TakeAndWrite</c> and
    /// <c>SessionLock.TakeOrReport</c>, which writes and holds separately so that
    /// it can tell <i>nothing was changed</i> from <i>the guard WAS written</i>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheOnlyOpenThatWaitsAHandleOutIsTheOneThatFollowsOurOwnWrite()
    {
        var product = RepositoryLayout.SourceAndScriptFiles
            .Where(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && file.Extension is ".cs")
            .ToList();

        // Not vacuous: a walk that found nothing would report every half clean.
        await Assert.That(product.Count).IsGreaterThan(0);

        var tolerant = new List<string>();
        var bare = new List<string>();

        foreach (var file in product)
        {
            var lines = (await RepositoryLayout.ReadCodeAsync(file)).Split('\n');

            for (var at = 0; at < lines.Length; at++)
            {
                if (lines[at].Contains("WaitOutWhereNoOwnerIsPossible(", StringComparison.Ordinal))
                {
                    // The write it follows: within this method, above this line.
                    var wrote = Enumerable.Range(Math.Max(0, at - 40), Math.Min(40, at))
                        .Any(line => lines[line].Contains("Write(path, holder)", StringComparison.Ordinal)
                            || lines[line].Contains("LockFile.Write(", StringComparison.Ordinal));

                    tolerant.Add($"{file.Name}:{(at + 1).ToString(CultureInfo.InvariantCulture)}{(wrote ? string.Empty : " — NOT PRECEDED BY A WRITE")}");
                }

                if (lines[at].Contains("LockFile.Hold(", StringComparison.Ordinal)
                    && !lines[at].Contains("WaitOutWhereNoOwnerIsPossible(", StringComparison.Ordinal))
                {
                    bare.Add($"{file.Name}:{(at + 1).ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        // Every tolerant open follows a write of the guard by this process.
        await Assert.That(tolerant.Where(site => site.Contains("NOT PRECEDED", StringComparison.Ordinal)).ToArray())
            .IsEmpty()
            .Because(string.Join(" | ", tolerant));

        // Two of them, and no more: LockFile.TakeAndWrite's, and the one
        // TakeOrReport takes itself so that it can report the two failures
        // apart.
        await Assert.That(tolerant.Count).IsEqualTo(2).Because(string.Join(" | ", tolerant));

        // And the ownership tests are bare. TryHoldUnowned's is the one that may
        // meet a real owner and must answer rather than wait.
        await Assert.That(bare.Count).IsGreaterThanOrEqualTo(1).Because(string.Join(" | ", bare));
    }

    [Test]
    public async Task EveryLogRecordWrittenWhileTheLockIsHeldCarriesTheSession()
    {
        using var logs = ScratchDirectory.Create("session-log-scope");
        using var scratch = ScratchDirectory.Create("session-log-scope-dir");
        var (_, path) = NewSession(scratch, "logged");

        using (var log = ProcessLog.Create(new LocalAppDataPaths(logs.Path), LogLevel.Trace))
        {
            var logger = log.Factory.CreateLogger("BrowserAI.Tests");
            var result = SessionLock.TryAcquire(path, Request("watched"), NullLogger.Instance);
            result.Acquired?.Dispose();

            // Taken again with the real logger, so that both the acquire record
            // and a record written by unrelated code inside the scope are
            // covered.
            var watched = SessionLock.TryAcquire(path, Request("watched"), logger);

            try
            {
                await Assert.That(watched.Taken).IsTrue();

                // ILogger.Log directly rather than LogInformation: the
                // extension method is what CA1848 objects to, and a
                // [LoggerMessage] here would need the generator in a project
                // that only references the abstractions transitively. What is
                // under test is the scope, not the call shape.
                logger.Log(LogLevel.Information, default, "something a session did", null, static (state, _) => state);
            }
            finally
            {
                watched.Acquired?.Dispose();
            }
        }

        var text = ProbeProcess.ReadProcessLog(logs.Path);
        var scope = $"{{session={path.FullPath}}}";

        foreach (var line in text.Split('\n').Where(line => line.Contains("something a session did", StringComparison.Ordinal)
            || line.Contains("Session lock reclaimed", StringComparison.Ordinal)
            || line.Contains("Session lock released", StringComparison.Ordinal)))
        {
            await Assert.That(line).Contains(scope);

            // Exactly once. Two providers share one external scope provider and
            // the composite logger calls BeginScope on each of them, so a naive
            // wiring pushes the same scope twice and every record carries it
            // twice. Counting is what tells the two apart.
            await Assert.That(line.Split(scope, StringSplitOptions.None).Length).IsEqualTo(2);
        }

        await Assert.That(text).Contains("something a session did");
        await Assert.That(text).Contains("Session lock released");
    }

    /// <summary>
    /// Every contender's own account of the race, plus what the host can see on
    /// disk while the winner is still holding.
    /// </summary>
    /// <remarks>
    /// <b>The whole set, never the failing member.</b> "Exactly one acquires and
    /// every other is told who" is a statement about all sixteen at once, and a
    /// message naming one of them cannot say whether the other fifteen agreed. It
    /// is rendered once and attached to every assertion in the race, so whichever
    /// one trips carries the same complete picture.
    /// </remarks>
    /// <param name="outcomes">What each contender wrote.</param>
    /// <param name="path">The session every one of them was racing for.</param>
    /// <returns>The dossier, for a failure message.</returns>
    private static string Dossier(IReadOnlyList<JsonNode> outcomes, SessionPath path)
    {
        var lines = new StringBuilder();

        _ = lines.Append(CultureInfo.InvariantCulture, $"{Contenders} contenders raced '{path.FullPath}'. What each one reported:");

        foreach (var report in outcomes.OrderByDescending(report => (double)report["elapsedMilliseconds"]!))
        {
            _ = lines.AppendLine().Append(CultureInfo.InvariantCulture,
                $"  pid {report["pid"]} (created {report["createdFileTime"]}): outcome={report["outcome"]} taken={report["taken"]} "
                + $"holderPid={report["holderPid"]} holderCreated={report["holderCreatedFileTime"]} holderRunning={report["holderRunning"]} "
                + $"gateAbandoned={report["gateWasAbandoned"]} elapsed={report["elapsedMilliseconds"]}ms of a {report["gateTimeoutMilliseconds"]}ms gate");

            if (report["failure"] is not null)
            {
                _ = lines.AppendLine().Append(CultureInfo.InvariantCulture, $"    THREW: {report["failure"]}");
            }

            _ = lines.AppendLine().Append(CultureInfo.InvariantCulture, $"    said: {report["message"]}");
            _ = lines.AppendLine().Append(CultureInfo.InvariantCulture, $"    saw on disk: {report["lockFile"]?.ToJsonString()}");
        }

        // The host's own look, taken while the winner still holds: a contender
        // describing the file it could not read is one witness, and this is the
        // second.
        _ = lines.AppendLine().Append(CultureInfo.InvariantCulture, $"The host, with the winner still holding, reads: {StateOfTheLockFile(path.LockFile).Description}");

        return lines.ToString();
    }

    /// <summary>
    /// Where <paramref name="call"/> is <b>called</b> in a source file, never
    /// where it is declared.
    /// </summary>
    /// <param name="source">The file, as text.</param>
    /// <param name="call">The call, with its opening parenthesis.</param>
    /// <param name="declaration">The tail of the declaration, so it can be excluded.</param>
    /// <returns>The offset of every call site, in file order.</returns>
    private static List<int> CallSites(string source, string call, string declaration)
    {
        var sites = new List<int>();

        for (var at = source.IndexOf(call, StringComparison.Ordinal); at >= 0; at = source.IndexOf(call, at + 1, StringComparison.Ordinal))
        {
            var from = at - (declaration.Length - call.Length);

            if (from < 0 || !source.AsSpan(from, declaration.Length).SequenceEqual(declaration))
            {
                sites.Add(at);
            }
        }

        return sites;
    }

    /// <summary>The trimmed line an offset falls on, for a failure message.</summary>
    /// <param name="source">The file, as text.</param>
    /// <param name="at">The offset, or <c>-1</c> for nothing.</param>
    /// <returns>The line.</returns>
    private static string LineAt(string source, int at)
    {
        if (at < 0)
        {
            return "nothing";
        }

        var start = source.LastIndexOf('\n', at) + 1;
        var end = source.IndexOf('\n', at);

        return source[start..(end < 0 ? source.Length : end)].Trim();
    }

    private static bool IsProductCode(FileInfo file) =>
        file.Extension is ".cs"
        && file.FullName.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// Whether nothing at all holds <c>browserai.lock</c>, asked the way a
    /// competing BrowserAI asks it.
    /// </summary>
    /// <remarks>
    /// <b>An exclusive open rather than a question put to the object under
    /// test.</b> Asking the lock whether it still holds the directory would be
    /// asking the thing under test; the kernel refuses this open while — and only
    /// while — a handle is really there. A file that is not there at all counts
    /// as unheld: a destroy that unlinked it took the handle with it, which is
    /// the outcome rather than the defect.
    /// </remarks>
    /// <param name="lockFile">The session's <c>browserai.lock</c>.</param>
    /// <returns>Whether it could be opened with no sharing at all.</returns>
    private static bool NothingHoldsTheLockFile(string lockFile)
    {
        if (!File.Exists(lockFile))
        {
            return true;
        }

        try
        {
            using var exclusive = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a lock file while its holder still has it open, which needs the
    /// holder's own write access shared back.
    /// </summary>
    /// <param name="lockFile">The lock file.</param>
    /// <returns>Its bytes, as text.</returns>
    private static string ReadBesideTheHolder(string lockFile)
    {
        using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static SessionLockRequest Request(string purpose) =>
        new() { Browser = "chromium", Purpose = purpose };

    private static (string Directory, SessionPath Path) NewSession(ScratchDirectory scratch, string name)
    {
        var directory = Path.Combine(scratch.Path, name);
        var path = SessionPath.For(directory);
        SessionLayout.Create(path);

        return (directory, path);
    }

    private static async Task KillAGateHolderAsync(string directory, string ready)
    {
        int pid;
        long created;

        using (var scope = new JobObjectScope())
        {
            _ = scope.Launch(ProbePath, AppContext.BaseDirectory, "session-hold-gate", directory, ready);

            var report = await ProbeReport.ReadAsync(ready, Patience);

            if ((string?)report["acquisition"] is not nameof(MutexAcquisition.Acquired))
            {
                throw new InvalidOperationException($"The gate probe reported '{report["acquisition"]}' rather than acquiring.");
            }

            pid = (int)report["pid"]!;
            created = ProcessIdentity.CreationTimeOf(pid);

            // A mutex is abandoned when the thread owning it dies without
            // releasing. Nothing short of a real process death produces that.
            ProcessIdentity.Terminate(pid, created);
        }

        await WaitUntilGoneAsync(pid, created);
    }

    private static async Task WaitUntilGoneAsync(int processId, long createdFileTime)
    {
        var clock = Stopwatch.StartNew();

        while (ProcessIdentity.IsAlive(processId, createdFileTime) && clock.Elapsed < Patience)
        {
            await Task.Delay(20);
        }

        if (ProcessIdentity.IsAlive(processId, createdFileTime))
        {
            throw new TimeoutException($"Process {processId} was still running {Patience} after being terminated.");
        }
    }

    /// <summary>
    /// What the lock file looks like at the instant a read of it produced no
    /// record.
    /// </summary>
    /// <remarks>
    /// <b>Three conditions produce a null and this is what tells them apart.</b>
    /// A name that does not resolve is a reader seeing an owned session as
    /// unowned; a zero-length file is an atomic rename that was not atomic; a
    /// file that reads perfectly well a microsecond later is the rename window,
    /// which is the only one of the three that is Windows rather than a defect.
    /// The temp count is the decisive half: the writer creates
    /// <c>browserai.lock.new-&lt;guid&gt;</c> in this directory and it exists for
    /// exactly the length of one acquisition, so its presence is direct evidence
    /// that a rename was in flight at this instant rather than an inference
    /// about how wide a window is. <i>(Corrected 2026-08-26, previously
    /// "<c>browserai.json.new-&lt;guid&gt;</c> … the length of one rewrite" —
    /// there are no rewrites left, so the one rename is acquisition's.)</i>
    /// </remarks>
    /// <param name="lockFile">The lock file that read as no record.</param>
    /// <returns>What the machine says, for the decision and for the message.</returns>
    private static LockFileState StateOfTheLockFile(string lockFile)
    {
        var exists = File.Exists(lockFile);
        var length = -1L;

        try
        {
            if (exists)
            {
                length = new FileInfo(lockFile).Length;
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            length = -2;
        }

        var directory = Path.GetDirectoryName(lockFile)!;
        int temps;

        try
        {
            temps = Directory.EnumerateFiles(directory, $"{SessionLayout.LockFileName}.new-*").Count();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            temps = -1;
        }

        var reread = SessionLock.ReadRecord(SessionPath.For(directory)) is not null;

        return new LockFileState(
            temps > 0,
            reread,
            $"name resolves: {exists}; length: {length.ToString(CultureInfo.InvariantCulture)}; "
            + $"directory exists: {Directory.Exists(directory)}; rewrite temps present: {temps.ToString(CultureInfo.InvariantCulture)}; "
            + $"immediate re-read found a record: {reread}");
    }

    /// <summary>
    /// What the lock file looked like at the instant a read of it produced no
    /// record.
    /// </summary>
    /// <param name="RewriteInFlight">
    /// The writer's own <c>browserai.lock.new-&lt;guid&gt;</c> is on disk, so an
    /// acquisition was demonstrably in progress at that instant. Direct evidence
    /// rather than an inference about how wide a window is.
    /// </param>
    /// <param name="RereadFoundARecord">The record was there on the next read.</param>
    /// <param name="Description">All of it, for the failure message.</param>
    private readonly record struct LockFileState(bool RewriteInFlight, bool RereadFoundARecord, string Description);

    /// <summary>
    /// Renames a file over another, waiting out whatever outside this repository
    /// is briefly holding the destination.
    /// </summary>
    /// <remarks>
    /// <b>The bound is a hang detector and nothing asserts on it.</b> Every
    /// observed occurrence of this transient cleared on the first retry; what the
    /// budget is there for is a destination that is held for good, which is a
    /// different failure and gets a message that says so.
    /// </remarks>
    /// <param name="from">The file to rename.</param>
    /// <param name="to">What to rename it over.</param>
    /// <returns>The rename.</returns>
    private static async Task MoveOnceNothingElseHoldsItAsync(string from, string to)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                File.Move(from, to, overwrite: true);
                return;
            }
            catch (Exception failure) when (failure is UnauthorizedAccessException or IOException)
            {
                if (waited.Elapsed > TestDefaults.ProcessHang)
                {
                    throw new InvalidOperationException(
                        $"'{to}' could not be replaced by a rename in {TestDefaults.ProcessHang.TotalMinutes:F0} minutes, and this process closed its own handle to it before the first attempt. Something outside this repository is holding it permanently.",
                        failure);
                }

                await Task.Delay(10);
            }
        }
    }

    private static async Task WaitForAllAsync(IEnumerable<string> paths)
    {
        var wanted = paths.ToList();
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < Patience && wanted.Exists(path => !File.Exists(path)))
        {
            await Task.Delay(10);
        }

        var missing = wanted.Where(path => !File.Exists(path)).ToList();

        if (missing.Count is not 0)
        {
            throw new TimeoutException($"{missing.Count} of {wanted.Count} probes never reported ready within {Patience}.");
        }
    }
}
