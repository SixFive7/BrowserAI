// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace BrowserAI;

/// <summary>
/// Entry point: start the child, serve the caller, and take the whole tree down
/// on the way out.
/// </summary>
/// <remarks>
/// The order matters. Logging first, because a failure before it exists has
/// nowhere to be reported. The child next, so a payload or browser problem is a
/// startup failure with a message rather than a tool call that fails later for
/// reasons the caller cannot see. stdout is acquired last, and by then it
/// belongs to the protocol.
/// </remarks>
internal static class Program
{
    private static async Task<int> Main()
    {
        var paths = new LocalAppDataPaths();

        using var log = ProcessLog.Create(paths, LogLevel.Information);
        var logger = log.Factory.CreateLogger("BrowserAI.Startup");

        StartupLog.Started(
            logger,
            Environment.ProcessId,
            Environment.ProcessPath ?? "<unknown>",
            Environment.CurrentDirectory);

        // One run, one directory. It holds this run's own child — the one that
        // answers `tools/list` before any session exists — together with its
        // profile and the config generated for every session this run opens.
        // Sessions do not replace it: they are additional, and each has its own
        // directory chosen by the caller.
        var instance = InstanceDirectory.CreateFresh(paths);

        try
        {
            var payload = new PayloadLayout();

            var options = ChildLaunch.Create(
                payload,
                paths.BrowsersDirectory,
                instance,
                Path.Combine(instance, "playwright-mcp.config.json"),
                BrowserConfiguration.ForSurface(instance),
                name: "playwright-mcp[surface]");

            var environment = new SessionEnvironment
            {
                Paths = paths,
                Payload = payload,
                InstanceDirectory = instance,
                OpenSessionLog = log.OpenSessionLog,
            };

            var proxy = await BrowserProxy.ConnectAsync(options, log.Factory, environment).ConfigureAwait(false);

            // `await using var x = …` awaits its DisposeAsync on the captured
            // context, which CA2007 refuses. Holding the ConfiguredAsyncDisposable
            // in its own local is the shape that keeps both the object usable
            // and the disposal context-free.
            await using var proxyScope = proxy.ConfigureAwait(false);

            // Last, and only once everything that could fail loudly has. From
            // here stdout is the protocol channel and nothing else in the
            // process can reach it.
            using var channel = StdioChannel.OpenStandardStreams();

            var transport = new DirectStdioServerTransport(channel, log.Factory);
            await using var transportScope = transport.ConfigureAwait(false);

            var server = McpServer.Create(transport, proxy.ServerOptions(), log.Factory);
            await using var serverScope = server.ConfigureAwait(false);

            StartupLog.Serving(logger, proxy.NegotiatedChildProtocolVersion ?? "<none>");

            // Ends when the caller closes our stdin, which is the same graceful
            // path BrowserAI uses on its own child.
            await server.RunAsync().ConfigureAwait(false);

            return 0;
        }
#pragma warning disable CA1031 // The process boundary reports every failure the same way: a log record and a non-zero exit code.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StartupLog.Failed(logger, ex);
            return 1;
        }
        finally
        {
            // The clean path. The killed path is the next run's sweep, because
            // nothing here runs when the process is terminated from outside.
            InstanceDirectory.Delete(instance);
        }
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

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "BrowserAI is serving stdio. childProtocol={ChildProtocol}")]
    public static partial void Serving(ILogger logger, string childProtocol);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Critical,
        Message = "BrowserAI could not start and is exiting.")]
    public static partial void Failed(ILogger logger, Exception exception);
}
