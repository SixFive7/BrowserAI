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
