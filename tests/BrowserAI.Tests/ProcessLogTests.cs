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
        var fileProviders = CountOf(source, "new FileLoggerProvider(");

        await Assert.That(consoleCallSites).IsGreaterThan(0);
        await Assert.That(pinned).IsEqualTo(consoleCallSites);
        await Assert.That(fileProviders).IsGreaterThanOrEqualTo(consoleCallSites);

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
