// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The stray sweep, and one test per row of
/// [§C's race table](../../plan/C-sessions.md#race-conditions-and-what-closes-each).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row of that table is a test, and the table is the specification.</b>
/// Each test below names its row. R8's second half — two sweeps in two different
/// <i>logon sessions</i> — is the one property that cannot be produced from a
/// single logon; what is asserted there is the mechanism that makes it correct
/// (a <c>Global\</c> name, and two processes serialising on it), with the
/// untested half named rather than implied.
/// </para>
/// <para>
/// <b>The candidate is a planted copy of the probe, not a browser, and that is a
/// stronger test rather than a weaker one.</b> Detection matches on <i>full
/// image path</i>, so a test that declares the planted copy as "a browser
/// BrowserAI provisioned" exercises the identical code path a real Chromium
/// takes — while making it impossible for a run of this suite to terminate
/// anything a developer or another test owns. The one arm that uses a real
/// browser is the session-0 blindness guard, and it deliberately only
/// <i>finds</i>.
/// </para>
/// <para>
/// <b>These run one at a time.</b> They share one planted image and they take
/// the real machine-wide sweep mutex; two of them at once would be two sweeps
/// racing for the same candidates, which is the product's problem to solve and
/// not the suite's to reproduce accidentally.
/// </para>
/// </remarks>
internal sealed class StraySweepTests
{
    private const string SweepGroup = "stray-sweep";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>
    /// R1, the positive half, and R10.
    /// </summary>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task ASweepEndsABrowserNoSessionHoldsAndLeavesItsProfileExactlyAsItWas()
    {
        using var scratch = ScratchDirectory.Create("sweep-kill");
        using var scope = new JobObjectScope();

        var session = NewSessionDirectory(scratch, "abandoned");
        var profile = Path.Combine(session, SessionLayout.ProfileFolderName);
        var lockFile = Path.Combine(session, SessionLayout.LockFileName);
        var before = await File.ReadAllBytesAsync(lockFile);

        var (pid, created) = await PublishAsync(scope, scratch, profile);
        var result = await SweepAsync();

        await Assert.That(result.Terminated.Select(entry => entry.ProcessId)).Contains(pid);
        await Assert.That(result.Terminated.Single(entry => entry.ProcessId == pid).Directory).IsEqualTo(session);
        await WaitUntilGoneAsync(pid, created);

        // R10, and it is the whole content of that row: the profile is left
        // exactly as it was found. Nothing repairs it, nothing deletes it, and
        // the record that says what the session was for survives the janitor.
        await Assert.That(await File.ReadAllBytesAsync(lockFile)).IsEquivalentTo(before);
        await Assert.That(Directory.Exists(profile)).IsTrue();
    }

    /// <summary>
    /// R1, the half that matters: a live session's browser is never touched.
    /// </summary>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task ASweepLeavesABrowserAloneWhileALiveSessionHoldsItsDirectory()
    {
        using var scratch = ScratchDirectory.Create("sweep-owned");
        using var scope = new JobObjectScope();

        var session = NewSessionDirectory(scratch, "owned");
        var profile = Path.Combine(session, SessionLayout.ProfileFolderName);
        var (pid, created) = await PublishAsync(scope, scratch, profile);

        var held = SessionLock.TryAcquire(
            SessionPath.Resolve(session),
            new SessionLockRequest { Mode = "headless", Browser = "chromium", Purpose = "driving a checkout flow" },
            NullLogger.Instance);

        await Assert.That(held.Taken).IsTrue();

        using (held.Acquired)
        {
            var refused = await SweepAsync();

            await Assert.That(refused.Terminated).IsEmpty();
            await Assert.That(refused.Spared.Single(entry => entry.ProcessId == pid).Why).Contains("held by a live session");
            await Assert.That(ProcessIdentity.IsAlive(pid, created)).IsTrue();
        }

        // AND THE SAME SWEEP ENDS IT ONCE NOTHING HOLDS THE DIRECTORY. Without
        // this half, a sweep that never terminated anything at all would pass
        // the assertions above.
        var second = await SweepAsync();

        await Assert.That(second.Terminated.Select(entry => entry.ProcessId)).Contains(pid);
        await WaitUntilGoneAsync(pid, created);
    }

    /// <summary>R2, the first half: the pid is pinned by a handle held from detection.</summary>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task ACandidatesPidCannotBeRecycledBecauseTheScanNeverLetGoOfItsHandle()
    {
        using var scratch = ScratchDirectory.Create("sweep-pid-pin");
        using var scope = new JobObjectScope();

        var (pid, created) = await PublishAsync(scope, scratch, Path.Combine(scratch.Path, "no-session"));

        using var scan = BrowserProcesses.ScanFor([PlantedProbe.ExecutablePath]);
        var candidate = scan.Candidates.Single(entry => entry.ProcessId == pid);

        await Assert.That(candidate.CreatedFileTime).IsEqualTo(created);
        await Assert.That(candidate.IsStillTheProcessThatWasFound()).IsTrue();

        // Killed from outside, the way a session limit or Task Manager would.
        ProcessIdentity.Terminate(pid, created);
        await WaitUntilGoneAsync(pid, created);

        // The process object outlives the process because the scan is still
        // holding a handle to it, so the pid cannot have become a stranger
        // between detection and the decision. That is the whole of R2's first
        // half, and it is why the scan opens the handle at the moment it
        // matches rather than when it comes to act.
        await Assert.That(candidate.IsStillTheProcessThatWasFound()).IsTrue();
    }

    /// <summary>R2, the second half: the identity is re-checked at the last instant.</summary>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task TerminationIsRefusedWhenTheRecordedCreationTimeNoLongerMatches()
    {
        using var scratch = ScratchDirectory.Create("sweep-pid-reuse");
        using var scope = new JobObjectScope();

        var (pid, created) = await PublishAsync(scope, scratch, Path.Combine(scratch.Path, "no-session"));

        // A candidate carrying the right pid and the WRONG creation time, which
        // is exactly the shape a recycled pid produces: the number still opens,
        // and it names somebody else.
        using var owner = Process.GetProcessById(pid);
        using var stale = new StrayCandidate(pid, created + 1, PlantedProbe.ExecutablePath, owner.SafeHandle);

        await Assert.That(stale.IsStillTheProcessThatWasFound()).IsFalse();
        await Assert.That(stale.TryTerminate(out var refusal)).IsFalse();
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!).Contains(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // And the process it would have killed is still running.
        await Assert.That(ProcessIdentity.IsAlive(pid, created)).IsTrue();
    }

    /// <summary>R3, on the sweep mutex rather than on the per-directory gate.</summary>
    /// <remarks>
    /// ⚠️ <b>The order is the test.</b> An abandoned mutex is only observable by
    /// a process that already held a handle when the holder died: open the name
    /// <i>after</i> the kill and the kernel object was already destroyed, so the
    /// acquire is ordinary and the test passes for a build that never handles
    /// the exception at all. The <c>keepAlive</c> handle below is what makes
    /// this measure anything.
    /// </remarks>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task AnAbandonedSweepMutexIsAcquiredAndThePassProceedsAnyway()
    {
        using var scratch = ScratchDirectory.Create("sweep-abandoned");

        // Opened BEFORE the holder dies, and never acquired. Without it the
        // object would be destroyed with the probe and the next Create would
        // make a fresh, unabandoned one.
        using var keepAlive = MachineMutex.Create(LockScopes.Sweep);

        var ready = Path.Combine(scratch.Path, "held.json");
        int holder;
        long holderCreated;

        using (var scope = new JobObjectScope())
        {
            _ = scope.Launch(ProbeExecutable, scratch.Path, "session-hold-named", LockScopes.Sweep, ready);

            var report = await ProbeReport.ReadAsync(ready, Patience);

            await Assert.That((string?)report["acquisition"]).IsNotEqualTo(nameof(MutexAcquisition.NotAcquired));
            holder = (int)report["pid"]!;
            holderCreated = ProcessIdentity.CreationTimeOf(holder);

            // While it holds, a sweep of ours skips -- which is R9's mechanism
            // and, here, the proof that the probe really has the object.
            await Assert.That((await Sweep()).Outcome).IsEqualTo(StraySweepOutcome.Skipped);

            ProcessIdentity.Terminate(holder, holderCreated);
        }

        await WaitUntilGoneAsync(holder, holderCreated);

        var afterwards = await Sweep();

        await Assert.That(afterwards.GateWasAbandoned).IsTrue();
        await Assert.That(afterwards.Outcome).IsEqualTo(StraySweepOutcome.Ran);

        // Consumed: the very next pass is ordinary. Unhandled, the exception
        // would disable sweeping permanently after the first crash and nothing
        // would report it.
        var next = await Sweep();

        await Assert.That(next.GateWasAbandoned).IsFalse();
        await Assert.That(next.Outcome).IsEqualTo(StraySweepOutcome.Ran);
    }

    /// <summary>
    /// R4: two sweepers cannot name two mutexes.
    /// </summary>
    /// <remarks>
    /// <b>Corrected 2026-08-16 (previously
    /// <c>TheSweepMutexIsNamedOnceInTheProductAndTheTaskRunsThatSameProduct</c>,
    /// which also asserted on the logon task's generated XML).</b>
    /// [The logon task is dropped](../../plan/build-order.md#16-the-stray-sweep),
    /// so the second sweeper is no longer a task — it is
    /// <c>BrowserAI.exe --sweep</c>, the measurement entry point
    /// [row 78](../../kb/README.md#re-verification-index) names. The half that
    /// mattered is unchanged and is the half kept: the name is <c>Global\</c>
    /// prefixed and is spelled in exactly one product file, because a second
    /// spelling is how two sweepers come to serialise against nothing while both
    /// report success.
    /// </remarks>
    [Test]
    public async Task TheSweepMutexIsNamedOnceInTheProductAndEveryEntryPointReachesThatName()
    {
        await Assert.That(LockScopes.Sweep).StartsWith(LockScopes.GlobalPrefix);

        var naming = RepositoryLayout.ProductSourceFiles
            .Where(file => File.ReadAllText(file.FullName).Contains("BrowserAI-Sweep", StringComparison.Ordinal))
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(string.Join(", ", naming)).IsEqualTo("LockScopes.cs");

        // Neither entry point carries a name of its own: the background thread
        // and the --sweep pass both build the same StraySweep, which takes the
        // one name above. Asserted on the source rather than by running two
        // processes, because what could drift is a second spelling and that is
        // what the check above already forbids -- this half only proves the two
        // entry points are the same code.
        var program = await File.ReadAllTextAsync(
            Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "Program.cs"));

        await Assert.That(program).DoesNotContain("BrowserAI-Sweep");
        await Assert.That(Regex.Count(program, @"CreateSweep\(paths, ", RegexOptions.None, TimeSpan.FromSeconds(5))).IsEqualTo(2);
    }

    /// <summary>
    /// R5, the session-0 blindness guard: the sweeper finds a browser it
    /// launched itself, in the interactive session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This arm deliberately only finds.</b> A sweep declaring the real
    /// Chromium as ours would be a sweep entitled to act on every other test's
    /// browser, and on a developer's. What is asserted is detection plus
    /// attribution — the two things a session-0 sweeper gets wrong, silently,
    /// by finding nothing at all and reporting success forever.
    /// </para>
    /// <para>
    /// <b>It is also the only arm that proves a real Chromium publishes what the
    /// attribution half reads.</b> Everything else here uses a probe that
    /// registers the class deliberately.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheSweeperFindsARealBrowserItLaunchedItselfInTheInteractiveSession()
    {
        // ⚠️ The gate, not a degraded branch. This is R5's ONLY real-browser
        // arm — the only proof that a real Chromium publishes what attribution
        // reads — and until 2026-08-16 a machine with no provisioned browser ran
        // a one-line directory check here and reported this test as PASSED. A
        // test that reports the same result whether or not it did the thing its
        // name claims is the founding failure class of this project, inside the
        // suite that exists to catch it. The "provisioned but the executable is
        // missing" case the old branch drew is now
        // `CapabilityState.Partial`, which fails in every run.
        SuiteEnvironment.RequireProvisionedChromium();

        var chromium = BrowserAiPaths.ExpectedChromiumExecutable;

        using var scratch = ScratchDirectory.Create("sweep-real-browser");
        using var scope = new JobObjectScope();

        var session = NewSessionDirectory(scratch, "real");
        var profile = Path.Combine(session, SessionLayout.ProfileFolderName);

        // ⚠️ `about:blank` rather than `--no-startup-window`, and that is not
        // cosmetic: with no window and nothing to do, a headless Chromium exits
        // on its own within a second or so, and the attribution loop below then
        // waits out its whole deadline against a browser that has already gone.
        // Observed 2026-08-16 — the test passed alone and failed under a fully
        // parallel suite, which is the shape of every timing bug this project
        // has met.
        var browser = scope.Launch(
            chromium,
            scratch.Path,
            "--headless=new",
            $"--user-data-dir={profile}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-component-update",
            "about:blank");

        var browserCreated = ProcessIdentity.CreationTimeOf(browser.Id);
        var attributed = await WaitForAttributionAsync(chromium, profile, browser.Id, browserCreated);

        // The browser is still running, so what was attributed is a live process
        // rather than the last echo of one that was leaving.
        await Assert.That(ProcessIdentity.IsAlive(browser.Id, browserCreated)).IsTrue();

        // Detection found it -- by full image path, which is our own binary.
        await Assert.That(attributed.Candidates).Contains(browser.Id);

        // And attribution tied that pid to the profile the session owns, which
        // is what a sweeper in session 0 can never do: message windows are
        // scoped to a window station and desktop, so it would walk an empty set
        // and report a clean machine.
        await Assert.That(attributed.Attributed).IsEqualTo(profile);
        await Assert.That(attributed.WindowOwner).IsEqualTo(browser.Id);
    }

    /// <summary>R6: the index is enumerated while entries are being written.</summary>
    [Test]
    public async Task EntriesWrittenWhileTheIndexIsBeingSweptAreNeverLost()
    {
        using var scratch = ScratchDirectory.Create("sweep-index-race");

        var paths = new LocalAppDataPaths(scratch.Path);
        var index = new SessionIndex(paths, NullLogger.Instance);

        var live = Enumerable.Range(0, 8)
            .Select(number => SessionPath.Resolve(NewSessionDirectory(scratch, $"live-{number}")))
            .ToList();

        // The baseline, written before anything sweeps. Without it a writer task
        // that the scheduler had not got round to starting would leave this test
        // asserting against an empty index and failing for the harness's timing
        // rather than for the product's behaviour -- observed on 2026-08-16
        // under a fully parallel suite.
        foreach (var session in live)
        {
            index.Record(session);
        }

        using var stop = new CancellationTokenSource();
        var passes = 0;

        // One side writes, exactly as `init` and `resume` do -- idempotently and
        // without any lock. The other side sweeps, over and over.
        //
        // ⚠️ A dedicated thread rather than Task.Run, and a Yield in the loop.
        // A tight file-I/O loop on a POOL thread starves the pool for as long as
        // it runs, and the suite's in-process rigs -- which answer in single
        // milliseconds and assert a two-second budget -- then fail somewhere
        // else entirely. Measured 2026-08-16: this loop took
        // `FakeChildHarnessTests` from 8 ms to 2.9 s.
        var writer = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var session in live)
                {
                    index.Record(session);
                }

                _ = Interlocked.Increment(ref passes);
                _ = Thread.Yield();
            }
        })
        {
            IsBackground = true,
            Name = "index race writer",
        };

        writer.Start();

        while (Volatile.Read(ref passes) is 0)
        {
            await Task.Delay(5);
        }

        var sweeps = 0;

        while (sweeps < 25)
        {
            var swept = index.Sweep();
            sweeps++;

            // A live session's entry is never removable: it exists, it has a
            // lock.json, and following it lands on a session. The race is only
            // ever about missing one that is about to be re-asserted anyway.
            await Assert.That(swept.Removed.Where(entry => live.Any(session => string.Equals(session.IndexKey, entry.Key, StringComparison.OrdinalIgnoreCase)))).IsEmpty();
        }

        await stop.CancelAsync();
        writer.Join(TimeSpan.FromSeconds(30));

        // Not vacuous: writes really did land while sweeps were running.
        await Assert.That(passes).IsGreaterThan(0);
        await Assert.That(writer.IsAlive).IsFalse();

        var followed = index.Follow();

        foreach (var session in live)
        {
            await Assert.That(followed.Any(entry => string.Equals(entry.Key, session.IndexKey, StringComparison.OrdinalIgnoreCase))).IsTrue();
        }
    }

    /// <summary>R7: a pointer whose directory came back before the delete is kept.</summary>
    /// <remarks>
    /// The race itself — an <c>init</c> landing between the enumeration and the
    /// delete microseconds later — is <b>absorbed rather than prevented</b>, and
    /// both halves are asserted: the re-check that catches the case it can, and
    /// the idempotent re-assert that makes losing one cost a single cycle of
    /// invisibility.
    /// </remarks>
    [Test]
    public async Task APointerWhoseDirectoryCameBackBeforeTheDeleteIsKeptAndOneThatIsLostComesStraightBack()
    {
        using var scratch = ScratchDirectory.Create("sweep-index-recheck");

        var paths = new LocalAppDataPaths(scratch.Path);
        var index = new SessionIndex(paths, NullLogger.Instance);

        var directory = NewSessionDirectory(scratch, "flickering");
        var session = SessionPath.Resolve(directory);
        index.Record(session);

        // The state an enumeration would capture a microsecond before an `init`
        // recreates the directory.
        // TreeDelete, never Directory.Delete(recursive: true): banned
        // repository-wide, and the assertions below depend on the directory
        // actually being gone, so the survivors are asserted.
        await Assert.That(string.Join(Environment.NewLine, ScratchDirectory.RemoveTree(directory))).IsEmpty();
        var stale = index.Follow().Single(entry => string.Equals(entry.Key, session.IndexKey, StringComparison.OrdinalIgnoreCase));

        await Assert.That(stale.State).IsEqualTo(SessionIndexEntryState.DirectoryMissing);
        await Assert.That(stale.IsRemovable).IsTrue();

        // The `init` lands.
        _ = NewSessionDirectory(scratch, "flickering");

        // What Sweep asks immediately before deleting: not what it read a
        // moment ago, but what is true now.
        await Assert.That(SessionIndex.ReFollow(stale).IsRemovable).IsFalse();
        await Assert.That(index.Sweep().Removed).IsEmpty();
        await Assert.That(index.Follow().Any(entry => string.Equals(entry.Key, session.IndexKey, StringComparison.OrdinalIgnoreCase))).IsTrue();

        // The other half: an entry that IS lost costs one cycle of invisibility
        // and no more, because every `init` and every `resume` re-asserts it.
        await Assert.That(string.Join(Environment.NewLine, ScratchDirectory.RemoveTree(directory))).IsEmpty();
        await Assert.That(index.Sweep().Removed.Count).IsEqualTo(1);

        _ = NewSessionDirectory(scratch, "flickering");
        index.Record(session);

        await Assert.That(index.Follow().Any(entry => string.Equals(entry.Key, session.IndexKey, StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    /// <summary>
    /// R8: two sweeps that can see each other, serialised by a machine-wide name.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The half this cannot produce, named rather than implied.</b> R8 is
    /// about two sweeps in two different <i>terminal-server logon sessions</i>,
    /// and a suite running in one logon cannot create a second. What is asserted
    /// is the mechanism that makes that case correct: the name is
    /// <c>Global\</c>-prefixed — so it is one kernel object across every logon
    /// session rather than one per session — and two processes contending for it
    /// really do serialise, with one acquiring and the other refused
    /// immediately. A <c>Local\</c> prefix would pass neither.
    /// </remarks>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task TwoSweepersContendingForOneMachineWideNameSerialiseRatherThanBothRunning()
    {
        using var scratch = ScratchDirectory.Create("sweep-two-sweepers");

        await Assert.That(LockScopes.Sweep).StartsWith(@"Global\");
        await Assert.That(LockScopes.Sweep).DoesNotContain(@"Local\");

        var ready = Path.Combine(scratch.Path, "held.json");
        using var scope = new JobObjectScope();

        _ = scope.Launch(ProbeExecutable, scratch.Path, "session-hold-named", LockScopes.Sweep, ready);
        var report = await ProbeReport.ReadAsync(ready, Patience);

        await Assert.That((string?)report["acquisition"]).IsNotEqualTo(nameof(MutexAcquisition.NotAcquired));
        await Assert.That((string?)report["mutexName"]).IsEqualTo(LockScopes.Sweep);

        // A second sweeper, in this process, sees the first one's object.
        var mine = await Sweep();

        await Assert.That(mine.Outcome).IsEqualTo(StraySweepOutcome.Skipped);
    }

    /// <summary>R9: a pass that is already running is skipped, never queued.</summary>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task ASweepAlreadyRunningMakesTheNextOneDoNothingAtAllRatherThanWait()
    {
        using var scratch = ScratchDirectory.Create("sweep-skip");
        using var scope = new JobObjectScope();

        var session = NewSessionDirectory(scratch, "would-be-stray");
        var profile = Path.Combine(session, SessionLayout.ProfileFolderName);
        var (pid, created) = await PublishAsync(scope, scratch, profile);

        var ready = Path.Combine(scratch.Path, "held.json");
        _ = scope.Launch(ProbeExecutable, scratch.Path, "session-hold-named", LockScopes.Sweep, ready);
        _ = await ProbeReport.ReadAsync(ready, Patience);

        var clock = Stopwatch.StartNew();
        var skipped = await Sweep();
        var elapsed = clock.Elapsed;

        await Assert.That(skipped.Outcome).IsEqualTo(StraySweepOutcome.Skipped);

        // Try-acquire-and-skip, never queue. The ten-minute re-check overrunning
        // a pass must cost nothing at all, so a skip that waited even briefly
        // would be the wrong mechanism wearing the right answer.
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(1));

        // It did no work: nothing was looked at and nothing was touched.
        await Assert.That(skipped.Candidates).IsEqualTo(0);
        await Assert.That(skipped.Terminated).IsEmpty();
        await Assert.That(ProcessIdentity.IsAlive(pid, created)).IsTrue();
    }

    /// <summary>R11: nothing the sweep can do ends the process.</summary>
    [Test]
    public async Task NothingTheSweepCanDoEndsTheProcess()
    {
        using var capturing = new CapturingLoggerProvider();
        var logger = capturing.CreateLogger("BrowserAI.Sweep");

        // A factory that throws, which is what a broken payload does: the sweep
        // reads browsers.json to know which binaries are ours.
        StraySweep.StartInBackground(
            () => throw new FileNotFoundException("browsers.json is not there"),
            logger);

        await WaitUntil(() => capturing.Records.Any(record => record.Level is LogLevel.Error));

        var failure = capturing.Records.Single(record => record.Level is LogLevel.Error);

        await Assert.That(failure.Exception).IsTypeOf<FileNotFoundException>();
        await Assert.That(failure.Message).Contains("Nothing was terminated");

        // And a logger that itself throws, which is the case the catch-all
        // cannot report through: the boundary must still not let anything out.
        var throwing = new ThrowingLogger();
        StraySweep.StartInBackground(() => new StraySweep([], index: null, throwing), throwing);

        await WaitUntil(() => throwing.Attempts >= 2);

        // Reaching here at all is the assertion: an exception escaping a
        // background thread ends the process, and this process is an MCP server
        // whose caller would see a transport that simply stopped.
        await Assert.That(throwing.Attempts).IsGreaterThanOrEqualTo(2);
    }

    /// <summary>R12: the sweep never writes to <c>stdout</c>.</summary>
    /// <remarks>
    /// <b>Measured across a process boundary, because that is the only place the
    /// question is answerable.</b> <c>stdout</c> is a process-wide handle, so a
    /// test host redirecting <see cref="Console.Out"/> would be measuring its own
    /// redirection as much as the product's silence. Here the pipe is read from
    /// outside and the expected count is zero characters.
    /// </remarks>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task TheSweepWritesNothingToStandardOutput()
    {
        using var scratch = ScratchDirectory.Create("sweep-stdout");
        using var scope = new JobObjectScope();

        var session = NewSessionDirectory(scratch, "silent");
        var profile = Path.Combine(session, SessionLayout.ProfileFolderName);
        var (pid, created) = await PublishAsync(scope, scratch, profile);

        var deadline = DateTime.UtcNow + Patience;
        JsonNode census;

        // Asked again while another process on the machine happens to be
        // sweeping -- a skipped sweep is not a missed one -- and stdout is
        // asserted empty on EVERY attempt, including the skipped ones.
        while (true)
        {
            var report = Path.Combine(scratch.Path, $"sweep-{Guid.NewGuid():N}.json");
            var run = await ProbeProcess.RunInAsync(scratch.Path, "stray-sweep", report, PlantedProbe.ExecutablePath);

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(run.StandardOutput).IsEmpty();

            census = await ProbeReport.ReadAsync(report, Patience);

            if ((string?)census["outcome"] is not nameof(StraySweepOutcome.Skipped) || DateTime.UtcNow > deadline)
            {
                break;
            }
        }

        // Not vacuous: the pass really ran, really found the candidate, and
        // really acted on it -- and said none of that on stdout.
        await Assert.That((string?)census["outcome"]).IsEqualTo(nameof(StraySweepOutcome.Ran));
        await Assert.That((int)census["candidates"]!).IsGreaterThanOrEqualTo(1);
        await Assert.That((int)census["terminated"]!).IsGreaterThanOrEqualTo(1);
        await WaitUntilGoneAsync(pid, created);
    }

    /// <summary>
    /// The title guard: rejected on the characters, before anything touches the
    /// filesystem.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The failure this prevents is a twenty-one-second stall, not a wrong
    /// answer</b> — measured 21,037 ms for <c>\\10.255.255.1\share</c> and
    /// 22,225 ms for a dead hostname — so the elapsed time is what is asserted.
    /// </remarks>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task AUncTitleIsRefusedByAStringCheckAndTheSweepStaysFast()
    {
        const string Unc = @"\\10.255.255.1\share\profile";

        await Assert.That(StraySweep.IsRootedLocalDriveLetterPath(Unc)).IsFalse();
        await Assert.That(StraySweep.IsRootedLocalDriveLetterPath(@"\\?\C:\profile")).IsFalse();
        await Assert.That(StraySweep.IsRootedLocalDriveLetterPath(@"C:profile")).IsFalse();
        await Assert.That(StraySweep.IsRootedLocalDriveLetterPath("profile")).IsFalse();
        await Assert.That(StraySweep.IsRootedLocalDriveLetterPath(@"C:\work\profile")).IsTrue();

        using var scratch = ScratchDirectory.Create("sweep-unc");
        using var scope = new JobObjectScope();

        var (pid, created) = await PublishAsync(scope, scratch, Unc);

        var clock = Stopwatch.StartNew();
        var result = await SweepAsync();
        var elapsed = clock.Elapsed;

        await Assert.That(result.RejectedTitles).Contains(Unc);
        await Assert.That(result.Terminated).IsEmpty();
        await Assert.That(ProcessIdentity.IsAlive(pid, created)).IsTrue();

        // Two orders of magnitude below the measured stall. A pass that reached
        // File.Exists with this string would take twenty-one seconds.
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Attribution failed, so the sweep declines to act and says so.
    /// </summary>
    [Test]
    [NotInParallel(SweepGroup)]
    public async Task ACandidateNoWindowClaimsIsReportedLoudlyAndLeftRunning()
    {
        using var scratch = ScratchDirectory.Create("sweep-unattributable");
        using var scope = new JobObjectScope();

        // A candidate with no window at all: the product's own detection sees
        // it, and nothing can say which directory it belongs to.
        var (plantedPid, plantedCreated) = await StartPlantedAsync(scope, scratch);
        using var capturing = new CapturingLoggerProvider();

        var result = await SweepAsync(capturing.CreateLogger("BrowserAI.Sweep"));

        await Assert.That(result.Unattributable.Select(entry => entry.ProcessId)).Contains(plantedPid);
        await Assert.That(result.Terminated).IsEmpty();
        await Assert.That(ProcessIdentity.IsAlive(plantedPid, plantedCreated)).IsTrue();

        var reported = capturing.Records.Single(record => record.Level is LogLevel.Warning && record.Message.Contains("could not attribute", StringComparison.Ordinal));

        await Assert.That(reported.Message).Contains("Nothing was terminated");
        await Assert.That(reported.Message).Contains(PlantedProbe.ExecutablePath);
    }

    /// <summary>
    /// The property that stops BrowserAI closing a developer's browser: detection
    /// matches our own binaries and nothing else running on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted against the whole machine rather than against a fixture.</b>
    /// The legacy <c>%LOCALAPPDATA%\ms-playwright</c> tree that the
    /// <c>npx</c>-based setup this project replaces leaves behind is the exact
    /// shape of the mistake — same Chromium revision, same vendor, a browser
    /// nobody is using — and it must not match, because its image path is not
    /// the binary BrowserAI provisioned.
    /// </para>
    /// <para>
    /// The synthetic arm is what keeps this non-vacuous on a machine that has
    /// never had that tree: a copy planted at the same shape of path is created
    /// and must be missed just as surely.
    /// </para>
    /// </remarks>
    [Test]
    public async Task DetectionMatchesOnlyTheBinariesBrowserAiProvisionedAndMissesTheLegacyTree()
    {
        string[] ours = [BrowserAiPaths.ExpectedChromiumExecutable, BrowserAiPaths.FirefoxExecutable];

        using var scratch = ScratchDirectory.Create("sweep-foreign");
        using var scope = new JobObjectScope();

        // A process at the same shape of path the retired npx setup leaves
        // behind, so this arm means something on a machine that has none.
        var legacyShape = Path.Combine(
            scratch.Path,
            "ms-playwright",
            $"chromium_headless_shell-{BrowserAiPaths.ChromiumRevision}",
            "chrome-headless-shell-win64");

        var (planted, plantedPath) = await PlantedProcess.StartInAsync(scope, legacyShape, scratch.Path);

        using var scan = BrowserProcesses.ScanFor(ours);
        var candidates = scan.Candidates.Select(candidate => candidate.ProcessId).ToHashSet();

        // Every candidate is running one of exactly two strings. Nothing else
        // on the machine can be one, whatever it is called.
        foreach (var candidate in scan.Candidates)
        {
            await Assert.That(ours).Contains(candidate.ImagePath);
        }

        await Assert.That(candidates).DoesNotContain(planted.Id);
        await Assert.That(File.Exists(plantedPath)).IsTrue();

        // Not vacuous: the scan really did look at the whole machine.
        await Assert.That(scan.Enumerated).IsGreaterThan(100);
        await Assert.That(scan.Opened).IsGreaterThan(50);

        // And the real thing, if this machine still has one: every live process
        // out of the retired %LOCALAPPDATA%\ms-playwright tree, matched by
        // DIRECTORY PREFIX rather than by name, is absent from the candidates.
        var legacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ms-playwright");

        foreach (var legacy in BrowserProcesses.RunningFrom(legacyRoot))
        {
            await Assert.That(candidates).DoesNotContain(legacy.ProcessId);
        }
    }

    /// <summary>
    /// The two executables the sweep is pointed at are the ones that exist.
    /// </summary>
    [Test]
    public async Task TheProvisionedExecutablesAreComposedFromTheResolvedRevisionRatherThanSpelled()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);
        var executables = ProvisionedBrowsers.Executables(BrowserAiPaths.BrowsersDirectory, manifest);

        // The harness composes these from the committed snapshot and the product
        // from the resolved payload. They are two independent routes to the same
        // two strings, which is what makes either one evidence.
        string[] expected = [BrowserAiPaths.ExpectedChromiumExecutable, BrowserAiPaths.FirefoxExecutable];

        await Assert.That(executables).IsEquivalentTo(expected);
    }

    private static string ProbeExecutable { get; } = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    private static string NewSessionDirectory(ScratchDirectory scratch, string label)
    {
        var directory = Path.Combine(scratch.Path, label);
        var path = SessionPath.Resolve(directory);

        SessionLayout.Create(path);

        var result = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Mode = "headless", Browser = "chromium", Purpose = $"session {label}" },
            NullLogger.Instance);

        // Taken and released: what a crashed BrowserAI leaves behind is a
        // lock.json with nothing holding it.
        result.Acquired?.Dispose();

        return directory;
    }

    private static async Task<(int ProcessId, long Created)> PublishAsync(JobObjectScope scope, ScratchDirectory scratch, string title)
    {
        var report = await PlantedProbe.PublishWindowAsync(scope, scratch.Path, MessageWindows.ChromiumSingletonClass, title);
        var pid = (int)report["pid"]!;

        await PlantedProbe.WaitUntilDetectableAsync([PlantedProbe.ExecutablePath], pid);

        return (pid, ProcessIdentity.CreationTimeOf(pid));
    }

    private static async Task<(int ProcessId, long Created)> StartPlantedAsync(JobObjectScope scope, ScratchDirectory scratch)
    {
        var ready = Path.Combine(scratch.Path, "held.json");
        var process = scope.Launch(PlantedProbe.ExecutablePath, scratch.Path, "session-hold-named", $@"Global\BrowserAI-Test-{Guid.NewGuid():N}", ready);

        _ = await ProbeReport.ReadAsync(ready, Patience);
        await PlantedProbe.WaitUntilDetectableAsync([PlantedProbe.ExecutablePath], process.Id);

        return (process.Id, ProcessIdentity.CreationTimeOf(process.Id));
    }

    /// <summary>
    /// Runs one pass over the planted image, retrying while another process
    /// happens to hold the machine-wide mutex.
    /// </summary>
    /// <remarks>
    /// A skipped sweep is not a missed sweep — that is the design — so a test
    /// that needs a pass to <i>run</i> asks again rather than failing. Anything
    /// else would be a suite that goes red because a real BrowserAI started
    /// somewhere on the machine at the wrong moment.
    /// </remarks>
    private static async Task<StraySweepResult> SweepAsync(ILogger? logger = null)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (true)
        {
            var result = await Sweep(logger);

            if (result.Outcome is not StraySweepOutcome.Skipped || DateTime.UtcNow > deadline)
            {
                return result;
            }

            await Task.Delay(25);
        }
    }

    private static Task<StraySweepResult> Sweep(ILogger? logger = null) =>
        Task.Run(() => new StraySweep([PlantedProbe.ExecutablePath], index: null, logger ?? NullLogger.Instance).Run());

    private static async Task<Attribution> WaitForAttributionAsync(string image, string profile, int browserId, long browserCreated)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (true)
        {
            if (!ProcessIdentity.IsAlive(browserId, browserCreated))
            {
                throw new InvalidOperationException(
                    $"The browser (pid {browserId}) exited before it published a message window for '{profile}'. "
                    + "That is a launch failure rather than an attribution failure, and waiting out the deadline would report the wrong one.");
            }

            using (var scan = BrowserProcesses.ScanFor([image]))
            {
                var walk = MessageWindows.Walk(MessageWindows.ChromiumSingletonClass);

                foreach (var window in walk.Windows)
                {
                    if (string.Equals(MessageWindows.TitleOf(window.Handle), profile, StringComparison.OrdinalIgnoreCase))
                    {
                        return new Attribution(
                            [.. scan.Candidates.Select(candidate => candidate.ProcessId)],
                            profile,
                            window.ProcessId);
                    }
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"No message-only window titled '{profile}' appeared within {Patience}. A real Chromium publishes its user-data-dir there, and the sweep's attribution half reads it.");
            }

            await Task.Delay(50);
        }
    }

    private static async Task WaitUntilGoneAsync(int processId, long created)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (ProcessIdentity.IsAlive(processId, created))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Process {processId} was still alive {Patience} after it should have gone.");
            }

            await Task.Delay(25);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"The condition was never met within {Patience}.");
            }

            await Task.Delay(10);
        }
    }

    private sealed record Attribution(IReadOnlyList<int> Candidates, string Attributed, int WindowOwner);

    /// <summary>
    /// A logger that fails, so the catch-all's own reporting path can be shown
    /// not to defeat it.
    /// </summary>
    private sealed class ThrowingLogger : ILogger
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _ = Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException("This sink is broken.");
        }
    }
}
