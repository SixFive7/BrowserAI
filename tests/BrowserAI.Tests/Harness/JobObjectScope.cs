// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Protocol;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A job object the <b>suite</b> owns, so that a failed assertion cannot leave
/// a process behind.
/// </summary>
/// <remarks>
/// <para>
/// Every process a lifecycle test starts goes in here. When the scope is
/// disposed — including by an exception unwinding past it — the handle closes
/// and <c>KILL_ON_JOB_CLOSE</c> takes the whole tree with it. <b>A leaked
/// process is a defect in the test, not an acceptable cost</b>, and the
/// mechanism that guarantees it is the same one the product relies on rather
/// than a second, weaker one written for tests.
/// </para>
/// <para>
/// <b>It also puts the code under test inside somebody else's job</b>, which is
/// the realistic production shape: any MCP client that spawns BrowserAI through
/// Node's <c>child_process</c> puts it inside libuv's. Containment has to hold
/// through nesting, and here it is nested on every run rather than in one test
/// that remembers to check.
/// </para>
/// </remarks>
internal sealed class JobObjectScope : IDisposable
{
    private readonly JobObject _job = JobObject.CreateKillOnClose();
    private readonly List<LaunchedProcess> _started = [];

    /// <summary>Starts a process inside this scope's job.</summary>
    /// <param name="command">The executable's absolute path.</param>
    /// <param name="workingDirectory">The working directory it is given.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The started process. The scope owns it.</returns>
    public LaunchedProcess Launch(string command, string workingDirectory, params string[] arguments)
    {
        var process = JobLauncher.Start(_job, command, arguments, workingDirectory, ChildEnvironment.Build());
        _started.Add(process);

        // Nothing in the suite reads these, and an undrained pipe stops the
        // child once its buffer fills -- which presents as "the launcher never
        // finished" rather than as a full pipe.
        Drain(process.StandardOutput);
        Drain(process.StandardError);

        return process;
    }

    /// <summary>
    /// Every process the kernel currently reports in this scope's job.
    /// </summary>
    /// <remarks>
    /// The kernel's own membership list rather than a tally the harness keeps,
    /// which is what makes "nothing was started" an assertion about the machine
    /// instead of about the test's bookkeeping — and it is unaffected by
    /// whatever else on the machine is starting at the same moment.
    /// </remarks>
    /// <returns>The pids.</returns>
    public IReadOnlyList<int> ProcessIds() => _job.ProcessIds();

    /// <inheritdoc />
    public void Dispose()
    {
        // The job first: it is what actually stops anything still running, and
        // disposing the process objects only closes handles.
        _job.Dispose();

        foreach (var process in _started)
        {
            process.Dispose();
        }

        _started.Clear();
    }

    private static void Drain(Stream stream) =>
        _ = Task.Run(async () =>
        {
            var buffer = new byte[4096];

            try
            {
                while (await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
                {
                    // Discarded deliberately.
                }
            }
#pragma warning disable CA1031 // The stream closing under a read in flight is how this loop normally ends.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        });
}
