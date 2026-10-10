// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App;

/// <summary>How a person's start came out.</summary>
internal enum PersonStartOutcome
{
    /// <summary>A background handed out a tab's address.</summary>
    Shown,

    /// <summary>No tab: the log says why.</summary>
    NotShown,
}

/// <summary>What a person's start needs to reach or start the background.</summary>
internal sealed record PersonStartSettings
{
    /// <summary>The background's pipe.</summary>
    public required string PipeName { get; init; }

    /// <summary>The background's record.</summary>
    public required string RecordPath { get; init; }

    /// <summary>The install root, or <see langword="null"/> for a build that is not installed.</summary>
    public required string? InstallRoot { get; init; }

    /// <summary>The data root.</summary>
    public required string DataRoot { get; init; }

    /// <summary>This build's own full path, which the line for a build that is not installed names.</summary>
    /// <remarks>
    /// <i>Added 2026-10-10</i>, the texts review's #166: a bare <c>BrowserAI.exe</c> is found
    /// through the PATH, where the hooks put the installed build, whose background serves a
    /// pipe this build's relays do not use. The relay's own sentence names the same path
    /// (<c>RelayErrors.NotInstalled</c>).
    /// </remarks>
    public required string Executable { get; init; }

    /// <summary>The task the background runs as, or <see langword="null"/> when its name cannot be told.</summary>
    public required string? TaskName { get; init; }

    /// <summary>The task's definition, for a task that is missing.</summary>
    public required Func<string?> Definition { get; init; }

    /// <summary>The Task Scheduler: the suite's seam.</summary>
    public required ILogonTasks Tasks { get; init; }

    /// <summary>How long a background has to answer <c>show</c> before it is judged hung.</summary>
    public TimeSpan HandOutBound { get; init; } = CoordinatorProtocol.HandOutBound;

    /// <summary>How long a background the task started has to open its pipe.</summary>
    public TimeSpan StartBound { get; init; } = PersonStart.DefaultStartBound;

    /// <summary>What the task's <c>$(Arg0)</c> carries for this start, which the background's first tab reads as its occasion.</summary>
    public string StartedBy { get; init; } = PersonStart.StartedByPerson;

    /// <summary>
    /// Ends a hung background by its pid, given the creation time its record names and the
    /// install root its image must lie under, and answers why not when it did not end it:
    /// the suite's seam, as <see cref="Tasks"/> is.
    /// </summary>
    /// <remarks>
    /// <i>Added 2026-10-10</i>, the texts review's #173: what Windows says about a process it
    /// would not open or end cannot be provoked in the suite's own process, whose own pid
    /// always opens, so an arm hands that reason in. The default is the one that ends
    /// anything, <see cref="PersonStart.EndByItsVerifiedIdentity"/>.
    /// </remarks>
    public Func<int, long, string, string?> EndProcess { get; init; } = PersonStart.EndByItsVerifiedIdentity;
}

/// <summary>
/// A person's start: the Start Menu, <c>Setup.exe</c> after a non-silent install, a
/// double-click, a toast's button. It opens a tab on the background's page, and it is
/// the only thing that ever restarts BrowserAI.
/// </summary>
/// <remarks>
/// <para>
/// <b>D13 a, the maintainer's words of 2026-10-08, verbatim: <i>"d13 a"</i></b>,
/// amended by R the same day: a person's start finds the background's pipe and hands
/// over <c>show</c>; with no background it asks the Task Scheduler to run the task,
/// registering it first if it is missing, waits for the pipe, and does the same. It
/// never becomes the background itself, so the background has one way into existence.
/// </para>
/// <para>
/// <b>R and RESOLUTIONS 10</b>: with a crash recorded, it clears the record and starts
/// a background through the task. A background that takes the connection and does not
/// answer <c>show</c> within <see cref="PersonStartSettings.HandOutBound"/> is judged
/// hung: it is ended by its pid after that pid's image path is verified to lie under
/// the install root, the record is cleared, and a new one is started through the task.
/// That is the person's own action, never an automatic one.
/// </para>
/// </remarks>
internal static partial class PersonStart
{
    /// <summary>
    /// How long a person's start waits for a background the task started to open its
    /// pipe: a hang detector over a start measured at a few hundred milliseconds.
    /// </summary>
    public static TimeSpan DefaultStartBound { get; } = ProcessBounds.PersonStartBound;

    /// <summary>How often the pipe is looked for while it waits.</summary>
    public static TimeSpan LookInterval { get; } = ProcessBounds.PersonStartLookInterval;

    /// <summary>The value the task's <c>$(Arg0)</c> carries for a person's start.</summary>
    public const string StartedByPerson = "person";

    /// <summary>The value it carries for the start <c>Setup.exe</c> makes after a non-silent install.</summary>
    public const string StartedByTheInstaller = "first-run";

    /// <summary>The value it carries for the start Velopack's restart makes after an update.</summary>
    public const string StartedAfterAnUpdate = "after-update";

    /// <summary>Opens a tab on the background's page, starting the background first when none runs.</summary>
    /// <param name="settings">Where the background is.</param>
    /// <param name="page">The page asked for, or <see langword="null"/> for the status page.</param>
    /// <param name="logger">Where it reports.</param>
    /// <returns>The address, or <see langword="null"/> with the outcome when none was handed out.</returns>
    public static (PersonStartOutcome Outcome, string? Address) Show(PersonStartSettings settings, string? page, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        var parameters = page is { Length: > 0 } named ? $"{{\"page\":\"{JsonEncodedText.Encode(named)}\"}}" : null;
        var answer = BackgroundClient.Ask(settings.PipeName, BackgroundPipe.Show, parameters, settings.HandOutBound);

        switch (answer.Outcome)
        {
            case BackgroundAnswerOutcome.Answered:
                return (PersonStartOutcome.Shown, answer.ResultMember("address"));

            case BackgroundAnswerOutcome.Refused:
                PersonStartLog.Refused(logger, answer.Sentence ?? "no reason given");
                return (PersonStartOutcome.NotShown, null);

            case BackgroundAnswerOutcome.NoAnswer:
                if (!EndTheHungBackground(settings, answer.ServerProcessId, answer.Sentence, logger))
                {
                    return (PersonStartOutcome.NotShown, null);
                }

                break;

            default:
                ClearARecordedCrash(settings, logger);
                break;
        }

        return StartThroughTheTask(settings, parameters, logger);
    }

    private static (PersonStartOutcome Outcome, string? Address) StartThroughTheTask(PersonStartSettings settings, string? parameters, ILogger logger)
    {
        if (settings.InstallRoot is not { } installRoot)
        {
            // D11 a: nothing starts a background for a build that is not installed.
            PersonStartLog.NotInstalled(logger, settings.Executable, settings.DataRoot);
            return (PersonStartOutcome.NotShown, null);
        }

        if (settings.TaskName is not { Length: > 0 } taskName)
        {
            // Installed, with no pack id to name its task by. Corrected 2026-10-10
            // (previously this took the not-installed line above, which told the person
            // the build is not installed), the texts review's #166.
            PersonStartLog.NoTaskName(logger, installRoot);
            return (PersonStartOutcome.NotShown, null);
        }

        var run = settings.Tasks.Run(taskName, settings.StartedBy);

        if (run.Change is TaskChange.NotRegistered)
        {
            // RESOLUTIONS 9: a person's start registers a missing task again, from the
            // definition the install hook wrote, and only then runs it.
            var definition = settings.Definition();

            if (definition is null)
            {
                PersonStartLog.NoDefinition(logger, taskName);
                return (PersonStartOutcome.NotShown, null);
            }

            var registered = settings.Tasks.Register(taskName, definition);

            // Said once the outcome is known. Corrected 2026-10-10 (previously 6104 was
            // written first, whatever the scheduler answered, so a refusal read "is
            // registered again: Failed"), the texts review's #168.
            if (registered.Change is not TaskChange.Registered)
            {
                PersonStartLog.NotRegisteredAgain(logger, taskName, registered.Detail);
                return (PersonStartOutcome.NotShown, null);
            }

            PersonStartLog.Registered(logger, taskName);
            run = settings.Tasks.Run(taskName, settings.StartedBy);
        }

        if (run.Change is not TaskChange.Started)
        {
            // D12 b: a disabled task stays disabled. This log line carries what the
            // Task Scheduler said, its HRESULT included; the sentence that says how to
            // enable the task is the relay's (RelayErrors.NotRunning), which every call
            // meets. Corrected 2026-10-09 (previously "the sentence says how to enable
            // it"), found by a reading of the code on 2026-10-09.
            PersonStartLog.TaskNotRun(logger, run.Detail);
            return (PersonStartOutcome.NotShown, null);
        }

        var deadline = Environment.TickCount64 + (long)settings.StartBound.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            var answer = BackgroundClient.Ask(settings.PipeName, BackgroundPipe.Show, parameters, settings.HandOutBound);

            switch (answer.Outcome)
            {
                case BackgroundAnswerOutcome.Answered:
                    return (PersonStartOutcome.Shown, answer.ResultMember("address"));

                case BackgroundAnswerOutcome.Refused:
                    PersonStartLog.Refused(logger, answer.Sentence ?? "no reason given");
                    return (PersonStartOutcome.NotShown, null);

                case BackgroundAnswerOutcome.NoAnswer:
                    PersonStartLog.NewBackgroundHung(logger, answer.Sentence ?? "no reason given");
                    return (PersonStartOutcome.NotShown, null);
            }

            Thread.Sleep(LookInterval);
        }

        // In seconds, as a person reads a bound. Corrected 2026-10-10 (previously the
        // TimeSpan itself, which a log line prints as 00:00:30), the texts review's #170.
        PersonStartLog.NoPipeInTime(logger, taskName, settings.StartBound.TotalSeconds);
        return (PersonStartOutcome.NotShown, null);
    }

    private static void ClearARecordedCrash(PersonStartSettings settings, ILogger logger)
    {
        if (BackgroundRecord.Read(settings.RecordPath) is not { Ended: null } record
            || ProcessLiveness.IsAlive(record.ProcessId, record.CreatedFileTime))
        {
            return;
        }

        // R: the person's start is what clears a recorded crash, and this is it. A code
        // nothing recorded reads "unknown", as the relay's crash sentence has it.
        // Corrected 2026-10-10 (previously the code itself, which a log line prints as
        // "(null)" when there is none), the texts review's #171.
        PersonStartLog.CrashCleared(
            logger,
            record.ProcessId,
            record.ExitCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "unknown");
        BackgroundRecord.Clear(settings.RecordPath);
    }

    /// <summary>A reason to put before a full stop of the line's own: its own full stop taken off.</summary>
    /// <remarks>
    /// <i>Added 2026-10-10</i>, the texts review's #172 and #173: 6108 and 6109 write a full
    /// stop after the reason, and most reasons already end in one, so those lines ended in
    /// two. Every reason <see cref="BackgroundClient"/> gives for a background that did not
    /// answer ends in one, and so do three of those <see cref="BrowserProcesses"/> gives for
    /// a process it would not open or end.
    /// </remarks>
    /// <param name="why">The reason.</param>
    /// <returns>The reason, ending in no full stop.</returns>
    private static string Clause(string why) => why.TrimEnd().TrimEnd('.');

    private static bool EndTheHungBackground(PersonStartSettings settings, int? processId, string? why, ILogger logger)
    {
        PersonStartLog.Hung(logger, processId ?? 0, Clause(why ?? "no reason given"));

        if (processId is not { } pid
            || BackgroundRecord.Read(settings.RecordPath) is not { } record
            || record.ProcessId != pid)
        {
            PersonStartLog.HungNotEnded(logger, processId ?? 0, "its pid is not the one the background's record names, so it is not known to be BrowserAI's");
            return false;
        }

        if (settings.InstallRoot is not { } root)
        {
            PersonStartLog.HungNotEnded(logger, pid, "this build is not installed, so there is no install root its image could be verified against");
            return false;
        }

        if (settings.EndProcess(pid, record.CreatedFileTime, root) is { } refusal)
        {
            PersonStartLog.HungNotEnded(logger, pid, Clause(refusal));
            return false;
        }

        BackgroundRecord.Clear(settings.RecordPath);
        PersonStartLog.HungEnded(logger, pid);
        return true;
    }

    /// <summary>
    /// Ends a process by its pid once its creation time and its image under the install
    /// root are verified: <see cref="PersonStartSettings.EndProcess"/>'s default.
    /// </summary>
    /// <param name="processId">The pid the record names.</param>
    /// <param name="createdFileTime">The creation time the record names.</param>
    /// <param name="installRoot">The install root its image must lie under.</param>
    /// <returns><see langword="null"/> when it was ended; otherwise why not.</returns>
    internal static string? EndByItsVerifiedIdentity(int processId, long createdFileTime, string installRoot)
    {
        // The person's action, and only on a pid whose creation time and image were
        // both verified just now against the record and the install root (the
        // repository's rule: never by image name).
        using var target = BrowserProcesses.OpenToEnd(processId, createdFileTime, installRoot, out var refusal);

        // Both say why on every failure, so there is no reason to make up. Corrected
        // 2026-10-10, round 2 of the texts review, #205 (previously a fallback reason,
        // "it could not be opened", which no failure gave).
        return target is not null && target.TryTerminate(out refusal)
            ? null
            : refusal ?? throw new System.Diagnostics.UnreachableException("BrowserProcesses.OpenToEnd and TryTerminate gave no reason for a failure, which each gives on every one.");
    }
}

/// <summary>Source-generated log messages for <see cref="PersonStart"/>.</summary>
internal static partial class PersonStartLog
{
    [LoggerMessage(EventId = 6101, Level = LogLevel.Warning, Message = "The background refused to open a page: {Why}")]
    public static partial void Refused(ILogger logger, string why);

    // Corrected 2026-10-10 (previously "Start one with: BrowserAI.exe --background
    // --data-root ...", and written for an installed build with no task name too), the
    // texts review's #166: this build's own full path, quoted, as the relay's sentence
    // names it, and only for a build with no install root. The other build is 6112.
    [LoggerMessage(EventId = 6102, Level = LogLevel.Warning, Message = "No background runs for this build, and a build that is not installed has nothing that starts one. Start one with: \"{Executable}\" --background --data-root \"{DataRoot}\"")]
    public static partial void NotInstalled(ILogger logger, string executable, string dataRoot);

    // The texts polish, 2026-10-10, pages #209 and #210 (previously "the definition the
    // install wrote" and "from the definition the install saved"): the saved copy, by the
    // name of the file to look for, as the task's own description calls it.
    [LoggerMessage(EventId = 6103, Level = LogLevel.Error, Message = "The task '{Task}' is missing and its saved copy, background-task.xml, could not be read, so it was not registered again. Reinstalling BrowserAI registers it.")]
    public static partial void NoDefinition(ILogger logger, string task);

    // Corrected 2026-10-10 (previously "The task was missing and is registered again:
    // {Change}. {Detail}", written whatever the scheduler answered), the texts review's
    // #168: written only once the task is registered. A refusal is 6113.
    [LoggerMessage(EventId = 6104, Level = LogLevel.Information, Message = "The task '{Task}' was missing and is registered again from its saved copy, background-task.xml.")]
    public static partial void Registered(ILogger logger, string task);

    // Corrected 2026-10-10, round 2 of the texts review, #201 (previously "The Task
    // Scheduler did not start BrowserAI's background: {Change}. {Detail}", with the
    // TaskChange member's name, NotRegistered or Failed): the detail is the Task
    // Scheduler's answer in words, and it is all the line needs.
    [LoggerMessage(EventId = 6105, Level = LogLevel.Error, Message = "The Task Scheduler did not start BrowserAI's background. {Detail}")]
    public static partial void TaskNotRun(ILogger logger, string detail);

    // Corrected 2026-10-10 (previously "within {Bound}", a TimeSpan, which prints as
    // 00:00:30), the texts review's #170.
    //
    // The texts polish, 2026-10-10, page #212 (previously "The log of the background, and
    // the task's last run result, say why."): the background writes into this same log.
    [LoggerMessage(EventId = 6106, Level = LogLevel.Error, Message = "The task '{Task}' was started, and no background opened its pipe within {Seconds} seconds. The background's own lines in this log, and the task's last run result in Task Scheduler, say why.")]
    public static partial void NoPipeInTime(ILogger logger, string task, double seconds);

    // Corrected 2026-10-10 (previously an int?, which prints as "(null)" when the record
    // carries no code), the texts review's #171: "unknown" then.
    [LoggerMessage(EventId = 6107, Level = LogLevel.Warning, Message = "The background's record says pid {ProcessId} crashed (exit code {ExitCode}); this start clears the record and starts a new background.")]
    public static partial void CrashCleared(ILogger logger, int processId, string exitCode);

    // The reason arrives without a full stop of its own since 2026-10-10 (PersonStart.Clause),
    // the texts review's #172: every reason the client gives ended in one, so the line
    // ended in two.
    //
    // The texts polish, 2026-10-10, pages #214 and #217 (previously "took the connection
    // and did not answer: {Why}. This start judges it hung." and "... did not answer
    // either: {Why}"): the reason says how it did not answer.
    [LoggerMessage(EventId = 6108, Level = LogLevel.Warning, Message = "The background (pid {ProcessId}) took the connection and is judged hung: {Why}.")]
    public static partial void Hung(ILogger logger, int processId, string why);

    // As 6108, the texts review's #173: three of the reasons BrowserProcesses gives for a
    // process it would not open or end ended in a full stop.
    [LoggerMessage(EventId = 6109, Level = LogLevel.Error, Message = "The hung background (pid {ProcessId}) was not ended: {Why}.")]
    public static partial void HungNotEnded(ILogger logger, int processId, string why);

    [LoggerMessage(EventId = 6110, Level = LogLevel.Warning, Message = "The hung background (pid {ProcessId}) was ended, its record cleared, and a new one is started through the task.")]
    public static partial void HungEnded(ILogger logger, int processId);

    [LoggerMessage(EventId = 6111, Level = LogLevel.Error, Message = "The background the task started took the connection and is judged hung too: {Why}")]
    public static partial void NewBackgroundHung(ILogger logger, string why);

    // Added 2026-10-10, the texts review's #166: an installed build whose pack id is
    // unknown, which took 6102's line until then. The hooks' own sentence for the same
    // case is SignInTask.Apply's.
    //
    // The texts polish, 2026-10-10, page #222 (previously "Installing BrowserAI again
    // registers the task."): the word every broken-install text uses.
    [LoggerMessage(EventId = 6112, Level = LogLevel.Error, Message = "No background runs for this build, which is installed in '{InstallRoot}'. The pack id is unknown, so the task that starts BrowserAI has no name and was not run. Reinstalling BrowserAI registers the task.")]
    public static partial void NoTaskName(ILogger logger, string installRoot);

    // Added 2026-10-10, the texts review's #168: the Task Scheduler refused the missing
    // task, or did not answer, and the detail is its own sentence.
    [LoggerMessage(EventId = 6113, Level = LogLevel.Error, Message = "The task '{Task}' was missing and was not registered again, so no background was started. {Detail}")]
    public static partial void NotRegisteredAgain(ILogger logger, string task, string detail);
}
