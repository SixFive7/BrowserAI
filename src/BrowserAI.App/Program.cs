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
    /// <param name="restarted">Whether Velopack's restart after an update started it, read the same way.</param>
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
            var written = StatusReport.Write(AppState.Read(tool, Environment.CurrentDirectory), report);
            AppLog.ReportWritten(logger, written);
            return 0;
        }

        var occasion = restarted ? Occasion.AfterUpdate : firstRun ? Occasion.FirstRun : Occasion.Ordinary;

        // ⚠️ WHO THE COORDINATOR IS, SETTLED BEFORE ANYTHING IS OPENED -- Q280 b,
        // Q284 a, 2026-09-25. Holding the pipe is being the coordinator. A start that
        // finds it held hands its verb over and exits here; a person's start gets the
        // address of a new tab back (Q334 a, Q337 a) and opens it, and a hidden start
        // that is not needed costs a few milliseconds. The root is the census's own:
        // the install root, or the data root of a build that is not installed, which
        // is what keeps a scratch BROWSERAI_ROOT one coordinator.
        var mode = StartModes.Of(args);
        var root = InstallLocation.RootAppDir ?? paths.RootAppDir;
        var person = StartModes.IsAPersons(mode);
        var verb = StartModes.VerbOf(mode);
        var writeAddress = PageOpener.WriteAddressFrom(args);
        var feed = UpdateConfiguration.Resolve(logger);

        // ⚠️ THE SESSION HOST'S KEEPER, BEFORE THE PIPE -- Q366 b, 2026-10-03. A
        // server's `host` is answered on the pipe's own thread, which may run the
        // moment the pipe exists, so what it starts the host with has to exist first.
        // An empty job and nothing started until somebody asks; a start that hands
        // over closes it again. Declared before the inbox and the pipe so it is
        // disposed after them: the pipe stops answering, and then the job's close
        // ends the host and everything it started.
        using var keeper = SessionHostKeeper.ForThisInstall(Environment.ProcessPath, paths.RootAppDir, logger);
        using var inbox = new CoordinatorInbox(keeper is null ? null : keeper.EnsureStarted);

        // ⚠️ THE PAGE EXISTS BEFORE THE PIPE, AND STARTS NOTHING UNTIL ASKED -- Q315 a,
        // 2026-10-03. The pipe hands out tabs from its first connection on, so the
        // page it hands them out of has to be there first; its listener starts on the
        // first hand-out and not before.
        using var page = new PageService(
            PageOpener.FactsFor(paths),
            occasion,
            new VelopackPageUpdates(feed, InstallLocation.IsInstalled),
            new CensusPageSessions(root, TimeProvider.System),
            new RegisterAiPageRegistration(tool, Environment.ProcessPath, () => AppState.Read(tool, Environment.CurrentDirectory), logger),
            new DesktopPageHost(inbox, logger),
            inbox.Wake,
            TimeProvider.System,
            PageTabs.ProductLinger,
            logger)
        {
            // An install from the page stops the session host the way this
            // process's own apply does (Q366 b).
            SessionHost = keeper,

            // The changelog page reads the section this build carries for its own
            // version (T, 2026-10-08). What holds an update is reported by the
            // resident background, which is not this process yet, so the update page
            // says nothing reports it.
            Changelog = ShippedChangelog.ForThisBuild(),
        };

        var start = CoordinatorStart.Settle(
            root,
            inbox,
            verb,
            person ? Foreground.Grant : null,
            logger,
            addressFor: asked => page.HandOut(StartModes.PageOf(asked)));

        if (start.Outcome is CoordinatorStartOutcome.HandedOver)
        {
            if (person)
            {
                _ = PageOpener.Deliver(start.HandOver?.Address, writeAddress, ShellInterop.OpenUrl, logger);
            }

            return 0;
        }

        using var pipe = start.Pipe;

        // The session host and everything it started run from the install too, and
        // are this process's own to stop before an apply, not processes it waits
        // for: the scan leaves them out (Q366 b).
        RootScan scanRoot() => keeper is null
            ? BrowserProcesses.HeldUnder(root, Environment.ProcessId)
            : keeper.LeaveOutMine(BrowserProcesses.HeldUnder(root, Environment.ProcessId));

        if (pipe is null)
        {
            // Neither the coordinator nor handed over, and the log says why. A
            // person still gets the tab they asked for, served by this process alone
            // until it closes; a hidden start has nothing to do without the pipe.
            if (!person)
            {
                return 1;
            }

            _ = PageOpener.Deliver(page.HandOut(StartModes.PageOf(verb)), writeAddress, ShellInterop.OpenUrl, logger);
            _ = new CoordinatorLoop(root, inbox, NothingStaged.Instance, scanRoot, page, logger).Run();
            return 0;
        }

        var started = mode.ToString();

        CoordinatorLog.Became(logger, pipe.Name, started);

        if (person)
        {
            _ = PageOpener.Deliver(page.HandOut(StartModes.PageOf(verb)), writeAddress, ShellInterop.OpenUrl, logger);
        }

        // A server found neither a host nor a coordinator and ran the logon task to
        // have one started; the loop below then stays for as long as it runs.
        if (mode is StartMode.StartHost)
        {
            _ = keeper?.EnsureStarted();
        }

        IStagedUpdates staged = feed is not null
            ? new VelopackUpdateClient(feed)
            : NothingStaged.Instance;

        // ⚠️ THE SIGN-IN STEP -- Q282 a and Q285 a, 2026-09-25. One pass: a staged
        // package and nothing else running from the install is handed to Update.exe
        // and this process exits so it can apply; anything else is logged and this
        // process exits too. The one exception is a verb that reached the pipe during
        // the pass -- a person's start asking for a tab, or a blocked server's start
        // handing over its recheck -- which the loop below then answers, since the
        // start that sent it has already exited.
        if (mode is StartMode.SignIn)
        {
            var signIn = SignInStep.Run(staged, scanRoot, logger, keeper);

            // Q366 b: a host a server asked for during the pass keeps this process,
            // whose job it is in, even before that server's verb reaches the inbox.
            if (signIn.Outcome is SignInOutcome.Applied || (inbox.IsEmpty && !page.IsServing && keeper?.Running is null))
            {
                return 0;
            }
        }

        // ⚠️ THE APPLY LOOP -- Q285 a, 2026-09-25, and since 2026-10-03 the tab's loop
        // too (Q336 a). It waits on every process the scan holds and on the pipe,
        // re-scans on each exit and each verb, applies once nothing else runs from the
        // install, and stops when nothing is staged and no tab has been open for a
        // minute. A build with no update feed has nothing staged, so it stops as soon
        // as its page has nobody left. Since 2026-10-03 it also stays for as long as
        // the session host it started runs, and stops that host before an apply
        // (Q366 b).
        _ = new CoordinatorLoop(root, inbox, staged, scanRoot, page, logger) { Host = keeper }.Run();

        return 0;
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

    // Ids 6001 (the window opening) and 6005 (a click in the window threw) went with
    // the configuration window on 2026-10-03, which the browser tab replaced. A log
    // query written against them reads the window's events, so neither comes back.
}
