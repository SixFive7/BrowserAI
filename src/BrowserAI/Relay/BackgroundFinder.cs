// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Relay;

/// <summary>
/// How a relay in production reaches its background, and how it reads why there is
/// none: the pipe, the background's record, this install's updater and the task.
/// </summary>
/// <remarks>
/// <para>
/// <b>It only looks</b> (S a and R, RESOLUTIONS 9): it opens the pipe's client end,
/// reads a file, reads the process table by full image path and asks the Task
/// Scheduler about one task without changing it. Nothing here starts, registers or
/// ends anything.
/// </para>
/// <para>
/// <b>The order of <see cref="Explain"/> is the order in which a reason outranks
/// another.</b> An updater running from this install comes first, because a relay
/// that started during an install is answered with the update sentence whatever else
/// is true (U2). Then the background's record: a root it refused is said as the
/// refusal (9 a, added 2026-10-10); a background that is still alive and has not
/// opened its pipe is starting; one that is gone with no clean end recorded crashed
/// (R). Then a build that is not installed, which nothing will ever start a
/// background for (D11 a). Last, the task, read and never repaired (D12 b).
/// </para>
/// <para>
/// <b>It holds the background it connected to</b>, by a handle opened while the pipe
/// was connected and its pid therefore still the background's, so that when the pipe
/// closes it can read the exit code and write it into the record: the code a crash
/// that wrote nothing still leaves (the design's "Where the reason can be found").
/// </para>
/// </remarks>
internal sealed partial class BackgroundFinder : IBackgroundFinder, IDisposable
{
    /// <summary>
    /// How long one look waits while every instance of the pipe is busy: a hang
    /// detector on a look that the engine repeats anyway.
    /// </summary>
    public static TimeSpan BusyBound { get; } = ProcessBounds.BackgroundBusyBound;

    /// <summary>
    /// How long <see cref="Explain"/> waits for a background whose pipe has closed to
    /// finish exiting, so its exit code can be read: a hang detector, since a pipe
    /// closes as its process ends.
    /// </summary>
    public static TimeSpan ExitBound { get; } = ProcessBounds.BackgroundExitBound;

    private readonly BackgroundFinderSettings _settings;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    private HeldProcess? _background;

    /// <summary>Creates the finder.</summary>
    /// <param name="settings">What it looks for, and where.</param>
    /// <param name="logger">Where it reports what it could not read.</param>
    public BackgroundFinder(BackgroundFinderSettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Stream?> TryConnectAsync(CancellationToken cancellationToken) =>
        await Task.Run(() => Connect(cancellationToken), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public BackgroundAbsence Explain(int? lastBackgroundPid)
    {
        RecordWhatTheLastBackgroundLeft(lastBackgroundPid);

        if (_settings.InstallRoot is { } installRoot && UpdaterRuns(installRoot))
        {
            return new BackgroundAbsence.UpdateInstalling();
        }

        var record = BackgroundRecord.Read(_settings.RecordPath);

        if (record is { Ended: BackgroundEnd.Refused })
        {
            // 9 a, 2026-10-10: a refused root is a setting to change, never a crash, and
            // every start meets it again until it is changed, so it is said at once.
            return new BackgroundAbsence.RootRefused(record.Refusal, _settings.LogPath);
        }

        if (record is { Ended: BackgroundEnd.Unrecognised })
        {
            // 23.3 b, 2026-10-10: an end this build cannot read is judged by who wrote the
            // record. This same version writing it is a bug; another version is an update
            // or a downgrade. Corrected that day (previously such an end read as a clean
            // one and fell through to the task below).
            return new BackgroundAbsence.UnreadableEnd(
                record.Build,
                string.Equals(record.Build, _settings.Build, StringComparison.Ordinal),
                record.EndedAt ?? _settings.Clock.GetUtcNow(),
                _settings.LogPath);
        }

        if (record is { Ended: null })
        {
            if (ProcessLiveness.IsAlive(record.ProcessId, record.CreatedFileTime))
            {
                return new BackgroundAbsence.Starting(record.ProcessId);
            }

            // Gone, with no clean end recorded: a crash under R. A relay that held it
            // has written when it went and its exit code; with neither, the first
            // relay that finds it gone writes the moment it found that, so every relay
            // names the same time.
            var at = record.ExitedAt ?? FoundGone(record);
            return new BackgroundAbsence.Crashed(at, record.ExitCode, _settings.LogPath);
        }

        if (_settings.InstallRoot is null)
        {
            return new BackgroundAbsence.NotInstalled(_settings.Executable, _settings.DataRoot);
        }

        // Installed, with no pack id to name its task by: a Start Menu start runs nothing
        // here (PersonStart's 6112), and a reinstall registers the task. Corrected
        // 2026-10-10, round 2 of the texts review, #140 (previously TaskState.Unknown,
        // whose answer sent the person to the Start Menu).
        if (_settings.TaskName is not { Length: > 0 } taskName)
        {
            return new BackgroundAbsence.NotRunning(TaskState.Unnamed, string.Empty, null);
        }

        var reading = _settings.ReadTask(taskName);

        return new BackgroundAbsence.NotRunning(
            reading.State switch
            {
                ScheduledTaskState.Ready => TaskState.Ready,
                ScheduledTaskState.Disabled => TaskState.Disabled,
                ScheduledTaskState.Missing => TaskState.Missing,
                _ => TaskState.Unknown,
            },
            taskName,
            reading.Detail);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _background?.Dispose();
            _background = null;
        }
    }

    private FileStream? Connect(CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)BusyBound.TotalMilliseconds;

        while (!cancellationToken.IsCancellationRequested)
        {
            var handle = NamedPipes.OpenClient(_settings.PipeName, out var error);

            try
            {
                if (!handle.IsInvalid)
                {
                    Hold(NamedPipes.ServerProcessIdOf(handle));

                    var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);

                    // The stream owns the handle from here.
                    handle = null;

                    return stream;
                }
            }
            finally
            {
                handle?.Dispose();
            }

            var left = deadline - Environment.TickCount64;

            if (error is not NamedPipes.ErrorPipeBusy || left <= 0)
            {
                // No background serves the name, which is an ordinary state and the
                // engine's to judge, not a failure to log on every look.
                return null;
            }

            _ = NamedPipes.WaitForFreeInstance(_settings.PipeName, (uint)left);
        }

        return null;
    }

    /// <summary>Holds the process serving the pipe, while the connection guarantees its pid is still its own.</summary>
    /// <param name="processId">The server's pid, as Windows reads it off the pipe.</param>
    private void Hold(int? processId)
    {
        var held = processId is { } pid ? BrowserProcesses.HoldConnected(pid) : null;

        lock (_gate)
        {
            _background?.Dispose();
            _background = held;
        }
    }

    /// <summary>Writes the exit of the background this relay held into its record, once it has gone.</summary>
    /// <param name="lastBackgroundPid">The pid the background answered the greeting with.</param>
    private void RecordWhatTheLastBackgroundLeft(int? lastBackgroundPid)
    {
        HeldProcess? held;

        lock (_gate)
        {
            held = _background;
        }

        if (held is null || lastBackgroundPid != held.ProcessId)
        {
            return;
        }

        if (!held.WaitOne(ExitBound) || held.ExitCodeOnceEnded() is not { } code)
        {
            return;
        }

        try
        {
            _ = BackgroundRecord.Exited(_settings.RecordPath, held.ProcessId, held.CreatedFileTime, code, _settings.Clock.GetUtcNow());
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            FinderLog.RecordNotWritten(_logger, _settings.RecordPath, failure);
        }

        lock (_gate)
        {
            if (ReferenceEquals(_background, held))
            {
                _background = null;
            }
        }

        held.Dispose();
    }

    /// <summary>The moment a crashed background was first found gone, written into its record.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The moment.</returns>
    private DateTimeOffset FoundGone(BackgroundRecordState record)
    {
        var now = _settings.Clock.GetUtcNow();

        try
        {
            _ = BackgroundRecord.Exited(_settings.RecordPath, record.ProcessId, record.CreatedFileTime, exitCode: null, now);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            FinderLog.RecordNotWritten(_logger, _settings.RecordPath, failure);
        }

        return now;
    }

    private bool UpdaterRuns(string installRoot)
    {
        try
        {
            using var updater = _settings.FindUpdater(installRoot);
            return updater is not null;
        }
        catch (Win32Exception failure)
        {
            FinderLog.UpdaterNotChecked(_logger, failure);
            return false;
        }
    }
}

/// <summary>What a <see cref="BackgroundFinder"/> looks for, and where.</summary>
internal sealed record BackgroundFinderSettings
{
    /// <summary>The background's pipe.</summary>
    public required string PipeName { get; init; }

    /// <summary>The background's record (<see cref="BackgroundRecord.PathFor"/>).</summary>
    public required string RecordPath { get; init; }

    /// <summary>The install root, or <see langword="null"/> for a build that is not installed.</summary>
    public required string? InstallRoot { get; init; }

    /// <summary>The data root the relay was registered for.</summary>
    public required string DataRoot { get; init; }

    /// <summary>The task the background runs as, or <see langword="null"/> when its name cannot be told.</summary>
    public required string? TaskName { get; init; }

    /// <summary>This binary's own path, for the sentence a build that is not installed gets.</summary>
    public required string Executable { get; init; }

    /// <summary>This build's version, which a record's writer is compared with (23.3 b).</summary>
    public required string Build { get; init; }

    /// <summary>The process log a person reads.</summary>
    public required string LogPath { get; init; }

    /// <summary>The clock a record's times are read from.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Reads the task without changing it: the suite's seam.</summary>
    public Func<string, ScheduledTaskReading> ReadTask { get; init; } = ScheduledTasks.StateOf;

    /// <summary>Finds this install's updater by its full image path: the suite's seam.</summary>
    public Func<string, IDisposable?> FindUpdater { get; init; } =
        static installRoot => BrowserProcesses.FirstRunning(Path.Combine(installRoot, Program.UpdaterFileName));
}

/// <summary>Source-generated log messages for <see cref="BackgroundFinder"/>.</summary>
/// <remarks>
/// <b>From 23, after <c>RelayLog</c>'s 22, since 2026-10-10</b>, round 2 of the texts
/// review, #195 (previously 1 and 2): a finder writes under the relay's logger, so it
/// shares <c>RelayLog</c>'s category, and <c>Relay[1]</c> and <c>Relay[2]</c> each named
/// two lines. <c>ProcessLogTests.EveryLineOfTheBackgroundsAndTheRelaysCategoriesHasAnEventIdOfItsOwn</c>
/// holds the category to one line per id.
/// </remarks>
internal static partial class FinderLog
{
    [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "Whether this install's updater runs could not be read, so the relay goes on as if it does not.")]
    public static partial void UpdaterNotChecked(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 24, Level = LogLevel.Warning, Message = "The background's record at {Path} could not be written, so another relay may name another time for the same crash.")]
    public static partial void RecordNotWritten(ILogger logger, string path, Exception failure);
}
