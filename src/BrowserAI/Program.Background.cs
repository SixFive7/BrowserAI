// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App;
using BrowserAI.App.Page;
using BrowserAI.Background;
using BrowserAI.Clients;
using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Proxy;
using BrowserAI.Registration;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI;

/// <summary>The background: every session, the tab and the update, in one resident process.</summary>
internal static partial class Program
{
    /// <summary>The argument the scheduled task starts the background with.</summary>
    /// <remarks>
    /// <b>S a, the maintainer's words of 2026-10-08, verbatim: <i>"s a"</i></b>: one
    /// resident background per user, install root and data root, from sign-in to
    /// sign-out. It is started by the Task Scheduler alone, asked by its sign-in
    /// trigger, by Velopack's restart after an install and by a person's start (D13 a),
    /// and never by a relay (RESOLUTIONS 9).
    /// </remarks>
    public const string BackgroundArgument = "--background";

    /// <summary>The argument that names the data root, on the background and on a relay alike.</summary>
    /// <remarks>
    /// <b>Settings travel as arguments</b> (the design's rule, kept by every decision
    /// of 2026-10-07 and 2026-10-08): the install hook reads the installer's
    /// <c>BROWSERAI_ROOT</c> once and writes this into the task's action and into the
    /// registration's arguments, so no running process reads the variable.
    /// </remarks>
    public const string DataRootArgument = "--data-root";

    /// <summary>The argument the task fills with <c>$(Arg0)</c>: who asked for this start.</summary>
    /// <remarks>
    /// <b>Literal at sign-in</b>, measured 2026-09-24 and again in step 0: the logon
    /// trigger passes no value, so the token reaches the process unexpanded, and that
    /// is how the background knows the trigger started it.
    /// </remarks>
    public const string StartedByArgument = "--started-by";

    /// <summary>
    /// The background: takes its pipe, writes its record, keeps a hidden window for the
    /// end of the session, sweeps once, and serves until a stop, an update or the end
    /// of the session.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="paths">The data root.</param>
    /// <param name="log">The process log.</param>
    /// <param name="logger">Startup's logger.</param>
    /// <returns>The exit code.</returns>
    private static int RunTheBackground(string[] args, LocalAppDataPaths paths, ProcessLog log, ILogger logger)
    {
        const int Refused = 1;

        var installRoot = InstallLocation.RootAppDir;
        var pipeName = ValueOf(args, BackgroundPipe.PipeArgument) is { Length: > 0 } named
            ? named
            : BackgroundPipe.NameFor(installRoot, paths.RootAppDir);
        var recordPath = BackgroundRecord.PathFor(paths.RootAppDir, pipeName);
        var clock = TimeProvider.System;
        var backgroundLogger = log.Factory.CreateLogger("BrowserAI.Background");

        var scope = InstallRootScope.Judge(paths.RootAppDir, installRoot);

        if (scope.Unestablished is { } unestablished)
        {
            StartupLog.AppRootScopeUnestablished(logger, unestablished);
        }

        if (!scope.MayServe)
        {
            // 9 a, 2026-10-10: a background that will not serve out of its root
            // records the refusal, what it refused and the remedy, before it exits, and
            // every relay answers each call with that at once. ⚠️ Corrected 2026-10-10
            // (previously "R: a background that cannot serve is a crash every relay
            // names"): the crash sentence sent the person to a bug report for a setting,
            // and to the Start Menu for a start that is refused the same way. Corrected
            // 2026-10-09 before that (previously the branch returned without writing
            // anything): a relay then found no record, held every call for the whole
            // hold bound and answered that no background was running. Held through the
            // real Task Scheduler by
            // RealInstallerTests.ABackgroundWhoseDataRootIsRefusedIsARecordedRefusalThatEveryCallIsToldAtOnce,
            // and over the published background by
            // InstallRootScopeTests.ThePublishedBinaryRefusesToServeOutOfASharedRootAndSaysWhyInTheLog.
            StartupLog.AppRootIsShared(logger, scope.Refusal!);
            RecordTheRefusal(recordPath, scope.Detail!, clock, backgroundLogger);
            return Refused;
        }

        var startedBy = ValueOf(args, StartedByArgument);

        // The roster reads each relay's conversation from its client's own records when
        // the dashboard or a toast is drawn (2026-10-10), and logs where it found it.
        var roster = new RelayRoster(clock, ConversationReader.Files, backgroundLogger);
        var verbs = new BackgroundVerbs(backgroundLogger);

        var run = InstanceDirectory.CreateFresh(paths, logger);
        var instance = run.Directory;

        try
        {
            var payload = new PayloadLayout();
            using var provisioner = new BrowserProvisioner(payload, paths.BrowsersDirectory, log.Factory);

            // 10 b, 2026-10-10: a session refused as a broken install tells the person
            // too, by one toast per background run and a notice on the dashboard. A
            // process under no application id raises no toast, and the notice stays.
            var installSurface = WindowsToastSurface.ForThisProcess();
            using var install = new BrokenInstallNotice(installSurface, backgroundLogger, owned: installSurface);

            var environment = new SessionEnvironment
            {
                Paths = paths,
                Payload = payload,
                Verdicts = ToolVerdicts.Compiled,
                UpstreamTools = UpstreamToolList.Compiled,
                Provisioner = provisioner,
                InstanceDirectory = instance,
                OpenSessionLog = ProcessLog.OpenSessionLog,
                InstallHealth = install,
            };

            var host = SessionHost.Create(log.Factory, environment);

            BackgroundServer? server = null;

            try
            {
                try
                {
                    server = new BackgroundServer(
                        host,
                        new BackgroundIdentity(pipeName, BuildVersion.Current, paths.RootAppDir),
                        roster,
                        verbs,
                        log.Factory);
                }
                catch (IOException taken) when (taken.HResult == NamedPipes.HResultFromWin32(NamedPipes.ErrorAccessDenied))
                {
                    // FILE_FLAG_FIRST_PIPE_INSTANCE: another background serves this
                    // user, install root and data root, and this one was never needed.
                    BackgroundLog.AlreadyServed(backgroundLogger, pipeName, startedBy ?? "nobody named");
                    return 0;
                }

                return Serve(args, paths, log, logger, backgroundLogger, host, server, roster, verbs, install, recordPath, startedBy, installRoot, clock);
            }
            finally
            {
                server?.DisposeAsync().AsTask().GetAwaiter().GetResult();

                // Every session is closed cleanly, each within the minute's cap.
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();

                if (server is not null && verbs.State is BackgroundState.Stopping)
                {
                    _ = BackgroundRecord.EndedCleanly(recordPath, BackgroundEnd.Stopped, clock.GetUtcNow());
                }
            }
        }
#pragma warning disable CA1031 // The process boundary reports every failure the same way: a log record and a non-zero exit code.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            StartupLog.Failed(logger, failure);
            return 1;
        }
        finally
        {
            run.Dispose();
            InstanceDirectory.Delete(instance, logger);
        }
    }

    /// <summary>
    /// Writes the record of a start whose root was refused: what was refused, why, and
    /// the remedy, so that every relay answers with that and never with the crash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written by the process itself</b>, because no relay holds a handle on a
    /// background that never opened its pipe. <i>Corrected 2026-10-10 (previously the
    /// record was a start with this process's exit code and no clean end, which reads
    /// as a crash under R), the maintainer's 9 a.</i>
    /// </para>
    /// <para>
    /// <b>Never over a background that runs</b>: a record naming another live process
    /// is that process's, and it is left as it is.
    /// </para>
    /// </remarks>
    /// <param name="recordPath">The record of the pipe this start was meant to serve.</param>
    /// <param name="refusal">What was refused, in its parts.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">The background's logger.</param>
    private static void RecordTheRefusal(string recordPath, RootRefusal refusal, TimeProvider clock, ILogger logger)
    {
        if (BackgroundRecord.Read(recordPath) is { } other
            && other.ProcessId != Environment.ProcessId
            && ProcessLiveness.IsAlive(other.ProcessId, other.CreatedFileTime))
        {
            return;
        }

        try
        {
            _ = BackgroundRecord.Refused(recordPath, BuildVersion.Current, Environment.ProcessPath ?? string.Empty, refusal, clock.GetUtcNow());
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            BackgroundLog.RefusalNotRecorded(logger, recordPath, failure);
        }
    }

    /// <summary>The background once its pipe is taken: until a stop, an update or the end of the session.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="paths">The data root.</param>
    /// <param name="log">The process log.</param>
    /// <param name="logger">Startup's logger.</param>
    /// <param name="backgroundLogger">The background's logger.</param>
    /// <param name="host">The sessions.</param>
    /// <param name="server">The pipe, taken and not yet accepting.</param>
    /// <param name="roster">The relays.</param>
    /// <param name="verbs">What a person's start and a stop are answered with.</param>
    /// <param name="install">Whether the install is broken, which the page shows while it stands.</param>
    /// <param name="recordPath">The background's record.</param>
    /// <param name="startedBy">Who asked the task for this start, as <c>$(Arg0)</c> carried it.</param>
    /// <param name="installRoot">The install root, or <see langword="null"/>.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The exit code.</returns>
    private static int Serve(
        string[] args,
        LocalAppDataPaths paths,
        ProcessLog log,
        ILogger logger,
        ILogger backgroundLogger,
        SessionHost host,
        BackgroundServer server,
        RelayRoster roster,
        BackgroundVerbs verbs,
        BrokenInstallNotice install,
        string recordPath,
        string? startedBy,
        string? installRoot,
        TimeProvider clock)
    {
        using var stop = new ManualResetEventSlim(initialState: false);
        verbs.StopRequested = stop.Set;

        // R: written once the pipe is ours and before anything can fail, so that
        // an end without the mark the clean ends write reads as the crash it is.
        try
        {
            _ = BackgroundRecord.Started(recordPath, BuildVersion.Current, Environment.ProcessPath ?? string.Empty, clock.GetUtcNow());
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Serving without a record costs a crash its name, and a relay then reports
            // the task's state instead; not serving at all would cost every call.
            BackgroundLog.RecordNotWritten(backgroundLogger, recordPath, failure.Message);
        }

        BackgroundLog.Started(backgroundLogger, server.Name, startedBy ?? "nobody named", recordPath);

        using var window = SessionEndWindow.Create(notice =>
        {
            // Windows waits on this: mark the record and return. The process ends
            // either way, by Windows at a sign-out and by the Task Scheduler about
            // a second after its End.
            var how = notice is SessionEndNotice.SessionEnding ? BackgroundEnd.SessionEnding : BackgroundEnd.EndCommand;
            _ = BackgroundRecord.EndedCleanly(recordPath, how, clock.GetUtcNow());
            BackgroundLog.EndingBecause(backgroundLogger, how);
        });

        // The only stray sweep in the product, once, before this process holds a
        // session (the design, risk 5).
        StraySweep.StartInBackground(() => CreateSweep(paths, installRoot ?? paths.RootAppDir, log.Factory), logger);

        // The toasts read the holds once a second, and the update core raises the
        // toasts: each needs the other, so the toasts are handed the holds through
        // one indirection that is filled in as soon as the core exists.
        var holds = new DeferredUpdateHolds();

        using var updates = new BackgroundUpdates(
            UpdateInstall.OfThisProcess(),
            UpdateSource.Read(args),
            static feed => new VelopackUpdateClient(feed),
            roster,
            new BackgroundSessions(host),
            UpdateToasts.ForThisProcess(holds, paths, backgroundLogger),
            UpdateCheckStampFile.In(paths.RootAppDir),
            () =>
            {
                verbs.State = BackgroundState.Updating;
                _ = BackgroundRecord.EndedCleanly(recordPath, BackgroundEnd.Update, clock.GetUtcNow());
                stop.Set();
            },
            clock,
            log.Factory.CreateLogger("BrowserAI.Updates"));

        holds.Target = updates;

        using var inbox = new CoordinatorInbox();
        var tool = RegisterAiTool.Beside(Environment.ProcessPath);

        using var page = new PageService(
            PageOpener.FactsFor(paths),

            // The occasion the first tab is handed out on: a person's start after an
            // install, or Velopack's restart after an update, says so through the
            // task's $(Arg0).
            startedBy switch
            {
                PersonStart.StartedByTheInstaller => Occasion.FirstRun,
                PersonStart.StartedAfterAnUpdate => Occasion.AfterUpdate,
                _ => Occasion.Ordinary,
            },
            new BackgroundPageSessions(host, roster, clock),
            new RegisterAiPageRegistration(tool, Environment.ProcessPath, () => AppState.Read(tool, Environment.CurrentDirectory, paths.RootAppDir), backgroundLogger),
            new DesktopPageHost(inbox, backgroundLogger),
            inbox.Wake,
            clock,
            PageTabs.ProductLinger,
            backgroundLogger)
        {
            Holds = updates,
            Changelog = ShippedChangelog.ForThisBuild(),
            Install = install,
        };

        install.Changed = page.InstallHealthChanged;

        verbs.Page = page;
        roster.TellUpdatesThrough(updates.Changed, updates.RelayWithdrew);

        server.Start();
        updates.Start();

        // This thread is the background's single-threaded apartment: the shell
        // calls the tab asks for run here, and it waits for nothing else.
        WaitHandle[] waits = [stop.WaitHandle, inbox.Arrived];

        while (WaitHandle.WaitAny(waits) is not 0)
        {
            page.RunQueuedWork();

            // Q336 a: the tab's listener ends a minute after its last tab has gone, so
            // a reload keeps working, and the next person's start opens a new one. The
            // page decides whether the minute has run out; this only asks, on every
            // wake the page's own timers make.
            _ = page.TryStop(final: false);
        }

        if (verbs.State is BackgroundState.Serving)
        {
            verbs.State = BackgroundState.Stopping;
        }

        // Every open tab is told why it stops, and an update says how to come back.
        page.Tell(verbs.State is BackgroundState.Updating
            ? "BrowserAI is installing an update, so this tab has stopped. Open BrowserAI from the Start Menu again once the installed notification has appeared."
            : "BrowserAI's background has stopped, so this tab has stopped. Open BrowserAI from the Start Menu to start it again.");

        BackgroundLog.Ending(backgroundLogger, host.Sessions.HeldCount, roster.Count);

        return 0;
    }
}

/// <summary>Source-generated log messages for the background mode.</summary>
internal static partial class BackgroundLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "BrowserAI's background serves {Pipe}, started by {StartedBy}; its record is {Record}.")]
    public static partial void Started(ILogger logger, string pipe, string startedBy, string record);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Another background already serves {Pipe}, so this one, started by {StartedBy}, exits.")]
    public static partial void AlreadyServed(ILogger logger, string pipe, string startedBy);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "The background is ending with {Sessions} session(s), each asked to close its browser first, and {Relays} relay(s).")]
    public static partial void Ending(ILogger logger, int sessions, int relays);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Windows is ending the background: {How}. It is recorded as a clean end.")]
    public static partial void EndingBecause(ILogger logger, BackgroundEnd how);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The background's record at {Record} could not be written ({Why}), so it serves without one: if it crashes, relays will name the task's state and not the crash.")]
    public static partial void RecordNotWritten(ILogger logger, string record, string why);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "The background's record at {Path} could not be written, so a relay does not know that this start was refused and holds its calls as though no background had started.")]
    public static partial void RefusalNotRecorded(ILogger logger, string path, Exception failure);
}
