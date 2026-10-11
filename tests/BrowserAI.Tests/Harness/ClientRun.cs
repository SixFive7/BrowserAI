// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A process an arm starts for a real client, or for what drives one or answers it: its
/// streams drained into files beside its working directory, and ended when the arm ends.
/// </summary>
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

    private readonly Process _process;

    private ClientRun(Process process) => _process = process;

    /// <summary>The process's pid.</summary>
    public int Id => _process.Id;

    /// <summary>Whether it has exited.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Starts a process with its streams redirected and no console.</summary>
    /// <param name="executable">What to start, by its absolute path.</param>
    /// <param name="workingDirectory">Where, and where its streams' files go.</param>
    /// <param name="environment">What to add to the inherited environment.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The run.</returns>
    public static ClientRun Start(
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(arguments);

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var inherited in ParentClientVariables)
        {
            _ = start.Environment.Remove(inherited);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        var process = Process.Start(start)
            ?? throw new InvalidOperationException($"'{executable}' did not start.");

        // Drained INTO FILES, not into nowhere. A child whose redirected pipe fills
        // stops writing and then stops working, so the drain is mandatory, and a real
        // client's own complaint is the only thing that explains a run that did
        // nothing, so it is kept where a failure can quote it.
        var stem = StemOf(workingDirectory, executable);

        _ = Drain(process.StandardOutput, $"{stem}.stdout.txt");
        _ = Drain(process.StandardError, $"{stem}.stderr.txt");
        process.StandardInput.Close();

        return new ClientRun(process);
    }

    /// <summary>Where a run's two streams are kept, without their suffixes.</summary>
    /// <param name="workingDirectory">The run's working directory.</param>
    /// <param name="executable">What was started.</param>
    /// <returns>The stem.</returns>
    public static string StemOf(string workingDirectory, string executable) =>
        Path.Combine(workingDirectory, Path.GetFileNameWithoutExtension(executable));

    /// <summary>Waits for the process to exit.</summary>
    /// <param name="timeout">The hang detector.</param>
    /// <returns>Whether it exited within it.</returns>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);

        try
        {
            await _process.WaitForExitAsync(deadline.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Ends the process this run started, if it is still running.</summary>
    /// <remarks>
    /// ⚠️ <b>Because <c>Process.Dispose</c> releases a handle and does not end a process,
    /// and an API stub never exits on its own.</b> Measured: four leaked <c>node.exe</c>
    /// stubs from four runs of the real-client arms held four scratch directories open,
    /// which the next run's reclaim pass could not delete.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                // This one and not the tree: Process.Kill(entireProcessTree: true) is
                // banned repository-wide.
                _process.Kill();
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
            // It has already gone, which is the ordinary case.
        }

        _process.Dispose();
    }

    /// <summary>Copies a redirected stream into a file as it arrives.</summary>
    /// <param name="stream">The reader.</param>
    /// <param name="path">Where to put it.</param>
    /// <returns>The copy.</returns>
    private static async Task Drain(StreamReader stream, string path)
    {
        var text = await stream.ReadToEndAsync();

        await File.WriteAllTextAsync(path, text);
    }
}
