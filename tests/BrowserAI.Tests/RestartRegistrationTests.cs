// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What the restart registration reader answers for a process that runs, and for one
/// that has exited while a handle to it is still open.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-11, after the first gate of that day after the maintainer's
/// decision 54</b>: <c>BrowserContainmentTests.AChromiumTreeIsContainedAndItsProfileDeletesCleanly</c>
/// went red at async-ps-1 on <i>"pid 79676 answered 0x8007012B rather than
/// ERROR_NOT_FOUND"</i>, for a Chromium process of the walk that had exited by the time
/// it was asked. <c>GetApplicationRestartSettings</c> reads the registration out of the
/// process's address space, and an exited process's is gone: measured that day, a
/// <c>cmd.exe</c> that had exited with a handle to it still open answered 0x8007012B,
/// <c>HRESULT_FROM_WIN32(ERROR_PARTIAL_COPY)</c>, and one that ran answered 0x80070490.
/// </para>
/// <para>
/// <b>The exited probe is held by the arm's job</b>, whose launch keeps the process
/// handle, so its pid still opens after it has gone, as a walked browser's did. The
/// running one has written its ready file before it is asked, so it is past its start.
/// </para>
/// </remarks>
internal sealed class RestartRegistrationTests
{
    /// <summary>The test probe, which plays both processes.</summary>
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// A process that has exited reads as gone, and one that runs, registered for
    /// nothing, reads as not registered.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProcessThatHasExitedReadsAsGoneAndOneThatRunsReadsAsNotRegistered()
    {
        using var work = await ScratchDirectory.CreateAsync("restart-registration");
        using var scope = new JobObjectScope();

        var exited = await scope.LaunchAsync(ProbePath, work.Path, "say", "an exited probe");

        await Assert.That(await exited.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await Assert.That(RestartRegistration.Of(exited.Id)).IsEqualTo(RestartRegistration.Gone);

        var ready = Path.Combine(work.Path, "ready.json");
        var running = await scope.LaunchAsync(ProbePath, work.Path, "hold-file", Path.Combine(work.Path, "held.txt"), ready);

        await Assert.That(await AppearsWithinAsync(ready, TestDefaults.ProcessHang)).IsTrue()
            .Because($"the holding probe never wrote its ready file. It said: {await scope.SaidByAsync(running.Id)}");
        await Assert.That(RestartRegistration.Of(running.Id)).IsEqualTo(RestartRegistration.NotRegistered);
    }

    /// <summary>Whether a file appears within a bound, read without parking a thread.</summary>
    /// <param name="path">The file.</param>
    /// <param name="patience">The hang detector.</param>
    /// <returns>Whether it appeared.</returns>
    private static async Task<bool> AppearsWithinAsync(string path, TimeSpan patience)
    {
        using var deadline = new CancellationTokenSource(patience);

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
