// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;
using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// Asserts the process log's durability properties at process scope, which is
/// the only scope at which they are true.
/// </summary>
internal sealed partial class ProcessLogTests
{
    /// <summary>
    /// Disposing the stack actually closes the file.
    /// </summary>
    /// <remarks>
    /// <b>It did not, and the claim that it did was in the code.</b> Measured
    /// 2026-08-16: <c>LoggerFactory.Dispose()</c> never calls <c>Dispose</c> on a
    /// provider <i>instance</i> handed to <c>AddProvider</c> — a container does
    /// not dispose what it did not create — so the rolling handle outlived every
    /// disposal and the log could not be opened exclusively afterwards. Harmless
    /// in <c>Main</c>, which exits immediately; found by the first short-lived
    /// caller that opened a log and then read it back, which is a Velopack hook.
    /// Planting the old body back turns this red at the exclusive open.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task DisposingTheProcessLogReleasesTheFileHandle()
    {
        using var scratch = ScratchDirectory.Create("processlog-handle");

        string? file;

        using (var log = ProcessLog.Create(new LocalAppDataPaths(scratch.Path), LogLevel.Information))
        {
            var logger = log.Factory.CreateLogger("BrowserAI.Test");

            ProcessLogProbe.Wrote(logger, nameof(DisposingTheProcessLogReleasesTheFileHandle));
            file = log.CurrentFile;
        }

        await Assert.That(file).IsNotNull();

        // FileShare.None is the assertion: it succeeds only when nothing at all
        // holds the file, which is exactly the property being claimed.
        using var exclusive = new FileStream(file!, FileMode.Open, FileAccess.Read, FileShare.None);

        await Assert.That(exclusive.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task TwoConsecutiveRunsBothAppearInTheProcessLog()
    {
        using var scratch = ScratchDirectory.Create("processlog-append");

        var first = await ProbeProcess.RunAsync("log-once", scratch.Path, "first-run-marker");
        var second = await ProbeProcess.RunAsync("log-once", scratch.Path, "second-run-marker");

        await Assert.That(first.ExitCode).IsEqualTo(0);
        await Assert.That(second.ExitCode).IsEqualTo(0);

        // The failure this prevents: a sink that truncates on start has deleted
        // the previous crash by the time anyone looks, and with ~100 concurrent
        // processes a start is the most common event there is.
        var log = ProbeProcess.ReadProcessLog(scratch.Path);
        await Assert.That(log).Contains("first-run-marker");
        await Assert.That(log).Contains("second-run-marker");
    }

    [Test]
    public async Task AnUnhandledExceptionStillLeavesItsLastLogLineOnDisk()
    {
        using var scratch = ScratchDirectory.Create("processlog-crash");

        var result = await ProbeProcess.RunAsync("crash", scratch.Path);

        // An unhandled exception is not guaranteed to unwind the stack, so the
        // probe's ProcessLog is never disposed. Anything on disk got there
        // because a record is flushed as it is written and because the
        // UnhandledException handler ran, not because a finally block did.
        await Assert.That(result.ExitCode).IsNotEqualTo(0);

        var log = ProbeProcess.ReadProcessLog(scratch.Path);
        await Assert.That(log).Contains("about to throw");
        await Assert.That(log).Contains("Unhandled exception reached the process boundary");
        await Assert.That(log).Contains("Deliberate unhandled exception from the crash probe.");
    }

    [Test]
    public async Task NothingIsWrittenToStdoutByTheLoggingStack()
    {
        using var scratch = ScratchDirectory.Create("processlog-stdout");

        var result = await ProbeProcess.RunAsync("log-once", scratch.Path, "stdout-must-stay-empty");

        // LogToStandardErrorThreshold is set to the lowest level that exists,
        // so no severity has a path to stdout. stdout is the protocol channel;
        // one stray Information record corrupts a JSON-RPC frame.
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError).Contains("stdout-must-stay-empty");
    }

    [Test]
    public async Task TheReclaimPassIsItselfATestAndReportsWhatItCouldNotTake()
    {
        // [testing]: "The pass is itself a test. It runs the same reclaim the
        // product performs, so a defect in reclaim shows up as a suite that
        // cannot start clean -- which is a louder signal than a sweep that
        // quietly finds nothing." The pass ran and nothing asserted that it had
        // until 2026-08-17, which is the exact shape the sentence warns about:
        // a sweep whose silence is indistinguishable from its success.
        var root = ScratchRoot.Path;

        await Assert.That(ScratchRoot.HasReclaimed).IsTrue();
        await Assert.That(Directory.Exists(root)).IsTrue();

        // Nothing survived it. A non-empty list is a previous run's leak and
        // names every node -- which is what switching the pass onto TreeDelete
        // bought, because the framework primitive names one node where the
        // per-node walk names all of them.
        await Assert.That(string.Join(Environment.NewLine, ScratchRoot.LastPassSurvivors)).IsEmpty();

        // Idempotent, which is the property that lets it run before anything
        // else without being ordered against anything. Asking again neither
        // re-runs it nor deletes what this run has since created.
        using var mine = ScratchDirectory.Create("reclaim-is-idempotent");
        await File.WriteAllTextAsync(Path.Combine(mine.Path, "written-after-the-pass.txt"), "still here");

        await Assert.That(ScratchRoot.Path).IsEqualTo(root);
        await Assert.That(File.Exists(Path.Combine(mine.Path, "written-after-the-pass.txt"))).IsTrue();
    }

    /// <summary>
    /// The spawn record ends a process a previous run left running, and refuses
    /// to touch a pid whose creation time has moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the bullet of the reclaim pass that had no input.</b>
    /// [Testing](../../TESTING.md#testing-a-hard-requirement-and-the-release-gate) asks that <i>anything
    /// the previous run recorded is terminated by <c>(pid,
    /// creationFileTime)</c> from its own spawn record</i>; nothing wrote a
    /// record until 2026-08-19, so a run killed mid-test left a process the next
    /// run could not identify — only a directory it could not delete, reported
    /// as a locked file rather than as a live process.
    /// </para>
    /// <para>
    /// ⚠️ <b>The second line of the record is this test host's own pid with a
    /// creation time that is deliberately wrong, and that is the assertion the
    /// whole mechanism turns on.</b> A pid Windows has recycled belongs to
    /// something else — plausibly the developer's editor — so a reclaim that
    /// acted on the number alone would end it. If this code ever regressed to
    /// matching on pid, the run would die here rather than fail here, which is
    /// the loudest possible form of red.
    /// </para>
    /// <para>
    /// <b>The record it reads is the test's own file, not the suite's.</b> The
    /// real one has already been consumed by the pass that ran before any test
    /// did, and a test that rewrote it would be arranging state for whatever
    /// runs next.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSpawnRecordEndsAPreviousRunsProcessAndSkipsARecycledPid()
    {
        using var scratch = ScratchDirectory.Create("spawn-record");
        var record = Path.Combine(scratch.Path, "spawn-record.txt");
        var probe = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");
        var session = Directory.CreateDirectory(Path.Combine(scratch.Path, "held")).FullName;
        var ready = Path.Combine(scratch.Path, "gate.json");

        // Contained as well as recorded: if an assertion below throws, the job
        // takes the probe with it rather than leaving the thing this test is
        // about to prove can be cleaned up.
        using (var scope = new JobObjectScope())
        {
            var started = scope.Launch(probe, AppContext.BaseDirectory, "session-hold-gate", session, ready);
            _ = await ProbeReport.ReadAsync(ready, TestDefaults.ProcessHang);

            var created = ProcessIdentity.CreationTimeOf(started.Id);
            var mine = ProcessIdentity.CreationTimeOf(Environment.ProcessId);

            await Assert.That(ProcessIdentity.IsAlive(started.Id, created)).IsTrue();

            await File.WriteAllLinesAsync(record,
            [
                $"{started.Id} {created}",

                // The pid is real and running; the creation time is not its own.
                // Nothing may happen to it.
                $"{Environment.ProcessId} {mine + 1}",

                // A pid that named a process once and does not now.
                $"{int.MaxValue - 1} {created}",
            ]);

            var report = SpawnRecord.Reclaim(record);

            await Assert.That(report.Count).IsEqualTo(3).Because(string.Join(" | ", report));
            await Assert.That(report.Count(line => line.StartsWith("terminated ", StringComparison.Ordinal))).IsEqualTo(1)
                .Because(string.Join(" | ", report));
            await Assert.That(report.Count(line => line.StartsWith("skipped ", StringComparison.Ordinal))).IsEqualTo(2)
                .Because(string.Join(" | ", report));

            // The recorded process is gone, and the one whose identity did not
            // match is not — which is this process, still executing.
            await Assert.That(ProcessIdentity.IsAlive(started.Id, created)).IsFalse();
            await Assert.That(ProcessIdentity.IsAlive(Environment.ProcessId, mine)).IsTrue();

            // Emptied, so a second reclaim of the same file is a no-op rather
            // than a second pass over pids that now belong to somebody else.
            await Assert.That(await File.ReadAllTextAsync(record)).IsEmpty();
            await Assert.That(SpawnRecord.Reclaim(record).Count).IsEqualTo(0);
        }

        // And the live suite really does write one, which is the half a
        // test-owned file cannot show: every process this run has launched went
        // through the same call.
        await Assert.That(SpawnRecord.Path).EndsWith("spawn-record.txt");
    }

    [Test]
    public async Task ALogDirectoryOnAShareIsRefusedOutrightRatherThanWrittenTo()
    {
        // §E: "No destination that can block or produce a window ... no UNC
        // path -- a \\host\share that is not answering blocks a file call for
        // 21 seconds, measured, and a log write is not somewhere to discover
        // that." That rule was stated in a doc comment and enforced by nothing
        // until 2026-08-17.
        //
        // The address below is in the RFC 5737 documentation range, so it can
        // never be a real host on anyone's network -- and the whole point is
        // that the test must not depend on how long it takes to fail, because a
        // check that reaches the network is the thing being prevented.
        using var writer = new RollingFileWriter(@"\\192.0.2.1\browserai$\logs");

        writer.Write("a record that must not cost a network round trip");

        // ⚠️ THE DECISION IS THE ASSERTION, and it is why the stopwatch that used
        // to be here is gone.
        //
        // Deleted 2026-08-18: `Assert.That(clock.Elapsed).IsLessThan(1 s)`, whose
        // note read "decided on the string, so it cannot have been a timeout".
        // `RefusedNetworkDirectory` is set on ONE path only -- the string check
        // that rejects a UNC root before any file call -- so it being true is
        // itself the proof that nothing reached the network; a writer that had
        // gone out to the share and failed would have a null CurrentFile and this
        // flag CLEAR. One second, meanwhile, is a number a starved machine
        // reaches while the product is behaving perfectly, and this test then
        // fails saying the product went to the network when it did not.
        await Assert.That(writer.RefusedNetworkDirectory).IsTrue();
        await Assert.That(writer.CurrentFile).IsNull();

        // And a local directory is still written, so the guard refuses shares
        // rather than refusing everything.
        using var scratch = ScratchDirectory.Create("rollingwriter-local");
        using var local = new RollingFileWriter(scratch.Path);

        local.Write("a record that must reach disk");

        await Assert.That(local.RefusedNetworkDirectory).IsFalse();
        await Assert.That(local.CurrentFile).IsNotNull();
    }

    [Test]
    public async Task EveryConsoleProviderInTheStackIsPinnedToStderrAndIsNeverTheOnlyOne()
    {
        // Two claims §E makes about the same few lines, both load-bearing and
        // both asserted by nothing before 2026-08-17.
        //
        // One: "Set it to the lowest level that exists and no severity has a
        // path to stdout at all." That is one argument on one call, in two
        // places, and deleting it silently routes Information to stdout -- the
        // protocol channel. NothingIsWrittenToStdoutByTheLoggingStack catches
        // it at the default level; this catches the wiring itself, at every
        // call site, including one added later that nobody thinks about.
        //
        // Two: §E leaves "whether the framework's own console provider drains
        // its queue at process exit" explicitly unverified and offers two ways
        // out -- verify it, or own the sink. BrowserAI takes the second: every
        // factory that adds the console provider also adds a FileLoggerProvider
        // over RollingFileWriter, which is one unbuffered WriteFile per record.
        // So no record exists ONLY in a queue whose drain nobody measured, and
        // the unverified behaviour is not relied on rather than being assumed
        // benign. What makes that true is a pairing, and a pairing is exactly
        // what a later edit breaks.
        var source = await RepositoryLayout.ReadCodeAsync(
            new FileInfo(Path.Combine(
                RepositoryLayout.Root.FullName, "src", "BrowserAI", "Logging", "ProcessLog.cs")));

        var consoleCallSites = CountOf(source, "AddConsole(");
        var pinned = CountOf(source, "LogToStandardErrorThreshold = LogLevel.Trace");

        await Assert.That(consoleCallSites).IsGreaterThan(0);
        await Assert.That(pinned).IsEqualTo(consoleCallSites);

        // ⚠️ THE SECOND CLAIM NARROWED WITH THE THING IT WAS ABOUT (2026-08-26,
        // previously `fileProviders >= consoleCallSites` over the whole file).
        // There are two stacks in here and only one of them still owns a file.
        // The PROCESS stack pairs its console with a FileLoggerProvider over
        // RollingFileWriter, so no machine-wide record exists only in a queue
        // whose drain nobody measured -- that is unchanged and is asserted
        // below. The SESSION stack has no file at all since `browserai.log` was
        // deleted, and it does not need one: what a session did is in
        // `browserai.data`, an INSERT per call, which is durable in a way a log
        // file never was and which `browserai_catch_up` can read back.
        var split = source.IndexOf("SessionLogging OpenSessionLog", StringComparison.Ordinal);

        // Not vacuous: a scan that failed to find the second stack would report
        // the first one's providers twice.
        await Assert.That(split).IsGreaterThan(0);

        var processStack = source[..split];
        var sessionStack = source[split..];

        await Assert.That(CountOf(processStack, "AddConsole(")).IsEqualTo(1);
        await Assert.That(CountOf(processStack, "new FileLoggerProvider(")).IsEqualTo(1);

        await Assert.That(CountOf(sessionStack, "AddConsole(")).IsEqualTo(1);
        await Assert.That(CountOf(sessionStack, "new FileLoggerProvider(")).IsEqualTo(0);

        // And nowhere else in the product builds a logging stack, so counting
        // one file is counting all of them.
        var elsewhere = RepositoryLayout.ProductSourceFiles
            .Where(file => !string.Equals(file.Name, "ProcessLog.cs", StringComparison.Ordinal))
            .Select(file => (file.Name, Code: File.ReadAllText(file.FullName)))
            .Where(entry => entry.Code.Contains("AddConsole(", StringComparison.Ordinal))
            .Select(entry => entry.Name)
            .ToList();

        await Assert.That(string.Join(", ", elsewhere)).IsEmpty();
    }

    /// <summary>
    /// A timed <c>WaitForExit</c> is always followed by a bare one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same shape as the pairing above, for the same reason: deleting one
    /// half loses diagnostics and nothing goes red.</b>
    /// <c>Process.WaitForExit(int)</c> returns as soon as the process is gone
    /// and does <b>not</b> drain the asynchronous output readers, so a
    /// <c>ErrorDataReceived</c> handler is routinely left holding half the
    /// child's stderr. The parameterless overload is what flushes the event
    /// queue. The SDK's own transport documents this in as many words, and this
    /// repository's one timed call site carries the second call and a comment
    /// saying why.
    /// </para>
    /// <para>
    /// <b>The analyzer cannot express this and the suppression makes it worse.</b>
    /// <c>build/BannedSymbols.txt</c> bans the timed overload, so the call site
    /// carries an <c>RS0030</c> suppression — which permits the timed call and
    /// has no way to require the flush. A later edit that deletes the bare call
    /// leaves the suppression, its comment and the whole build intact, and
    /// truncates stderr silently. That is the exact gap this fills.
    /// </para>
    /// <para>
    /// <b>Why it does not assert that a timed call exists.</b> A corpus check
    /// would pin today's tree and go red the day the last timed call is
    /// legitimately replaced by <c>WaitForExitAsync</c>, which is the direction
    /// this repository wants to move in. What is asserted instead is that the
    /// matcher still works, against two samples held here — so the scan cannot
    /// pass by having quietly stopped matching, and an empty tree stays green.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryTimedWaitForExitIsFollowedByABareOne()
    {
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.SourceAndScriptFiles.Where(file => file.Extension is ".cs"))
        {
            // Comments stripped, because ProbeProcess.cs explains this very rule
            // in a comment that names the banned call, and a scan that could not
            // tell a rule from its statement would fail on the sentence
            // describing it. No line number is reported for the same reason:
            // ReadCodeAsync removes those lines rather than emptying them, so
            // what survives no longer agrees with the file about numbering.
            offenders.AddRange(UnpairedWaits(await RepositoryLayout.ReadCodeAsync(file))
                .Select(call => $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}: '{call}' is not followed by a bare WaitForExit()"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ Composed rather than spelled, so that this file does not match its
        // own scan. It is the trap NeverByImageNameTests assembles its needles
        // to avoid, and this test fell straight into it on the first run: the
        // samples below were offenders in the very list they exist to verify.
        // Excluding this file would have been the other way out, and it is the
        // worse one -- it would create the single place in the repository where
        // the rule does not apply.
        const string Timed = ".WaitFor" + "Exit(5000)";
        const string Later = ".WaitFor" + "Exit(9000)";
        const string Bare = ".WaitFor" + "Exit()";

        // The matcher is alive: one sample pairs and one does not.
        await Assert.That(UnpairedWaits($"if (!p{Timed}) {{ return; }}\np{Bare};\nvar code = p.ExitCode;")).IsEmpty();
        await Assert.That(UnpairedWaits($"if (!p{Timed}) {{ return; }}\nvar code = p.ExitCode;").Count).IsEqualTo(1);

        // And a second timed call cannot be covered by the first one's flush.
        await Assert.That(UnpairedWaits($"p{Timed};\np{Bare};\np{Later};").Count).IsEqualTo(1);
    }

    /// <summary>Every timed wait with no bare wait between it and the next one.</summary>
    /// <param name="code">Source with comment-only lines already removed.</param>
    /// <returns>The offending call text, one entry each.</returns>
    private static List<string> UnpairedWaits(string code)
    {
        var unpaired = new List<string>();
        var timed = TimedWait().Matches(code).ToList();

        for (var at = 0; at < timed.Count; at++)
        {
            // The window ends at the NEXT timed call rather than at the end of
            // the file: one bare call flushes one wait, so two timed calls
            // sharing a single flush is the same defect wearing a disguise.
            var from = timed[at].Index + timed[at].Length;
            var to = at + 1 < timed.Count ? timed[at + 1].Index : code.Length;

            if (!BareWait().IsMatch(code[from..to]))
            {
                unpaired.Add(timed[at].Value.Trim());
            }
        }

        return unpaired;
    }

    /// <summary>
    /// A <c>WaitForExit</c> call with an argument.
    /// </summary>
    /// <remarks>
    /// Anchored on the dot so a declaration cannot match, and it cannot reach
    /// <c>WaitForExitAsync</c> because the parenthesis has to follow the name
    /// immediately. Both overloads of the async form are what this repository
    /// uses everywhere else, and they are correct: on .NET,
    /// <c>WaitForExitAsync</c> waits for the readers as well.
    /// </remarks>
    [GeneratedRegex(@"\.WaitForExit\((?!\s*\))(?:[^()]|\([^()]*\))*\)")]
    private static partial Regex TimedWait();

    /// <summary>The parameterless overload, which is the one that drains the queue.</summary>
    [GeneratedRegex(@"\.WaitForExit\(\s*\)")]
    private static partial Regex BareWait();

    private static int CountOf(string source, string needle)
    {
        var found = 0;

        for (var at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }

    [Test]
    public async Task ConcurrentProcessesDoNotLoseEachOthersRecords()
    {
        using var scratch = ScratchDirectory.Create("processlog-concurrent");

        const int Processes = 8;
        const int LinesEach = 25;

        var runs = await Task.WhenAll(Enumerable.Range(0, Processes)
            .Select(i => ProbeProcess.RunAsync("log-many", scratch.Path, $"writer{i}", LinesEach.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        foreach (var run in runs)
        {
            await Assert.That(run.ExitCode).IsEqualTo(0);
        }

        // The design says ~100 concurrent BrowserAI processes share one process
        // log. That only works if a concurrent append cannot overwrite another
        // process's bytes, and that is a property of the platform rather than
        // of our code -- so it is measured here rather than assumed.
        var log = ProbeProcess.ReadProcessLog(scratch.Path);

        var missing = (from writer in Enumerable.Range(0, Processes)
                       from line in Enumerable.Range(0, LinesEach)
                       let marker = $"writer{writer}#{line}"
                       where !log.Contains(marker, StringComparison.Ordinal)
                       select marker).ToList();

        await Assert.That(string.Join(", ", missing)).IsEmpty();
    }

    /// <summary>
    /// A session's records go to that session's own log and to nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rule taken 2026-08-24: anything attributable to a session goes to
    /// that session's own log; the central log keeps only what has none.</b>
    /// Until then <c>OpenSessionLog</c> added a second provider over the
    /// machine-wide <see cref="RollingFileWriter"/>, so every session record was
    /// written twice - and with ~100 processes each running N sessions, that
    /// duplicate was the bulk of the shared file's traffic.
    /// </para>
    /// <para>
    /// <b>Planted red by putting that provider back</b>, which is a one-line
    /// revert in <c>ProcessLog.OpenSessionLog</c>: the marker then appears in
    /// both files and the second assertion fails.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionsRecordsGoToItsOwnLogAndNotToTheSharedOne()
    {
        using var scratch = ScratchDirectory.Create("processlog-session-scope");

        var session = Path.Combine(scratch.Path, "session");
        _ = Directory.CreateDirectory(session);

        var marker = $"session-scope-{Guid.NewGuid():N}";

        using (var log = ProcessLog.Create(new LocalAppDataPaths(scratch.Path), LogLevel.Information))
        {
            // The shared file has to be open and working, or "it is not in
            // there" is answerable by a writer that never wrote anything.
            var machine = log.Factory.CreateLogger("BrowserAI.Startup");

            ProcessLogProbe.Wrote(machine, "the machine's own record");

            using var sessionLog = ProcessLog.OpenSessionLog(session, LogLevel.Information);
            var mine = sessionLog.Factory.CreateLogger("BrowserAI.Tests.Session");

            ProcessLogProbe.Wrote(mine, marker);
        }

        var shared = ProbeProcess.ReadProcessLog(scratch.Path);

        // The positive control for the line below: the shared file exists, is
        // readable, and holds this run's records.
        await Assert.That(shared).Contains("the machine's own record", StringComparison.Ordinal);

        // ⚠️ THE CLAIM, AND IT IS NOW THE WHOLE OF THE TEST (2026-08-26,
        // previously the marker was also required to be in
        // `<session-dir>\browserai.log`). There is no such file: a session's
        // stack is stderr alone. What has not changed, and is the reason the
        // duplicate was removed in the first place, is that a session's records
        // must not go to the file every BrowserAI on the machine queues at --
        // that duplicate was the bulk of the shared file's traffic, from ~100
        // processes at once.
        await Assert.That(shared).DoesNotContain(marker, StringComparison.Ordinal);

        // And the session directory gained no file for it, which is what makes
        // the deletion real rather than a rename.
        await Assert.That(Directory.EnumerateFiles(session).Select(Path.GetFileName).ToArray()).IsEmpty();
    }

    /// <summary>
    /// Every record in the shared log sits in the file in the order it was
    /// written, because the instant on it was read inside the write gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the property the gate exists for, and it is strictly stronger
    /// than the torn-record check in <c>SaturationTests</c>.</b> A file whose
    /// leading timestamps never go backwards is one where no writer read its
    /// clock, lost the race, and wrote behind somebody who read theirs later -
    /// which is what a lock-free appender does under load, and what made
    /// on-write sorting look like a problem needing a sort.
    /// </para>
    /// <para>
    /// <b>Planted red</b> by taking the stamp where it used to be taken - in
    /// <c>FileLoggerProvider.Log</c>, before the record reaches the sink at all:
    /// with eight processes writing sixty records each, the file comes back with
    /// timestamps out of order.
    /// </para>
    /// <para>
    /// <b>Scoped to a scratch root</b>, because the real one is shared with every
    /// other BrowserAI on the machine and this assertion is about a file, not
    /// about a run.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryRecordInTheSharedLogSitsInTheOrderItWasWritten()
    {
        using var scratch = ScratchDirectory.Create("processlog-ordered");

        const int Processes = 8;
        const int LinesEach = 60;

        var runs = await Task.WhenAll(Enumerable.Range(0, Processes)
            .Select(i => ProbeProcess.RunAsync("log-many", scratch.Path, $"ordered{i}", LinesEach.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        foreach (var run in runs)
        {
            await Assert.That(run.ExitCode).IsEqualTo(0);
        }

        var stamps = WriteStampsIn(ProbeProcess.ReadProcessLog(scratch.Path));

        // Not vacuous: eight processes wrote sixty records each, so anything
        // near zero means the file was not read rather than that it was sorted.
        await Assert.That(stamps.Count).IsGreaterThanOrEqualTo(Processes * LinesEach);

        var backwards = new List<string>();

        for (var i = 1; i < stamps.Count; i++)
        {
            if (stamps[i] < stamps[i - 1])
            {
                backwards.Add($"record {i.ToString(System.Globalization.CultureInfo.InvariantCulture)} is stamped {stamps[i]:O}, behind the record before it at {stamps[i - 1]:O}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, backwards.Take(10))).IsEmpty();
    }

    /// <summary>
    /// Every record names its writer by the pair, never by a bare pid, and
    /// carries both of its times.
    /// </summary>
    /// <remarks>
    /// <b>A pid alone does not identify a writer of this file.</b> The shared log
    /// keeps thirty days of records and Windows reuses pids within seconds, so a
    /// bare pid in a week-old record eventually names a stranger - which is the
    /// same reasoning <c>browserai.lock</c> already follows, and the FILETIME here
    /// is spelled the way that file spells <c>processCreatedFileTime</c> so the
    /// two name a writer with the same characters. <b>Planted red</b> by dropping the
    /// <c>made=</c> field, which is the half a reader loses first because it looks
    /// like the timestamp already at the front of the line.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryRecordNamesItsWriterByPidAndCreationTimeAndCarriesBothTimes()
    {
        using var scratch = ScratchDirectory.Create("processlog-writer");

        using (var log = ProcessLog.Create(new LocalAppDataPaths(scratch.Path), LogLevel.Information))
        {
            var logger = log.Factory.CreateLogger("BrowserAI.Test");

            ProcessLogProbe.Wrote(logger, nameof(EveryRecordNamesItsWriterByPidAndCreationTimeAndCarriesBothTimes));
        }

        var lines = ProbeProcess.ReadProcessLog(scratch.Path)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        await Assert.That(lines.Count).IsGreaterThan(0);

        var wrong = lines.Where(line => !WriterHeader().IsMatch(line)).ToList();

        await Assert.That(string.Join(Environment.NewLine, wrong.Take(5))).IsEmpty();

        // The pair has to be usable, not merely present: a zero FILETIME is the
        // documented "could not read my own creation time" value and would make
        // a liveness check answer 'not running' for a process that is.
        var pair = WriterHeader().Match(lines[0]);

        await Assert.That(int.Parse(pair.Groups["pid"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo(Environment.ProcessId);
        await Assert.That(long.Parse(pair.Groups["created"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .IsGreaterThan(0);
    }

    /// <summary>
    /// The roll happens exactly at the cap: no file in the directory ever
    /// exceeds it.
    /// </summary>
    /// <remarks>
    /// <b>Corrected 2026-08-24 (previously the writer said "the roll happens at
    /// approximately the cap rather than exactly at it").</b> That was true while
    /// the size was a per-process counter seeded once at open, which drifts as
    /// soon as a second process appends. Under the gate the length is the file's
    /// own, read through the open handle inside the claim, and the decision is
    /// made <i>before</i> the write rather than after it. <b>Planted red</b> by
    /// restoring the old shape - a counter, incremented after each write, rolling
    /// once it has already passed the cap - which leaves every full file over the
    /// line by one record.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoFileInTheSharedLogEverExceedsItsCap()
    {
        using var scratch = ScratchDirectory.Create("processlog-cap");

        var directory = new LocalAppDataPaths(scratch.Path).LogDirectory;

        // 64 KiB a record: enough that filling two 8 MiB files is a couple of
        // hundred writes rather than tens of thousands, and far enough below the
        // cap that this is the ordinary arm and not the oversized-record one.
        var padding = new string('x', 64 * 1024);

        using (var writer = new RollingFileWriter(directory))
        {
            for (var i = 0; i < 260; i++)
            {
                writer.Write(padding);
            }
        }

        var files = Directory.EnumerateFiles(directory, "browserai-*.log").Order(StringComparer.Ordinal).ToList();

        // Not vacuous: 260 records of 64 KiB is over 16 MiB, so a single file
        // would mean nothing rolled at all.
        await Assert.That(files.Count).IsGreaterThan(1);

        var over = files
            .Select(file => new FileInfo(file))
            .Where(file => file.Length > 8L * 1024 * 1024)
            .Select(file => $"{file.Name} is {file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} bytes, past the 8 MiB cap")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, over)).IsEmpty();
    }

    /// <summary>
    /// Nothing can unlink the shared log while a writer holds it open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[Finding 10](../../docs/reviews/2026-08-18-adversarial-processes.md),
    /// closed by removing <c>FILE_SHARE_DELETE</c>.</b> With it granted, anything
    /// on the machine could delete or rename the live log while a hundred
    /// BrowserAIs held it; every subsequent write then <i>succeeded</i> into an
    /// unlinked file object, <c>CurrentFile</c> went on naming a path that no
    /// longer existed, and the writer's own catch never fired because nothing had
    /// failed.
    /// </para>
    /// <para>
    /// <b>The cost is stated where it is paid, and it is the machine's log rather
    /// than one process's own</b>: while any BrowserAI runs, nobody can remove
    /// today's file. That was accepted knowingly. <b>Planted red</b> by putting
    /// the share flag back, which makes the delete succeed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSharedLogCannotBeUnlinkedWhileAWriterHoldsIt()
    {
        using var scratch = ScratchDirectory.Create("processlog-unlink");

        using var log = ProcessLog.Create(new LocalAppDataPaths(scratch.Path), LogLevel.Information);

        var logger = log.Factory.CreateLogger("BrowserAI.Test");

        ProcessLogProbe.Wrote(logger, nameof(TheSharedLogCannotBeUnlinkedWhileAWriterHoldsIt));

        var file = log.CurrentFile;

        await Assert.That(file).IsNotNull();

        // The delete is refused, and a reader is still admitted -- both halves,
        // because a share mode narrow enough to lock out the reader would have
        // traded one silent failure for a louder one.
        //
        // IOException rather than UnauthorizedAccessException, and the pair is
        // worth keeping straight because this repository already records the
        // other half: a RENAME over an open file is refused ERROR_ACCESS_DENIED
        // and surfaces as UnauthorizedAccessException (kb/re-verification.md row
        // 65), while a DELETE against a handle that withheld FILE_SHARE_DELETE
        // is refused ERROR_SHARING_VIOLATION and surfaces as IOException.
        // Measured here 2026-08-24; the type is .NET is mapping, so it floats
        // with the framework and the Windows half does not.
        await Assert.That(() => File.Delete(file!)).Throws<IOException>();
        await Assert.That(ProbeProcess.ReadProcessLog(scratch.Path)).IsNotEmpty();
        await Assert.That(File.Exists(file!)).IsTrue();
    }

    /// <summary>The write stamps in a log, in the order the file holds them.</summary>
    /// <param name="log">The log's whole text.</param>
    /// <returns>One <see cref="DateTime"/> per record header.</returns>
    private static List<DateTime> WriteStampsIn(string log) =>
        [.. log.Split('\n')
            .Select(line => WriterHeader().Match(line))
            .Where(match => match is { Success: true, Index: 0 })
            .Select(match => DateTime.ParseExact(
                match.Groups["written"].Value,
                "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind))];

    /// <summary>
    /// One record's header: the write stamp, the creation stamp, the level, and
    /// the writer as the pair.
    /// </summary>
    /// <remarks>
    /// Anchored at the start of a line, and deliberately not shared with
    /// <c>SaturationTests</c>' expression: that one exists to prove a header
    /// appears <i>only</i> at offset zero and this one to read the fields out, so
    /// one expression serving both would be a change to either that quietly
    /// weakened the other.
    /// </remarks>
    /// <returns>The compiled expression.</returns>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"^(?<written>\d{4}-\d{2}-\d{2}T[\d:.]+Z)\s\smade=(?<made>\d{4}-\d{2}-\d{2}T[\d:.]+Z)\s\s\S+\s+pid=(?<pid>\d+)@(?<created>\d+)\s\s")]
    private static partial System.Text.RegularExpressions.Regex WriterHeader();
}

/// <summary>
/// One source-generated record, so the handle test writes through the real
/// logging path rather than a stub.
/// </summary>
/// <remarks>
/// Source-generated because <c>CA1848</c> is an error here and a
/// <c>LogInformation</c> call would not compile — which is the correct rule, and
/// means a test that logs needs its own message.
/// </remarks>
internal static partial class ProcessLogProbe
{
    /// <summary>Writes one record.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="test">The test that wrote it.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Test} wrote this record.")]
    public static partial void Wrote(ILogger logger, string test);
}
