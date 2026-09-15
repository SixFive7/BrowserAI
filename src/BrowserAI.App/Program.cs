// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Interop;
using BrowserAI.App.Ui;
using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Registration;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;
using Velopack.Logging;

namespace BrowserAI.App;

/// <summary>
/// The configuration app: the Velopack main executable, the Start Menu entry,
/// and the owner of all four installer hooks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three modes, decided by the arguments and the environment, and only one of
/// them has a window.</b> A hook runs headless and exits; <c>--report</c> writes
/// a file and exits; anything else opens the dialog. Nothing else is a mode, and
/// nothing about the dialog runs on any other path.
/// </para>
/// <para>
/// ⚠️ <b>The hooks are dispatched by Velopack itself rather than by reading
/// <c>args</c> here.</b> <c>VelopackApp.Run()</c> recognises its own four
/// arguments, invokes the callback and <b>exits the process</b> — so serving a
/// hook never reaches the line below it. Re-implementing that dispatch would be
/// a second parser for somebody else's argument syntax, running beside theirs,
/// and the first divergence would be an installer that hangs on its own timeout.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Writes a JSON status report and exits.
    /// </summary>
    /// <remarks>
    /// <b>This application's testable non-interactive path.</b> A window
    /// application whose only entry point opens a window is one nothing can
    /// assert about, and a support artifact that describes a different state
    /// from the window would be worse than none — so the dialog and this file
    /// render the same <see cref="AppState"/>.
    /// </remarks>
    public const string ReportArgument = "--report";

    /// <summary>
    /// The variable Velopack sets on the restart it performs after an update.
    /// </summary>
    /// <remarks>
    /// Read out of 1.2.0's own source (<c>constants.rs</c>:
    /// <c>HOOK_ENV_RESTART</c>), beside the first-run one. It is what turns the
    /// heading into <i>Updated to …</i> rather than a guess from a timestamp.
    /// </remarks>
    public const string RestartVariable = "VELOPACK_RESTART";

    /// <summary>
    /// ⚠️ <b>Cleared from this process's environment before anything is started
    /// from it.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a defect the two-binary design creates and it had to be closed
    /// here.</b> The installer starts THIS process with
    /// <c>VELOPACK_FIRSTRUN=true</c>, and a child inherits its parent's
    /// environment block: click <i>Register</i>, and <c>claude.exe</c> is
    /// started carrying it — and anything <i>that</i> process starts carries it
    /// too, including <c>BrowserAI.Server.exe</c>. The server exits 0 on that
    /// variable, deliberately, because a server the installer started has no
    /// client. It would then have exited 0 for a client that really was there,
    /// on first run, presenting as <i>failed to connect</i> with nothing in any
    /// log to say why.
    /// </para>
    /// <para>
    /// <b>Cleared rather than filtered per launch</b>: there is more than one
    /// place this process starts something, and a filter that had to be
    /// remembered at each of them is the shape of the defect rather than its
    /// fix. The value is read first, so the dialog still knows it was a first
    /// run.
    /// </para>
    /// </remarks>
    public static void ClearTheInstallersOwnVariables()
    {
        Environment.SetEnvironmentVariable(VelopackStartup.FirstRunVariable, null);
        Environment.SetEnvironmentVariable(RestartVariable, null);
    }

    /// <summary>
    /// ⚠️ <b>Single-threaded apartment, and it is load-bearing in two
    /// places.</b>
    /// </summary>
    /// <remarks>
    /// The shell's folder picker falls back to the pre-Vista dialog on a thread
    /// that is not in one — silently, with no error — and the common controls a
    /// task dialog is made of expect it. Neither failure is one a test here can
    /// see, so the reason is written at both ends.
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <returns>Zero when what was asked for happened.</returns>
    [STAThread]
    private static int Main(string[] args)
    {
        args ??= [];

        var firstRun = VelopackStartup.StartedByTheInstaller();
        var restarted = Environment.GetEnvironmentVariable(RestartVariable) is { Length: > 0 };

        ClearTheInstallersOwnVariables();

        // ⚠️ FIRST. This call serves the installer's four hooks and exits the
        // process when it does, so everything below it belongs to a run that is
        // not a hook. It also carries SetAutoApplyOnStartup(false).
        var buffered = new List<(VelopackLogLevel Level, string Message, Exception? Failure)>();
        VelopackStartup.RunAndServeLifecycleHooks(args, (level, message, failure) => buffered.Add((level, message, failure)));

        var paths = new LocalAppDataPaths(LocalAppDataPaths.Overridden());

        using var log = ProcessLog.Create(paths, LogLevel.Information);
        var logger = log.Factory.CreateLogger("BrowserAI.App");

        foreach (var (level, message, failure) in buffered)
        {
            if (level >= VelopackLogLevel.Warning
                && !VelopackStartup.IsRoutineNotInstalledNotice(level, message, InstallLocation.IsInstalled))
            {
                AppLog.VelopackProblem(logger, message, failure);
            }
            else
            {
                AppLog.Velopack(logger, message);
            }
        }

        var commands = new ClientCommandLine();
        var state = AppState.Read(commands, Environment.CurrentDirectory);

        if (ReportPathFrom(args) is { Length: > 0 } report)
        {
            var written = StatusReport.Write(state, report);
            AppLog.ReportWritten(logger, written);
            return 0;
        }

        var occasion = restarted ? Occasion.AfterUpdate : firstRun ? Occasion.FirstRun : Occasion.Ordinary;
        var status = state.StatusSentence();

        AppLog.Opening(logger, state.Version, occasion, status);

        return new ConfigurationSession(state, commands, paths, logger, occasion).Show();
    }

    /// <summary>
    /// The path <c>--report</c> was given, or <see langword="null"/>.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>The path, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <b>The argument is required to carry a path.</b> A bare <c>--report</c>
    /// writing to a directory of this application's choosing would be a file
    /// nobody knows the name of, which is the opposite of a support artifact.
    /// </remarks>
    public static string? ReportPathFrom(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], ReportArgument, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

/// <summary>
/// One run of the dialog: the state it is showing, and what each click does to
/// it.
/// </summary>
/// <remarks>
/// <b>Every action is one explicit click and nothing runs on open.</b> The
/// window is built from state that was read; the update check, the registration
/// calls and the folder picker all wait to be asked.
/// </remarks>
internal sealed class ConfigurationSession(
    AppState state,
    IRegistrationCommand commands,
    IAppPaths paths,
    ILogger logger,
    Occasion occasion)
{
    private AppState _state = state;
    private string? _note;
    private string? _available;
    private TaskDialogHost? _host;

    /// <summary>Opens the window and returns when it closes.</summary>
    /// <returns>Zero when the dialog ran.</returns>
    public int Show()
    {
        // ⚠️ THE APP IS A MEMBER OF THE LIVE CENSUS for exactly as long as its
        // window is open. Velopack's run_hook ends with force_stop_package,
        // which kills every process whose image path is under the install root
        // -- so an update applied by a SERVER while this window is open would
        // take the window with it, mid-click. The server's update lane defers
        // while the census is non-empty, and this marker is what makes it
        // non-empty.
        using var live = _state.InstallRoot is { Length: > 0 } root
            ? LiveInstances.Join(root, logger)
            : null;

        using var host = new TaskDialogHost(
            () => ConfigurationDialog.Page(_state, occasion, _note, _available),
            OnCommand,
            OnLink);

        _host = host;

        try
        {
            return host.Show() is 0 ? 0 : 1;
        }
        finally
        {
            _host = null;
        }
    }

    private ClickOutcome OnCommand(int id)
    {
        switch (id)
        {
            case ConfigurationDialog.Command.CheckForUpdates:
                return CheckForUpdates();

            case ConfigurationDialog.Command.ApplyUpdate:
                return ApplyUpdate();

            case ConfigurationDialog.Command.Register:
                return Apply(RegistrationIntent.Install);

            case ConfigurationDialog.Command.Unregister:
                return Apply(RegistrationIntent.Uninstall);

            case ConfigurationDialog.Command.RegisterInProject:
                return RegisterInProject();

            case ConfigurationDialog.Command.OpenLogs:
                _ = Directory.CreateDirectory(paths.LogDirectory);
                _ = ShellInterop.OpenInExplorer(paths.LogDirectory);
                return ClickOutcome.Stay;

            default:
                return ClickOutcome.Stay;
        }
    }

    private void OnLink(string href)
    {
        if (ConfigurationDialog.FolderFrom(href) is { Length: > 0 } folder)
        {
            _ = Directory.CreateDirectory(folder);
            _ = ShellInterop.OpenInExplorer(folder);
            return;
        }

        _ = ShellInterop.OpenUrl(href);
    }

    private ClickOutcome Apply(RegistrationIntent intent)
    {
        var report = McpRegistrar.Apply(intent, Environment.ProcessPath, commands, logger);

        _note = ConfigurationDialog.NoteFor(report);
        _state = AppState.Read(commands, Environment.CurrentDirectory) with { LastUpdateCheck = _state.LastUpdateCheck };

        return ClickOutcome.Rerender;
    }

    private ClickOutcome RegisterInProject()
    {
        var folder = ShellInterop.PickFolder(0, "Choose the folder to register BrowserAI in. A .mcp.json is written at its root, to be committed with the project.");

        if (folder is not { Length: > 0 })
        {
            return ClickOutcome.Stay;
        }

        var client = _state.ClientPath;

        if (client is not { Length: > 0 })
        {
            _note = "Claude Code was not found, so nothing was written.";
            return ClickOutcome.Rerender;
        }

        var command = ProjectCommand(out var portable);
        var outcome = commands.Run(
            client,
            McpClientRegistration.AddArguments(command, McpClientRegistration.ProjectScope),
            McpClientRegistration.Budget,
            folder);

        _note = outcome.Succeeded || McpClientRegistration.MeansAlreadyRegistered(outcome.ExitCode, outcome.Output)
            ? $"Wrote {McpRegistryView.ProjectConfigFile(folder)} registering '{command}'. "
                + (portable
                    ? string.Empty
                    : "This install is not at its default location, so the entry carries its absolute path and will not resolve on another machine. ")
                + ConfigurationDialog.ProjectApprovalHint + " " + ConfigurationDialog.RestartHint
            : $"Claude Code could not write the project file: {outcome.Output}";

        _state = AppState.Read(commands, Environment.CurrentDirectory) with { LastUpdateCheck = _state.LastUpdateCheck };

        return ClickOutcome.Rerender;
    }

    /// <summary>
    /// The command a project file gets: portable when it expands to this
    /// install, absolute otherwise.
    /// </summary>
    private string ProjectCommand(out bool portable)
    {
        var absolute = _state.ServerCommand ?? string.Empty;
        var candidate = McpClientRegistration.PortableCommandFor(InstallRootFolderName());

        portable = absolute.Length > 0
            && string.Equals(
                Path.GetFullPath(McpRegistryView.Expand(candidate)),
                Path.GetFullPath(absolute),
                StringComparison.OrdinalIgnoreCase);

        return portable ? candidate : absolute;
    }

    private string InstallRootFolderName() =>
        _state.InstallRoot is { Length: > 0 } root
            ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : "BrowserAI.app";

    private ClickOutcome CheckForUpdates()
    {
        if (UpdateConfiguration.Resolve(logger) is not { } feed)
        {
            _note = "No release feed is configured for this build, so there is nothing to check.";
            return ClickOutcome.Rerender;
        }

        try
        {
            var client = new VelopackUpdateClient(feed);
            var candidate = client.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();

            _available = candidate?.Version;
            _note = candidate is null
                ? $"BrowserAI {_state.Version} is up to date."
                : $"BrowserAI {candidate.Version} is available.";
        }
#pragma warning disable CA1031 // A failed check is a sentence in a dialog, never a crash on somebody's screen.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            _available = null;
            _note = $"The update check did not finish: {failure.Message}";
        }

        _state = _state with { LastUpdateCheck = _note };

        return ClickOutcome.Rerender;
    }

    private ClickOutcome ApplyUpdate()
    {
        if (UpdateConfiguration.Resolve(logger) is not { } feed || _available is not { Length: > 0 })
        {
            return ClickOutcome.Stay;
        }

        try
        {
            var client = new VelopackUpdateClient(feed);
            var candidate = client.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();

            if (candidate is null)
            {
                _available = null;
                _note = "There is nothing to install any more: the feed no longer offers a newer version.";
                return ClickOutcome.Rerender;
            }

            client.DownloadAsync(candidate, _ => { }, CancellationToken.None).GetAwaiter().GetResult();

            // ⚠️ restart: true, which is the opposite of what the SERVER's lane
            // does. A server that restarted would pop this window in the middle
            // of somebody's session; an app that did not restart would vanish
            // mid-click with nothing to say it had succeeded.
            client.ApplyAndRestart(candidate);

            return ClickOutcome.Close;
        }
#pragma warning disable CA1031 // Same boundary as the check: a sentence, never a crash.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            _note = $"The update did not install: {failure.Message}";
            return ClickOutcome.Rerender;
        }
    }
}

/// <summary>The configuration app's own records.</summary>
internal static partial class AppLog
{
    /// <summary>The window is opening.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="version">What this build is.</param>
    /// <param name="occasion">Why it opened.</param>
    /// <param name="status">What it is about to say.</param>
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "BrowserAI {Version} opening its configuration window ({Occasion}). {Status}")]
    public static partial void Opening(ILogger logger, string version, Occasion occasion, string status);

    /// <summary>A status report was written.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="path">Where it went.</param>
    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "Wrote a BrowserAI status report to {Path}.")]
    public static partial void ReportWritten(ILogger logger, string path);

    /// <summary>Something Velopack said, replayed.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="message">What it said.</param>
    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Debug,
        Message = "Velopack: {Message}")]
    public static partial void Velopack(ILogger logger, string message);

    /// <summary>Something Velopack complained about, replayed.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="message">What it said.</param>
    /// <param name="failure">What it carried.</param>
    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Warning,
        Message = "Velopack reported a problem: {Message}")]
    public static partial void VelopackProblem(ILogger logger, string message, Exception? failure);
}
