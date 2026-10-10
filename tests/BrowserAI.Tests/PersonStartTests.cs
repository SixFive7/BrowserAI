// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
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
    /// touched, whether it has no install root or no task name.
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
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNoBackgroundABuildThatIsNotInstalledIsNotShownAndNoTaskIsTouched()
    {
        using var scratch = new StartScratch("person-start-not-installed");

        var tasks = new ScriptedLogonTasks();
        using var logs = new CapturingLoggerProvider();

        var noRoot = await ShowAsync(scratch.Settings(tasks) with { InstallRoot = null }, page: null, logs);
        var noTask = await ShowAsync(scratch.Settings(tasks) with { TaskName = null }, page: null, logs);

        await Assert.That(noRoot).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(noTask).IsEqualTo((PersonStartOutcome.NotShown, (string?)null));
        await Assert.That(tasks.Events).IsEmpty();
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 6102 && record.Message.Contains(scratch.DataRoot, StringComparison.Ordinal))).IsEqualTo(2);
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
            await Assert.That(logs.Records.Count(record => record.EventId.Id is 6104)).IsEqualTo(1).Because("the start did not say it registered the task again");
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
}
