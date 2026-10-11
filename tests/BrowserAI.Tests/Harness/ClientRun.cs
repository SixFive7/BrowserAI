// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A process an arm starts for a real client, or for what drives one or answers it: in a
/// kill-on-close job of the arm's own, inheriting its three pipes and nothing else, its
/// streams kept in files beside its working directory.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>64 c (b), the maintainer's words verbatim: <i>"64 c now"</i>.</b> Until 2026-10-11
/// these arms started a real Claude Code, Codex's driver and the API stub with
/// <c>Process.Start</c> and no job: every inheritable handle of the test host went to the
/// client, and what the client started could outlive the arm. That day a real Claude Code's
/// git launcher left a suspended git behind, pid 102292, holding the test host's standard
/// output, which held a gate driver's pipe until the git was ended by hand (HAZARDS.md, the
/// frozen-child wedge).
/// </para>
/// <para>
/// <b>Through <see cref="JobLauncher.Start"/></b>, which makes the job a creation attribute
/// and the inherited set an explicit list of the child's three pipe ends. A child of the
/// client is in the job from its first instruction, the job's close at the end of the arm
/// ends every one of them, and no other handle of the test host reaches the client.
/// <c>ClientRunTests.AClientAnArmStartedLeavesNoProcessBehindOnceTheArmEnds</c> holds it.
/// </para>
/// </remarks>
internal sealed class ClientRun : IDisposable
{
    /// <summary>
    /// The variables that say the suite is running underneath a real client, which a child
    /// of that suite must not inherit.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Removed before anything is added, and this is why the first real-client arm
    /// existed for an hour without working.</b> The suite may itself be running underneath
    /// one of these clients, which is the ordinary case on this machine, and the variables
    /// that say so are inherited by every child. A nested client that reads them takes a
    /// different path entirely: measured 2026-09-24, the client connected to BrowserAI,
    /// asked the stub nothing but <c>HEAD /api/hello</c>, and exited, so the arm saw one
    /// server process and failed on its own premise with nothing to say about why. The
    /// rig's shell drivers unset the same variables for the same reason.
    /// </remarks>
    public static IReadOnlyList<string> ParentClientVariables { get; } =
    [
        "CLAUDECODE",
        "CLAUDE_CODE_ENTRYPOINT",
        "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CONFIG_DIR",
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_AUTH_TOKEN",
        "CODEX_HOME",
    ];

    private readonly JobObject _job;
    private readonly LaunchedProcess _process;

    private ClientRun(JobObject job, LaunchedProcess process)
    {
        _job = job;
        _process = process;
    }

    /// <summary>The process's pid.</summary>
    public int Id => _process.Id;

    /// <summary>Whether it has exited.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Starts a process in a job of its own, with no console.</summary>
    /// <param name="executable">What to start, by its absolute path.</param>
    /// <param name="workingDirectory">Where, and where its streams' files go.</param>
    /// <param name="environment">What to add to the inherited environment.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The run. Disposing it ends the process and everything it started.</returns>
    public static async Task<ClientRun> StartAsync(
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(arguments);

        // The whole of the test host's environment, as Process.Start passed it, without
        // the variables that would make the client a nested one.
        var block = PublishedSlice.InheritedEnvironment();

        foreach (var inherited in ParentClientVariables)
        {
            _ = block.Remove(inherited);
        }

        foreach (var (name, value) in environment)
        {
            block[name] = value;
        }

        var job = JobObject.CreateKillOnClose();

        try
        {
            var process = JobLauncher.Start(job, executable, arguments, workingDirectory, block);

            // Written where the NEXT run can read it, as JobObjectScope does: the job is
            // what contains this process while this run is alive, and the record is what
            // names it if this run is killed and the job dies with it.
            await SpawnRecord.AddAsync(process.Id);

            // Drained INTO FILES, not into nowhere. A child whose redirected pipe fills
            // stops writing and then stops working, so the drain is mandatory, and a real
            // client's own complaint is the only thing that explains a run that did
            // nothing, so it is kept where a failure can quote it.
            var stem = StemOf(workingDirectory, executable);

            _ = Drain(process.StandardOutput, $"{stem}.stdout.txt");
            _ = Drain(process.StandardError, $"{stem}.stderr.txt");
            await process.StandardInput.DisposeAsync();

            return new ClientRun(job, process);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    /// <summary>Where a run's two streams are kept, without their suffixes.</summary>
    /// <param name="workingDirectory">The run's working directory.</param>
    /// <param name="executable">What was started.</param>
    /// <returns>The stem.</returns>
    public static string StemOf(string workingDirectory, string executable) =>
        Path.Combine(workingDirectory, Path.GetFileNameWithoutExtension(executable));

    /// <summary>Waits for the process to exit, through a wait registration that parks no thread.</summary>
    /// <param name="timeout">The hang detector.</param>
    /// <returns>Whether it exited within it.</returns>
    public Task<bool> WaitForExitAsync(TimeSpan timeout) => _process.WaitForExitAsync(timeout);

    /// <summary>Ends the process and everything it started, through the job's close.</summary>
    /// <remarks>
    /// <b>The job first</b>: it is what ends the client and every process the client
    /// started, and disposing the process object only closes handles. An API stub never
    /// exits on its own, and four leaked <c>node.exe</c> stubs from four runs once held four
    /// scratch directories open, which the next run's reclaim pass could not delete.
    /// </remarks>
    public void Dispose()
    {
        _job.Dispose();
        _process.Dispose();
    }

    /// <summary>Copies a stream into a file as it arrives, the file readable as it grows.</summary>
    /// <param name="stream">The child's end of one of its streams.</param>
    /// <param name="path">Where to put it.</param>
    /// <returns>The copy.</returns>
    private static async Task Drain(Stream stream, string path)
    {
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.Asynchronous);

        try
        {
            await stream.CopyToAsync(file);
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException)
        {
            // The job's close ended the writer, or the run's disposal closed the stream
            // under the read: either is how a drain ends, and what it copied stays.
        }
    }
}
