// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Protocol;
using BrowserAI.Registration;
using BrowserAI.Relay;
using BrowserAI.Sessions;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI;

/// <summary>The relay: what a client starts, <c>BrowserAI.exe --mcp</c>.</summary>
internal static partial class Program
{
    /// <summary>
    /// The relay: answers the handshake and the tool list from the binary, and passes
    /// every call to the background, holding it while there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One relay per client process, and it starts nothing</b> (S a and R, decided
    /// 2026-10-08): no background, no task, no child, no sweep. What it does when there
    /// is no background is <see cref="RelayEngine"/>'s, and what it learns about why is
    /// <see cref="BackgroundFinder"/>'s.
    /// </para>
    /// <para>
    /// <b>The tool list is compiled into the binary</b> (step 1), rewritten the way every
    /// session's list is, so a client lists BrowserAI's tools with no background at all.
    /// </para>
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <param name="paths">The data root the relay was registered for.</param>
    /// <param name="log">The process log.</param>
    /// <param name="logger">Startup's logger.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> RunTheRelayAsync(string[] args, LocalAppDataPaths paths, ProcessLog log, ILogger logger)
    {
        var relayLogger = log.Factory.CreateLogger("BrowserAI.Relay");
        var installRoot = InstallLocation.RootAppDir;
        var pipeName = ValueOf(args, BackgroundPipe.PipeArgument) is { Length: > 0 } named
            ? named
            : BackgroundPipe.NameFor(installRoot, paths.RootAppDir);

        using var finder = new BackgroundFinder(
            new BackgroundFinderSettings
            {
                PipeName = pipeName,
                RecordPath = BackgroundRecord.PathFor(paths.RootAppDir, pipeName),
                InstallRoot = installRoot,
                DataRoot = paths.RootAppDir,
                TaskName = installRoot is { } root && InstallLocation.AppId is { Length: > 0 } appId ? SignInTask.NameFor(appId, root) : null,
                Executable = Environment.ProcessPath ?? string.Empty,
                LogPath = log.CurrentFile ?? paths.LogDirectory,
            },
            relayLogger);

        var handshake = SdkHandshake.Start();

        await using var handshakeScope = handshake.ConfigureAwait(false);

        using var stopping = new CancellationTokenSource();
        using var client = ClientLivenessWatcher.ForParentProcess(stopping.Cancel, logger);
        using var channel = StdioChannel.OpenStandardStreams();

        var engine = new RelayEngine(
            channel.Input,
            channel.Output,
            finder,
            handshake,
            static () => SessionToolSurface.Rewrite(UpstreamToolList.Compiled.Result(), ToolVerdicts.Compiled),
            static clientName => ClientRecognition.Reconnect(
                clientName,
                ClientRecognition.ReadsTheParent ? ProcessLiveness.ParentCommandLine() : null,
                Environment.GetEnvironmentVariable(ClientRecognition.EntrypointVariable)),
            new RelayFacts(
                BuildVersion.Current,
                Environment.ProcessId,
                ProcessLiveness.ParentProcessId() is var parent and > 0 ? parent : null,
                Environment.CurrentDirectory,
                paths.RootAppDir,
                log.CurrentFile ?? paths.LogDirectory),
            TimeProvider.System,
            relayLogger);

        var end = await engine.RunAsync(stopping.Token).ConfigureAwait(false);

        RelayModeLog.Ended(relayLogger, end.Reason);

        return 0;
    }
}

/// <summary>Source-generated log messages for the relay mode.</summary>
internal static partial class RelayModeLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "The relay ended: {Why}.")]
    public static partial void Ended(ILogger logger, RelayEnding why);
}
