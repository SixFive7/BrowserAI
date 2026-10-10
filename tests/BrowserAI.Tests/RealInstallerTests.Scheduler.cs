// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.App;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Relay;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The installed one-background design, through the suite's own pack, its real
/// <c>Setup.exe</c> and the real Task Scheduler.
/// </summary>
/// <remarks>
/// <para>
/// <b>D14 b, the maintainer's words of 2026-10-08, verbatim: <i>"d14 b"</i></b>: the
/// real-scheduler test runs inside the suite, with the suite's own pack and task,
/// removed by the arm, under the installer lock. Under the one-background design the
/// task is the only thing that starts an installed BrowserAI's background (S a), so
/// these arms are where the product's one start path runs at all.
/// </para>
/// <para>
/// <b>Four arms, each over an install of its own</b>, so that each case goes red on
/// its own and a defect planted for one cannot hide the next: the ordinary path, the
/// two cases a relay can answer only once its hold has run out (a missing task and a
/// disabled one, 150 s each), and the crash every call is told at once. Inside an arm
/// the cases answered at once come first, so a hold is waited out only where nothing
/// else can answer the case.
/// </para>
/// <para>
/// <b>Nothing reaches the screen.</b> Every person's start carries
/// <c>--write-address</c> and runs on a desktop of the suite's own; the background the
/// scheduler starts runs on the person's desktop with one hidden window, which the
/// first arm reads; every session is headless. A background is stopped through its pipe
/// (<see cref="BackgroundStop.AskAndWait"/>) and never through the task's End, and no
/// arm ends a process it did not start.
/// </para>
/// <para>
/// <b>The relays and the person's starts get a client's environment</b>: no
/// <c>BROWSERAI_ROOT</c> and no <c>BROWSERAI_UPDATE_FEED</c>, so the data root reaches
/// each of them as an argument, as it does through a registration and through the
/// task's action. The installer is the one process the two variables are set for.
/// </para>
/// </remarks>
internal sealed partial class RealInstallerTests
{
    /// <summary>What the task's <c>$(Arg0)</c> carries when an arm runs the task itself, the way its sign-in trigger does.</summary>
    private const string StartedByTheSuite = "suite";

    /// <summary>
    /// The exit code of a background whose data root was refused: what
    /// <c>Program.RunTheBackground</c> returns for every start that cannot serve.
    /// </summary>
    private const int RefusedExitCode = 1;

    /// <summary>
    /// The Task Scheduler's own answer to a run of a disabled task,
    /// <c>SCHED_E_TASK_DISABLED</c>, as the person's start's log spells it.
    /// </summary>
    /// <remarks>Measured 2026-10-08 with stand-ins, 5 of 5, in <c>kb/windows/processes.md</c>.</remarks>
    private const string DisabledByTheScheduler = "0x80041326";

    /// <summary>The harmless executable a stand-in updater is a copy of.</summary>
    private static string StandInSource { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    /// <summary>
    /// A person's start of the installed BrowserAI runs the task; the background the
    /// scheduler starts serves the page at the address it hands out, shows no window,
    /// keeps a session across a relay that is killed, and stops the tab's listener a
    /// minute after its last tab while it stays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Carried over from
    /// <c>TheInstalledMainExecutableServesItsPageAndShowsNoWindowOfItsOwn</c>, retired
    /// 2026-10-09</b>: the page answering at the address the start was handed, no visible
    /// window of BrowserAI's own on the desktop, and the minute after the last tab, which
    /// is now the listener's and no longer a process's.
    /// </para>
    /// <para>
    /// <b>The scheduler's process, read off the kernel and the service control
    /// manager</b>: its parent is the process hosting the <c>Schedule</c> service, it sits
    /// in a job no process of this suite made for it, and its command line is the task's
    /// action with <c>$(Arg0)</c> filled by the start that asked for it.
    /// </para>
    /// <para>
    /// <b>The minute is the product's own</b> (<see cref="PageTabs.ProductLinger"/>). The
    /// relay half runs inside it, and the wait for the listener to stop is bounded by
    /// <see cref="TestDefaults.ProcessHang"/>, a hang detector.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel]
    public async Task APersonsStartRunsTheTaskAndTheBackgroundServesThePageAndKeepsASessionAcrossItsRelays()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();
        SuiteEnvironment.RequireProvisionedChromium();

        await using var install = await SuiteInstall.InstallAsync(setup, "real-scheduler");

        await Assert.That(install.SetupExitCode).IsEqualTo(0).Because(install.Evidence());

        // The data root's browsers, copied from the suite's own provisioned Chromium, so
        // that the session below downloads nothing.
        SeedTheProvisionedChromium(install.DataRoot);

        // ---- A person's start: no background runs, so it asks the scheduler for one.
        await Assert.That(install.BackgroundRuns()).IsFalse();

        var start = await PersonStartAsync(install, "first");

        await Assert.That(start.ExitCode).IsEqualTo(0).Because(start.Evidence + install.Evidence());
        await Assert.That(start.Address).IsNotNull().Because(start.Evidence);

        var background = await TheSchedulersBackgroundAsync(install, PersonStart.StartedByPerson);

        // ---- No visible window of BrowserAI's own on the person's desktop. The one
        // window it keeps is hidden, and finding it is what shows that this
        // enumeration reads the desktop the background runs on. The two input
        // indicator classes are the input framework's, as they were for the app.
        var owned = TopLevelWindows.All()
            .Where(window => TopLevelWindows.ProcessIdOf(window) == background.ProcessId)
            .ToList();

        await Assert.That(owned.Count).IsGreaterThan(0)
            .Because("the background keeps one hidden top-level window, which is how a sign-out reaches it");

        var visible = owned
            .Where(TopLevelWindows.IsVisible)
            .Select(TopLevelWindows.ClassNameOf)
            .Where(name => !InputIndicatorClasses.Contains(name, StringComparer.Ordinal))
            .ToList();

        await Assert.That(string.Join(", ", visible)).IsEmpty();

        // ---- The page answers at the address the start was handed.
        var uri = new Uri(start.Address!);
        var page = await RawHttp.SendAsync(uri.Port, RawHttp.Get(uri.Port, uri.PathAndQuery, $"Host: 127.0.0.1:{uri.Port}", "Sec-Fetch-Site: none"));

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains("<h1>BrowserAI ");

        // A tab connects and closes: the listener's minute starts here.
        using (var tab = await RawEventStream.OpenAsync(uri.Port, uri.AbsolutePath.Trim('/'), tab: 1))
        {
            await Assert.That(tab.Status).IsEqualTo(200);
            await Assert.That(await tab.NextNamedAsync(PageEvents.State)).IsNotNull();
        }

        // ---- A relay, started the way a registration starts it: the handshake and the
        // tool list come from the binary, and a session opened through it is the
        // background's.
        using var sessions = ScratchDirectory.Create("real-scheduler-session");
        var session = Path.Combine(sessions.Path, "kept-across-a-relay");
        var marker = $"KEPT-{Guid.NewGuid():N}";

        var first = install.StartRelay();

        try
        {
            var hello = await first.InitializeAsync(SliceRun.OfferedProtocolVersion);

            await Assert.That((string?)hello["protocolVersion"]).IsEqualTo(SliceRun.OfferedProtocolVersion);
            await Assert.That((string?)hello["serverInfo"]?["version"]).IsEqualTo(PublishedSlice.BakedVersion());

            var listed = await first.RoundTripAsync("tools/list", new JsonObject());

            await Assert.That(NamesIn(listed)).IsEqualTo(NamesIn(SessionToolSurface.Rewrite(UpstreamToolList.Compiled.Result(), ToolVerdicts.Compiled)));

            // Every setting a session's open names, each at what a hidden session takes
            // by default, so the call is the one every client makes.
            _ = await CallOkAsync(first, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the real-scheduler arm's session, kept across a relay's death",
                ["headed"] = false,
                ["transcript"] = false,
                ["captureNetwork"] = false,
                [IdleSetting.ParameterName] = SessionTimes.HiddenIdleMinutes,
            });

            _ = await CallOkAsync(first, "browser_navigate", new JsonObject
            {
                ["url"] = "data:text/html," + Uri.EscapeDataString($"<title>{marker}</title><h1>{marker}</h1><input id=typed value=left-as-it-was>"),
                ["session"] = session,
                ["why"] = "the suite leaving a page in a session whose relay it is about to kill",
            });
        }
        finally
        {
            // ⚠️ THE RELAY DIES THE WAY CODEX ENDS A SERVER: its job is closed and it is
            // terminated, with nothing sent and nothing closed first.
            await first.DisposeAsync();
        }

        // ---- The next relay takes the session over, page and all.
        await using (var second = install.StartRelay())
        {
            _ = await second.InitializeAsync(SliceRun.OfferedProtocolVersion);

            var snapshot = await CallOkAsync(second, "browser_snapshot", new JsonObject
            {
                ["session"] = session,
                ["why"] = "the suite's next relay reading the page the killed relay left",
            });

            await Assert.That(TextOf(snapshot)).Contains(marker)
                .Because("the browser the first relay drove is the one the second relay reads, page and all");
            await Assert.That(TextOf(snapshot)).Contains("left-as-it-was");

            _ = await CallOkAsync(second, SessionToolSurface.Destroy, new JsonObject
            {
                ["directory"] = session,
                ["why"] = "the suite cleaning up its session",
            });
        }

        // ---- The tab's listener stops a minute after its last tab, and the background
        // stays: same process, same pipe.
        await Assert.That(await ListenerStopsAsync(uri.Port)).IsTrue()
            .Because($"the listener should stop {PageTabs.ProductLinger} after its last tab, and a hang detector of {TestDefaults.ProcessHang} ran out first");
        await Assert.That(ProcessIdentity.IsAlive(background.ProcessId, background.CreatedFileTime)).IsTrue()
            .Because("the background is resident (S a): the tab's listener ends, the background does not");
        await Assert.That(NamedPipes.WaitForFreeInstance(install.Pipe, 1)).IsTrue();

        // ---- Stopped through its pipe, never through the task's End.
        var (stopped, detail) = BackgroundStop.AskAndWait(install.Pipe, install.Record, BackgroundStop.Bound);

        await Assert.That(stopped).IsEqualTo(BackgroundStopOutcome.Ended).Because(detail);
        await Assert.That(BackgroundRecord.Read(install.Record)?.Ended).IsEqualTo(BackgroundEnd.Stopped);
    }

    /// <summary>
    /// A relay started while the install's updater runs answers its first call at once
    /// with the update sentence and starts nothing; with no updater, the same relay holds
    /// its next call and names the missing task once the hold runs out; and a person's
    /// start registers the task again from the definition the install saved, and starts
    /// the background the same relay then reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The updater is a stand-in, found by its full image path and nothing else</b>,
    /// the shape <c>UpdateInProgressTests</c> used until 2026-10-08: the install's own
    /// <c>Update.exe</c> is set aside, a copy of <c>cmd.exe</c> runs at its path, started
    /// with <c>/d /q /k</c> in a kill-on-close job the arm owns, and the arm puts the real
    /// one back once that job is closed. The relay knows it only as a process whose image
    /// is its install root's <c>Update.exe</c>, which is how it would know Velopack's.
    /// </para>
    /// <para>
    /// <b>The update case comes first because it is answered at once (U2)</b>; the missing
    /// task is answered only when the 150 s hold runs out, because the background appears
    /// by itself at sign-in and after an update, and that wait is the arm's one.
    /// </para>
    /// <para>
    /// <b>Only the saved definition carries the update feed</b>, so a task registered
    /// again with this install's <c>--update-source</c> is one registered from
    /// <c>background-task.xml</c> and not one composed from defaults.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel]
    public async Task ARelayAnswersAnUpdateAtOnceAndAMissingTaskAfterItsHoldAndAPersonsStartRegistersTheTaskAgain()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();

        await using var install = await SuiteInstall.InstallAsync(setup, "real-scheduler-missing");

        await Assert.That(install.SetupExitCode).IsEqualTo(0).Because(install.Evidence());

        using var listedDirectory = ScratchDirectory.Create("real-scheduler-list");
        var list = new JsonObject { ["directory"] = listedDirectory.Path };

        // Nothing has asked the scheduler for a start since the install: no background runs.
        await Assert.That(install.BackgroundRuns()).IsFalse();

        // ---- The install's Update.exe runs, and a relay starts.
        await using var updater = StandInUpdater.Start(install);
        await using var relay = install.StartRelay();

        _ = await relay.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var (update, updateTook) = await CallAsync(relay, SessionToolSurface.List, list);

        await Assert.That((bool?)update["isError"]).IsTrue();
        await Assert.That(TextOf(update))
            .IsEqualTo(RelayErrors.UpdateInstalling(SessionToolSurface.List, version: null, RawStdioClient.DefaultClientName))
            .Because(install.Evidence());
        await Assert.That(updateTook).IsLessThan(RelayConstants.HoldBound)
            .Because("waiting cannot change an update that is installing, so the relay answers at once (U2)");

        // It started nothing: its job holds the relay alone, and no background runs.
        await Assert.That(relay.JobProcessIds()).IsEquivalentTo([relay.ProcessId]);
        await Assert.That(install.BackgroundRuns()).IsFalse();

        // ---- The updater goes, and the install's own Update.exe is back at its path.
        await updater.DisposeAsync();

        await Assert.That(Hash(install.UpdateExe)).IsEqualTo(install.UpdaterDigest);

        // ---- The task deleted, as a person deletes it.
        await Assert.That(ScheduledTasks.Instance.Remove(install.TaskName).Change).IsEqualTo(TaskChange.Removed);

        // The same relay, with no updater running: its next call is held for the hold
        // bound and then answered with the missing task, which it read and did not
        // repair.
        var (missing, held) = await CallAsync(relay, SessionToolSurface.List, list);

        await Assert.That(TextOf(missing))
            .IsEqualTo(RelayErrors.NotRunning(SessionToolSurface.List, TaskState.Missing, install.TaskName, detail: null))
            .Because(install.Evidence());
        await Assert.That(held).IsGreaterThanOrEqualTo(RelayConstants.HoldBound)
            .Because("a missing task is said when the hold runs out, since a background appears by itself at sign-in and after an update");
        await Assert.That(ScheduledTasks.DefinitionOf(install.TaskName)).IsNull();
        await Assert.That(install.BackgroundRuns()).IsFalse();
        await Assert.That(relay.JobProcessIds()).IsEquivalentTo([relay.ProcessId]);

        // ---- A person's start registers the task again from background-task.xml, and
        // starts the background through it.
        var start = await PersonStartAsync(install, "registers-again");

        await Assert.That(start.ExitCode).IsEqualTo(0).Because(start.Evidence + install.Evidence());
        await Assert.That(start.Address).IsNotNull().Because(start.Evidence);

        var registered = StoredTaskOf(install.TaskName);

        await Assert.That(registered).IsNotNull().Because(start.Evidence);
        await Assert.That(Normalised(registered!.Command)).IsEqualTo(Normalised(install.App));
        await Assert.That(registered.Arguments).IsEqualTo(install.TaskAction);
        await Assert.That(registered.InstancesPolicy).IsEqualTo(StoredTask.IgnoreNew);

        _ = await TheSchedulersBackgroundAsync(install, PersonStart.StartedByPerson);

        var uri = new Uri(start.Address!);
        var page = await RawHttp.SendAsync(uri.Port, RawHttp.Get(uri.Port, uri.PathAndQuery, $"Host: 127.0.0.1:{uri.Port}", "Sec-Fetch-Site: none"));

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);

        // ---- The same relay reaches it: its next call is passed on and served.
        var (served, servedTook) = await CallAsync(relay, SessionToolSurface.List, list);

        await Assert.That((bool?)served["isError"]).IsNotEqualTo(true)
            .Because($"{TextOf(served)}{Environment.NewLine}--- answered after {servedTook}; an instance of {install.Pipe} free now: {NamedPipes.WaitForFreeInstance(install.Pipe, 1)} ---{install.Evidence(lines: 400)}{Environment.NewLine}--- the relay's standard error ---{Environment.NewLine}{relay.StandardErrorSoFar()}");

        // ---- Stopped through its pipe.
        var (stopped, detail) = BackgroundStop.AskAndWait(install.Pipe, install.Record, BackgroundStop.Bound);

        await Assert.That(stopped).IsEqualTo(BackgroundStopOutcome.Ended).Because(detail);
    }

    /// <summary>
    /// A disabled task refuses a person's start, which leaves it disabled and starts
    /// nothing, and a relay holds its call and then names the disabled task with how to
    /// enable it, leaving it disabled too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D12 b, the maintainer's words of 2026-10-08, verbatim: <i>"d12 b"</i></b>: one
    /// task; a disabled one is named in the error, with how to enable it, and left as it
    /// is. The task is disabled with <c>schtasks /Change /DISABLE</c>, the tool the
    /// sentence itself names for enabling it again.
    /// </para>
    /// <para>
    /// <b>The person's start is refused by the scheduler</b>, which answers a run of a
    /// disabled task with <c>SCHED_E_TASK_DISABLED</c>; the start says so in its log and
    /// exits without an address.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel]
    public async Task ADisabledTaskRefusesAPersonsStartAndARelayNamesItWithHowToEnableItAfterItsHold()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();

        await using var install = await SuiteInstall.InstallAsync(setup, "real-scheduler-disabled");

        await Assert.That(install.SetupExitCode).IsEqualTo(0).Because(install.Evidence());

        // ---- The task disabled, the way a person disables it.
        var (disabled, said) = await SchedulerToolAsync("/Change", "/TN", install.TaskName, "/DISABLE");

        await Assert.That(disabled).IsEqualTo(0).Because(said);
        await Assert.That(ScheduledTasks.StateOf(install.TaskName).State).IsEqualTo(ScheduledTaskState.Disabled);

        // ---- A person's start is refused, and starts nothing.
        var start = await PersonStartAsync(install, "refused");

        await Assert.That(start.ExitCode).IsEqualTo(1).Because(start.Evidence);
        await Assert.That(start.Address).IsNull().Because(start.Evidence);
        await Assert.That(start.Records).Contains(DisabledByTheScheduler).Because(start.Evidence);
        await Assert.That(install.BackgroundRuns()).IsFalse();
        await Assert.That(ScheduledTasks.StateOf(install.TaskName).State).IsEqualTo(ScheduledTaskState.Disabled);

        // ---- A relay's call: held for the hold bound, and then answered with the disabled
        // task and how to enable it.
        using var listedDirectory = ScratchDirectory.Create("real-scheduler-disabled-list");
        await using var relay = install.StartRelay();

        _ = await relay.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var (answer, held) = await CallAsync(relay, SessionToolSurface.List, new JsonObject { ["directory"] = listedDirectory.Path });

        await Assert.That(TextOf(answer))
            .IsEqualTo(RelayErrors.NotRunning(SessionToolSurface.List, TaskState.Disabled, install.TaskName, detail: null))
            .Because(install.Evidence());
        await Assert.That(TextOf(answer)).Contains($"schtasks /Change /TN \"{install.TaskName}\" /ENABLE");
        await Assert.That(held).IsGreaterThanOrEqualTo(RelayConstants.HoldBound)
            .Because("a disabled task is said when the hold runs out, as every reason a background may still appear for is");

        // And left the task as the person left it, and started nothing.
        await Assert.That(ScheduledTasks.StateOf(install.TaskName).State).IsEqualTo(ScheduledTaskState.Disabled);
        await Assert.That(install.BackgroundRuns()).IsFalse();
        await Assert.That(relay.JobProcessIds()).IsEquivalentTo([relay.ProcessId]);
    }

    /// <summary>
    /// A background the scheduler starts for a data root <see cref="InstallRootScope"/>
    /// refuses records the refusal, and every call from every relay is told what was
    /// refused and the remedy at once, with nothing started again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>R, the maintainer's words of 2026-10-08, verbatim: <i>"R I like option 1 and the
    /// call response"</i> and <i>"r ok"</i></b>: a background that ends without a clean
    /// end is a crash every relay names at once, and only the person's start clears it.
    /// The design's own note on this arm: it needs no switch for the suite, because a
    /// data root the scope refuses is a real failure path.
    /// </para>
    /// <para>
    /// <b>The root is outside the user's profile</b>, which the scope refuses. The installer
    /// names it, so the hooks write it into the task's action and into both registrations,
    /// as they would for any root a person named; the task is then run the way its sign-in
    /// trigger runs it.
    /// </para>
    /// <para>
    /// ⚠️ <b>This holds the sentence the product gives today, and a decision is open on
    /// it.</b> The hazard row lane ARCH's helper T2 opened on 2026-10-09 says the crash
    /// sentence sends the person to a bug report for what is a configuration problem,
    /// and asks whether a refusal gets a kind of its own in the record. If it does, this
    /// arm's expected answer changes with it.
    /// </para>
    /// <para>
    /// ⚠️ <b>It did, on 2026-10-10: the maintainer's 9 a</b>, and this arm moved with it
    /// (<i>previously <c>ABackgroundWhoseDataRootIsRefusedIsARecordedCrashThatEveryCallIsToldAtOnce</c>,
    /// holding a crash record and R's crash sentence</i>). The record names the refusal
    /// with what was refused and the remedy, and every call is told those, never a bug
    /// report.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel]
    public async Task ABackgroundWhoseDataRootIsRefusedIsARecordedRefusalThatEveryCallIsToldAtOnce()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();

        using var refused = ScratchDirectory.Create("real-scheduler-refused-data");

        await using var install = await SuiteInstall.InstallAsync(setup, "real-scheduler-refused", refused.Path);

        await Assert.That(install.SetupExitCode).IsEqualTo(0).Because(install.Evidence());
        await Assert.That(StoredTaskOf(install.TaskName)?.Arguments).IsEqualTo(install.TaskAction);

        // ---- The task runs, as its sign-in trigger runs it.
        var run = ScheduledTasks.Instance.Run(install.TaskName, StartedByTheSuite);

        await Assert.That(run.Change).IsEqualTo(TaskChange.Started).Because(run.Detail);

        // The background refuses its data root, writes its record as the refusal and
        // exits: read off the scheduler's own last result, and off the record.
        await Assert.That(await WaitForAsync(() => ScheduledTasks.LastRunOf(install.TaskName) is { Result: RefusedExitCode }))
            .IsTrue()
            .Because($"the task's last result should become {RefusedExitCode}. {install.Evidence()}");

        var refusal = BackgroundRecord.Read(install.Record);

        await Assert.That(refusal).IsNotNull().Because(install.Evidence());
        await Assert.That(refusal!.Ended).IsEqualTo(BackgroundEnd.Refused);
        await Assert.That(refusal.Refusal).IsNotNull();
        await Assert.That(refusal.Refusal!.Which).IsEqualTo(JudgedRoot.Data);
        await Assert.That(ProcessIdentity.IsAlive(refusal.ProcessId, refusal.CreatedFileTime)).IsFalse();
        await Assert.That(install.BackgroundRuns()).IsFalse();

        // ---- Every call, from every relay, is told the refusal at once, and nothing starts.
        // The sentence names the root, why and the remedy, and the relay's own log.
        var logs = new LocalAppDataPaths(install.DataRoot).LogDirectory;

        using var listedDirectory = ScratchDirectory.Create("real-scheduler-refused-list");
        var list = new JsonObject { ["directory"] = listedDirectory.Path };

        for (var relays = 0; relays < 2; relays++)
        {
            await using var relay = install.StartRelay();

            _ = await relay.InitializeAsync(SliceRun.OfferedProtocolVersion);

            for (var calls = 0; calls < 2; calls++)
            {
                var (answer, took) = await CallAsync(relay, SessionToolSurface.List, list);
                var text = TextOf(answer);

                await Assert.That((bool?)answer["isError"]).IsTrue();
                await Assert.That(text).IsEqualTo(RelayErrors.RootRefused(SessionToolSurface.List, refusal.Refusal, logs)).Because(install.Evidence());
                await Assert.That(text).Contains(refusal.Refusal.Root);
                await Assert.That(text).DoesNotContain(RelayErrors.IssuesUrl);
                await Assert.That(took).IsLessThan(RelayConstants.HoldBound)
                    .Because("a recorded refusal is answered at once; holding cannot change it (9 a)");
            }

            await Assert.That(relay.JobProcessIds()).IsEquivalentTo([relay.ProcessId]);
        }

        // Nothing started it again: the record still names the same refusal.
        var after = BackgroundRecord.Read(install.Record);

        await Assert.That(after?.ProcessId).IsEqualTo(refusal.ProcessId);
        await Assert.That(after?.Ended).IsEqualTo(BackgroundEnd.Refused);
        await Assert.That(install.BackgroundRuns()).IsFalse();
    }

    /// <summary>
    /// Holds that the background of an install is the Task Scheduler's process, running
    /// the install's one executable with the task's action.
    /// </summary>
    /// <param name="install">The install.</param>
    /// <param name="startedBy">What the start that asked for it put in <c>$(Arg0)</c>.</param>
    /// <returns>Its record.</returns>
    private static async Task<BackgroundRecordState> TheSchedulersBackgroundAsync(SuiteInstall install, string startedBy)
    {
        var record = BackgroundRecord.Read(install.Record);

        await Assert.That(record).IsNotNull().Because(install.Evidence());
        await Assert.That(record!.Ended).IsNull().Because(install.Evidence());
        await Assert.That(ProcessIdentity.IsAlive(record.ProcessId, record.CreatedFileTime)).IsTrue().Because(install.Evidence());

        // Its parent is the process hosting the Task Scheduler's service, and it sits in
        // a job, which is the scheduler's: nothing of this suite started it.
        await Assert.That(ParentProcess.IdOf(record.ProcessId)).IsEqualTo(ScheduledProcess.ServiceProcessId());
        await Assert.That(ScheduledProcess.IsInAnyJob(record.ProcessId)).IsTrue();

        // It runs this install's one executable, with the task's action and the start's
        // reason in place of $(Arg0).
        await Assert.That(Normalised(ProcessCommandLine.ImagePathOf(record.ProcessId) ?? "<unreadable>")).IsEqualTo(Normalised(install.App));
        await Assert.That(ProcessCommandLine.Of(record.ProcessId) ?? "<unreadable>")
            .Contains(install.TaskAction.Replace("$(Arg0)", startedBy, StringComparison.Ordinal));

        return record;
    }

    /// <summary>What one person's start did.</summary>
    /// <param name="ExitCode">Its exit code, or <see langword="null"/> when it had not exited within the hang detector.</param>
    /// <param name="Address">The address it wrote, or <see langword="null"/>.</param>
    /// <param name="Records">Its own records in the data root's process log.</param>
    private sealed record PersonStartRun(int? ExitCode, string? Address, string Records)
    {
        /// <summary>The whole of it, for a failure message.</summary>
        public string Evidence =>
            $"The person's start exited with {ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "<nothing: still running>"} and wrote {Address ?? "<no address>"}. Its own records:{Environment.NewLine}{Records}{Environment.NewLine}";
    }

    /// <summary>
    /// A person's start of the installed BrowserAI, on a desktop of the suite's own, with
    /// <c>--write-address</c>, so that nothing reaches the screen.
    /// </summary>
    /// <remarks>
    /// <b>The data root as an argument and nothing else</b>: the start gets a client's
    /// environment, and <c>--data-root</c> names the root the installer was given.
    /// </remarks>
    /// <param name="install">The install.</param>
    /// <param name="label">A word for the address file and the desktop.</param>
    /// <returns>What it did.</returns>
    private static async Task<PersonStartRun> PersonStartAsync(SuiteInstall install, string label)
    {
        var addressFile = Path.Combine(install.Logs, $"{label}-address.txt");

        using var desktop = PrivateDesktop.Create($"real-scheduler-{label}");
        using var job = JobObject.CreateKillOnClose();
        using var start = desktop.Launch(
            job,
            install.App,
            [PageOpener.WriteAddressArgument, addressFile, Program.DataRootArgument, install.DataRoot],
            install.InstallRoot,
            ClientEnvironment());

        var created = ProcessIdentity.CreationTimeOf(start.Id);

        // Drained, because a pipe nobody reads can stop a child that writes to it.
        var drained = DrainAsync(start);
        var exited = await start.WaitForExitAsync(TestDefaults.ProcessHang);

        // The job, and not a kill by pid: a start that hung goes with it, and the
        // background it asked the scheduler for is in the scheduler's job and not here.
        job.Dispose();
        await drained.WaitAsync(TestDefaults.ProcessHang);

        var address = File.Exists(addressFile) ? (await File.ReadAllTextAsync(addressFile)).Trim() : null;

        return new PersonStartRun(
            exited ? start.TryReadExitCode() : null,
            address is { Length: > 0 } ? address : null,
            ProcessLogRecords.In(new LocalAppDataPaths(install.DataRoot).LogDirectory, start.Id, created));
    }

    /// <summary>
    /// The environment a client gives the BrowserAI it starts: this process's, without
    /// the two variables only the installer's hooks may read.
    /// </summary>
    /// <returns>The environment.</returns>
    private static Dictionary<string, string> ClientEnvironment()
    {
        var environment = PublishedSlice.InheritedEnvironment();

        _ = environment.Remove(BrowserAiPaths.AppRootOverride);
        _ = environment.Remove(UpdateConfiguration.FeedVariable);

        return environment;
    }

    /// <summary>Calls a tool through a relay, and times the answer.</summary>
    /// <param name="relay">The relay.</param>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The result and how long it took, a hang detector's reading and never a promptness claim.</returns>
    private static async Task<(JsonObject Result, TimeSpan Elapsed)> CallAsync(RawStdioClient relay, string tool, JsonObject arguments)
    {
        var watch = Stopwatch.StartNew();

        // A copy: a node has one parent, and an arm calls more than once with the same arguments.
        var result = await relay.RoundTripAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments.DeepClone() });

        return (result, watch.Elapsed);
    }

    /// <summary>Calls a tool and requires an answer that is not an error.</summary>
    /// <param name="relay">The relay.</param>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The result.</returns>
    private static async Task<JsonObject> CallOkAsync(RawStdioClient relay, string tool, JsonObject arguments)
    {
        var (result, _) = await CallAsync(relay, tool, arguments);

        return (bool?)result["isError"] is true
            ? throw new InvalidOperationException($"'{tool}' was refused: {TextOf(result)}{Environment.NewLine}{relay.StandardErrorSoFar()}")
            : result;
    }

    /// <summary>The text of a tool result.</summary>
    /// <param name="result">The result.</param>
    /// <returns>Every text block, joined.</returns>
    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? []).Select(block => (string?)block?["text"] ?? string.Empty));

    /// <summary>The tools a list names, in its order.</summary>
    /// <param name="list">A <c>tools/list</c> result.</param>
    /// <returns>The names, joined.</returns>
    private static string NamesIn(JsonObject list) =>
        string.Join(",", (list["tools"]?.AsArray() ?? []).Select(tool => (string?)tool?["name"] ?? "<unnamed>"));

    /// <summary>
    /// Copies the suite's provisioned Chromium into a data root's browsers, the
    /// completion markers last.
    /// </summary>
    /// <remarks>
    /// <b>The markers last is upstream's own order</b>: BrowserAI and Playwright read a
    /// browser as present from them alone, so a marker that arrived before the bytes
    /// under it would hand a half-copied tree to whatever looked first.
    /// <c>FirstRunCache.SeedInto</c> keeps the same order.
    /// </remarks>
    /// <param name="dataRoot">The data root.</param>
    private static void SeedTheProvisionedChromium(string dataRoot)
    {
        var source = BrowserAiPaths.ChromiumDirectory;
        var target = Path.Combine(new LocalAppDataPaths(dataRoot).BrowsersDirectory, Path.GetFileName(source));
        var markers = new List<(string From, string To)>();

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target, Path.GetRelativePath(source, file));

            _ = Directory.CreateDirectory(Path.GetDirectoryName(to)!);

            if (string.Equals(Path.GetDirectoryName(file), source, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(file) is BrowsersManifest.InstallationCompleteMarker or "DEPENDENCIES_VALIDATED")
            {
                markers.Add((file, to));
                continue;
            }

            File.Copy(file, to);
        }

        foreach (var (from, to) in markers)
        {
            File.Copy(from, to);
        }
    }

    /// <summary>Waits for a listener on a loopback port to stop accepting.</summary>
    /// <param name="port">The port.</param>
    /// <returns>Whether it stopped within the hang detector.</returns>
    private static async Task<bool> ListenerStopsAsync(int port)
    {
        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;

        while (DateTime.UtcNow < deadline)
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, port);
                }
                catch (SocketException)
                {
                    return true;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }

    /// <summary>Runs <c>schtasks.exe</c>, with nothing on its standard input, and reads what it said.</summary>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>Its exit code, and its two outputs joined.</returns>
    private static async Task<(int ExitCode, string Output)> SchedulerToolAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"))
        {
            UseShellExecute = false,

            // The house rule for every launch in this tree.
            CreateNoWindow = true,

            // Closed at once, so a question it might ask is answered by the end of its
            // input and never by a wait.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;

        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);

        return (process.ExitCode, await output + await error);
    }

    /// <summary>Reads a file another process may be writing, sharing with it.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>Where an arm sets an install's own <c>Update.exe</c> aside while a stand-in runs at its path.</summary>
    /// <param name="installRoot">The install root.</param>
    /// <returns>The path.</returns>
    private static string SetAsideUpdaterOf(string installRoot) =>
        Path.Combine(installRoot, Program.UpdaterFileName + ".suite-set-aside");

    /// <summary>
    /// Puts an install's own <c>Update.exe</c> back at its path, over a stand-in that
    /// ran there, waiting out the moment Windows still holds the stand-in's image.
    /// </summary>
    /// <remarks>Never throws: it runs on the failure path too.</remarks>
    /// <param name="installRoot">The install root.</param>
    /// <returns>Whether the path holds the install's own <c>Update.exe</c>, or never held anything else.</returns>
    private static bool PutTheUpdaterBack(string installRoot)
    {
        var aside = SetAsideUpdaterOf(installRoot);

        if (!File.Exists(aside))
        {
            return true;
        }

        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;

        while (true)
        {
            try
            {
                File.Move(aside, Path.Combine(installRoot, Program.UpdaterFileName), overwrite: true);
                return true;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }

                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    /// <summary>
    /// The suite's pack, installed silently by its real <c>Setup.exe</c> into a scratch
    /// root under the profile, with the sandbox every arm here sets, and everything it
    /// put on the machine taken back on the way out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The sandbox</b>: <c>CLAUDE_CONFIG_DIR</c> and <c>CODEX_HOME</c> at scratch, so
    /// the hooks' RegisterAI writes no real client configuration; <c>BROWSERAI_ROOT</c> at
    /// the arm's data root and <c>BROWSERAI_UPDATE_FEED</c> at an empty scratch folder, the
    /// two the install hook reads once and writes into the task's action, so the
    /// background the scheduler starts asks no feed on the network and keeps its data in
    /// scratch.
    /// </para>
    /// <para>
    /// <b>On the way out</b>: any background of the install is asked through its pipe to
    /// stop, the install's own <c>Update.exe</c> is put back if a stand-in was left at its
    /// path, and <see cref="ReclaimAsync"/> uninstalls through it and takes back the task,
    /// the key, the PATH entry, the shortcut, the toasts' activator and the tree, all
    /// inside the sandbox, so the uninstall hook stops the background of this data root
    /// and unregisters the scratch clients.
    /// </para>
    /// </remarks>
    private sealed class SuiteInstall : IAsyncDisposable
    {
        private readonly ScratchDirectory _installRoot;
        private readonly ScratchDirectory? _dataRoot;
        private readonly ScratchDirectory _clientConfig;
        private readonly ScratchDirectory _logs;
        private readonly ScratchDirectory _feed;
        private readonly EnvironmentScope _sandbox;
        private int _disposed;

        private SuiteInstall(string label, string? dataRoot)
        {
            _installRoot = ScratchDirectory.CreateUnderProfile(label);
            _dataRoot = dataRoot is null ? ScratchDirectory.CreateUnderProfile($"{label}-data") : null;
            _clientConfig = ScratchDirectory.Create($"{label}-client");
            _logs = ScratchDirectory.Create($"{label}-logs");
            _feed = ScratchDirectory.Create($"{label}-feed");

            DataRoot = dataRoot ?? _dataRoot!.Path;
            ClaudeConfig = _clientConfig.Path;
            CodexHome = Directory.CreateDirectory(Path.Combine(_clientConfig.Path, "codex")).FullName;

            _sandbox = new EnvironmentScope(new Dictionary<string, string?>
            {
                [RegistrationTests.ConfigDirectoryVariable] = OnboardedClientConfig.Seed(ClaudeConfig),
                [BrowserAiPaths.AppRootOverride] = DataRoot,
                [RegistrationTests.CodexHomeVariable] = CodexHome,
                [UpdateConfiguration.FeedVariable] = Feed,
            });
        }

        /// <summary>The install root.</summary>
        public string InstallRoot => _installRoot.Path;

        /// <summary>The data root the installer named.</summary>
        public string DataRoot { get; }

        /// <summary>The empty update feed the installer named.</summary>
        public string Feed => _feed.Path;

        /// <summary>Where the setup log, the address files and the relays' working directory are.</summary>
        public string Logs => _logs.Path;

        /// <summary>The scratch Claude Code configuration.</summary>
        public string ClaudeConfig { get; }

        /// <summary>The scratch Codex home.</summary>
        public string CodexHome { get; }

        /// <summary>How <c>Setup.exe</c> exited.</summary>
        public int SetupExitCode { get; private set; } = -1;

        /// <summary>The SHA-256 of the install's own <c>Update.exe</c>, read after the install.</summary>
        public string UpdaterDigest { get; private set; } = string.Empty;

        /// <summary>The one executable, in <c>current\</c>.</summary>
        public string App => Path.Combine(InstallRoot, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);

        /// <summary>The install's own updater.</summary>
        public string UpdateExe => Path.Combine(InstallRoot, Program.UpdaterFileName);

        /// <summary>The task the install hook registers.</summary>
        public string TaskName => SignInTask.NameFor(ReleaseLayout.TestPackId, InstallRoot);

        /// <summary>The task's action, as the install hook writes it from the installer's settings.</summary>
        public string TaskAction => SignInTask.ArgumentsFor(DataRoot, Feed);

        /// <summary>The background's pipe, for this install and this data root.</summary>
        public string Pipe => BackgroundPipe.NameFor(InstallRoot, DataRoot);

        /// <summary>The background's record.</summary>
        public string Record => BackgroundRecord.PathFor(DataRoot, Pipe);

        /// <summary>Installs the suite's pack.</summary>
        /// <param name="setup">The suite's installer.</param>
        /// <param name="label">A word for the scratch directories.</param>
        /// <param name="dataRoot">The data root to name, or <see langword="null"/> for a scratch one under the profile.</param>
        /// <returns>The install, whatever <c>Setup.exe</c> said: the arm asserts it.</returns>
        public static async Task<SuiteInstall> InstallAsync(string setup, string label, string? dataRoot = null)
        {
            var install = new SuiteInstall(label, dataRoot);

            try
            {
                install.SetupExitCode = await RunAsync(setup, ["--silent", "--log", Path.Combine(install.Logs, "setup.log"), "--installto", install.InstallRoot]);

                if (File.Exists(install.UpdateExe))
                {
                    install.UpdaterDigest = Hash(install.UpdateExe);
                }

                return install;
            }
            catch
            {
                await install.DisposeAsync();
                throw;
            }
        }

        /// <summary>A relay of this install, started the way its registration starts it.</summary>
        /// <returns>The relay, in a job of its own.</returns>
        public RawStdioClient StartRelay() =>
            RawStdioClient.Start(App, [Program.McpArgument, Program.DataRootArgument, DataRoot], Logs, ClientEnvironment());

        /// <summary>Whether a background of this install runs: its record names a live process, or its pipe is there.</summary>
        /// <returns>Whether one runs.</returns>
        public bool BackgroundRuns() =>
            (BackgroundRecord.Read(Record) is { Ended: null } record && ProcessIdentity.IsAlive(record.ProcessId, record.CreatedFileTime))
            || NamedPipes.WaitForFreeInstance(Pipe, 1);

        /// <summary>What the background's record, the task and the newest process log say, for a failure message.</summary>
        /// <param name="lines">How many of the log's last lines.</param>
        /// <returns>The text.</returns>
        public string Evidence(int lines = 80)
        {
            var text = new StringBuilder();

            _ = text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}--- the background's record, {Record} ---{Environment.NewLine}")
                .Append(File.Exists(Record) ? ReadShared(Record) : "<none>")
                .Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}--- the task, '{TaskName}' ---{Environment.NewLine}")
                .Append(ScheduledTasks.StateOf(TaskName).State.ToString());

            var logs = new LocalAppDataPaths(DataRoot).LogDirectory;

            if (Directory.Exists(logs)
                && Directory.EnumerateFiles(logs, "browserai-*.log").Order(StringComparer.Ordinal).LastOrDefault() is { } newest)
            {
                var all = ReadShared(newest).Split('\n');

                _ = text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}--- the last lines of {newest} ---{Environment.NewLine}")
                    .AppendJoin(Environment.NewLine, all.TakeLast(lines).Select(line => line.TrimEnd('\r')));
            }

            return text.ToString();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            {
                return;
            }

            try
            {
                // Any background of this install, through its pipe and never through the
                // task's End. A background that is not there answers at once.
                _ = BackgroundStop.AskAndWait(Pipe, Record, BackgroundStop.Bound);
            }
#pragma warning disable CA1031 // A cleanup on the failure path reports nothing and must replace no finding.
            catch (Exception)
#pragma warning restore CA1031
            {
            }

            // Inside the sandbox, so the uninstall hook stops this data root's background
            // and unregisters the scratch clients.
            await ReclaimAsync(InstallRoot);

            _sandbox.Dispose();
            _feed.Dispose();
            _logs.Dispose();
            _clientConfig.Dispose();
            _dataRoot?.Dispose();
            _installRoot.Dispose();
        }
    }

    /// <summary>
    /// A stand-in for an install's own <c>Update.exe</c>, running from its full path, with
    /// the real one put back when it goes.
    /// </summary>
    private sealed class StandInUpdater : IAsyncDisposable
    {
        private readonly string _installRoot;
        private readonly JobObject _job;
        private readonly LaunchedProcess _process;
        private int _disposed;

        private StandInUpdater(string installRoot, JobObject job, LaunchedProcess process)
        {
            _installRoot = installRoot;
            _job = job;
            _process = process;
        }

        /// <summary>Sets the install's own updater aside and starts a stand-in at its path.</summary>
        /// <param name="install">The install.</param>
        /// <returns>The running stand-in.</returns>
        public static StandInUpdater Start(SuiteInstall install)
        {
            File.Move(install.UpdateExe, SetAsideUpdaterOf(install.InstallRoot));

            try
            {
                File.Copy(StandInSource, install.UpdateExe);

                var job = JobObject.CreateKillOnClose();

                try
                {
                    // /d: no AutoRun commands; /q: no echo; /k: stay, reading a standard
                    // input the launcher holds and nothing writes to.
                    var process = JobLauncher.Start(job, install.UpdateExe, ["/d", "/q", "/k"], install.InstallRoot, PublishedSlice.InheritedEnvironment());

                    return new StandInUpdater(install.InstallRoot, job, process);
                }
                catch
                {
                    job.Dispose();
                    throw;
                }
            }
            catch
            {
                _ = PutTheUpdaterBack(install.InstallRoot);
                throw;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            {
                return;
            }

            // The job the arm made, closed: the stand-in ends with it.
            _job.Dispose();
            _ = await _process.WaitForExitAsync(TestDefaults.ProcessHang);
            _process.Dispose();

            _ = PutTheUpdaterBack(_installRoot);
        }
    }
}
