// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
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
        IReadOnlyList<string> relayArguments) =>
        Start(job, workingDirectory, environment, pipe, relayArguments, standardError: null, out _);

    /// <summary>
    /// Starts a background in a job, drains both its streams, keeps what it writes to
    /// stderr when asked to, and waits for its pipe.
    /// </summary>
    /// <remarks>
    /// <b>Both streams are read, always, since 2026-10-09.</b> The background writes
    /// every record to stderr as well as to its log, through a console logger whose
    /// queue blocks the caller once it is full, and an anonymous pipe nobody reads
    /// takes about 4 KiB: a background the harness started and never read would stop
    /// on its own logging after enough records, which a hundred sessions reach.
    /// </remarks>
    /// <param name="job">The job it runs in.</param>
    /// <param name="workingDirectory">Its working directory.</param>
    /// <param name="environment">Its environment.</param>
    /// <param name="pipe">The pipe it serves.</param>
    /// <param name="relayArguments">The relay's arguments, whose data root, if any, the background takes too.</param>
    /// <param name="standardError">Where its stderr goes, line by line, or <see langword="null"/> to drop it.</param>
    /// <param name="standardErrorPump">The read of its stderr, which ends when every holder of the write end has gone.</param>
    /// <returns>The background.</returns>
    /// <exception cref="InvalidOperationException">It ended, or opened no pipe within <see cref="StartBound"/>.</exception>
    public static LaunchedProcess Start(
        JobObject job,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string pipe,
        IReadOnlyList<string> relayArguments,
        StringBuilder? standardError,
        out Task standardErrorPump)
    {
        List<string> arguments = [Program.BackgroundArgument, BackgroundPipe.PipeArgument, pipe];

        if (Program.ValueOf(relayArguments, Program.DataRootArgument) is { Length: > 0 } root)
        {
            arguments.AddRange([Program.DataRootArgument, root]);
        }

        var background = JobLauncher.Start(job, PublishedSlice.Executable, arguments, workingDirectory, environment);

        _ = PumpAsync(background.StandardOutput, into: null);
        standardErrorPump = PumpAsync(background.StandardError, standardError);

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

    /// <summary>Reads a stream line by line to its end, keeping the lines when asked to.</summary>
    /// <param name="stream">One of the background's streams.</param>
    /// <param name="into">Where the lines go, or <see langword="null"/>.</param>
    /// <returns>A task that ends at the stream's end, or when it is closed under the read.</returns>
    private static async Task PumpAsync(Stream stream, StringBuilder? into)
    {
        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (into is not null)
                {
                    lock (into)
                    {
                        _ = into.AppendLine(line);
                    }
                }
            }
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException)
        {
            // The pipe closing under a read in flight is how this ends when the
            // launched process is disposed first.
        }
    }

    /// <summary>The record a background on a pipe keeps, under the data root the relay's arguments name.</summary>
    /// <remarks>
    /// <i>Changed 2026-10-08 by step 5 of the one-binary build (previously the relay's
    /// argument, else the environment's <c>BROWSERAI_ROOT</c>)</i>: no running BrowserAI
    /// reads the variable, so neither does this.
    /// </remarks>
    /// <param name="environment">The environment the background was started with, which names no data root.</param>
    /// <param name="relayArguments">The relay's arguments.</param>
    /// <param name="pipe">The pipe.</param>
    /// <returns>The record's path.</returns>
    public static string RecordFor(IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> relayArguments, string pipe)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var root = Program.ValueOf(relayArguments, Program.DataRootArgument) ?? LocalAppDataPaths.Default;

        return BackgroundRecord.PathFor(root, pipe);
    }
}
