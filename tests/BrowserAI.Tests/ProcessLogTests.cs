// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Asserts the process log's durability properties at process scope, which is
/// the only scope at which they are true.
/// </summary>
internal sealed class ProcessLogTests
{
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
