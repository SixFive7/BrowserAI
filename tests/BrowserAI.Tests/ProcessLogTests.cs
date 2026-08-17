// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// Asserts the process log's durability properties at process scope, which is
/// the only scope at which they are true.
/// </summary>
internal sealed class ProcessLogTests
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
        var clock = System.Diagnostics.Stopwatch.StartNew();

        using var writer = new RollingFileWriter(@"\\192.0.2.1\browserai$\logs");

        writer.Write("a record that must not cost a network round trip");
        clock.Stop();

        await Assert.That(writer.RefusedNetworkDirectory).IsTrue();
        await Assert.That(writer.CurrentFile).IsNull();

        // Decided on the string, so it cannot have been a timeout: a single
        // file call against a dead share was measured at 21 s, and one second
        // is two orders of magnitude inside that without being a tight bound
        // that fails on a loaded machine.
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(1));

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
