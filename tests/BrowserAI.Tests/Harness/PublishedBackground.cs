// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
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
    /// How long the harness waits for a background it started to open its pipe and write
    /// its record: a hang detector over a start measured at a few hundred milliseconds.
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
    public static async Task<LaunchedProcess> StartAsync(
        JobObject job,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string pipe,
        IReadOnlyList<string> relayArguments) =>
        (await StartAsync(job, workingDirectory, environment, pipe, relayArguments, standardError: null)).Process;

    /// <summary>
    /// Starts a background in a job, drains both its streams, keeps what it writes to
    /// stderr when asked to, and waits for its pipe and its record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both streams are read, always, since 2026-10-09.</b> The background writes
    /// every record to stderr as well as to its log, through a console logger whose
    /// queue blocks the caller once it is full, and an anonymous pipe nobody reads
    /// takes about 4 KiB: a background the harness started and never read would stop
    /// on its own logging after enough records, which a hundred sessions reach.
    /// </para>
    /// <para>
    /// ⚠️ <b>The pipe AND the record, since 2026-10-14, the maintainer's 171</b>, in his
    /// words verbatim: <i>"171 your recommendations"</i>. <i>Previously the pipe alone.</i>
    /// The background takes its pipe before it writes its record, and a client can open
    /// the pipe from that moment. A relay asked a call before its first look has
    /// connected holds it and reads that record to say why it has no background, and a
    /// build that is not installed with no record is answered at once that no background
    /// runs for it. In the Git Bash half of the gate at <c>24202002</c> the kill arm's
    /// first call met exactly that, 0.7 s after this wait had seen the pipe, while the
    /// background had logged its start and not yet its record (the hazard index). A
    /// person starts a background before a client, so nothing outside the suite meets
    /// the gap this way; the wait reads the record through the product's own reader and
    /// requires it to name this process, by pid and creation time, with no end.
    /// <b>Planted red 2026-10-14</b> in a filtered run against a build never committed, in
    /// which the background held its record back 5 s after taking its pipe and the relay
    /// held its first look back 2 s: with the wait for the pipe alone, the kill arm's
    /// <c>browserai_init</c> was refused in the gate's own words, no background running for
    /// this build, its relay answering the call 64 ms after the background's start; with this wait the
    /// same build passed the arm, the record written 5.0 s after the background's start and
    /// the relay started 51 ms after that.
    /// </para>
    /// </remarks>
    /// <param name="job">The job it runs in.</param>
    /// <param name="workingDirectory">Its working directory.</param>
    /// <param name="environment">Its environment.</param>
    /// <param name="pipe">The pipe it serves.</param>
    /// <param name="relayArguments">The relay's arguments, whose data root, if any, the background takes too.</param>
    /// <param name="standardError">Where its stderr goes, line by line, or <see langword="null"/> to drop it.</param>
    /// <param name="stampEachLine">
    /// Whether each line kept in <paramref name="standardError"/> opens with the moment it
    /// was read, since the console the background writes to carries no time of its own.
    /// <i>Added 2026-10-14 with the maintainer's 171</i>, for the stop of
    /// <c>BackgroundProcessTests</c>, whose kept rig says when each line of a slow close came.
    /// </param>
    /// <returns>
    /// The background, and the read of its stderr, which ends when every holder of the
    /// write end has gone. <i>Returned beside it since 2026-10-11, when the start became
    /// awaited (previously an out parameter, which an asynchronous method cannot have).</i>
    /// </returns>
    /// <exception cref="InvalidOperationException">It ended, or opened no pipe within <see cref="StartBound"/>.</exception>
    public static async Task<(LaunchedProcess Process, Task StandardErrorPump)> StartAsync(
        JobObject job,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string pipe,
        IReadOnlyList<string> relayArguments,
        StringBuilder? standardError,
        bool stampEachLine = false)
    {
        List<string> arguments = [Program.BackgroundArgument, BackgroundPipe.PipeArgument, pipe];

        if (Program.ValueOf(relayArguments, Program.DataRootArgument) is { Length: > 0 } root)
        {
            arguments.AddRange([Program.DataRootArgument, root]);
        }

        var background = JobLauncher.Start(job, PublishedSlice.Executable, arguments, workingDirectory, environment);

        _ = PumpAsync(background.StandardOutput, into: null, stamp: false);
        var standardErrorPump = PumpAsync(background.StandardError, standardError, stampEachLine);

        var record = RecordFor(environment, relayArguments, pipe);
        var created = ProcessIdentity.CreationTimeOf(background.Id);
        var waited = Stopwatch.StartNew();

        while (!NamedPipes.HasFreeInstanceNow(pipe) || !await NamesAsync(record, background.Id, created))
        {
            if (background.HasExited)
            {
                throw new InvalidOperationException(
                    $"The published background (pid {background.Id}) ended with {background.TryReadExitCode()} before it opened {pipe} and wrote its record. Its log is under the data root's logs folder.");
            }

            if (waited.Elapsed > StartBound)
            {
                throw new InvalidOperationException(
                    $"The published background (pid {background.Id}) had not both opened {pipe} and written its record at {record} within {StartBound}: "
                    + $"the pipe {(NamedPipes.HasFreeInstanceNow(pipe) ? "has" : "has no")} free instance, and the record {(await NamesAsync(record, background.Id, created) ? "names it" : "does not name it")}.");
            }

            await Task.Delay(25);
        }

        return (background, standardErrorPump);
    }

    /// <summary>Whether a background's record names this process as a background that runs.</summary>
    /// <remarks>
    /// Read through <see cref="BackgroundRecord.ReadAsync"/>, the reader a relay asks, which
    /// answers <see langword="null"/> while the record is absent, unreadable or being moved
    /// into place.
    /// </remarks>
    /// <param name="record">The record's path.</param>
    /// <param name="processId">The background's pid.</param>
    /// <param name="created">Its creation time.</param>
    /// <returns>Whether the record names it, with no end.</returns>
    private static async Task<bool> NamesAsync(string record, int processId, long created) =>
        await BackgroundRecord.ReadAsync(record) is { Ended: null } state
            && state.ProcessId == processId
            && state.CreatedFileTime == created;

    /// <summary>Reads a stream line by line to its end, keeping the lines when asked to.</summary>
    /// <param name="stream">One of the background's streams.</param>
    /// <param name="into">Where the lines go, or <see langword="null"/>.</param>
    /// <param name="stamp">Whether each kept line opens with the moment it was read, in UTC.</param>
    /// <returns>A task that ends at the stream's end, or when it is closed under the read.</returns>
    private static async Task PumpAsync(Stream stream, StringBuilder? into, bool stamp)
    {
        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (into is not null)
                {
                    var kept = stamp ? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + line : line;

                    lock (into)
                    {
                        _ = into.AppendLine(kept);
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
