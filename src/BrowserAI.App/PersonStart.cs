// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
        if (settings.InstallRoot is null || settings.TaskName is not { Length: > 0 } taskName)
        {
            // D11 a: nothing starts a background for a build that is not installed.
            PersonStartLog.NotInstalled(logger, settings.DataRoot);
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
            PersonStartLog.Registered(logger, registered.Change, registered.Detail);

            if (registered.Change is not TaskChange.Registered)
            {
                return (PersonStartOutcome.NotShown, null);
            }

            run = settings.Tasks.Run(taskName, settings.StartedBy);
        }

        if (run.Change is not TaskChange.Started)
        {
            // D12 b: a disabled task stays disabled. This log line carries what the
            // Task Scheduler said, its HRESULT included; the sentence that says how to
            // enable the task is the relay's (RelayErrors.NotRunning), which every call
            // meets. Corrected 2026-10-09 (previously "the sentence says how to enable
            // it"), found by lane ARCH's helper T1.
            PersonStartLog.TaskNotRun(logger, run.Change, run.Detail);
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

        PersonStartLog.NoPipeInTime(logger, taskName, settings.StartBound);
        return (PersonStartOutcome.NotShown, null);
    }

    private static void ClearARecordedCrash(PersonStartSettings settings, ILogger logger)
    {
        if (BackgroundRecord.Read(settings.RecordPath) is not { Ended: null } record
            || ProcessLiveness.IsAlive(record.ProcessId, record.CreatedFileTime))
        {
            return;
        }

        // R: the person's start is what clears a recorded crash, and this is it.
        PersonStartLog.CrashCleared(logger, record.ProcessId, record.ExitCode);
        BackgroundRecord.Clear(settings.RecordPath);
    }

    private static bool EndTheHungBackground(PersonStartSettings settings, int? processId, string? why, ILogger logger)
    {
        PersonStartLog.Hung(logger, processId ?? 0, why ?? "no reason given");

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

        // The person's action, and only on a pid whose creation time and image were
        // both verified just now against the record and the install root (the
        // repository's rule: never by image name).
        using (var target = BrowserProcesses.OpenToEnd(pid, record.CreatedFileTime, root, out var refusal))
        {
            if (target is null || !target.TryTerminate(out refusal))
            {
                PersonStartLog.HungNotEnded(logger, pid, refusal ?? "it could not be opened");
                return false;
            }
        }

        BackgroundRecord.Clear(settings.RecordPath);
        PersonStartLog.HungEnded(logger, pid);
        return true;
    }
}

/// <summary>Source-generated log messages for <see cref="PersonStart"/>.</summary>
internal static partial class PersonStartLog
{
    [LoggerMessage(EventId = 6101, Level = LogLevel.Warning, Message = "The background refused to open a page: {Why}")]
    public static partial void Refused(ILogger logger, string why);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Warning, Message = "No background runs for this build, and a build that is not installed has nothing that starts one. Start one with: BrowserAI.exe --background --data-root \"{DataRoot}\"")]
    public static partial void NotInstalled(ILogger logger, string dataRoot);

    [LoggerMessage(EventId = 6103, Level = LogLevel.Error, Message = "The task '{Task}' is missing and the definition the install wrote could not be read, so it was not registered again. Reinstalling BrowserAI registers it.")]
    public static partial void NoDefinition(ILogger logger, string task);

    [LoggerMessage(EventId = 6104, Level = LogLevel.Information, Message = "The task was missing and is registered again: {Change}. {Detail}")]
    public static partial void Registered(ILogger logger, TaskChange change, string detail);

    [LoggerMessage(EventId = 6105, Level = LogLevel.Error, Message = "The Task Scheduler did not start BrowserAI's background: {Change}. {Detail}")]
    public static partial void TaskNotRun(ILogger logger, TaskChange change, string detail);

    [LoggerMessage(EventId = 6106, Level = LogLevel.Error, Message = "The task '{Task}' was started, and no background opened its pipe within {Bound}. The log of the background, and the task's last run result, say why.")]
    public static partial void NoPipeInTime(ILogger logger, string task, TimeSpan bound);

    [LoggerMessage(EventId = 6107, Level = LogLevel.Warning, Message = "The background's record says pid {ProcessId} crashed (exit code {ExitCode}); this start clears the record and starts a new background.")]
    public static partial void CrashCleared(ILogger logger, int processId, int? exitCode);

    [LoggerMessage(EventId = 6108, Level = LogLevel.Warning, Message = "The background (pid {ProcessId}) took the connection and did not answer: {Why}. This start judges it hung.")]
    public static partial void Hung(ILogger logger, int processId, string why);

    [LoggerMessage(EventId = 6109, Level = LogLevel.Error, Message = "The hung background (pid {ProcessId}) was not ended: {Why}.")]
    public static partial void HungNotEnded(ILogger logger, int processId, string why);

    [LoggerMessage(EventId = 6110, Level = LogLevel.Warning, Message = "The hung background (pid {ProcessId}) was ended, its record cleared, and a new one is started through the task.")]
    public static partial void HungEnded(ILogger logger, int processId);

    [LoggerMessage(EventId = 6111, Level = LogLevel.Error, Message = "The background the task started took the connection and did not answer either: {Why}")]
    public static partial void NewBackgroundHung(ILogger logger, string why);
}
