// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Logging;
using Microsoft.Extensions.Logging;

namespace BrowserAI;

/// <summary>
/// Entry point.
/// </summary>
/// <remarks>
/// At build-order step 2 this starts the logging stack, records that it
/// started, and exits. The MCP server itself arrives at step 7. What is real
/// here is the invariant everything after it depends on: stdout belongs to the
/// protocol and nothing in this process can reach it.
/// </remarks>
internal static class Program
{
    private static int Main()
    {
        using var log = ProcessLog.Create(new LocalAppDataPaths(), LogLevel.Information);
        var logger = log.Factory.CreateLogger("BrowserAI.Startup");

        StartupLog.Started(
            logger,
            Environment.ProcessId,
            Environment.ProcessPath ?? "<unknown>",
            Environment.CurrentDirectory);

        return 0;
    }
}

/// <summary>Source-generated log messages for process startup.</summary>
internal static partial class StartupLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "BrowserAI started. pid={ProcessId} image={ImagePath} cwd={WorkingDirectory}")]
    public static partial void Started(ILogger logger, int processId, string imagePath, string workingDirectory);
}
