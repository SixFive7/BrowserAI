// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

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
    /// <param name="releaseFile">The file whose existence lets a held close through.</param>
    /// <param name="command">The child's executable.</param>
    /// <param name="arguments">Its arguments, passed as they are.</param>
    /// <returns>The child's exit code.</returns>
    public static int Relay(string releaseFile, string command, string[] arguments)
    {
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var child = Process.Start(start) ?? throw new InvalidOperationException($"'{command}' did not start.");
        using var output = Console.OpenStandardOutput();
        using var error = Console.OpenStandardError();

        var down = Task.Run(() => Copy(child.StandardOutput.BaseStream, output));
        var errors = Task.Run(() => Copy(child.StandardError.BaseStream, error));

        using var lines = new BlockingCollection<string>();

        var reader = new Thread(() => Read(lines))
        {
            IsBackground = true,
            Name = "close relay, stdin",
        };

        reader.Start();

        var toChild = child.StandardInput.BaseStream;

        try
        {
            foreach (var line in lines.GetConsumingEnumerable())
            {
                if (line.Contains(HeldTool, StringComparison.Ordinal) && !WaitForTheRelease(releaseFile, lines))
                {
                    break;
                }

                var bytes = Utf8NoBom.GetBytes(line + "\n");

                toChild.Write(bytes);
                toChild.Flush();
            }
        }
        catch (IOException)
        {
            // The child went first; its exit below is the report.
        }

        try
        {
            child.StandardInput.Close();
        }
        catch (IOException)
        {
            // Already closed by the child's exit.
        }

        child.WaitForExit();
        down.Wait();
        errors.Wait();

        return child.ExitCode;
    }

    /// <summary>Reads this process's stdin a line at a time until it ends.</summary>
    /// <param name="lines">Where each line goes; completed at the end of input.</param>
    private static void Read(BlockingCollection<string> lines)
    {
        try
        {
            using var input = new StreamReader(Console.OpenStandardInput(), Utf8NoBom);

            while (input.ReadLine() is { } line)
            {
                lines.Add(line);
            }
        }
        catch (IOException)
        {
            // A pipe that broke is an end of input like any other.
        }
        finally
        {
            lines.CompleteAdding();
        }
    }

    /// <summary>Waits until the release file exists, or until this process's stdin has ended.</summary>
    /// <param name="releaseFile">The file.</param>
    /// <param name="lines">The input, whose completion is the end of stdin.</param>
    /// <returns>Whether the held close may go through.</returns>
    private static bool WaitForTheRelease(string releaseFile, BlockingCollection<string> lines)
    {
        while (!File.Exists(releaseFile))
        {
            if (lines.IsAddingCompleted)
            {
                return false;
            }

            Thread.Sleep(Look);
        }

        return true;
    }

    /// <summary>Copies one stream to another until the first ends.</summary>
    /// <param name="from">The child's stream.</param>
    /// <param name="to">This process's.</param>
    private static void Copy(Stream from, Stream to)
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (from.Read(buffer, 0, buffer.Length) is var read and > 0)
            {
                to.Write(buffer, 0, read);
                to.Flush();
            }
        }
        catch (IOException)
        {
            // Either end going away is how a copy ends.
        }
    }
}
