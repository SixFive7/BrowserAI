// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Sessions;
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
    /// How long <see cref="AProbeThatFindsTheDirectoryFreeStillProvesItAtTheGate"/>
    /// watches a call that must not return.
    /// </summary>
    /// <remarks>
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
        await Assert.That(record!.Holder.ProcessId).IsEqualTo(winner);
        await Assert.That(Directory.GetFiles(directory).Length).IsEqualTo(1);
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
    /// alone. The sharing violation on <c>lock.json</c> already proves ownership,
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
            await Assert.That(refused.Holder?.Holder.ProcessId).IsEqualTo(holderPid);
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
    /// lands, and <b>B holds <c>lock.json</c> while A holds a valid handle to a
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

        // No lock.json at all, so the probe's open fails with "not found" rather
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
            await Assert.That(reclaimed.Acquired.Record.PurposeHistory[0]).IsEqualTo("reading the customer portal");
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

        foreach (var skipped in outcomes.Where(report => (string?)report["acquisition"] == nameof(MutexAcquisition.NotAcquired)))
        {
            // Zero timeout means zero. Anything that queued would show here as
            // the time the winner held it.
            await Assert.That((double)skipped["elapsedMilliseconds"]!).IsLessThan(1000);
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

            var record = SessionLock.ReadRecord(path);
            await Assert.That(record!.Holder.ProcessId).IsEqualTo(Environment.ProcessId);
            await Assert.That(record.Purpose).IsEqualTo("the first");
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

    [Test]
    public async Task ARewriteIsNeverObservedTorn()
    {
        using var scratch = ScratchDirectory.Create("session-rewrite");
        var (directory, path) = NewSession(scratch, "rewrite");

        var ready = Path.Combine(scratch.Path, "ready.json");
        var done = Path.Combine(scratch.Path, "done.json");

        // A durable write costs a real disk round trip: 16.1 and 18.2 ms per
        // rewrite, measured twice on this machine on 2026-08-16 with no reader
        // in the way. A hundred of them is a couple of seconds on an idle
        // machine and comfortably inside the patience below when the rest of the
        // suite is running beside it.
        const int Rewrites = 100;

        var damaged = new List<string>();
        var reads = 0;

        // ⚠️ A NULL RECORD IS DAMAGE UNLESS THE MACHINE SAYS A REWRITE WAS IN
        // FLIGHT. That is not a tolerance with a width; it is a discriminator
        // with evidence behind it, and getting here took two wrong attempts.
        //
        // ⚠️⚠️ THE MEASUREMENT, 2026-08-18, and it is why this is not the guess
        // the two attempts before it were. At the instant of a null, with the
        // suite at `SuiteParallelism.Unbounded`:
        //
        //     name resolves:                    False
        //     directory exists:                 True
        //     rewrite temps present:            1
        //     immediate re-read found a record: True
        //
        // The name was genuinely UNBOUND -- not a zero-length file, not a
        // missing directory -- while the writer's own `lock.json.new-<guid>` was
        // on disk, which is direct evidence that `WriteDurably` was mid-rewrite,
        // and the record was back on the next read. So `MoveFileEx` with
        // `MOVEFILE_REPLACE_EXISTING` does NOT keep the name bound throughout on
        // this machine: a reader can see the name vanish and come back.
        //
        // ⚠️ The two attempts this replaces were both unmeasured timing
        // assumptions, written into the change set whose entire purpose is
        // removing them. The first asserted the absence could not happen at all;
        // the second tolerated it only if an immediate re-read succeeded, on the
        // reasoning that "a rename window is one syscall wide and cannot survive
        // a second read". Streak run 3 falsified that: absent on two consecutive
        // reads. The width of that window is not a thing this test may assume,
        // and it no longer does -- it asks whether a rewrite was in flight.
        //
        // `SessionLock.ReadRecord` returns null for THREE conditions it does not
        // distinguish, and they are not equally serious:
        //
        //   1. the name is genuinely unbound       -- FileNotFoundException
        //   2. the directory is gone               -- DirectoryNotFoundException
        //   3. the file exists and is ZERO-LENGTH  -- `Parse` returns null,
        //      deliberately, because nothing this product writes can produce one
        //
        // (1) observed while a rewrite is in flight would say a reader can see a
        // session as UNOWNED while another process owns it, which is the
        // precondition for two BrowserAI processes driving one directory and is
        // the single thing this file's locking exists to prevent. (3) would say
        // the atomic rename is not atomic. They need completely different fixes
        // and the old message named neither.
        //
        // So every null is probed on the spot -- does the name resolve, how long
        // is the file, is a rewrite in flight (the writer's own
        // `lock.json.new-<guid>` temp is present for exactly the length of one),
        // and does an immediate re-read succeed -- and the probe goes in the
        // failure. The next occurrence answers the question instead of
        // re-opening it.
        var absences = new List<string>();

        // Every distinct record the reader actually saw. This, not a read
        // count, is what proves the reader was looking WHILE the rewriter was
        // writing: two different purposes cannot both be observed unless the
        // file changed under the reader.
        var observed = new HashSet<string>(StringComparer.Ordinal);

        using (var scope = new JobObjectScope())
        {
            _ = scope.Launch(
                ProbePath,
                AppContext.BaseDirectory,
                "session-rewrite",
                directory,
                ready,
                Rewrites.ToString(CultureInfo.InvariantCulture),
                done);

            var report = await ProbeReport.ReadAsync(ready, Patience);
            await Assert.That((bool)report["taken"]!).IsTrue();

            var clock = Stopwatch.StartNew();

            // The handshake is released from INSIDE the loop, after a read has
            // demonstrably happened.
            //
            // It used to be written just before the loop, and that guarded the
            // wrong side: `WriteAllTextAsync` yields, and under the parallel
            // browser tests added at step 17a the continuation can be
            // descheduled past the rewriter's entire ~1.7 s of work. The loop
            // then found `done` already set and read nothing -- observed
            // 2026-08-16, twice in a row, `reads` at zero. The comment above it
            // named that exact failure and the fix did not prevent it, because
            // saying "go" before you are looking is not a handshake.
            var released = false;

            while (!File.Exists(done) && clock.Elapsed < Patience)
            {
                try
                {
                    var record = SessionLock.ReadRecord(path);
                    reads++;

                    if (record is null)
                    {
                        // Probed BEFORE anything else runs, so the state
                        // recorded is as close to the moment of the null as this
                        // process can get.
                        var state = StateOfTheLockFile(path.LockFile);
                        absences.Add(state.Description);

                        // ⚠️ DAMAGE UNLESS THE EVIDENCE SAYS OTHERWISE, and both
                        // exits are evidence rather than a duration.
                        //
                        // A rewrite demonstrably in flight -- the writer's own
                        // temp is on disk -- or the record demonstrably back on
                        // the next read. Either one identifies the rename window
                        // measured below. Neither is a guess about how wide it
                        // is, which is what the previous two attempts at this
                        // line both were.
                        //
                        // What still fails, and it is the case that matters: a
                        // null with NO rewrite in flight and NO record on the
                        // re-read. That is a reader seeing an owned session as
                        // unowned while nobody is writing -- the file lost, or
                        // emptied -- which is the precondition for two BrowserAI
                        // processes driving one directory.
                        if (!state.RewriteInFlight && !state.RereadFoundARecord)
                        {
                            damaged.Add($"the lock file read as no record with nothing rewriting it — {state.Description}");
                        }
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

            // A rewriter that died is invisible otherwise: its streams are
            // drained into the void, so the failure presents as the host waiting
            // out its own patience and naming the wrong thing.
            await Assert.That((string?)finished["failure"]).IsNull();
            await Assert.That((int?)finished["rewrites"]).IsEqualTo(Rewrites);
        }

        // A rename replaces the directory entry in one step, so a reader sees
        // the old record or the new one and never half of either. That is the
        // property the superseded "retry a torn read once" scheme could only
        // approximate.
        await Assert.That(string.Join(Environment.NewLine, damaged.Distinct(StringComparer.Ordinal))).IsEmpty();

        // And it is still there afterwards, readable, with the rewriter finished
        // and nothing renaming anything — so a run that somehow passed the line
        // above while having actually lost the file still fails here.
        await Assert.That(SessionLock.ReadRecord(path)).IsNotNull()
            .Because($"the reader read no record {absences.Count.ToString(CultureInfo.InvariantCulture)} time(s) during the rewrite, and the file has not come back");

        // The reader has to have been looking, and looking WHILE the rewriter
        // wrote, or "never torn" is a claim about an empty observation.
        //
        // Asserted on distinct records rather than on a read count, and the
        // difference is the point: a count is a proxy for overlap that a loaded
        // machine can defeat without the property being false, which is how the
        // previous `reads > 25` failed at `reads == 0`. Two different purposes
        // cannot both be seen unless the file changed under the reader, so this
        // asserts the overlap itself. The read count stays as a floor, because
        // zero reads must still be a failure rather than a vacuous pass.
        await Assert.That(reads).IsGreaterThan(0);
        await Assert.That(observed.Count).IsGreaterThan(1);
    }

    [Test]
    public async Task ADirectoryThatDoesNotExistIsRefusedRatherThanCreated()
    {
        using var scratch = ScratchDirectory.Create("session-missing");
        var path = SessionPath.Resolve(Path.Combine(scratch.Path, "never-created"));

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
        await File.WriteAllTextAsync(path.LockFile, original.Replace(@"""purpose""", @"""purpse""", StringComparison.Ordinal));

        var refused = SessionLock.TryAcquire(path, Request("after the edit"), NullLogger.Instance);

        try
        {
            await Assert.That(refused.Outcome).IsEqualTo(SessionLockOutcome.Unreadable);
            await Assert.That(refused.Taken).IsFalse();
            await Assert.That(refused.Message).Contains("purpse");
            await Assert.That(refused.Message).Contains("Recovery:");
            await Assert.That(refused.Message).Contains("Repeating the call that just failed will fail identically.");
        }
        finally
        {
            refused.Acquired?.Dispose();
        }

        // The refusal changed nothing, which is what makes the recovery the
        // caller is offered actually available.
        await Assert.That(await File.ReadAllTextAsync(path.LockFile)).Contains("purpse");
        await Assert.That(Directory.GetFiles(path.FullPath).Length).IsEqualTo(1);
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

    private static bool IsProductCode(FileInfo file) =>
        file.Extension is ".cs"
        && file.FullName.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static SessionLockRequest Request(string purpose) =>
        new() { Mode = "headless", Browser = "chromium", Purpose = purpose };

    private static (string Directory, SessionPath Path) NewSession(ScratchDirectory scratch, string name)
    {
        var directory = Path.Combine(scratch.Path, name);
        var path = SessionPath.Resolve(directory);
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
    /// <c>lock.json.new-&lt;guid&gt;</c> in this directory and it exists for
    /// exactly the length of one rewrite, so its presence is direct evidence
    /// that a rename was in flight at this instant rather than an inference
    /// about how wide a window is.
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

        var reread = SessionLock.ReadRecord(SessionPath.Resolve(directory)) is not null;

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
    /// The writer's own <c>lock.json.new-&lt;guid&gt;</c> is on disk, so a
    /// rewrite was demonstrably in progress at that instant. Direct evidence
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
