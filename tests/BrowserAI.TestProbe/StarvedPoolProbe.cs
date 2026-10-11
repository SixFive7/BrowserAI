// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Sessions;

namespace BrowserAI.TestProbe;

/// <summary>
/// The product's waiting paths, each run on a thread pool of one worker, and the stand-ins
/// they wait on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-11 for the maintainer's decision 54, in his words verbatim: <i>"Never
/// block a worker anywhere."</i></b> A path that waits without a thread needs the pool only
/// to run what comes after the wait, so it finishes on a pool of one worker. A path that
/// parks its thread while it waits holds that one worker, and whatever it waits for that
/// needs a worker too, an I/O completion, a timer or a second read, never runs.
/// </para>
/// <para>
/// <b>The pool is one worker by construction</b>: the suite starts this process with
/// <c>DOTNET_PROCESSOR_COUNT=1</c>, which the runtime reads as the processor count, so
/// <see cref="ThreadPool.SetMaxThreads"/> accepts one worker and one completion thread,
/// and the first line this process writes says what the pool's maximum then is. The path
/// under test runs on that worker, inside <see cref="Task.Run(Func{Task})"/>, as product
/// code runs on the pool.
/// </para>
/// </remarks>
internal static class StarvedPoolProbe
{
    /// <summary>How long the lock retry's file stays held before the timer lets go of it.</summary>
    /// <remarks>
    /// Long enough that the first open is refused and the wait has begun, and short beside
    /// the bound the suite gives the path. It is not a bound on anything.
    /// </remarks>
    private static readonly TimeSpan ReleaseAfter = TimeSpan.FromMilliseconds(200);

    /// <summary>Caps the pool at one worker, runs one waiting path on it, and says how it came out.</summary>
    /// <param name="scenario">Which path: <c>ask</c>, <c>rename-window</c> or <c>child-pipes</c>.</param>
    /// <param name="argument">The path's one argument: a pipe, a file or nothing.</param>
    /// <param name="boundSeconds">The path's own bound, in seconds, which the suite derives from its hang detector.</param>
    /// <returns>Zero when the path finished as it should, one when it did not.</returns>
    public static async Task<int> RunAsync(string scenario, string argument, string boundSeconds)
    {
        var bound = TimeSpan.FromSeconds(double.Parse(boundSeconds, CultureInfo.InvariantCulture));

        _ = ThreadPool.SetMaxThreads(1, 1);
        ThreadPool.GetMaxThreads(out var workers, out var completions);

        await SayLineAsync(string.Create(CultureInfo.InvariantCulture, $"workers={workers} completions={completions} processors={Environment.ProcessorCount}"));

        var outcome = scenario switch
        {
            "ask" => await Task.Run(() => AskAsync(argument, bound)),
            "rename-window" => await Task.Run(() => WaitOutAsync(argument, bound)),
            "child-pipes" => await Task.Run(() => ReadTwoChildrenAsync(bound)),
            _ => $"failed: no scenario called '{scenario}'",
        };

        await SayLineAsync(outcome);
        return outcome.StartsWith("ok", StringComparison.Ordinal) ? 0 : 1;
    }

    /// <summary>A background stand-in that answers one connection's first request with an address.</summary>
    /// <param name="pipeName">The pipe it serves.</param>
    /// <param name="readyPath">The file it writes once it is listening.</param>
    /// <returns>Zero once its standard input has closed.</returns>
    public static async Task<int> AnsweringBackgroundAsync(string pipeName, string readyPath)
    {
        using var listening = NamedPipes.CreateStreamServer(pipeName);

        await File.WriteAllTextAsync(readyPath, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        if (await NamedPipes.WaitForClientAsync(listening, CancellationToken.None))
        {
            await AnswerAsync(listening);
        }

        // Then until the suite closes this process's standard input, or ends its job.
        await UntilTheInputEndsAsync();

        return 0;
    }

    /// <summary>A child that writes nothing and ends once its standard input has closed.</summary>
    /// <returns>Zero.</returns>
    public static async Task<int> QuietAsync()
    {
        await UntilTheInputEndsAsync();

        return 0;
    }

    /// <summary>A child that writes one line and ends.</summary>
    /// <param name="text">The line.</param>
    /// <returns>Zero.</returns>
    public static async Task<int> SayAsync(string text)
    {
        await SayLineAsync(text);
        return 0;
    }

    /// <summary>Reads this process's standard input until it ends, through the product's channel.</summary>
    /// <remarks>
    /// The channel reads the handle on a thread of its own, so the wait holds no worker of
    /// the pool. <i>Since 2026-10-11 (previously the console stream's <c>ReadAsync</c>, which
    /// parked a pool thread in a synchronous read for as long as the input stayed open).</i>
    /// </remarks>
    /// <returns>The read.</returns>
    private static async Task UntilTheInputEndsAsync()
    {
        using var channel = StdioChannel.OpenStandardStreams();
        var buffer = new byte[64];

        while (await channel.Input.ReadAsync(buffer) > 0)
        {
        }
    }

    /// <summary>Writes one line to this process's standard output through the product's pump, in the console's encoding.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The write, once the line is on the wire.</returns>
    private static async Task SayLineAsync(string line)
    {
#pragma warning disable CA2000 // The pump disposes the stream it wraps.
        await using var output = StdioChannel.PumpWrites(Console.OpenStandardOutput());
#pragma warning restore CA2000

        await output.WriteAsync(Console.OutputEncoding.GetBytes(line + Environment.NewLine));
        await output.FlushAsync();
    }

    /// <summary>The pipe client: one request to the background, and its answer.</summary>
    private static async Task<string> AskAsync(string pipeName, TimeSpan bound)
    {
        var answer = await BackgroundClient.AskAsync(pipeName, BackgroundPipe.Show, null, bound);

        return answer.Outcome is BackgroundAnswerOutcome.Answered
            ? $"ok: answered with {answer.ResultMember("address")}"
            : $"failed: {answer.Outcome}, {answer.Sentence}";
    }

    /// <summary>
    /// The lock retry: an open refused for a sharing violation, waited out while a timer on
    /// the same pool lets go of the file.
    /// </summary>
    private static async Task<string> WaitOutAsync(string file, TimeSpan bound)
    {
        // Held without sharing, so every open of it is refused for a sharing violation,
        // which is the refusal this wait exists to wait out.
        var holder = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);

        // The thing the wait needs: a pool callback that lets go of the file. A wait that
        // holds the one worker keeps this from ever running.
        await using var release = new Timer(static state => ((FileStream)state!).Dispose(), holder, ReleaseAfter, Timeout.InfiniteTimeSpan);

        using var deadline = new CancellationTokenSource(bound);

        try
        {
            await using var opened = await RenameWindow.WaitOutWhereNoOwnerIsPossibleAsync(
                () => new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.Asynchronous),
                deadline.Token);

            return "ok: opened once the timer had let go of the file";
        }
        catch (IOException failure)
        {
            return $"failed: {failure.Message}";
        }
        finally
        {
            await holder.DisposeAsync();
        }
    }

    /// <summary>
    /// The child pipes: a quiet child's output read beside a child that writes at once, both
    /// pending on one pool worker.
    /// </summary>
    private static async Task<string> ReadTwoChildrenAsync(TimeSpan bound)
    {
        using var job = JobObject.CreateKillOnClose();

        var probe = Environment.ProcessPath!;
        var environment = ChildEnvironment.Build();
        using var quiet = JobLauncher.Start(job, probe, ["quiet"], Environment.CurrentDirectory, environment);
        using var loud = JobLauncher.Start(job, probe, ["say", "the loud child"], Environment.CurrentDirectory, environment);

        // The quiet child's read is pending for as long as this path runs: a read that
        // parks a worker parks the only one there is.
        var never = new byte[16];
        var pending = quiet.StandardOutput.ReadAsync(never).AsTask();

        using var reader = new StreamReader(loud.StandardOutput, Encoding.UTF8);
        var said = await reader.ReadToEndAsync().WaitAsync(bound);

        await quiet.StandardInput.DisposeAsync();
        _ = await pending.WaitAsync(bound);

        return said.Contains("the loud child", StringComparison.Ordinal)
            ? "ok: read the loud child while the quiet one's read was pending"
            : $"failed: the loud child said '{said}'";
    }

    /// <summary>Answers one connection's first request, and closes it.</summary>
    private static async Task AnswerAsync(Microsoft.Win32.SafeHandles.SafeFileHandle connected)
    {
        await using var stream = new FileStream(connected, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);

        var line = new List<byte>();
        var buffer = new byte[256];

        while (true)
        {
            var read = await stream.ReadAsync(buffer);

            if (read is 0)
            {
                return;
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);

            line.AddRange(buffer.AsSpan(0, newline < 0 ? read : newline).ToArray());

            if (newline >= 0)
            {
                break;
            }
        }

        var answer = new System.Text.Json.Nodes.JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString([.. line]))?["id"]?.DeepClone(),
            ["result"] = new System.Text.Json.Nodes.JsonObject { ["address"] = "http://stand-in.invalid/", ["pid"] = Environment.ProcessId },
        }.ToJsonString() + "\n";

        await stream.WriteAsync(Encoding.UTF8.GetBytes(answer));
        await stream.FlushAsync();
    }
}
