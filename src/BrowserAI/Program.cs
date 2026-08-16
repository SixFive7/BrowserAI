// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Interop;
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
    /// <summary>
    /// The one environment variable BrowserAI reads about <b>itself</b>, and it
    /// moves the whole app root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists for one thing the suite otherwise cannot do: an empty
    /// browsers root.</b> First-run provisioning can only be proven against a
    /// root where nothing has ever been installed, and the alternative — deleting
    /// the developer's own <c>%LocalAppData%\BrowserAI\browsers</c> mid-suite —
    /// would destroy 430 MiB and break every other browser test running beside
    /// it. <see cref="Hosting.IAppPaths"/> deliberately does not resolve relative
    /// to the binary, so moving the executable does not move the root either.
    /// </para>
    /// <para>
    /// <b>It is read here rather than inside
    /// <see cref="LocalAppDataPaths"/></b>, so it stays a decision the host makes
    /// once. Step 19 swaps that class for one over
    /// <c>VelopackLocator.Current.RootAppDir</c> and has to decide what this
    /// means then, visibly, rather than losing it in a replaced file.
    /// </para>
    /// <para>
    /// <b>Never silent.</b> A BrowserAI running against a root nobody expects
    /// would look exactly like one that lost its sessions, so an override is
    /// logged at Warning on the way past. A relative value is ignored rather than
    /// resolved, for the same reason a relative <c>PLAYWRIGHT_BROWSERS_PATH</c>
    /// is refused: it would land somewhere nobody chose and report nothing.
    /// </para>
    /// </remarks>
    public const string AppRootVariable = "BROWSERAI_ROOT";

    private static async Task<int> Main(string[] args)
    {
        var overridden = Environment.GetEnvironmentVariable(AppRootVariable);
        var root = overridden is { Length: > 0 } && Path.IsPathFullyQualified(overridden) ? overridden : null;
        var paths = new LocalAppDataPaths(root);

        using var log = ProcessLog.Create(paths, LogLevel.Information);
        var logger = log.Factory.CreateLogger("BrowserAI.Startup");

        StartupLog.Started(
            logger,
            Environment.ProcessId,
            Environment.ProcessPath ?? "<unknown>",
            Environment.CurrentDirectory);

        if (root is not null)
        {
            StartupLog.AppRootOverridden(logger, AppRootVariable, root);
        }

        // The logon task's mode: one pass, synchronously, and nothing else --
        // no child, no stdio, no server. Matched against the same constant the
        // task's own definition carries, so the two cannot drift apart.
        if (args is not null && Array.Exists(args, argument => string.Equals(argument, LogonSweepTask.SweepArgument, StringComparison.Ordinal)))
        {
            return SweepOnce(paths, log.Factory, logger);
        }

        // Fire-and-forget, on its own background thread, before anything that
        // can be slow. Nothing on the request path waits for it or observes it,
        // and it is deliberately never a startup gate: a BrowserAI that cannot
        // sweep is degraded, one that will not start is broken.
        StraySweep.StartInBackground(() => CreateSweep(paths, log.Factory), logger);

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

            // Created before the proxy, and it starts nothing: the first init
            // decides whether a download is needed and never waits for one.
            using var provisioner = new BrowserProvisioner(payload, paths.BrowsersDirectory, log.Factory);

            var environment = new SessionEnvironment
            {
                Paths = paths,
                Payload = payload,
                Provisioner = provisioner,
                InstanceDirectory = instance,
                OpenSessionLog = log.OpenSessionLog,
            };

            // Declared before the watcher below so that it is disposed after it:
            // a watch that fired into a disposed source would report a teardown
            // failure while the process was already tearing down.
            using var stopping = new CancellationTokenSource();

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

            // ⚠️ The second of the two teardown mechanisms, and neither is a
            // close tool. stdin EOF is the backstop; this covers what EOF
            // cannot — a client that started BrowserAI through a wrapper, so the
            // pipe outlives the process that owns the conversation. It is an
            // OpenProcess handle, never a ping: `ping` was removed at protocol
            // revision 2026-07-28, and a handle is an event rather than a poll.
            //
            // ⚠️ It disposes the transport rather than only cancelling.
            // Measured 2026-08-16 against ModelContextProtocol 2.2.0 over real
            // stdio: cancelling `RunAsync`'s token does NOT end it, because the
            // read is parked in a syscall on the console handle and a token
            // cannot wake it — the transport's own DisposeAsync says as much
            // about the child leg, and it is just as true here. Closing the
            // channel is what produces the end-of-input this process would have
            // seen if the client had closed its end, so there is one shutdown
            // path rather than two. The cancellation is kept because a
            // caller-supplied token must still be honoured where it can be.
            //
            // Declared after the transport so that it is disposed BEFORE it, and
            // a client that cannot be watched is a warning rather than a refusal
            // to start.
            using var client = ClientLivenessWatcher.ForParentProcess(
                () =>
                {
                    stopping.Cancel();
                    _ = EndTheConversationAsync(transport, logger);
                },
                logger);

            StartupLog.Serving(logger, proxy.NegotiatedChildProtocolVersion ?? "<none>");

            try
            {
                // Ends when the caller closes our stdin, which is the same
                // graceful path BrowserAI uses on its own child -- or when the
                // watcher above reports the client gone, which does not wait
                // for EOF at all. Either way the disposals below take every
                // session's child, browser and job with them.
                await server.RunAsync(stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The watcher asked for this. It has already said why.
            }

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

    /// <summary>
    /// Closes the caller-facing transport once the client has gone, and reports
    /// rather than discards a failure to do so.
    /// </summary>
    /// <remarks>
    /// <b>Fire-and-forget with the result observed, which is not the same as
    /// fire-and-forget.</b> This runs on a thread-pool callback that must not
    /// block, so nothing awaits it — but a discarded <c>Task</c> is a discarded
    /// exception, and the one thing that must never happen here is the shutdown
    /// path failing in silence while every other signal stays green.
    /// </remarks>
    /// <param name="transport">The caller-facing transport. Disposing it is what ends <c>RunAsync</c>.</param>
    /// <param name="logger">Where a failure is reported.</param>
    /// <returns>The disposal.</returns>
    private static async Task EndTheConversationAsync(DirectStdioServerTransport transport, ILogger logger)
    {
        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The process is going down either way; what matters is that a failure to close cleanly is not silent.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            StartupLog.ChannelNotClosed(logger, failure);
        }
    }

    /// <summary>
    /// Composes a sweep over the browsers this build provisions and the session
    /// index it keeps.
    /// </summary>
    /// <remarks>
    /// Called on the sweep's own thread, never on the startup path: it reads the
    /// payload's <c>browsers.json</c>, and a payload that is absent or broken
    /// must not be able to stop BrowserAI serving.
    /// </remarks>
    private static StraySweep CreateSweep(IAppPaths paths, ILoggerFactory factory)
    {
        var payload = new PayloadLayout();
        var manifest = BrowsersManifest.Read(payload);
        var logger = factory.CreateLogger("BrowserAI.Sweep");

        return new StraySweep(
            ProvisionedBrowsers.Executables(paths.BrowsersDirectory, manifest),
            new SessionIndex(paths, logger),
            logger,

            // Firefox publishes no message window, so its candidates can only be
            // attributed through a session's own profile lock. Named as a subset
            // of the images above rather than as a second detection rule: what
            // counts as ours is still one full-image-path match.
            ProvisionedBrowsers.ExecutablesFor(ProvisionedBrowsers.Firefox, paths.BrowsersDirectory, manifest));
    }

    /// <summary>Runs one sweep and exits, for the logon task.</summary>
    private static int SweepOnce(IAppPaths paths, ILoggerFactory factory, ILogger logger)
    {
        try
        {
            _ = CreateSweep(paths, factory).Run();
            return 0;
        }
#pragma warning disable CA1031 // Same boundary as the background thread's: a sweep failure is a log line and an exit code, never a crash dialog.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            SweepLog.Failed(logger, failure);
            return 1;
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

    /// <summary>
    /// The app root came from the environment rather than from
    /// <c>%LocalAppData%</c>.
    /// </summary>
    /// <remarks>
    /// Warning rather than Information, and it is the first line after startup:
    /// a BrowserAI whose sessions, log and 430 MiB of browsers are somewhere
    /// nobody expected looks exactly like one that lost them.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="variable">Which variable moved it.</param>
    /// <param name="root">Where it now is.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "{Variable} is set, so this BrowserAI's app root is {Root} rather than the one under %LocalAppData%. Its sessions, log and provisioned browsers all live there.")]
    public static partial void AppRootOverridden(ILogger logger, string variable, string root);

    /// <summary>
    /// The client went and closing the protocol channel after it threw.
    /// </summary>
    /// <remarks>
    /// The process still goes down — the disposals on the way out of
    /// <c>Main</c> run regardless, and the job objects are the guarantee under
    /// all of it. This line exists so that a shutdown which did not go the way
    /// it was meant to is visible rather than inferred from a missing log.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="exception">Why.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "The MCP client exited and BrowserAI's protocol channel could not be closed. Shutdown continues; the job objects still take every child and browser down.")]
    public static partial void ChannelNotClosed(ILogger logger, Exception exception);
}
