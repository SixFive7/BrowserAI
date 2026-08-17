// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Text;
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
    private readonly ConcurrentDictionary<int, StringBuilder> _said = [];

    /// <summary>Starts a process inside this scope's job.</summary>
    /// <param name="command">The executable's absolute path.</param>
    /// <param name="workingDirectory">The working directory it is given.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The started process. The scope owns it.</returns>
    public LaunchedProcess Launch(string command, string workingDirectory, params string[] arguments)
    {
        var process = JobLauncher.Start(_job, command, arguments, workingDirectory, ChildEnvironment.Build());
        _started.Add(process);

        // ⚠️ Drained because an undrained pipe stops the child once its buffer
        // fills -- which presents as "the launcher never finished" rather than
        // as a full pipe -- and KEPT because a discarded one is worse.
        //
        // Corrected 2026-08-17 (previously "nothing in the suite reads these",
        // and they were thrown away). A real Chromium launched here died 267 ms
        // in during a fully parallel run, and the only thing any assertion could
        // say was that it was gone: the browser's own account of why went into a
        // 4 KiB buffer and was discarded a line later. That is this repository's
        // founding failure class inside the harness that exists to catch it.
        var said = _said.GetOrAdd(process.Id, _ => new StringBuilder());

        Drain(process.StandardOutput, said);
        Drain(process.StandardError, said);

        return process;
    }

    /// <summary>
    /// Everything a process this scope started has written to either stream.
    /// </summary>
    /// <remarks>
    /// <b>For failure messages, and only for them.</b> No assertion should read
    /// this: what a browser writes to stderr is upstream's business and changes
    /// between revisions. What it is for is the sentence a test prints when the
    /// process it was asserting about is no longer there.
    /// </remarks>
    /// <param name="processId">The pid <see cref="Launch"/> returned.</param>
    /// <returns>The captured text, or a note that there was none.</returns>
    public string SaidBy(int processId)
    {
        if (!_said.TryGetValue(processId, out var said))
        {
            return "<this scope never started that pid>";
        }

        lock (said)
        {
            return said.Length is 0 ? "<it wrote nothing to either stream>" : said.ToString();
        }
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

    private static void Drain(Stream stream, StringBuilder said) =>
        _ = Task.Run(async () =>
        {
            var buffer = new byte[4096];

            try
            {
                int read;

                while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, read);

                    lock (said)
                    {
                        // Bounded: a chatty child must not turn a failure message
                        // into a megabyte, and the first lines are the ones that
                        // say why something would not start.
                        if (said.Length < 16 * 1024)
                        {
                            _ = said.Append(text);
                        }
                    }
                }
            }
#pragma warning disable CA1031 // The stream closing under a read in flight is how this loop normally ends.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        });
}
