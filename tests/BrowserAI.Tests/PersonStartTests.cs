// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using BrowserAI.App;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// A person's start: it asks the background for a tab and opens it, and with no
/// background it starts one through the task, registering the task first when it is
/// missing, and is the only thing that clears a recorded crash.
/// </summary>
/// <remarks>
/// <para>
/// <b>D13 a, the maintainer's words of 2026-10-08, verbatim: <i>"d13 a"</i></b>,
/// amended by R the same day, and the design's "A person's start, the tab and the
/// hooks": a person's start finds the background's pipe and hands over <c>show</c>;
/// with no background it asks the Task Scheduler to run the task, registering it first
/// if it is missing, waits for the pipe, and does the same. With a crash recorded, it
/// clears the record first.
/// </para>
/// <para>
/// <b>The background is the product's own server, in this process</b>
/// (<see cref="BackgroundServerRig"/>), and the Task Scheduler is
/// <see cref="ScriptedLogonTasks"/>: an arm's task "starts" the background by starting
/// that server on the pipe the start is waiting on. No task is registered or run on the
/// machine, and nothing is opened in a browser: the start's own answer is the address.
/// </para>
/// </remarks>
internal sealed class PersonStartTests
{
    /// <summary>The task every arm's start names.</summary>
    private const string TaskName = "BrowserAI.app.scratch sign-in person-start-tests";

    /// <summary>The path every arm's start gives as its own: a checkout's, with a space in it, so the quoting shows.</summary>
    private const string Executable = @"C:\Users\someone\source\Browser AI\src\BrowserAI\bin\Debug\BrowserAI.exe";

    /// <summary>
    /// A running background's <c>show</c> answers the address of the page asked for,
    /// the status page when none is, and no task is touched.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-09</b> against a start that never passed the page it was
    /// asked for, so every start opened the status page.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARunningBackgroundHandsOverTheAddressOfThePageAskedForAndNoTaskIsTouched()
    {
        using var scratch = new StartScratch("person-start-shown");
        await using var background = BackgroundServerRig.Start(scratch.PipeName);

        var tasks = new ScriptedLogonTasks();
        using var logs = new CapturingLoggerProvider();

        var sessions = await ShowAsync(scratch.Settings(tasks), "sessions", logs);
        var status = await ShowAsync(scratch.Settings(tasks), page: null, logs);

        await Assert.That(sessions).IsEqualTo((PersonStartOutcome.Shown, (string?)FakeBackgroundVerbs.DefaultAddress));
        await Assert.That(status).IsEqualTo((PersonStartOutcome.Shown, (string?)FakeBackgroundVerbs.DefaultAddress));
        await Assert.That(string.Join(" | ", background.Verbs.Pages.Select(page => page ?? "(status)"))).IsEqualTo("sessions | (status)");
        await Assert.That(tasks.Events).IsEmpty();
    }

    /// <summary>
    /// A background that refuses to hand out a tab is not shown, the refusal is logged in
    /// its own words, and no task is touched: a background that answers is not replaced.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-09</b> against a start that went on to the task after a
    /// refusal, as it does when no background answers.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusalIsNotShownAndStartsNothing()
    {
        using var scratch = new StartScratch("person-start-refused");
        await using var background = BackgroundServerRig.Start(scratch.PipeName, verbs: new FakeBackgroundVerbs { Address = null });

        var tasks = new ScriptedLogonTasks();
        using var logs = new CapturingLoggerProvider();

        var refused = await ShowAsync(scratch.Settings(tasks), page: null, logs);

        await Assert.That(refused).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 6101 && record.Message.Contains(background.Verbs.Refusal, StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(tasks.Events).IsEmpty();
    }

    /// <summary>
    /// With no background, a build that is not installed is not shown and no task is
    /// touched, whether it has no install root or no task name; the first is told the
    /// command that starts this build's own background, and the second that its pack id
    /// is unknown and a reinstall registers the task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D11 a</b>: nothing starts a background for a build that is not installed; the
    /// log names the command that starts one by hand.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a start that asked the task whenever it
    /// had a task name, install root or not.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-10 (previously both starts were held to write 6102 naming
    /// the data root)</b>, the texts review's #166: 6102 gave a bare <c>BrowserAI.exe</c>,
    /// which a terminal finds on the PATH the hooks point at the installed build, and an
    /// installed build whose pack id is unknown took it too and was told it is not
    /// installed. Each line is now held word for word. <b>Planted red 2026-10-10 twice</b>:
    /// against 6102 as it was, whose line named <c>BrowserAI.exe</c> bare, and against the
    /// branch as it was with 6102 fixed, under which the installed build wrote 6102 and no
    /// 6112.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNoBackgroundABuildThatIsNotInstalledIsNotShownAndNoTaskIsTouched()
    {
        using var scratch = new StartScratch("person-start-not-installed");

        var tasks = new ScriptedLogonTasks();
        using var notInstalled = new CapturingLoggerProvider();
        using var unnamed = new CapturingLoggerProvider();

        var noRoot = await ShowAsync(scratch.Settings(tasks) with { InstallRoot = null }, page: null, notInstalled);
        var noTask = await ShowAsync(scratch.Settings(tasks) with { TaskName = null }, page: null, unnamed);

        await Assert.That(noRoot).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(noTask).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(tasks.Events).IsEmpty();

        await Assert.That(Lines(notInstalled, 6102)).IsEqualTo(
            "No background runs for this build, and a build that is not installed has nothing that starts one. "
            + $"Start one with: \"{Executable}\" --background --data-root \"{scratch.DataRoot}\"");
        await Assert.That(Lines(notInstalled, 6112)).IsEmpty();

        await Assert.That(Lines(unnamed, 6112)).IsEqualTo(
            $"No background runs for this build, which is installed in '{scratch.InstallRoot}'. "
            + "The pack id is unknown, so the task that starts BrowserAI has no name and was not run. Installing BrowserAI again registers the task.");
        await Assert.That(Lines(unnamed, 6102)).IsEmpty().Because("an installed build was told it is not installed");
    }

    /// <summary>
    /// With no background and the task missing, the start registers it again from the
    /// definition the install saved, runs it, waits for the pipe the background it
    /// started opens, and hands over the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>RESOLUTIONS 9</b>: only a person's start registers a missing task again, and
    /// from the definition the install hook wrote, which carries the settings the
    /// installer named and nothing else keeps. The definition here is the one the
    /// install hook's own step saved into a scratch install.
    /// </para>
    /// <para>
    /// <b>The task's start is the background's server coming up on another thread</b>,
    /// so the start meets the pipe whenever the server has taken it, the first look or a
    /// later one.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a start that ran a missing task again
    /// without registering it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Extended 2026-10-10 by addition</b>, the texts review's #168: 6104 is held word
    /// for word, since it is written only once the task is registered and no longer
    /// carries the scheduler's outcome. <b>Planted red 2026-10-10</b> against the line as
    /// it was, "The task was missing and is registered again: Registered. The task '...'
    /// is registered."
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AMissingTaskIsRegisteredAgainFromTheSavedDefinitionRunAndThePipeWaitedFor()
    {
        using var scratch = new StartScratch("person-start-missing");

        var saved = SaveTheInstallsDefinition(scratch);
        var tasks = new ScriptedLogonTasks();
        var started = new ConcurrentQueue<Task<BackgroundServerRig>>();

        tasks.Started = (_, _) => started.Enqueue(Task.Run(() => BackgroundServerRig.Start(scratch.PipeName)));

        try
        {
            using var logs = new CapturingLoggerProvider();
            var shown = await ShowAsync(scratch.Settings(tasks) with { Definition = () => SignInTask.SavedDefinition(scratch.InstallRoot) }, "update", logs);

            await Assert.That(shown).IsEqualTo((PersonStartOutcome.Shown, (string?)FakeBackgroundVerbs.DefaultAddress));
            await Assert.That(string.Join(" | ", tasks.Events)).IsEqualTo($"run {TaskName} {PersonStart.StartedByPerson} | register {TaskName} | run {TaskName} {PersonStart.StartedByPerson}");
            await Assert.That(tasks.Registered[TaskName]).IsEqualTo(saved).Because("the task was registered from something other than the definition the install saved");
            await Assert.That(started.Count).IsEqualTo(1);

            var background = await started.Single();

            await Assert.That(string.Join(" | ", background.Verbs.Pages)).IsEqualTo("update");
            await Assert.That(Lines(logs, 6104))
                .IsEqualTo($"The task '{TaskName}' was missing and is registered again, from the definition the install saved.")
                .Because("the start did not say it registered the task again, in these words");
        }
        finally
        {
            foreach (var starting in started)
            {
                await (await starting).DisposeAsync();
            }
        }
    }

    /// <summary>
    /// A task the Task Scheduler will not run, a disabled one, is not shown, and nothing
    /// is registered: BrowserAI leaves a disabled task disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D12 b</b>: a disabled task stays disabled; the start's log carries the Task
    /// Scheduler's own sentence, with its <c>HRESULT</c>.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a start that treated any refusal of the run
    /// as a missing task and registered it again, which re-enables it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADisabledTaskIsNotShownAndNothingIsRegistered()
    {
        using var scratch = new StartScratch("person-start-disabled");

        const string Disabled = "The task scheduler could not start the task: 0x80041326, The task is disabled.";

        var tasks = new ScriptedLogonTasks { RunAnswer = (_, _) => new TaskReport(TaskChange.Failed, Disabled) };
        using var logs = new CapturingLoggerProvider();

        var refused = await ShowAsync(scratch.Settings(tasks), page: null, logs);

        await Assert.That(refused).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(string.Join(" | ", tasks.Events)).IsEqualTo($"run {TaskName} {PersonStart.StartedByPerson}");
        await Assert.That(tasks.Registered).IsEmpty();
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 6105 && record.Message.Contains(Disabled, StringComparison.Ordinal))).IsEqualTo(1);
    }

    /// <summary>
    /// A missing task the Task Scheduler will not register again is not shown and is never
    /// said to be registered again: its own line carries the scheduler's sentence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The texts review's #168, 2026-10-10</b>: 6104 was written before the outcome was
    /// looked at, so a refusal read "The task was missing and is registered again:
    /// Failed. ...", at Information. It is written once the task is registered, and a
    /// refusal is 6113, at Error.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10 twice</b>: against the start as it was, which wrote 6104
    /// first whatever the scheduler answered, "The task was missing and is registered
    /// again: Failed. ..."; and against one that wrote neither, which left 6113 empty.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AMissingTaskTheSchedulerWillNotRegisterAgainIsNeverSaidToBeRegisteredAgain()
    {
        using var scratch = new StartScratch("person-start-not-registered-again");

        const string Refused = "The task scheduler could not register 'BrowserAI.app.scratch sign-in person-start-tests': 0x80070005, Access is denied.";

        var tasks = new RefusingScheduler(Refused);
        using var logs = new CapturingLoggerProvider();

        var refused = await ShowAsync(scratch.Settings(tasks) with { Definition = static () => "the definition the install saved" }, page: null, logs);

        await Assert.That(refused).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(string.Join(" | ", tasks.Events)).IsEqualTo($"run {TaskName} {PersonStart.StartedByPerson} | register {TaskName}");
        await Assert.That(Lines(logs, 6104)).IsEmpty().Because("a task the scheduler refused was said to be registered again");
        await Assert.That(Lines(logs, 6113)).IsEqualTo($"The task '{TaskName}' was missing and was not registered again, so no background was started. {Refused}");
    }

    /// <summary>
    /// A task that starts and no background that opens its pipe is said with its bound in
    /// seconds, as a person reads it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The texts review's #170, 2026-10-10</b>: the bound went into 6106 as a
    /// <see cref="TimeSpan"/>, which a log line prints as <c>00:00:30</c>. The bound here is
    /// the start's own look interval, a product constant, so the arm waits one look and
    /// not a person's thirty seconds.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against 6106 as it was, which printed this bound as
    /// <c>00:00:00.1000000</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATaskThatStartsNoBackgroundIsSaidWithItsBoundInSeconds()
    {
        using var scratch = new StartScratch("person-start-no-pipe");

        // Registered, so the run "starts" the task, and nothing ever serves the pipe.
        var tasks = new ScriptedLogonTasks();
        tasks.Registered[TaskName] = "the suite's task";

        using var logs = new CapturingLoggerProvider();

        var shown = await ShowAsync(scratch.Settings(tasks) with { StartBound = PersonStart.LookInterval }, page: null, logs);

        await Assert.That(shown).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(Lines(logs, 6106)).IsEqualTo(
            $"The task '{TaskName}' was started, and no background opened its pipe within {PersonStart.LookInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds. "
            + "The log of the background, and the task's last run result, say why.");
    }

    /// <summary>
    /// A recorded crash with no exit code is cleared with its code said to be unknown, as
    /// the relay's crash sentence says it, and one with a code is cleared with that code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The texts review's #171, 2026-10-10</b>: a code nothing recorded went into 6107
    /// as a null <c>int?</c>, which a log line prints as <c>(null)</c>.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against 6107 as it was, which read "(exit code
    /// (null))" for the first crash.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARecordedCrashWithNoExitCodeIsClearedWithItsCodeSaidToBeUnknown()
    {
        using var scratch = new StartScratch("person-start-crash-code");

        var tasks = new ScriptedLogonTasks { RunAnswer = (_, _) => new TaskReport(TaskChange.Failed, "The suite's task does not start anything.") };
        using var logs = new CapturingLoggerProvider();

        HandWrittenRecord.Gone(scratch.RecordPath);
        _ = await ShowAsync(scratch.Settings(tasks), page: null, logs);

        HandWrittenRecord.Gone(scratch.RecordPath, exitCode: -1073741819);
        _ = await ShowAsync(scratch.Settings(tasks), page: null, logs);

        await Assert.That(Lines(logs, 6107)).IsEqualTo(
            $"The background's record says pid {Environment.ProcessId} crashed (exit code unknown); this start clears the record and starts a new background."
            + $" | The background's record says pid {Environment.ProcessId} crashed (exit code -1073741819); this start clears the record and starts a new background.");
    }

    /// <summary>
    /// The lines about a hung background end in one full stop, whatever the reasons they
    /// carry end in: the client's for the silence, and Windows' for a process it would not
    /// end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The texts review's #172 and #173, 2026-10-10</b>: 6108 and 6109 put a full stop
    /// after the reason, and the client's every reason for a background that did not
    /// answer ends in one, as do the three from a process that could not be opened or
    /// ended. The record names this process, as a background's names its own, so the start
    /// goes on to end it, and <see cref="PersonStartSettings.EndProcess"/> answers with
    /// Windows' sentence for a process it would not open: this process's own pid always
    /// opens, and nothing here may end it.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against each line as it was, one at a time, which put
    /// its own full stop after the reason's: 6108 read "within 0 s.. This start judges it
    /// hung.", and with 6108 fixed, 6109 read "Access is denied..".
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLinesAboutAHungBackgroundEndInOneFullStopWhateverTheirReasonsEndIn()
    {
        using var scratch = new StartScratch("person-start-hung-full-stops");
        await using var background = BackgroundServerRig.Start(scratch.PipeName, verbs: new FakeBackgroundVerbs { HoldsShow = true });

        var pid = Environment.ProcessId;
        var created = ProcessLiveness.CreationTimeOfThisProcess();

        HandWrittenRecord.Write(scratch.RecordPath, pid, created);

        var asked = new List<string>();
        var tasks = new ScriptedLogonTasks();
        using var logs = new CapturingLoggerProvider();

        var settings = scratch.Settings(tasks) with
        {
            HandOutBound = PersonStart.LookInterval,
            EndProcess = (processId, createdFileTime, installRoot) =>
            {
                asked.Add(string.Create(CultureInfo.InvariantCulture, $"{processId} {createdFileTime} {installRoot}"));
                return $"pid {processId} could not be opened: Access is denied.";
            },
        };

        var hung = await ShowAsync(settings, page: null, logs);

        await Assert.That(hung).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(Lines(logs, 6108)).IsEqualTo($"The background (pid {pid}) took the connection and did not answer: The background did not answer within 0 s. This start judges it hung.");
        await Assert.That(Lines(logs, 6109)).IsEqualTo($"The hung background (pid {pid}) was not ended: pid {pid} could not be opened: Access is denied.");
        await Assert.That(string.Join(" | ", asked)).IsEqualTo(string.Create(CultureInfo.InvariantCulture, $"{pid} {created} {scratch.InstallRoot}"));
        await Assert.That(tasks.Events).IsEmpty();
    }

    /// <summary>
    /// A recorded crash is cleared before the task is asked for a new background, and
    /// only a crash is: the record of a background that is alive, or that ended cleanly,
    /// is left as it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>R</b>: only the person's start clears a recorded crash, and it does so before
    /// it starts a background, so the new background's relays never read the old crash.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a start that asked the task first and
    /// cleared the record after.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARecordedCrashIsClearedBeforeTheTaskIsAskedAndOnlyACrashIs()
    {
        using var scratch = new StartScratch("person-start-crash");

        var recordAtTheRun = new List<bool>();
        var tasks = new ScriptedLogonTasks
        {
            // The task is there and the run is refused, so the start ends at the run.
            RunAnswer = (_, _) => new TaskReport(TaskChange.Failed, "The suite's task does not start anything."),
            Asked = (_, _) => recordAtTheRun.Add(File.Exists(scratch.RecordPath)),
        };
        using var logs = new CapturingLoggerProvider();

        HandWrittenRecord.Gone(scratch.RecordPath, exitCode: 3);

        _ = await ShowAsync(scratch.Settings(tasks), page: null, logs);

        await Assert.That(string.Join(" | ", recordAtTheRun)).IsEqualTo("False").Because("the task was asked while the crash was still recorded");
        await Assert.That(File.Exists(scratch.RecordPath)).IsFalse();
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 6107)).IsEqualTo(1);

        // A background that is alive and has not opened its pipe is not a crash.
        _ = BackgroundRecord.Started(scratch.RecordPath, BackgroundServerRig.Build, "image", HandWrittenRecord.StartedAt);
        var alive = await File.ReadAllBytesAsync(scratch.RecordPath);

        _ = await ShowAsync(scratch.Settings(tasks), page: null, logs);

        await Assert.That((await File.ReadAllBytesAsync(scratch.RecordPath)).SequenceEqual(alive)).IsTrue();

        // Nor is one that ended cleanly.
        HandWrittenRecord.Write(scratch.RecordPath, Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess() - 1, ended: nameof(BackgroundEnd.Stopped));
        var clean = await File.ReadAllBytesAsync(scratch.RecordPath);

        _ = await ShowAsync(scratch.Settings(tasks), page: null, logs);

        await Assert.That((await File.ReadAllBytesAsync(scratch.RecordPath)).SequenceEqual(clean)).IsTrue();
        await Assert.That(string.Join(" | ", recordAtTheRun)).IsEqualTo("False | True | True");
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 6107)).IsEqualTo(1);
    }

    /// <summary>
    /// A background that takes the connection and does not answer within the hand-out
    /// bound is judged hung, and one the background's record does not name is not ended
    /// and nothing is started in its place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>RESOLUTIONS 10 and the repository's rule</b>: a hung background is ended only
    /// by its pid after its creation time and its image under the install root are
    /// verified, and a pid the record does not name is not known to be BrowserAI's. The
    /// pid here is this test host's own, which is why the arm keeps every half of that
    /// verification out of reach: no record is written.
    /// </para>
    /// <para>
    /// <b>The hand-out bound is the start's own look interval</b>, a product constant,
    /// so the arm judges the background hung without the ten seconds a person gives it;
    /// the background holds the request until the rig lets it go.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a start that went on to the task when it
    /// could not end the hung background.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHungBackgroundTheRecordDoesNotNameIsNotEndedAndNothingIsStarted()
    {
        using var scratch = new StartScratch("person-start-hung");
        await using var background = BackgroundServerRig.Start(scratch.PipeName, verbs: new FakeBackgroundVerbs { HoldsShow = true });

        var tasks = new ScriptedLogonTasks();
        using var logs = new CapturingLoggerProvider();

        var hung = await ShowAsync(scratch.Settings(tasks) with { HandOutBound = PersonStart.LookInterval }, page: null, logs);

        await Assert.That(hung).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 6108)).IsEqualTo(1).Because("the start did not judge the background hung");
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 6109)).IsEqualTo(1).Because("the start did not say why it ended nothing");
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 6110)).IsEqualTo(0);
        await Assert.That(tasks.Events).IsEmpty();
    }

    /// <summary>
    /// A background the record names, whose image lies under the install root, that
    /// takes the connection and does not answer is ended, its record cleared, and a new
    /// background started through the task, whose page is the one shown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>RESOLUTIONS 10 and R</b>: a person's start is the one thing that ends a stuck
    /// background, and only by a pid whose creation time the record names and whose image
    /// is verified to lie under the install root. Left untested by lane ARCH's helper T1
    /// on 2026-10-09, because the background has to be a process of its own with its
    /// image there: here it is the suite's probe, copied under the scratch install's
    /// <c>current\</c> with everything it loads (<see cref="ProbeImage"/>), standing in
    /// for a background that took its pipe, wrote its record and stopped answering.
    /// </para>
    /// <para>
    /// <b>The hand-out bound is a person's own</b>, because one bound governs both the
    /// question the stuck background never answers and the one the new background must
    /// answer in; the task's start waits for the stand-in to have gone before it starts
    /// the new background on the same pipe.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a start that verified the image against a
    /// folder it does not lie under, another install's beside this one, which ended
    /// nothing and started nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHungBackgroundTheRecordNamesIsEndedItsRecordClearedAndANewOneStarted()
    {
        using var scratch = new StartScratch("person-start-hung-ended");
        using var scope = new JobObjectScope();

        var standInImage = ProbeImage.CopyInto(Path.Combine(scratch.InstallRoot, "current"));
        var ready = Path.Combine(scratch.DataRoot, "stand-in.ready");

        var standIn = scope.Launch(standInImage, scratch.DataRoot, "background-standin", scratch.PipeName, scratch.RecordPath, ready, "0");

        await BackgroundServerRig.WaitUntilAsync(() => File.Exists(ready) || standIn.HasExited, "the stand-in never took the pipe");
        await Assert.That(standIn.HasExited).IsFalse().Because(scope.SaidBy(standIn.Id));

        var recordAtTheRun = new List<bool>();
        var started = new ConcurrentQueue<Task<BackgroundServerRig>>();
        var tasks = new ScriptedLogonTasks
        {
            Asked = (_, _) => recordAtTheRun.Add(File.Exists(scratch.RecordPath)),
            Started = (_, _) =>
            {
                // The new background takes the pipe once the stuck one has let it go.
                _ = standIn.WaitForExitAsync(TestDefaults.InProcessHang).GetAwaiter().GetResult();
                started.Enqueue(Task.Run(() => BackgroundServerRig.Start(scratch.PipeName)));
            },
        };

        // Registered, so the run starts what the arm says and asks for no definition.
        tasks.Registered[TaskName] = "the suite's task";

        try
        {
            using var logs = new CapturingLoggerProvider();
            var shown = await ShowAsync(scratch.Settings(tasks), page: null, logs);

            await Assert.That(shown).IsEqualTo((PersonStartOutcome.Shown, (string?)FakeBackgroundVerbs.DefaultAddress))
                .Because(string.Join(Environment.NewLine, logs.Records.Select(record => record.Message)));
            await Assert.That(logs.Records.Count(record => record.EventId.Id is 6108)).IsEqualTo(1).Because("the start did not judge the background hung");
            await Assert.That(logs.Records.Count(record => record.EventId.Id is 6110)).IsEqualTo(1).Because("the start did not say it ended the hung background");
            await Assert.That(logs.Records.Count(record => record.EventId.Id is 6109)).IsEqualTo(0);
            await Assert.That(standIn.HasExited).IsTrue();
            await Assert.That(string.Join(" | ", recordAtTheRun)).IsEqualTo("False").Because("the task was asked while the stuck background's record was still there");
            await Assert.That(string.Join(" | ", tasks.Events)).IsEqualTo($"run {TaskName} {PersonStart.StartedByPerson}");
            await Assert.That(started.Count).IsEqualTo(1);
        }
        finally
        {
            foreach (var starting in started)
            {
                await (await starting).DisposeAsync();
            }
        }
    }

    /// <summary>Runs a person's start on a thread of its own, as the program's main thread runs it.</summary>
    /// <param name="settings">Where the background is.</param>
    /// <param name="page">The page asked for.</param>
    /// <param name="logs">Where it reports.</param>
    /// <returns>What it came to.</returns>
    private static async Task<(PersonStartOutcome Outcome, string? Address)> ShowAsync(PersonStartSettings settings, string? page, CapturingLoggerProvider logs) =>
        await Task.Run(() => PersonStart.Show(settings, page, logs.CreateLogger("BrowserAI.App"))).WaitAsync(TestDefaults.InProcessHang);

    /// <summary>Every line one event wrote, in order, joined.</summary>
    /// <param name="logs">What the start wrote.</param>
    /// <param name="eventId">The event.</param>
    /// <returns>The lines, joined with <c> | </c>; empty when it wrote none.</returns>
    private static string Lines(CapturingLoggerProvider logs, int eventId) =>
        string.Join(" | ", logs.Records.Where(record => record.EventId.Id == eventId).Select(record => record.Message));

    /// <summary>Writes the definition the install hook saves, through the hook's own step, and reads it back.</summary>
    /// <param name="scratch">The start's scratch install.</param>
    /// <returns>The definition as saved.</returns>
    private static string SaveTheInstallsDefinition(StartScratch scratch)
    {
        if (!RegistrationTarget.TryResolve(InstalledLayout.Create(scratch.InstallRoot), out var target, out var refusal))
        {
            throw new InvalidOperationException($"The scratch install does not resolve: {refusal}");
        }

        var report = SignInTask.Apply(
            RegistrationIntent.Install,
            target!,
            ScratchLogonTasks.AppId,
            new ScratchLogonTasks(),
            NullLogger.Instance,
            SignInTask.ArgumentsFor(@"C:\Users\someone\BrowserAI-data", updateSource: null));

        return report.Change is TaskChange.Registered && SignInTask.SavedDefinition(scratch.InstallRoot) is { } saved
            ? saved
            : throw new InvalidOperationException($"The install hook's step saved no definition: {report.Detail}");
    }

    /// <summary>One start's scratch: an install root, a data root, a pipe of its own and the record beside it.</summary>
    private sealed class StartScratch : IDisposable
    {
        private readonly ScratchDirectory _install;
        private readonly ScratchDirectory _data;

        /// <summary>Makes the scratch.</summary>
        /// <param name="label">What the scratch folders are called.</param>
        public StartScratch(string label)
        {
            _install = ScratchDirectory.Create(label + "-install");
            _data = ScratchDirectory.Create(label + "-data");
            PipeName = PublishedBackground.NewPipeName();
            RecordPath = BackgroundRecord.PathFor(DataRoot, PipeName);
        }

        /// <summary>The install root.</summary>
        public string InstallRoot => _install.Path;

        /// <summary>The data root.</summary>
        public string DataRoot => _data.Path;

        /// <summary>The background's pipe.</summary>
        public string PipeName { get; }

        /// <summary>The background's record.</summary>
        public string RecordPath { get; }

        /// <summary>The settings a person's start of this install has.</summary>
        /// <param name="tasks">The Task Scheduler.</param>
        /// <returns>The settings.</returns>
        public PersonStartSettings Settings(ILogonTasks tasks) => new()
        {
            PipeName = PipeName,
            RecordPath = RecordPath,
            InstallRoot = InstallRoot,
            DataRoot = DataRoot,
            Executable = Executable,
            TaskName = TaskName,
            Definition = static () => throw new InvalidOperationException("The suite's start asked for a definition it was not given."),
            Tasks = tasks,
        };

        /// <inheritdoc />
        public void Dispose()
        {
            _install.Dispose();
            _data.Dispose();
        }
    }

    /// <summary>A Task Scheduler with no task, which refuses to register one: a refusal the real one gives to a definition it will not take.</summary>
    /// <param name="refusal">What it says to a registration.</param>
    private sealed class RefusingScheduler(string refusal) : ILogonTasks
    {
        /// <summary>Every call, as <c>verb name [argument]</c>, in the order asked.</summary>
        public ConcurrentQueue<string> Events { get; } = new();

        /// <inheritdoc />
        public TaskReport Register(string name, string definition)
        {
            Events.Enqueue($"register {name}");
            return new TaskReport(TaskChange.Failed, refusal);
        }

        /// <inheritdoc />
        public TaskReport Remove(string name) => throw new InvalidOperationException("A person's start removes no task.");

        /// <inheritdoc />
        public TaskReport Run(string name, string argument)
        {
            Events.Enqueue($"run {name} {argument}");
            return new TaskReport(TaskChange.NotRegistered, $"There is no task '{name}' to start.");
        }
    }
}
