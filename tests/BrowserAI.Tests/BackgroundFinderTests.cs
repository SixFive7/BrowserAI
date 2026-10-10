// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Relay;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// How a relay in production learns why it has no background: the order in which one
/// reason outranks another, each answer, and the one thing it writes, the moment a
/// crash was first found.
/// </summary>
/// <remarks>
/// <para>
/// <b>It only looks</b> (S a and R, RESOLUTIONS 9): it reads a record, asks whether
/// this install's updater runs, and reads the task without changing it. The order is
/// the design's "The first call" and "The background will not start": an updater
/// running from this install first (U2), then the record, a starting background or a
/// recorded crash (R), then a build that is not installed (D11 a), and last the task,
/// read and never repaired (D12 b).
/// </para>
/// <para>
/// <b>Every look goes through the settings' seams</b>: the updater is a stand-in the
/// arm sets, the task's reading is the arm's, and the records are files the arm wrote.
/// No updater, no task and no background is ever looked for on the machine.
/// </para>
/// </remarks>
internal sealed class BackgroundFinderTests
{
    /// <summary>The task every arm's settings name.</summary>
    private const string TaskName = "BrowserAI.app.scratch sign-in finder-tests";

    /// <summary>The binary every arm's settings say the relay is.</summary>
    private const string Executable = @"C:\Users\someone\builds\BrowserAI.exe";

    /// <summary>
    /// An updater running from this install outranks a recorded crash and the task, and
    /// is let go once looked at; once it has gone, the crash it outranked is named; one
    /// that cannot be looked for is read as none, and the log says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>U2</b>: a relay that starts during an install is answered with the update
    /// sentence whatever else is true, because nothing the record or the task says is
    /// about the background the update will start.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a finder that read the record before it
    /// looked for the updater.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUpdaterRunningFromThisInstallOutranksARecordedCrashAndTheTask()
    {
        using var data = ScratchDirectory.Create("finder-updater");
        using var install = ScratchDirectory.Create("finder-updater-install");

        var seams = new FinderSeams { UpdaterRuns = true, Task = new ScheduledTaskReading(ScheduledTaskState.Missing, null) };
        using var logs = new CapturingLoggerProvider();
        var settings = Settings(data.Path, install.Path, TaskName, seams, new ManualClock());

        HandWrittenRecord.Gone(settings.RecordPath);

        using var finder = new BackgroundFinder(settings, logs.CreateLogger("BrowserAI.Relay"));

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsTypeOf<BackgroundAbsence.UpdateInstalling>();
        await Assert.That(string.Join(" | ", seams.UpdatersLookedFor)).IsEqualTo(install.Path);
        await Assert.That(seams.UpdatersLetGo).IsEqualTo(1).Because("the updater found was held past the look");
        await Assert.That(seams.TasksRead).IsEmpty();
        await Assert.That(BackgroundRecord.Read(settings.RecordPath)!.ExitedAt).IsNull().Because("nothing below the updater was even read");

        // Gone: the crash it outranked.
        seams.UpdaterRuns = false;

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsTypeOf<BackgroundAbsence.Crashed>();

        // An updater that cannot be looked for is read as none, and the log says so.
        seams.UpdaterUnreadable = true;
        BackgroundRecord.Clear(settings.RecordPath);

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(new BackgroundAbsence.NotRunning(TaskState.Missing, TaskName, null));
        // 23 since 2026-10-10, round 2 of the texts review, #195 (previously 1, which the
        // relay's own Connected line has too).
        await Assert.That(logs.Records.Count(record => record.EventId.Id is 23 && record.Level is LogLevel.Warning)).IsEqualTo(1);
    }

    /// <summary>
    /// A record whose background is alive and has not opened its pipe is a background
    /// starting; one whose background is gone with no clean end is a crash, at the time
    /// and with the exit code a relay recorded; one that ended cleanly is neither, and
    /// the task is read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>R</b>: a crash is answered at once, a starting background is held for. A clean
    /// end, at a stop, a sign-out or the task's End, is not a crash and is never named
    /// as one.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a finder that read any record whose process
    /// was gone as a crash, a clean end included.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALiveRecordIsAStartAGoneOneACrashAndACleanEndNeither()
    {
        using var data = ScratchDirectory.Create("finder-records");
        using var install = ScratchDirectory.Create("finder-records-install");

        var seams = new FinderSeams();
        var clock = new ManualClock();
        var settings = Settings(data.Path, install.Path, TaskName, seams, clock);

        using var finder = new BackgroundFinder(settings, NullLogger.Instance);

        // Alive: this process's own record.
        _ = BackgroundRecord.Started(settings.RecordPath, BackgroundServerRig.Build, "image", HandWrittenRecord.StartedAt);

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsTypeOf<BackgroundAbsence.Starting>();

        // Gone, with what a relay that held it saw.
        var exitedAt = HandWrittenRecord.StartedAt.AddMinutes(17);
        HandWrittenRecord.Gone(settings.RecordPath, exitCode: unchecked((int)0xC0000409), exitedAt);

        await Assert.That(finder.Explain(lastBackgroundPid: null))
            .IsEqualTo(new BackgroundAbsence.Crashed(exitedAt, unchecked((int)0xC0000409), settings.LogPath));

        // Gone, with nothing recorded: the moment this relay found it gone, and no code.
        HandWrittenRecord.Gone(settings.RecordPath);

        await Assert.That(finder.Explain(lastBackgroundPid: null))
            .IsEqualTo(new BackgroundAbsence.Crashed(clock.GetUtcNow(), null, settings.LogPath));

        await Assert.That(seams.TasksRead).IsEmpty().Because("a start and a crash are answered from the record alone");

        // Ended cleanly, gone or not: no crash, and the task says the rest.
        HandWrittenRecord.Write(settings.RecordPath, Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess() - 1, ended: nameof(BackgroundEnd.EndCommand));

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(new BackgroundAbsence.NotRunning(TaskState.Ready, TaskName, null));

        _ = BackgroundRecord.Started(settings.RecordPath, BackgroundServerRig.Build, "image", HandWrittenRecord.StartedAt);
        _ = BackgroundRecord.EndedCleanly(settings.RecordPath, BackgroundEnd.Stopped, HandWrittenRecord.StartedAt.AddHours(2));

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(new BackgroundAbsence.NotRunning(TaskState.Ready, TaskName, null));
        await Assert.That(seams.TasksRead.Count).IsEqualTo(2);
    }

    /// <summary>
    /// The first relay that finds a crashed background gone writes when, so a second
    /// relay asking later, on a clock of its own, names the same moment, and so does the
    /// first asking again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The design's "The background dies"</b>: every call is answered with the crash
    /// text, which names the time, and two clients told two times for one crash would be
    /// told about two crashes.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a finder that answered with its own clock
    /// and wrote nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACrashIsTimedOnceSoEveryRelayNamesTheSameMoment()
    {
        using var data = ScratchDirectory.Create("finder-crash-time");
        using var install = ScratchDirectory.Create("finder-crash-time-install");

        var firstClock = new ManualClock();
        var secondClock = new ManualClock();

        firstClock.Advance(TimeSpan.FromHours(1));
        secondClock.Advance(TimeSpan.FromHours(2));

        var first = Settings(data.Path, install.Path, TaskName, new FinderSeams(), firstClock);
        var second = first with { Clock = secondClock };

        HandWrittenRecord.Gone(first.RecordPath);

        using var firstRelay = new BackgroundFinder(first, NullLogger.Instance);
        using var secondRelay = new BackgroundFinder(second, NullLogger.Instance);

        var found = firstClock.GetUtcNow();
        var expected = new BackgroundAbsence.Crashed(found, null, first.LogPath);

        await Assert.That(firstRelay.Explain(lastBackgroundPid: null)).IsEqualTo(expected);
        await Assert.That(secondRelay.Explain(lastBackgroundPid: null)).IsEqualTo(expected).Because("the second relay named its own time for the same crash");

        firstClock.Advance(TimeSpan.FromHours(5));

        await Assert.That(firstRelay.Explain(lastBackgroundPid: null)).IsEqualTo(expected);

        var record = BackgroundRecord.Read(first.RecordPath)!;

        await Assert.That(record.ExitedAt).IsEqualTo(found);
        await Assert.That(record.ExitCode).IsNull();
        await Assert.That(record.Ended).IsNull();
    }

    /// <summary>
    /// A relay that reached a background holds it, and once that background has gone
    /// with no clean end its exit code and the moment are written into its record, so
    /// every relay names the crash with the code it left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The design's "Where the reason can be found"</b>: a crash that wrote nothing
    /// still leaves its exit code, and only a process holding a handle on the background
    /// can read it. Left untested by a reading of the code on 2026-10-09, because the
    /// background has to be a process of its own that really exits: here the suite's
    /// probe stands in for it (<c>background-standin</c>), taking the pipe and writing
    /// the record through the product's own code, and ends with the code the arm gives it
    /// when its standard input closes.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a finder that never read what the background
    /// it held left, which named the crash with no exit code.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheExitCodeOfTheBackgroundARelayHeldIsWrittenIntoItsRecord()
    {
        using var data = ScratchDirectory.Create("finder-exit-code");
        using var install = ScratchDirectory.Create("finder-exit-code-install");

        const int ExitCode = 77;

        var clock = new ManualClock();
        var settings = Settings(data.Path, install.Path, TaskName, new FinderSeams(), clock);
        var ready = Path.Combine(data.Path, "stand-in.ready");

        using var scope = new JobObjectScope();

        var standIn = scope.Launch(
            Path.Combine(AppContext.BaseDirectory, ProbeImage.Name + ".exe"),
            data.Path,
            "background-standin",
            settings.PipeName,
            settings.RecordPath,
            ready,
            ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await BackgroundServerRig.WaitUntilAsync(() => File.Exists(ready) || standIn.HasExited, "the stand-in never took the pipe");
        await Assert.That(standIn.HasExited).IsFalse().Because(scope.SaidBy(standIn.Id));

        using var finder = new BackgroundFinder(settings, NullLogger.Instance);
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

        await using (var pipe = await finder.TryConnectAsync(hang.Token))
        {
            await Assert.That(pipe).IsNotNull();
        }

        // It goes with the code it was given, and nothing marked its record: a crash.
        await standIn.StandardInput.DisposeAsync();

        await BackgroundServerRig.WaitUntilAsync(() => standIn.HasExited, "the stand-in did not end when its input closed");

        var absence = finder.Explain(lastBackgroundPid: standIn.Id);

        await Assert.That(absence).IsEqualTo(new BackgroundAbsence.Crashed(clock.GetUtcNow(), ExitCode, settings.LogPath));

        var record = BackgroundRecord.Read(settings.RecordPath)!;

        await Assert.That(record.ProcessId).IsEqualTo(standIn.Id);
        await Assert.That(record.ExitCode).IsEqualTo(ExitCode);
        await Assert.That(record.ExitedAt).IsEqualTo(clock.GetUtcNow());
        await Assert.That(record.Ended).IsNull();
    }

    /// <summary>
    /// A root the background refused is named as the refusal, with what was refused and
    /// the remedy, whether or not the refusing process is still there and before the task
    /// is read; and a record marked with an end this build does not know is no crash, and
    /// is judged by who wrote it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's 9 a, 2026-10-10</b>, which closed two hazard rows opened on
    /// 2026-10-09: a refused data root was answered with the crash sentence, and an
    /// unknown end read as a crash once its background had gone. A refusal is a setting
    /// to change, so a relay says it at once; an end of any name is a clean end, so the
    /// task says the rest.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> twice: against a finder with no refusal of its own,
    /// which named the task, and against a record reader that read an unknown end as no
    /// end, which named a crash.
    /// </para>
    /// <para>
    /// ⚠️ <b>Judged by who wrote it since later that day, the maintainer's 23.3 b</b>
    /// (previously such an end read as clean and the task said the rest): written by this
    /// same version, a bug; by another, an update or a downgrade, named by that version.
    /// Planted red against the finder as it was, which named the task's state for both.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusedRootIsNamedAsTheRefusalAndALaterBuildsEndIsNoCrash()
    {
        using var data = ScratchDirectory.Create("finder-refused");
        using var install = ScratchDirectory.Create("finder-refused-install");

        var seams = new FinderSeams();
        var settings = Settings(data.Path, install.Path, TaskName, seams, new ManualClock());
        var refusal = new BrowserAI.Hosting.RootRefusal(
            BrowserAI.Hosting.JudgedRoot.Data,
            @"D:\Shared\BrowserAI",
            "it is outside this user's profile, so it is not storage Windows keeps per-user",
            @"give BrowserAI a data root under 'C:\Users\someone'.");

        using var finder = new BackgroundFinder(settings, NullLogger.Instance);

        // Written by this process, which is alive: a refusal all the same, never a start.
        _ = BackgroundRecord.Refused(settings.RecordPath, BackgroundServerRig.Build, "image", refusal, HandWrittenRecord.StartedAt);

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(new BackgroundAbsence.RootRefused(refusal, settings.LogPath));

        // Gone, and nothing about it is a crash.
        HandWrittenRecord.Write(settings.RecordPath, Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess() - 1, ended: nameof(BackgroundEnd.Refused));

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(new BackgroundAbsence.RootRefused(null, settings.LogPath));
        await Assert.That(seams.TasksRead).IsEmpty().Because("a refusal is answered from the record alone");

        // An end another version wrote, gone: an update or a downgrade, named by that
        // version, and never a crash.
        HandWrittenRecord.Write(settings.RecordPath, Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess() - 1, ended: "SomethingALaterBuildWrites", build: "9.9.10-a-later-build");

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(
            new BackgroundAbsence.UnreadableEnd("9.9.10-a-later-build", ThisBuild: false, HandWrittenRecord.StartedAt.AddHours(1), settings.LogPath));
        await Assert.That(BackgroundRecord.Read(settings.RecordPath)!.ExitedAt).IsNull().Because("an end was timed as a crash");

        // The same end written by this same version: a bug, and still no crash.
        HandWrittenRecord.Write(settings.RecordPath, Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess() - 1, ended: "SomethingALaterBuildWrites");

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(
            new BackgroundAbsence.UnreadableEnd(BackgroundServerRig.Build, ThisBuild: true, HandWrittenRecord.StartedAt.AddHours(1), settings.LogPath));
        await Assert.That(seams.TasksRead).IsEmpty().Because("an end the record names is answered from the record alone");
    }

    /// <summary>
    /// With no record, the task is read without changing it and each of its states is
    /// named, with what the Task Scheduler said when it could not be asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D12 b and RESOLUTIONS 9</b>: a relay never runs, registers or enables the task;
    /// its answer names a disabled task and how to enable it, and a missing one.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a finder that named a missing task as one it
    /// could not read.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10 for the other three rows</b>, which the plant above did
    /// not reach, against a finder that named a ready task missing, a disabled one ready
    /// and one it could not read missing: each read <i>"differs at member Task"</i>, the
    /// <c>Ready</c> row expecting <c>Ready</c> but finding <c>Missing</c>, the
    /// <c>Disabled</c> row expecting <c>Disabled</c> but finding <c>Ready</c> and the
    /// <c>Unknown</c> row expecting <c>Unknown</c> but finding <c>Missing</c>, and the
    /// <c>Missing</c> row, whose mapping the plant left alone, stayed green. Then against a
    /// finder that dropped what the Task Scheduler said: the <c>Unknown</c> row alone
    /// went red, <i>"differs at member Detail"</i> with the scheduler's sentence expected
    /// and null found, since no other row carries one.
    /// </para>
    /// </remarks>
    /// <param name="read">What the Task Scheduler says.</param>
    /// <param name="named">What the relay's answer names.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(ScheduledTaskState.Ready, TaskState.Ready)]
    [Arguments(ScheduledTaskState.Disabled, TaskState.Disabled)]
    [Arguments(ScheduledTaskState.Missing, TaskState.Missing)]
    [Arguments(ScheduledTaskState.Unknown, TaskState.Unknown)]
    public async Task TheTaskIsReadWithoutChangingItAndEachOfItsStatesIsNamed(ScheduledTaskState read, TaskState named)
    {
        using var data = ScratchDirectory.Create("finder-task");
        using var install = ScratchDirectory.Create("finder-task-install");

        var detail = read is ScheduledTaskState.Unknown ? "The task scheduler could not read 'it': 0x80070005, Access is denied." : null;
        var seams = new FinderSeams { Task = new ScheduledTaskReading(read, detail) };

        using var finder = new BackgroundFinder(Settings(data.Path, install.Path, TaskName, seams, new ManualClock()), NullLogger.Instance);

        await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(new BackgroundAbsence.NotRunning(named, TaskName, detail));
        await Assert.That(string.Join(" | ", seams.TasksRead)).IsEqualTo(TaskName);
    }

    /// <summary>
    /// A build that is not installed is named, with this binary and its data root, and
    /// nothing is asked of an updater or a task; a crash recorded for it still outranks
    /// that; and an install whose task cannot be named reads no task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D11 a</b>: nothing will ever start a background for a build that is not
    /// installed, so the relay says so at once; it has no updater and no task of its own
    /// to read.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a finder that read the task of a build that
    /// is not installed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABuildThatIsNotInstalledIsNamedAndNothingElseIsAsked()
    {
        using var data = ScratchDirectory.Create("finder-not-installed");
        using var install = ScratchDirectory.Create("finder-not-installed-install");

        var seams = new FinderSeams();
        var settings = Settings(data.Path, installRoot: null, TaskName, seams, new ManualClock());

        using (var finder = new BackgroundFinder(settings, NullLogger.Instance))
        {
            await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(new BackgroundAbsence.NotInstalled(Executable, data.Path));

            // A crash recorded for it is named first.
            HandWrittenRecord.Gone(settings.RecordPath);

            await Assert.That(finder.Explain(lastBackgroundPid: null)).IsTypeOf<BackgroundAbsence.Crashed>();
        }

        await Assert.That(seams.TasksRead).IsEmpty();
        await Assert.That(seams.UpdatersLookedFor).IsEmpty();

        // Installed, with no task name to read: named as such, since round 2 of the texts
        // review (#140, 2026-10-10; previously TaskState.Unknown), because the Start Menu
        // start the Unknown answer asks for runs nothing here. Planted red against the
        // finder that answered Unknown.
        var unnamed = Settings(data.Path, install.Path, taskName: null, seams, new ManualClock());

        using (var finder = new BackgroundFinder(unnamed, NullLogger.Instance))
        {
            await Assert.That(finder.Explain(lastBackgroundPid: null)).IsEqualTo(new BackgroundAbsence.NotRunning(TaskState.Unnamed, string.Empty, null));
        }

        await Assert.That(seams.TasksRead).IsEmpty();
    }

    /// <summary>
    /// A look for a pipe nobody serves answers nothing, and the same look at a pipe this
    /// process serves answers a connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No background is an ordinary state</b>, the engine's to judge: the finder
    /// answers <see langword="null"/>, throws nothing and logs nothing for it.
    /// </para>
    /// <para>
    /// <b>The positive control is a pipe's server end this arm holds</b>, so an answer of
    /// nothing for everything would fail.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a finder that threw when no background
    /// served the name.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALookForAPipeNobodyServesAnswersNothing()
    {
        using var data = ScratchDirectory.Create("finder-connect");

        using var logs = new CapturingLoggerProvider();
        var settings = Settings(data.Path, installRoot: null, TaskName, new FinderSeams(), new ManualClock());

        using var finder = new BackgroundFinder(settings, logs.CreateLogger("BrowserAI.Relay"));
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

        await Assert.That(await finder.TryConnectAsync(hang.Token)).IsNull();
        await Assert.That(logs.Records).IsEmpty();

        // The positive control.
        using var served = NamedPipes.CreateStreamServer(settings.PipeName);

        await using var connected = await finder.TryConnectAsync(hang.Token);

        await Assert.That(connected).IsNotNull();
        await Assert.That(connected!.CanRead && connected.CanWrite).IsTrue();
    }

    /// <summary>The settings of a finder whose every look goes through the arm's seams.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <param name="installRoot">The install root, or <see langword="null"/> for a build that is not installed.</param>
    /// <param name="taskName">The task, or <see langword="null"/>.</param>
    /// <param name="seams">The arm's updater and task.</param>
    /// <param name="clock">The relay's clock.</param>
    /// <returns>The settings, on a pipe of the arm's own.</returns>
    private static BackgroundFinderSettings Settings(string dataRoot, string? installRoot, string? taskName, FinderSeams seams, TimeProvider clock)
    {
        var pipe = PublishedBackground.NewPipeName();

        return new BackgroundFinderSettings
        {
            PipeName = pipe,
            RecordPath = BackgroundRecord.PathFor(dataRoot, pipe),
            InstallRoot = installRoot,
            DataRoot = dataRoot,
            TaskName = taskName,
            Executable = Executable,
            Build = BackgroundServerRig.Build,
            LogPath = Path.Combine(dataRoot, "logs", "browserai.log"),
            Clock = clock,
            ReadTask = seams.ReadTask,
            FindUpdater = seams.FindUpdater,
        };
    }

    /// <summary>The updater and the task a finder looks at, as an arm sets them, with a record of every look.</summary>
    private sealed class FinderSeams
    {
        private int _letGo;

        /// <summary>What the task reads as.</summary>
        public ScheduledTaskReading Task { get; set; } = new(ScheduledTaskState.Ready, null);

        /// <summary>Whether this install's updater runs.</summary>
        public bool UpdaterRuns { get; set; }

        /// <summary>Whether the process list cannot be read.</summary>
        public bool UpdaterUnreadable { get; set; }

        /// <summary>Every task name read, in order.</summary>
        public List<string> TasksRead { get; } = [];

        /// <summary>Every install root an updater was looked for under, in order.</summary>
        public List<string> UpdatersLookedFor { get; } = [];

        /// <summary>How many updaters found were let go.</summary>
        public int UpdatersLetGo => Volatile.Read(ref _letGo);

        /// <summary>The task's reading.</summary>
        /// <param name="name">The task.</param>
        /// <returns>What it reads as.</returns>
        public ScheduledTaskReading ReadTask(string name)
        {
            TasksRead.Add(name);
            return Task;
        }

        /// <summary>The updater, held, when it runs.</summary>
        /// <param name="installRoot">The install root.</param>
        /// <returns>The held updater, or <see langword="null"/>.</returns>
        /// <exception cref="Win32Exception">The process list cannot be read.</exception>
        public IDisposable? FindUpdater(string installRoot)
        {
            UpdatersLookedFor.Add(installRoot);

            if (UpdaterUnreadable)
            {
                throw new Win32Exception(5, "The suite's process list could not be read.");
            }

            return UpdaterRuns ? new HeldUpdater(this) : null;
        }

        private void LetGo() => _ = Interlocked.Increment(ref _letGo);

        private sealed class HeldUpdater(FinderSeams seams) : IDisposable
        {
            public void Dispose() => seams.LetGo();
        }
    }
}
