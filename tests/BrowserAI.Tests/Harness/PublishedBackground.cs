// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The background a published relay of the suite talks to, started by the harness in
/// the relay's own job, on a pipe of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>D11 a, the maintainer's words of 2026-10-08, verbatim: <i>"d11 a"</i></b>: a
/// build that is not installed starts no background, and the harness starts one with
/// <c>--background</c>, outside every client's tree. Here the "client" is the suite, so
/// the background shares the relay's kill-on-close job: closing the job ends both and
/// every browser the background started, which is the containment every published arm
/// has always had.
/// </para>
/// <para>
/// <b>A pipe of its own for every relay</b>, through the suite's
/// <see cref="BackgroundPipe.PipeArgument"/> seam, because the suite's arms run in
/// parallel over one data root and one background per data root would be one
/// background for every arm at once, ended by whichever arm finished first.
/// </para>
/// </remarks>
internal static class PublishedBackground
{
    /// <summary>
    /// How long the harness waits for a background it started to open its pipe: a hang
    /// detector over a start measured at a few hundred milliseconds.
    /// </summary>
    public static TimeSpan StartBound => TestDefaults.ProcessHang;

    /// <summary>A pipe no other background of this run uses.</summary>
    /// <returns>The full pipe name.</returns>
    public static string NewPipeName() =>
        $"{BackgroundPipe.NamePrefix}suite-{Environment.ProcessId}-{Guid.NewGuid():N}";

    /// <summary>Whether a start of this command with these arguments is a published relay.</summary>
    /// <param name="command">The command.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>Whether the harness starts a background beside it.</returns>
    public static bool IsAPublishedRelay(string command, IReadOnlyList<string> arguments) =>
        string.Equals(Path.GetFullPath(command), Path.GetFullPath(PublishedSlice.Executable), StringComparison.OrdinalIgnoreCase)
        && arguments.Contains(Program.McpArgument, StringComparer.Ordinal)
        && !arguments.Contains(BackgroundPipe.PipeArgument, StringComparer.Ordinal);

    /// <summary>Starts a background in a job and waits for its pipe.</summary>
    /// <param name="job">The job it runs in, the relay's.</param>
    /// <param name="workingDirectory">Its working directory.</param>
    /// <param name="environment">Its environment, the relay's.</param>
    /// <param name="pipe">The pipe it serves.</param>
    /// <param name="relayArguments">The relay's arguments, whose data root, if any, the background takes too.</param>
    /// <returns>The background.</returns>
    /// <exception cref="InvalidOperationException">It ended, or opened no pipe within <see cref="StartBound"/>.</exception>
    public static LaunchedProcess Start(
        JobObject job,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string pipe,
        IReadOnlyList<string> relayArguments)
    {
        List<string> arguments = [Program.BackgroundArgument, BackgroundPipe.PipeArgument, pipe];

        if (Program.ValueOf(relayArguments, Program.DataRootArgument) is { Length: > 0 } root)
        {
            arguments.AddRange([Program.DataRootArgument, root]);
        }

        var background = JobLauncher.Start(job, PublishedSlice.Executable, arguments, workingDirectory, environment);
        var waited = Stopwatch.StartNew();

        while (!NamedPipes.WaitForFreeInstance(pipe, 1))
        {
            if (background.HasExited)
            {
                throw new InvalidOperationException(
                    $"The published background (pid {background.Id}) ended with {background.TryReadExitCode()} before it opened {pipe}. Its log is under the data root's logs folder.");
            }

            if (waited.Elapsed > StartBound)
            {
                throw new InvalidOperationException($"The published background (pid {background.Id}) opened no pipe {pipe} within {StartBound}.");
            }

            Thread.Sleep(25);
        }

        return background;
    }

    /// <summary>The record a background on a pipe keeps, under the data root the environment names.</summary>
    /// <param name="environment">The environment the background was started with.</param>
    /// <param name="relayArguments">The relay's arguments.</param>
    /// <param name="pipe">The pipe.</param>
    /// <returns>The record's path.</returns>
    public static string RecordFor(IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> relayArguments, string pipe)
    {
        var root = Program.ValueOf(relayArguments, Program.DataRootArgument)
            ?? (environment.TryGetValue(LocalAppDataPaths.RootVariable, out var named) && named is { Length: > 0 } ? named : LocalAppDataPaths.Default);

        return BackgroundRecord.PathFor(root, pipe);
    }
}
