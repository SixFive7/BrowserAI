// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Interop;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Registration;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;
using Velopack.Logging;

namespace BrowserAI.App;

/// <summary>
/// The configuration app's half of the one executable: a person's start, the
/// logon task's start and the status report.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three modes, decided by the arguments and the environment, and none of them
/// opens a window by itself.</b> A hook runs headless and exits; <c>--report</c>
/// writes a file and exits; anything else settles who the coordinator is, and a
/// person's start then opens a tab in the person's own browser.
/// <i>Corrected 2026-10-03 (previously "and only one of them has a window ...
/// anything else opens the dialog"), Q315 a: the browser tab replaces the window as
/// what a start opens, and the window is deleted (Q319 b).</i>
/// </para>
/// <para>
/// ⚠️ <b>And every start that is neither a hook nor a report settles who the
/// coordinator is first, since 2026-09-25</b> -- Q280 b and Q284 a, the
/// maintainer's words verbatim: <i>"Q284 a"</i>. The app is the coordinator when
/// it holds its pipe, <c>\\.\pipe\BrowserAI-Coordinator-</c> and the install
/// root's key. A start that finds the pipe held hands over instead: a person's
/// start grants the coordinator the foreground and asks it for a tab
/// (<c>show</c>, or <c>sessions</c> for the sessions page), a hidden start
/// (<c>--coordinate</c>, <c>--sign-in</c>) asks it to <c>recheck</c>, and either
/// way this process exits. <i>Corrected 2026-10-03 (previously "asks it to
/// <c>show</c> its window ... The dialog is still the only window, and only a
/// person's start or a <c>show</c> opens it"): the coordinator answers a tab's
/// address through the pipe, and the app has no window at all.</i>
/// </para>
/// <para>
/// ⚠️ <b>The hooks are dispatched by Velopack itself and not by reading
/// <c>args</c> here.</b> <c>VelopackApp.Run()</c> recognises its own four
/// arguments, invokes the callback and <b>exits the process</b> -- so serving a
/// hook never reaches the line below it. Re-implementing that dispatch would be
/// a second parser for somebody else's argument syntax, running beside theirs,
/// and the first divergence would be an installer that hangs on its own timeout.
/// </para>
/// <para>
/// ⚠️ <b>Not an entry point since 2026-10-08, D7 a, the maintainer's words
/// verbatim: <i>"d7 a"</i></b> (previously this class carried the configuration
/// app's <c>Main</c>, on a single-threaded apartment). There is one executable,
/// and its <c>Main</c> in <c>src/BrowserAI/Program.cs</c> serves the hooks,
/// reads the installer's two variables, clears them, and hands every start that
/// is not a server's to <see cref="Run"/> on the same single-threaded thread.
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
    /// from the window would be worse than none -- so the dialog and this file
    /// render the same <see cref="AppState"/>. <i>Corrected 2026-10-03 (previously
    /// as written, of the configuration window): the browser tab's registration
    /// section and this file render the same <see cref="AppState"/>, read through
    /// RegisterAI.</i>
    /// </remarks>
    public const string ReportArgument = "--report";

    /// <summary>
    /// The variable Velopack sets on the restart it performs after an update.
    /// </summary>
    /// <remarks>
    /// Read out of 1.2.0's own source (<c>constants.rs</c>:
    /// <c>HOOK_ENV_RESTART</c>), beside the first-run one. It is what turns the
    /// heading into <i>Updated to ...</i> instead of a guess from a timestamp.
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
    /// started carrying it -- and anything <i>that</i> process starts carries it
    /// too, including <c>BrowserAI.Server.exe</c>. The server exits 0 on that
    /// variable, deliberately, because a server the installer started has no
    /// client. It would then have exited 0 for a client that really was there,
    /// on first run, presenting as <i>failed to connect</i> with nothing in any
    /// log to say why.
    /// </para>
    /// <para>
    /// <b>Cleared, not filtered per launch</b>: there is more than one
    /// place this process starts something, and a filter that had to be
    /// remembered at each of them is the shape of the defect and not its
    /// fix. The value is read first, so the page still knows it was a first
    /// run.
    /// </para>
    /// <para>
    /// ⚠️ <b>The server no longer exits on the variable, since 2026-09-24 --
    /// Q276 a</b>, so the first paragraph's consequence no longer follows from
    /// it. The clearing stays: a child should not inherit an installer's marker
    /// it has no use for, and the Velopack callbacks still decide from it before
    /// this runs.
    /// </para>
    /// </remarks>
    public static void ClearTheInstallersOwnVariables()
    {
        Environment.SetEnvironmentVariable(VelopackStartup.FirstRunVariable, null);
        Environment.SetEnvironmentVariable(RestartVariable, null);
    }

    /// <summary>
    /// A start that is not a server's: a person's, the logon task's, a blocked
    /// server's through the task, or a report.
    /// </summary>
    /// <remarks>
    /// <b>Called on the one executable's main thread, which is a single-threaded
    /// apartment, and that is load-bearing.</b> The shell's folder picker falls back
    /// to the pre-Vista dialog on a thread that is not in one -- silently, with no
    /// error -- so it runs on a single-threaded thread of its own
    /// (<see cref="Page.DesktopPageHost"/>), and Explorer, which the page opens on
    /// the coordinator's thread, is this one. <i>Corrected 2026-10-08 (previously
    /// this was <c>Main</c>, carrying the attribute itself, and read the installer's
    /// variables and served the hooks before anything else): the one executable's
    /// <c>Main</c> does both, first, and passes what it read.</i>
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <param name="firstRun">Whether the installer started this process, read before Velopack's <c>Run()</c> cleared it.</param>
    /// <param name="restarted">Whether Velopack's restart after an update started it, read the same way: such a start raises a toast and opens no tab.</param>
    /// <param name="buffered">What Velopack said before the log existed.</param>
    /// <returns>Zero when what was asked for happened.</returns>
    public static int Run(
        string[] args,
        bool firstRun,
        bool restarted,
        IReadOnlyList<(VelopackLogLevel Level, string Message, Exception? Failure)> buffered)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(buffered);

        // The override, as the one-binary build carries it (step 5, 2026-10-08): the
        // --data-root a start was handed, and for a start that was handed none, a
        // person's or Velopack's, the one the hooks wrote into this install's task.
        // Never a variable, and never anything derived from the install root.
        var overridden = DataRootFrom(args)
            ?? (InstallLocation.RootAppDir is { } installed ? SignInTask.DataRootIn(SignInTask.SavedDefinition(installed)) : null);
        var paths = new LocalAppDataPaths(overridden);

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

        // ⚠️ A CLICK ON ONE OF BROWSERAI'S TOASTS -- T, decided 2026-10-08. COM starts this
        // program with -ToastActivated -Embedding for a click; the click is taken from
        // COM and acted on here, and a click that opens a dashboard page goes on below as
        // a person's start for that page, which is what it is.
        if (ToastActivation.IsActivation(args))
        {
            string[]? opening = null;
            var click = ToastInterop.CurrentAppUserModelId() is { } id ? ToastActivation.Receive(id, logger) : null;

            _ = ToastActivation.Act(click, UpdateToastMemoryFile.In(paths.RootAppDir), page =>
            {
                opening = StartModes.ArgumentsFor(page);
                return 0;
            });

            if (opening is null)
            {
                return 0;
            }

            args = opening;
        }

        var tool = RegisterAiTool.Beside(Environment.ProcessPath);

        // ⚠️ THE STATE IS READ ONLY WHERE IT IS SHOWN -- 2026-09-25. Reading it asks
        // every client for its registration, which starts each client's CLI; a
        // start that hands over to the coordinator, or a hidden one, shows nothing
        // and must not pay for that or start anybody's CLI. The report reads it
        // here, and the page reads it each time a tab loads its status page.
        if (ReportPathFrom(args) is { Length: > 0 } report)
        {
            var written = StatusReport.Write(AppState.Read(tool, Environment.CurrentDirectory, paths.RootAppDir), report);
            AppLog.ReportWritten(logger, written);
            return 0;
        }

        // ⚠️ AFTER AN UPDATE -- T and the step-0 research, settled in the plan of the
        // one-binary build on 2026-10-08. Velopack's restart starts this program with the version the
        // apply was meant to install, in the new version after a success and in the
        // old one after a failure, with the same arguments either way. The two are told
        // apart by comparing that version with this build's, and the background is
        // asked for through the task in both cases: every BrowserAI process has gone.
        // ⚠️ And a restart with no version of ours, from an apply another build made, is
        // this build's install, since the maintainer's 20 of 2026-10-10, verbatim: "20
        // nothing except for the toast". No tab opens after an update; until that day
        // such a restart went on below as a person's start, which opens one.
        if (AfterUpdate.TargetOf(args, restarted, BuildVersion.Current) is { } target)
        {
            AfterUpdate.Report(
                target,
                BuildVersion.Current,
                InstallLocation.AppId is { Length: > 0 } packId
                    ? AfterUpdate.VelopackLogPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), packId)
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "velopack"),
                UpdateToasts.ForThisProcess(null, paths, logger),
                () => AskTheTaskForTheBackground(PersonStart.StartedAfterAnUpdate, logger),
                logger);

            return 0;
        }

        // ⚠️ A PERSON'S START -- D13 a, the maintainer's words of 2026-10-08,
        // verbatim: "d13 a", amended by R the same day. Corrected 2026-10-08
        // (previously this settled who the coordinator was: a start that held the
        // coordinator's pipe became the coordinator, with the tab, the sign-in step
        // and the apply loop, and one that found it held handed its verb over). The
        // background holds the tab now, and a person's start only asks it for one,
        // starting it through the task when none runs. It is also the only thing that
        // restarts BrowserAI after a crash or a hang (R).
        var page = PageNameOf(args);
        var writeAddress = PageOpener.WriteAddressFrom(args);
        var installRoot = InstallLocation.RootAppDir;
        var pipe = PipeFrom(args) ?? BackgroundPipe.NameFor(installRoot, paths.RootAppDir);

        var (outcome, address) = PersonStart.Show(
            new PersonStartSettings
            {
                PipeName = pipe,
                RecordPath = BackgroundRecord.PathFor(paths.RootAppDir, pipe),
                InstallRoot = installRoot,
                DataRoot = paths.RootAppDir,
                Executable = Environment.ProcessPath ?? RegistrationTarget.AppFileName,
                TaskName = TaskNameForThisInstall(),
                Definition = () => installRoot is null ? null : SignInTask.SavedDefinition(installRoot),
                Tasks = ScheduledTasks.Instance,
                StartedBy = firstRun ? PersonStart.StartedByTheInstaller : PersonStart.StartedByPerson,
            },
            page,
            logger);

        if (outcome is not PersonStartOutcome.Shown)
        {
            return 1;
        }

        _ = PageOpener.Deliver(address, writeAddress, ShellInterop.OpenUrl, logger);
        return 0;
    }

    /// <summary>The page a person's start asks for, by the names the background's <c>show</c> takes.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The page's name, or <see langword="null"/> for the status page.</returns>
    internal static string? PageNameOf(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Contains(CoordinatorProtocol.SessionsArgument, StringComparer.Ordinal) ? "sessions"
            : args.Contains(CoordinatorProtocol.UpdateArgument, StringComparer.Ordinal) ? "update"
            : args.Contains(CoordinatorProtocol.ChangelogArgument, StringComparer.Ordinal) ? "changelog"
            : null;
    }

    /// <summary>The data root a start names, or <see langword="null"/>.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The data root.</returns>
    private static string? DataRootFrom(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], DataRootArgument, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>The argument that names the data root: the one executable's, read here for a person's start.</summary>
    public const string DataRootArgument = "--data-root";

    /// <summary>The suite's pipe, when a start names one.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The pipe, or <see langword="null"/>.</returns>
    private static string? PipeFrom(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], BackgroundPipe.PipeArgument, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>The task this install's background runs as, or <see langword="null"/> when it is not installed.</summary>
    /// <returns>The name.</returns>
    private static string? TaskNameForThisInstall() =>
        InstallLocation.RootAppDir is { } root && InstallLocation.AppId is { Length: > 0 } appId
            ? SignInTask.NameFor(appId, root)
            : null;

    /// <summary>Asks the Task Scheduler to run this install's task, for a start that is not a person's.</summary>
    /// <param name="startedBy">What <c>$(Arg0)</c> carries.</param>
    /// <param name="logger">Where the outcome is recorded.</param>
    private static void AskTheTaskForTheBackground(string startedBy, ILogger logger)
    {
        if (TaskNameForThisInstall() is not { } name)
        {
            return;
        }

        var run = ScheduledTasks.Instance.Run(name, startedBy);
        AppLog.TaskAsked(logger, run.Detail);
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

/// <summary>The configuration app's own records.</summary>
internal static partial class AppLog
{
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

    // The texts polish, 2026-10-10 (previously "... BrowserAI's background: {Change}.
    // {Detail}", a TaskChange member's name in mid-sentence): the detail says it.
    [LoggerMessage(
        EventId = 6006,
        Level = LogLevel.Information,
        Message = "Asked the Task Scheduler for BrowserAI's background. {Detail}")]
    public static partial void TaskAsked(ILogger logger, string detail);

    // Ids 6001 (the window opening) and 6005 (a click in the window threw) went with
    // the configuration window on 2026-10-03, which the browser tab replaced. A log
    // query written against them reads the window's events, so neither comes back.
}
