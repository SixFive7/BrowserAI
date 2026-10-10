// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;
using Velopack.Logging;

namespace BrowserAI;

/// <summary>
/// Entry point: start the child, serve the caller, and take the whole tree down
/// on the way out.
/// </summary>
/// <remarks>
/// The order matters. Logging first, because a failure before it exists has
/// nowhere to be reported. The child next, so a payload or browser problem is a
/// startup failure with a message and not a tool call that fails later for
/// reasons the caller cannot see. stdout is acquired last, and by then it
/// belongs to the protocol.
/// </remarks>
internal static partial class Program
{
    /// <summary>
    /// The one environment variable BrowserAI reads about <b>itself</b>, and it
    /// moves the <b>data</b> root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists for one thing the suite otherwise cannot do: an empty
    /// browsers root.</b> First-run provisioning can only be proven against a
    /// root where nothing has ever been installed, and the alternative -- deleting
    /// the developer's own <c>%LocalAppData%\BrowserAI\browsers</c> mid-suite --
    /// would destroy 430 MiB and break every other browser test running beside
    /// it. <see cref="Hosting.IAppPaths"/> deliberately does not resolve relative
    /// to the binary, so moving the executable does not move the root either.
    /// </para>
    /// <para>
    /// ⚠️ <b>Narrowed 2026-09-15 (previously "it moves the whole app root" and
    /// "it is read here rather than inside <see cref="LocalAppDataPaths"/>, so it
    /// stays a decision the host makes once ... step 19 swaps that class for one
    /// over <c>VelopackLocator.Current.RootAppDir</c>").</b> It moves the data
    /// root and <b>never the install root</b>, which is Velopack's to choose and
    /// which this process only ever reads. It is also read inside
    /// <c>LocalAppDataPaths.Overridden</c> now and not here, because
    /// <c>Main</c> is not the only entry point into this binary: a Velopack
    /// fast-exit hook never reaches this method's body, and the uninstall hook
    /// offers to delete the data root -- so a second reader that answered
    /// differently would be offering to delete a directory nobody used.
    /// <i>Changed 2026-10-08 by step 5 of the one-binary build:</i> that method is
    /// gone, the hooks read the variable once in <c>InstallerSettings.Read</c> and
    /// hand every start <c>--data-root</c>, and no running BrowserAI reads it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-09-15 (previously the declaration itself).</b> The
    /// literal moved to <see cref="LocalAppDataPaths.RootVariable"/> and this is
    /// an alias for it. Two executables resolve a data root now -- the server and
    /// the configuration app -- and the class that reads the variable lives in
    /// the library both of them link, which an executable's own constant cannot.
    /// The name is unchanged, and every existing reader still compiles against
    /// this spelling.
    /// </para>
    /// <para>
    /// <b>Never silent.</b> A BrowserAI running against a root nobody expects
    /// would look exactly like one that lost its sessions, so an override is
    /// logged at Warning on the way past. A relative value is ignored and not
    /// resolved, for the same reason a relative <c>PLAYWRIGHT_BROWSERS_PATH</c>
    /// is refused: it would land somewhere nobody chose and report nothing.
    /// </para>
    /// </remarks>
    public const string AppRootVariable = LocalAppDataPaths.RootVariable;

    /// <summary>
    /// Runs one stray sweep synchronously and exits, instead of serving stdio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Corrected 2026-08-16 (previously "the argument the logon task's action
    /// passes").</b> [The logon task is dropped](../../kb/windows/detection.md#the-logon-sweep-task) --
    /// it cannot be registered from BrowserAI's own non-elevated token, measured
    /// twice, for a minimal task definition as much as for ours -- so this
    /// argument has exactly one caller left and it is a
    /// <i>measurement</i> and not a product path:
    /// [re-verification row 78](../../kb/re-verification.md) says to
    /// re-establish the sweep-pass census with
    /// <c>BrowserAI.Server.exe --sweep</c> under a scratch
    /// <c>BROWSERAI_ROOT</c> and read the process log. ⚠️ <i>Corrected
    /// 2026-09-16 (previously <c>BrowserAI.exe --sweep</c>)</i> -- that name
    /// belongs to the configuration app since 2026-09-15, which does not take
    /// this argument and opens a window instead, so the procedure did not
    /// produce a wrong number: it produced a dialog. ⚠️ <i>And corrected again
    /// 2026-10-08 by addition</i>: there is one executable, so the row's command is
    /// <c>BrowserAI.exe --sweep</c> once more, and the argument is what makes it a
    /// sweep (<see cref="ServesStdio"/>); since step 5 the scratch root is
    /// <c>--data-root</c>, because no running BrowserAI reads the variable. That row is the
    /// only route to the <b>published AOT</b> column of
    /// [the table](../../kb/windows/detection.md#the-sweep-measured-through-the-products-own-code-paths) --
    /// the test probe is a framework-dependent Debug build and measures the
    /// other column -- so deleting this would strand a `[MACHINE]` figure with no
    /// way back to it.
    /// </para>
    /// <para>
    /// It is deliberately not a supported interface: nothing registers it, no
    /// installer passes it, and it is undocumented in the model-facing surface.
    /// </para>
    /// </remarks>
    public const string SweepArgument = "--sweep";

    /// <summary>The file name Velopack gives its updater, directly under the install root.</summary>
    /// <remarks>
    /// A relay that finds it running from its own install root answers at once with
    /// the update sentence (U2), found by its full image path and never by its name.
    /// </remarks>
    public const string UpdaterFileName = "Update.exe";

    /// <summary>
    /// The argument a client's registration starts the one executable with: serve
    /// this client over stdio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D7 a, the maintainer's words of 2026-10-08, verbatim: <i>"d7 a"</i>.</b>
    /// There is one file, <c>BrowserAI.exe</c>, and a client is registered as
    /// <c>current\BrowserAI.exe --mcp</c>. A start with no argument is a person's:
    /// the Start Menu, <c>Setup.exe</c> after a non-silent install, a double-click.
    /// </para>
    /// <para>
    /// <b>The argument says what was meant, and a pipe on standard input is
    /// required as well.</b> Choosing by the pipe alone was rejected in the plan: a
    /// hook whose standard input Velopack pipes, or a person piping into the binary,
    /// would become a server. A start with the argument and no pipe writes one record
    /// and exits (<see cref="StartupLog.NoPipeToServe"/>).
    /// </para>
    /// </remarks>
    public const string McpArgument = "--mcp";

    /// <summary>
    /// The one entry point: the installer's hooks first, then the job the arguments
    /// name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>One <c>Main</c> since 2026-10-08, D7 a</b> (previously two: this file's,
    /// asynchronous and serving stdio, and the configuration app's, on a
    /// single-threaded apartment). The app's apartment is kept, for the reason its
    /// own remarks give: Explorer, which the tab opens on the coordinator's thread,
    /// expects one. A mode that serves stdio blocks this thread on its asynchronous
    /// work, and nothing that work owns needs the thread to pump.
    /// </para>
    /// <para>
    /// <b>The hooks are served first, by Velopack's own <c>Run()</c></b>, which
    /// exits the process when it serves one, so nothing below runs inside a hook.
    /// It also carries <c>SetAutoApplyOnStartup(false)</c>, whose default would
    /// make a server exit at handshake time and relaunch detached with dead pipes.
    /// The two variables the installer and Velopack's restart set are read before
    /// it, because <c>Run()</c> clears them, and cleared from this process after
    /// it (<see cref="App.Program.ClearTheInstallersOwnVariables"/>).
    /// </para>
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code of the job the arguments named.</returns>
    [STAThread]
    private static int Main(string[] args)
    {
        args ??= [];

        var firstRun = VelopackStartup.StartedByTheInstaller();
        var restarted = Environment.GetEnvironmentVariable(App.Program.RestartVariable) is { Length: > 0 };

        // Velopack's own records are buffered and not dropped: the log cannot
        // exist yet, because WHERE it goes depends on the install root this call
        // is what establishes. Each mode replays them into its log.
        var velopack = new List<(VelopackLogLevel Level, string Message, Exception? Failure)>();
        VelopackStartup.RunAndServeLifecycleHooks(args, (level, message, failure) => velopack.Add((level, message, failure)));

        App.Program.ClearTheInstallersOwnVariables();

        return ServesStdio(args) || IsTheTasksStart(args)
            ? ServeAsync(args, velopack).GetAwaiter().GetResult()
            : App.Program.Run(args, firstRun, restarted, velopack);
    }

    /// <summary>Whether the arguments name a mode that serves stdio.</summary>
    /// <remarks>
    /// <b>Two of them</b>: <see cref="McpArgument"/>, a client's relay, and
    /// <see cref="SweepArgument"/>, one stray sweep for a kb re-verification row.
    /// <i>Corrected 2026-10-08 (previously three, with <c>--host</c> and a pipe, the
    /// session host the coordinator started)</i>: the background holds every session
    /// since S a, and the session host went with the coordinator. Every other start
    /// is the background's (<see cref="IsTheTasksStart"/>) or the configuration
    /// app's: a person's start, a toast's click, <c>--after-update</c>,
    /// <c>--report</c>.
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <returns>Whether the start serves stdio.</returns>
    internal static bool ServesStdio(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Contains(McpArgument, StringComparer.Ordinal)
            || args.Contains(SweepArgument, StringComparer.Ordinal);
    }

    /// <summary>Whether the Task Scheduler started this process: the background's start.</summary>
    /// <remarks>
    /// <b><see cref="BackgroundArgument"/>, and the three arguments a task registered
    /// before 2026-10-08 still passes</b>: <c>--sign-in</c> at sign-in,
    /// <c>--coordinate</c> and <c>--start-host</c> from a server of that build. Such a
    /// task runs this file once the update has swapped it in, until the update hook
    /// registers the task again, and each of its starts is the task's. Read as a
    /// person's start, a sign-in would open a tab.
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <returns>Whether the background runs.</returns>
    internal static bool IsTheTasksStart(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Contains(BackgroundArgument, StringComparer.Ordinal)
            || args.Contains(LegacySignInArgument, StringComparer.Ordinal)
            || args.Contains(LegacyCoordinateArgument, StringComparer.Ordinal)
            || args.Contains(LegacyStartHostArgument, StringComparer.Ordinal);
    }

    /// <summary>What a task registered before 2026-10-08 passes at sign-in.</summary>
    public const string LegacySignInArgument = "--sign-in";

    /// <summary>What a server of a build before 2026-10-08 ran that task with.</summary>
    public const string LegacyCoordinateArgument = "--coordinate";

    /// <summary>What a server of a build before 2026-10-08 ran that task with to have its session host started.</summary>
    public const string LegacyStartHostArgument = "--start-host";

    /// <summary>The value after an argument, or <see langword="null"/>.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The value that follows it.</returns>
    internal static string? ValueOf(IReadOnlyList<string>? args, string argument)
    {
        if (args is null)
        {
            return null;
        }

        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], argument, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>The server half: the background, a client's relay, or one sweep.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="velopack">What Velopack said before the log existed, to replay into it.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> ServeAsync(string[] args, List<(VelopackLogLevel Level, string Message, Exception? Failure)> velopack)
    {
        // ⚠️ TWO SOURCES, NOT THREE -- 2026-09-15. The locator used to sit
        // between these two and it is gone: an installed BrowserAI no longer
        // takes its DATA root from VelopackLocator.Current.RootAppDir, because
        // that root is the one Setup.exe renames aside and deletes and uninstall
        // empties. The data root is the constant %LocalAppData%\BrowserAI and
        // the only thing that moves it is the suite's override. Never
        // AppContext.BaseDirectory, which resolves INSIDE current\ and is
        // replaced by every update; never the image path, which moves with
        // --installto. See Hosting/IAppPaths.cs for the whole argument.
        //
        // ⚠️ THE ARGUMENT AND NOTHING ELSE SINCE 2026-10-08, step 5 of the one-binary
        // build: the hooks write --data-root from the installer's environment into the
        // task's action and every registration, and no running BrowserAI reads a
        // BROWSERAI_ variable (previously the variable, read when no argument named one).
        var overridden = ValueOf(args, DataRootArgument) is { Length: > 0 } named ? named : null;
        var paths = new LocalAppDataPaths(overridden);

        using var log = ProcessLog.Create(paths, LogLevel.Information);
        var logger = log.Factory.CreateLogger("BrowserAI.Startup");
        var updateLogger = log.Factory.CreateLogger("BrowserAI.Updates");

        StartupLog.Started(
            logger,
            BuildVersion.Current,
            Environment.ProcessId,
            Environment.ProcessPath ?? "<unknown>",
            Environment.CurrentDirectory,

            // ⚠️ THE FIRST CALL INTO THE STATICALLY LINKED SQLITE, and it is
            // here and not anywhere later, on purpose. If the amalgamation
            // did not compile, or ILC did not link the archive, this line is
            // where that shows -- on the startup path, in the first record,
            // before a session exists. The alternative is finding out at the
            // moment a session first writes its record, which is the worst
            // available place and the one the loose-DLL deployments hit.
            Sqlite.Version,

            // ⚠️ AND THE ONLY PLACE THE COMPILE FLAGS CAN BE CHECKED AT ALL.
            // `PRAGMA compile_options` describes the library that is actually
            // bound, and the two hosts this code runs under bind different
            // ones: the published binary links the archive build/Sqlite.targets
            // compiles from vendored source, while a CoreCLR test host loads a
            // loose e_sqlite3.dll somebody else built with somebody else's
            // flags. So the claim "these are the flags" is a claim about the
            // artifact, and this is the artifact saying it. A test reads this
            // field back off the published slice; nothing here refuses, because
            // the record is not stored in SQLite yet and a startup that died
            // over a storage question nothing has asked would be refusing on
            // behalf of a phase that has not happened.
            Sqlite.BuildReport);

        foreach (var (level, message, failure) in velopack)
        {
            // Replayed here and not at the call site because THIS is where
            // the second half of the question can be answered: InstallLocation
            // cannot speak until VelopackApp.Run() above has set the locator.
            // "Not installed" is a supported configuration -- dotnet run, every
            // test host, CI -- and a supported configuration must not warn; a
            // genuine locator failure carries different text and still does.
            if (level >= VelopackLogLevel.Warning
                && !VelopackStartup.IsRoutineNotInstalledNotice(level, message, InstallLocation.IsInstalled))
            {
                UpdateLog.VelopackProblem(updateLogger, message, failure);
            }
            else
            {
                UpdateLog.Velopack(updateLogger, message);
            }
        }

        if (InstallLocation.IsInstalled)
        {
            UpdateLog.Installed(
                updateLogger,
                InstallLocation.RootAppDir ?? "<unknown>",
                InstallLocation.InstalledChannel ?? "<none>",
                InstallLocation.InstalledVersion ?? "<unknown>",
                BuildVersion.Current);
        }

        if (overridden is not null)
        {
            StartupLog.AppRootOverridden(logger, DataRootArgument, overridden);
        }

        // ⚠️ THE BACKGROUND -- S a, the maintainer's words of 2026-10-08, verbatim:
        // "s a". One resident process per user, install root and data root holds
        // every session, the tab and the update, from sign-in to sign-out. The Task
        // Scheduler starts it, and nothing else does (RESOLUTIONS 9). A task
        // registered by a build before 2026-10-08 still starts the program with
        // --sign-in, --coordinate or --start-host until the update hook registers it
        // again, and each of those starts is the task's: it becomes the background,
        // where until that day it became the coordinator. See Program.Background.cs.
        if (IsTheTasksStart(args))
        {
            return RunTheBackground(args, paths, log, logger);
        }

        // ⚠️ ONE SWEEP, FOR A KB RE-VERIFICATION ROW, AND NOTHING ELSE. A person's
        // measurement from a terminal: it judges the roots the way the background
        // does and prints nothing to a client.
        if (args.Contains(SweepArgument, StringComparer.Ordinal))
        {
            var scope = InstallRootScope.Judge(paths.RootAppDir, InstallLocation.RootAppDir);

            if (scope.Unestablished is { } unestablished)
            {
                StartupLog.AppRootScopeUnestablished(logger, unestablished);
            }

            if (!scope.MayServe)
            {
                StartupLog.AppRootIsShared(logger, scope.Refusal!);
                return 1;
            }

            return SweepOnce(paths, InstallLocation.RootAppDir ?? paths.RootAppDir, log.Factory, logger);
        }

        // ⚠️ A CLIENT'S START WITH NO PIPE ON STANDARD INPUT IS NOBODY TO SERVE --
        // 2026-10-08, D7 a. Every client gives the process it starts a pipe, 54 of
        // 54 runs measured on 2026-10-04, and a windowless start with no handles
        // reads end of input at once. Corrected 2026-10-08 (previously "if (client
        // is null && StandardInput.IsAConsole())", the conjunction measured on
        // 2026-09-14 against the installer's own start: a launcher already gone
        // and a console that never ends, which left a server and its node child
        // serving nobody until the machine was rebooted). The installer never
        // starts a server since 2026-09-15, and a pipe cannot be that console, so
        // the narrower rule holds the old shape too; StartupLog[9] is retired with
        // it.
        if (!StandardInput.IsAPipe())
        {
            StartupLog.NoPipeToServe(logger, ProcessLiveness.ParentProcessId());
            return 0;
        }

        // ⚠️ THE RELAY, AND NOTHING ELSE SERVES A CLIENT -- S a and R, 2026-10-08.
        // Corrected 2026-10-08 (previously this method went on to serve the client
        // in this process: a census marker, a pipe of its own, the front's search
        // for the session host, the surface child and an MCP server over stdio,
        // with Q296 c's serving during an update). The relay answers the handshake
        // and the tool list from the binary and passes every call to the
        // background; with none, it holds and then says why, and it starts nothing.
        return await RunTheRelayAsync(args, paths, log, logger).ConfigureAwait(false);
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
    private static StraySweep CreateSweep(IAppPaths paths, string installRoot, ILoggerFactory factory)
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
            // of the images above and not as a second detection rule: what
            // counts as ours is still one full-image-path match.
            ProvisionedBrowsers.ExecutablesFor(ProvisionedBrowsers.Firefox, paths.BrowsersDirectory, manifest),

            // And the live-marker reclaim rides the same pass, for the mutex
            // discipline this one already has. ⚠️ It is handed the INSTALL root
            // while everything above it came from the DATA root: the two are
            // siblings since 2026-09-15 and this is the one place both are in
            // one expression, which is why the split is spelled here and not
            // resolved inside the sweep.
            installRoot,

            // A sweep that ends a crashed session's browser is the one close path
            // no session is left to reap after, so this pass carries the reaper
            // too -- started detached, never awaited, and only when something was
            // really terminated. `SessionManager` builds its own for the three
            // close paths a live session has.
            new ServerRegistryReap(payload, factory.CreateLogger<ServerRegistryReap>()));
    }

    /// <summary>Runs one sweep and exits, for <see cref="SweepArgument"/>.</summary>
    private static int SweepOnce(IAppPaths paths, string installRoot, ILoggerFactory factory, ILogger logger)
    {
        try
        {
            _ = CreateSweep(paths, installRoot, factory).Run();
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
    /// <summary>
    /// The first line of every run, and the only place the running build's
    /// version is recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The version is here because the process log survives an update</b> --
    /// it lives outside <c>current\</c>, which an update replaces wholesale, so
    /// the log of a machine that updated itself carries both versions and the
    /// moment it changed. Without it, *"which build was running when this
    /// happened"* is unanswerable for every past run.
    /// </para>
    /// <para>
    /// <b>The SQLite version is here for the same reason and for one more.</b>
    /// It is the only dependency this product does not float -- no maintained
    /// package ships a win-x64 static library, so the amalgamation is pinned in
    /// the tree and compiled by the build -- and a pin is exactly the thing that
    /// stops matching what a reader assumes. Recording it beside the build
    /// version makes <i>"which SQLite was linked when this happened"</i>
    /// answerable for every past run, on the same file that already survives an
    /// update.
    /// </para>
    /// <para>
    /// It is also the whole of what proves the static link at all: the archive
    /// is resolved by the linker at publish time, so a build in which the
    /// compile step quietly did nothing is indistinguishable from a correct one
    /// everywhere except at a call, and this is the earliest call there is.
    /// </para>
    /// <para>
    /// <b>And the build field beside it is the only place the compile-time
    /// options can be checked.</b> The version says <i>which SQLite</i>; the
    /// options say <i>which build of it</i>, and those are separate ways to be
    /// wrong -- an archive compiled without <c>SQLITE_OMIT_AUTOINIT</c>, or with
    /// sqlite.org's recommended <c>SQLITE_THREADSAFE=0</c> that this tree
    /// deliberately does not take, reports the same version and behaves
    /// differently. A test reads this field off the published binary's own
    /// record, because the loose library a test host loads is somebody else's
    /// build and can say nothing about the artifact.
    /// </para>
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version derived from the git tag at build time.</param>
    /// <param name="processId">This process.</param>
    /// <param name="imagePath">The binary it is running.</param>
    /// <param name="workingDirectory">Where it was started.</param>
    /// <param name="sqlite">The version of the SQLite compiled into this binary.</param>
    /// <param name="sqliteBuild">
    /// Whether that SQLite carries the compile-time options this tree intends,
    /// and what is missing when it does not.
    /// </param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "BrowserAI {Version} started. pid={ProcessId} image={ImagePath} cwd={WorkingDirectory} sqlite={Sqlite} sqliteBuild={SqliteBuild}")]
    public static partial void Started(ILogger logger, string version, int processId, string imagePath, string workingDirectory, string sqlite, string sqliteBuild);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "BrowserAI is serving stdio. callerProtocol={CallerProtocol} childProtocol={ChildProtocol}")]
    public static partial void Serving(ILogger logger, string callerProtocol, string childProtocol);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Critical,
        Message = "BrowserAI could not start and is exiting.")]
    public static partial void Failed(ILogger logger, Exception exception);

    /// <summary>
    /// The app root came from the environment and not from
    /// <c>%LocalAppData%</c>.
    /// </summary>
    /// <remarks>
    /// Warning, not Information, and it is the first line after startup:
    /// a BrowserAI whose sessions, log and 430 MiB of browsers are somewhere
    /// nobody expected looks exactly like one that lost them.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="argument">Which argument moved it.</param>
    /// <param name="root">Where it now is.</param>
    /// <remarks>
    /// <i>Corrected 2026-10-10, the texts polish, page #183 (previously "{Variable} is set,
    /// so this BrowserAI's app root is ... Its sessions, log and provisioned browsers all
    /// live there.")</i>: the root is moved by an argument, the argument calls it the data
    /// root, and it holds the session index, not the sessions.
    /// </remarks>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "This BrowserAI was started with {Argument}, so its data root is {Root}, not the one under %LocalAppData%. Its browsers, session index and log are there.")]
    public static partial void AppRootOverridden(ILogger logger, string argument, string root);

    /// <summary>
    /// The app root is one more than this user can reach, so this process is not
    /// serving.
    /// </summary>
    /// <remarks>
    /// <b>Critical, and the whole sentence is the parameter.</b> The message
    /// template is a constant by construction, and the refusal has to name the
    /// root it found, why a shared root is unsafe and what to change -- so it is
    /// composed by <see cref="Hosting.InstallRootScope"/>, where the reasoning
    /// lives, and carried here whole and not reassembled out of fields a
    /// template would fix the order of.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="refusal">The whole refusal, remedy included.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Critical,
        Message = "{Refusal}")]
    public static partial void AppRootIsShared(ILogger logger, string refusal);

    /// <summary>
    /// Whether the app root is per-user could not be settled, and BrowserAI is
    /// serving anyway.
    /// </summary>
    /// <remarks>
    /// Warning, not Critical: an unreadable ancestor is a locked-down
    /// machine and not a shared root, and refusing on it would stop a
    /// background MCP server starting at all. What it must not be is silent --
    /// that is the state the whole 2026-08-20 measurement was about.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="why">What stopped the question being answered.</param>
    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Warning,
        Message = "{Why}")]
    public static partial void AppRootScopeUnestablished(ILogger logger, string why);

    /// <summary>
    /// The client went and closing the protocol channel after it threw.
    /// </summary>
    /// <remarks>
    /// The process still goes down -- the disposals on the way out of
    /// <c>Main</c> run regardless, and the job objects are the guarantee under
    /// all of it. This line exists so that a shutdown which did not go the way
    /// it was meant to is visible and not inferred from a missing log.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="exception">Why.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "The MCP client exited and BrowserAI's protocol channel could not be closed. Shutdown continues; the job objects still take every child and browser down.")]
    public static partial void ChannelNotClosed(ILogger logger, Exception exception);

    /// <summary>
    /// A client's start, <c>--mcp</c>, found no pipe on standard input, so there is
    /// nobody to serve.
    /// </summary>
    /// <remarks>
    /// <b>Warning: no client ever starts a server that way.</b> Every client gives
    /// its server a pipe, 54 of 54 runs measured on 2026-10-04, so a start that
    /// carries the argument and no pipe is a person or a script typing it, and the
    /// record says what the argument needs. Added 2026-10-08 with the one
    /// executable, in place of event 9, retired below.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="launcher">The pid the kernel recorded as this process's creator.</param>
    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Warning,
        Message = "BrowserAI was started with --mcp by pid {Launcher}, and its standard input is not a pipe, so there is no client to serve and it exits. A client starts BrowserAI with --mcp and a pipe on standard input; a person starts it with no argument.")]
    public static partial void NoPipeToServe(ILogger logger, int launcher);

    /// <summary>
    /// This install's updater is running, so this server refuses every tool call
    /// until it has gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Warning: the client this server answers cannot use it for the length of
    /// the update, which is the state an investigator of a failed call wants to
    /// find first.
    /// </para>
    /// <para>
    /// ⚠️ <b>The same event with a new consequence since 2026-10-03, Q296 c</b>
    /// (previously the message said the server "answers its client's handshake,
    /// refuses every tool call and starts no browser server until the updater has
    /// gone"): it starts its child and answers the tool list now. The event is
    /// still <i>an updater was running when this server started</i>, so the id
    /// stays.
    /// </para>
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="updater">The updater's pid.</param>
    /// <param name="image">The updater's full image path.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "This install's updater is running (pid={Updater}, {Image}), so this server answers its client's handshake and tool list and refuses every tool call until the updater has gone; then it serves them.")]
    public static partial void UpdateInProgress(ILogger logger, int updater, string image);

    /// <summary>The updater this server found at startup has gone, so its calls are served from here on.</summary>
    /// <remarks>
    /// ⚠️ <b>The same event with a new consequence since 2026-10-03, Q296 c</b>
    /// (previously "so this server ends its conversation; the client's next call
    /// starts a server from whatever the update left in place"): the conversation
    /// goes on. The event is still <i>the updater this server found has gone</i>, so
    /// the id stays.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="updater">The updater's pid.</param>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "The updater (pid={Updater}) has gone, so this server now serves its client's tool calls, and starts the stray sweep and the update check it held back.")]
    public static partial void UpdaterExited(ILogger logger, int updater);

    /// <summary>The refusals a stop sends to the calls in flight failed; the stop went ahead.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "The refusals for the calls in flight could not all be sent; the stop goes ahead.")]
    public static partial void StopRefusalsFailed(ILogger logger, Exception failure);

    /// <summary>The process list could not be read, so whether an update is running is not known.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Warning,
        Message = "Whether this install's updater is running could not be checked, so this server serves normally.")]
    public static partial void UpdaterNotChecked(ILogger logger, Exception failure);

    // ⚠️ EVENT ID 8 IS RETIRED -- Q276 a, 2026-09-24. It was
    // `StartedByTheInstaller`, "BrowserAI was started by the installer
    // (VELOPACK_FIRSTRUN=true) and there is nothing to serve", written by the
    // installer exit `Main` carried until that day. Every shipped build up to
    // 1.1.0 carries it, so a saved query for Startup[8] may still meet it in an
    // old log, and a new event under the same id would answer that query about
    // something else. Nothing may take it.
    //
    // ⚠️ EVENT ID 9 IS RETIRED -- 2026-10-08, D7 a. It was `NoClientToServe`,
    // "BrowserAI has no client to serve and is exiting: the process that started
    // it (pid=...) could not be opened or is gone, and standard input is a
    // console, not a pipe", written when a launcher that had gone met a console
    // standard input. The one executable serves a client only under --mcp with a
    // pipe, and says so as event 14. Builds up to 1.1.0 write 9, so it stays
    // taken.
    //
    // ⚠️ THE LINE BELOW IS READ BY `ProxyLogTests`, per class: the
    // machine-readable half of the paragraphs above, beside them and not in place
    // of them.
    //
    // RETIRED-EVENT-IDS: 8, 9
}
