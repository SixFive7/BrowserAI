// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Threading.Channels;

namespace BrowserAI.TestProbe;

/// <summary>
/// Stands between BrowserAI and a real <c>@playwright/mcp</c> child and holds the
/// child's <c>browser_close</c> back until a file appears, so that a close is slow
/// when an arm needs it to be.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> A real browser answers its close in a few hundred
/// milliseconds, too soon for an arm to land a resume inside it on every run, and
/// the arms that hold the ordering rule of 2026-10-04 need a close that is still
/// running when the resume arrives. Holding the request on its way to the child
/// gives BrowserAI a close it has sent and the browser has not yet started; every
/// other frame passes byte for byte in both directions.
/// </para>
/// <para>
/// <b>Its end is the child's end.</b> When its own stdin closes it closes the
/// child's, which is what <c>@playwright/mcp</c> treats as the end of the session,
/// and it exits with the child's exit code once the child has gone. A close still
/// held when its stdin closes is dropped: BrowserAI has given up on it, and the child
/// then meets the end of its input with no close before it.
/// </para>
/// <para>
/// <b>It runs inside the session's own job</b>, started there by the product's
/// launcher in place of <c>node.exe</c>, and the child it starts joins that job, so
/// the job's membership is what the product reads and the job's close is what ends
/// everything.
/// </para>
/// </remarks>
internal static class CloseRelayProbe
{
    /// <summary>What a held request carries: the tool's name as JSON spells it.</summary>
    private const string HeldTool = "\"browser_close\"";

    /// <summary>
    /// How often a held close looks for its release file. A polling interval and not
    /// a bound: it can make a run slower and never redder.
    /// </summary>
    private static readonly TimeSpan Look = TimeSpan.FromMilliseconds(20);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Starts the child and relays until the child has gone.</summary>
    /// <remarks>
    /// <b>Every copy is awaited since 2026-10-11</b>, the maintainer's decision 54: the child
    /// is started through the product's own launcher, whose pipes are overlapped at this
    /// end, and this probe's own standard handles through the product's channel, which reads
    /// and writes them on threads of their own. <i>Previously a <c>Process</c>'s redirected
    /// streams, a thread reading the input into a blocking queue, and sleeps between looks.</i>
    /// </remarks>
    /// <param name="releaseFile">The file whose existence lets a held close through.</param>
    /// <param name="command">The child's executable.</param>
    /// <param name="arguments">Its arguments, passed as they are.</param>
    /// <returns>The child's exit code.</returns>
    public static async Task<int> RelayAsync(string releaseFile, string command, string[] arguments)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                environment[name] = value;
            }
        }

        using var child = global::BrowserAI.Interop.JobLauncher.StartOutsideAnyJob(command, arguments, Environment.CurrentDirectory, environment);
        using var channel = global::BrowserAI.Protocol.StdioChannel.OpenStandardStreams();

        var down = CopyAsync(child.StandardOutput, channel.Output);
        var errors = CopyToStandardErrorAsync(child.StandardError);

        var lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var reading = ReadAsync(channel.Input, lines.Writer);

        var toChild = child.StandardInput;

        try
        {
            await foreach (var line in lines.Reader.ReadAllAsync())
            {
                if (line.Contains(HeldTool, StringComparison.Ordinal) && !await WaitForTheReleaseAsync(releaseFile, lines.Reader))
                {
                    break;
                }

                await toChild.WriteAsync(Utf8NoBom.GetBytes(line + "\n"));
                await toChild.FlushAsync();
            }
        }
        catch (IOException)
        {
            // The child went first; its exit below is the report.
        }

        try
        {
            await toChild.DisposeAsync();
        }
        catch (IOException)
        {
            // Already closed by the child's exit.
        }

        _ = await child.WaitForExitAsync(Timeout.InfiniteTimeSpan);
        await down;
        await errors;

        return child.TryReadExitCode() ?? 1;
    }

    /// <summary>Copies the child's standard error to this process's own, until the child's ends.</summary>
    /// <param name="from">The child's standard error.</param>
    /// <returns>The copy.</returns>
    private static async Task CopyToStandardErrorAsync(Stream from)
    {
        // Console's handle, deliberately: what is under test is the pipe, as in TransportChild.
#pragma warning disable CA2000 // The pump disposes the stream it wraps.
        await using var error = global::BrowserAI.Protocol.StdioChannel.PumpWrites(Console.OpenStandardError());
#pragma warning restore CA2000
        await CopyAsync(from, error);
    }

    /// <summary>Reads this process's stdin a line at a time until it ends.</summary>
    /// <param name="input">The input.</param>
    /// <param name="lines">Where each line goes; completed at the end of input.</param>
    /// <returns>The read.</returns>
    private static async Task ReadAsync(Stream input, ChannelWriter<string> lines)
    {
        try
        {
            using var reader = new StreamReader(input, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            while (await reader.ReadLineAsync() is { } line)
            {
                _ = lines.TryWrite(line);
            }
        }
        catch (IOException)
        {
            // A pipe that broke is an end of input like any other.
        }
        finally
        {
            _ = lines.TryComplete();
        }
    }

    /// <summary>Waits until the release file exists, or until this process's stdin has ended.</summary>
    /// <param name="releaseFile">The file.</param>
    /// <param name="lines">The input, whose completion is the end of stdin.</param>
    /// <returns>Whether the held close may go through.</returns>
    private static async Task<bool> WaitForTheReleaseAsync(string releaseFile, ChannelReader<string> lines)
    {
        while (!File.Exists(releaseFile))
        {
            if (lines.Completion.IsCompleted)
            {
                return false;
            }

            await Task.Delay(Look);
        }

        return true;
    }

    /// <summary>Copies one stream to another until the first ends, flushing each piece.</summary>
    /// <param name="from">The child's stream.</param>
    /// <param name="to">This process's.</param>
    /// <returns>The copy.</returns>
    private static async Task CopyAsync(Stream from, Stream to)
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (await from.ReadAsync(buffer) is var read and > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read));
                await to.FlushAsync();
            }
        }
        catch (IOException)
        {
            // Either end going away is how a copy ends.
        }
    }
}
