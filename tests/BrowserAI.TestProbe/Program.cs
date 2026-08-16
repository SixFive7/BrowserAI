// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Logging;
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
            _ => Usage(),
        };
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
