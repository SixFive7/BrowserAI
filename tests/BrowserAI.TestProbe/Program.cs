// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Protocol;
using Microsoft.Extensions.Logging;

namespace BrowserAI.TestProbe;

/// <summary>
/// Runs one named probe and exits. Every probe drives real product code.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length is 0)
        {
            return Usage();
        }

        return args[0] switch
        {
            "log-once" when args.Length is 3 => LogOnce(args[1], args[2]),
            "log-many" when args.Length is 4 => LogMany(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture)),
            "crash" when args.Length is 2 => Crash(args[1]),
            "transport-child" when args.Length is 4 =>
                TransportChild(args[1], int.Parse(args[2], CultureInfo.InvariantCulture), args[3]),
            "job-launcher" when args.Length >= 4 =>
                JobProbe.Launcher(args[1], args[2], args[3], args[4..]),
            "job-child" when args.Length is 3 =>
                JobProbe.Child(args[1], int.Parse(args[2], CultureInfo.InvariantCulture)),
            "job-grandchild" when args.Length is 1 => JobProbe.Grandchild(),
            "session-identity" when args.Length is 3 => SessionProbe.Identity(args[1], args[2]),
            "session-race" when args.Length is 5 => SessionProbe.Race(args[1], args[2], args[3], args[4]),
            "session-hold" when args.Length is 4 => SessionProbe.Hold(args[1], args[2], args[3]),
            "session-hold-gate" when args.Length is 3 => SessionProbe.HoldGate(args[1], args[2]),
            "session-sweep" when args.Length is 5 => SessionProbe.Sweep(args[1], args[2], args[3], args[4]),
            "session-rewrite" when args.Length is 5 =>
                SessionProbe.Rewrite(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture), args[4]),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Stands in for the <c>@playwright/mcp</c> child while the transport that
    /// starts it is being proven.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It reports what only the child can know — its own pid and the
    /// environment block it was actually handed — then echoes every frame it is
    /// sent, byte for byte, and lives until its stdin closes.
    /// </para>
    /// <para>
    /// A real <c>node.exe</c> would prove the same things, and the suite
    /// deliberately does not use one: <c>payload/</c> is a build output that a
    /// clean clone does not have, and build-order step 1 requires the suite to
    /// pass there. The hazards under test — an interposed shell, a merged
    /// environment block, an exit code read after <c>Dispose()</c> — are
    /// properties of how Windows starts a process, not of which process it is.
    /// </para>
    /// </remarks>
    /// <param name="reportPath">Where to write the pid-and-environment report.</param>
    /// <param name="standardErrorLines">How many lines to write to stderr before anything else.</param>
    /// <param name="framePath">Where to append every frame received, exactly as its bytes arrived.</param>
    private static int TransportChild(string reportPath, int standardErrorLines, string framePath)
    {
        // First, before the report and before a single frame is read. A child
        // that only writes to stderr once it has work to do would never catch a
        // handler attached after Start().
        for (var i = 0; i < standardErrorLines; i++)
        {
            // Console rather than ILogger, deliberately: what is under test is
            // the pipe, and a logger here would prove the logging stack
            // instead. The console ban is enforced in src/ by an analyzer this
            // test asset does not reference, which is why no suppression is
            // needed and why one must never be copied from here into src/.
            Console.Error.WriteLine($"probe-stderr#{i.ToString(CultureInfo.InvariantCulture)}");
        }

        var environment = new JsonObject();

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value;
        }

        var report = new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["workingDirectory"] = Environment.CurrentDirectory,
            ["environment"] = environment,
        };

        File.WriteAllText(reportPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), Encoding.UTF8);

        using var channel = StdioChannel.OpenStandardStreams();
        using var frames = new FileStream(framePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var reader = new StreamReader(channel.Input, StdioChannel.Utf8NoBom);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length is 0)
            {
                continue;
            }

            // The bytes as they arrived, recorded so the test can assert that
            // nothing escaped them on the way down.
            var bytes = StdioChannel.Utf8NoBom.GetBytes(line);
            frames.Write(bytes);
            frames.WriteByte((byte)'\n');
            frames.Flush();

            channel.WriteFrame(bytes);
        }

        return 0;
    }

    /// <summary>Writes one record through the real stack and exits cleanly.</summary>
    private static int LogOnce(string root, string message)
    {
        using var log = ProcessLog.Create(new LocalAppDataPaths(root), LogLevel.Trace);
        var logger = log.Factory.CreateLogger("BrowserAI.TestProbe");
        ProbeLog.Message(logger, message);
        return 0;
    }

    /// <summary>
    /// Writes many records so several concurrent processes can be shown not to
    /// lose each other's lines.
    /// </summary>
    private static int LogMany(string root, string tag, int count)
    {
        using var log = ProcessLog.Create(new LocalAppDataPaths(root), LogLevel.Trace);
        var logger = log.Factory.CreateLogger("BrowserAI.TestProbe");

        for (var i = 0; i < count; i++)
        {
            var marker = $"{tag}#{i.ToString(CultureInfo.InvariantCulture)}";
            ProbeLog.Message(logger, marker);
        }

        return 0;
    }

    /// <summary>
    /// Throws past the process boundary. Deliberately not inside a
    /// <c>using</c>: an unhandled exception is not guaranteed to unwind, so a
    /// last line that survives here proves the crash handler rather than a
    /// <c>finally</c>.
    /// </summary>
    private static int Crash(string root)
    {
        var log = ProcessLog.Create(new LocalAppDataPaths(root), LogLevel.Trace);
        var logger = log.Factory.CreateLogger("BrowserAI.TestProbe");
        ProbeLog.Message(logger, "about to throw");
        throw new InvalidOperationException("Deliberate unhandled exception from the crash probe.");
    }

    private static int Usage() => 2;
}

/// <summary>Source-generated log messages for the probe.</summary>
internal static partial class ProbeLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "probe: {Text}")]
    public static partial void Message(ILogger logger, string text);
}
