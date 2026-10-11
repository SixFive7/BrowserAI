// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Protocol;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Three of the product's waiting paths finish on a thread pool of one worker, because
/// none of them holds a thread while it waits.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The maintainer's decision 54, in his words verbatim: <i>"Never block a worker
/// anywhere."</i></b> The release gates of 2026-10-11 went red on a starved pool in the
/// test host, and three shapes were found behind it: a pipe client that waited on every
/// byte it read by parking its thread on an asynchronous read, a lock retry that slept on
/// a pool thread, and the pipes the product gives a child, which Windows creates
/// synchronous, so that every read of a child's output parked a pool thread for as long as
/// the child said nothing.
/// </para>
/// <para>
/// <b>Each arm runs the path in a child process whose pool has one worker</b>
/// (<c>StarvedPoolProbe</c>), and the path needs that worker for something only after it
/// has stopped waiting. A path that parks its thread while it waits takes the one worker,
/// and what it waits for never runs: the I/O completion that would end the read, the timer
/// that would let go of the file, the second child's read. The wait for the child is
/// <see cref="TestDefaults.ProcessHang"/>; the path's own bound is
/// <see cref="TestDefaults.InProcessHang"/>, so a path that parks fails the first and a path
/// that does not is done in seconds.
/// </para>
/// </remarks>
internal sealed class StarvedPoolTests
{
    /// <summary>The test probe, which runs the paths and the stand-ins.</summary>
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// A person's start, or the uninstall hook, asks the background one thing over its pipe
    /// and gets the answer on a pool of one worker.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePipeClientGetsItsAnswerOnAPoolOfOneWorker()
    {
        using var work = await ScratchDirectory.CreateAsync("starved-ask");
        using var scope = new JobObjectScope();

        var pipe = $@"\\.\pipe\BrowserAI-Tests-starved-{Guid.NewGuid():N}";
        var ready = Path.Combine(work.Path, "ready.txt");
        var standIn = await scope.LaunchAsync(ProbePath, work.Path, "answering-background", pipe, ready);

        await Assert.That(await AppearsAsync(ready)).IsTrue()
            .Because($"the answering stand-in never wrote '{ready}'. It said: {await scope.SaidByAsync(standIn.Id)}");

        await StarvedPathFinishesAsync(scope, work.Path, "ask", pipe);
    }

    /// <summary>
    /// An open refused for a sharing violation is waited out while a timer on the same pool
    /// of one worker lets go of the file.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLockRetryWaitsOutAHeldFileOnAPoolOfOneWorker()
    {
        using var work = await ScratchDirectory.CreateAsync("starved-rename");
        using var scope = new JobObjectScope();

        await StarvedPathFinishesAsync(scope, work.Path, "rename-window", Path.Combine(work.Path, "held.lock"));
    }

    /// <summary>
    /// A child that writes at once is read while another child's read is pending, on a pool
    /// of one worker.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AChildsOutputIsReadOnAPoolOfOneWorker()
    {
        using var work = await ScratchDirectory.CreateAsync("starved-pipes");
        using var scope = new JobObjectScope();

        await StarvedPathFinishesAsync(scope, work.Path, "child-pipes", "-");
    }

    /// <summary>Runs one path in a child whose pool has one worker, and holds how it came out.</summary>
    /// <param name="scope">The arm's job.</param>
    /// <param name="workingDirectory">The child's working directory.</param>
    /// <param name="scenario">The path.</param>
    /// <param name="argument">Its argument.</param>
    /// <returns>The assertions.</returns>
    private static async Task StarvedPathFinishesAsync(JobObjectScope scope, string workingDirectory, string scenario, string argument)
    {
        var environment = ChildEnvironment.Build([new("DOTNET_PROCESSOR_COUNT", "1")]);
        var bound = TestDefaults.InProcessHang.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);
        var starved = await scope.LaunchWithAsync(ProbePath, workingDirectory, environment, "starved", scenario, argument, bound);

        var finished = await starved.WaitForExitAsync(TestDefaults.ProcessHang);
        var said = await scope.SaidByAsync(starved.Id);

        await Assert.That(finished).IsTrue()
            .Because($"the '{scenario}' path had not finished {TestDefaults.ProcessHang.TotalMinutes:F0} minutes after it started on a pool of one worker, which is a path that holds its thread while it waits. It said: {said}");

        // ⚠️ THE PREMISE: the pool really was one worker, or finishing proves nothing.
        await Assert.That(said).Contains("workers=1 ").Because($"the child's pool was not capped at one worker. It said: {said}");

        await Assert.That(starved.TryReadExitCode()).IsEqualTo(0).Because(said);
        await Assert.That(said).Contains("ok: ");
    }

    /// <summary>Whether a file appears within the hang detector, read without parking a thread.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Whether it appeared.</returns>
    private static async Task<bool> AppearsAsync(string path)
    {
        using var deadline = new CancellationTokenSource(TestDefaults.ProcessHang);

        while (!File.Exists(path))
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return File.Exists(path);
            }
        }

        return true;
    }
}
