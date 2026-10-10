// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Relay;

/// <summary>
/// How a relay reaches the one background process of its user, install and data
/// root, and how it learns why there is none.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both members only look.</b> A relay never starts, restarts or registers
/// anything (S and R, decided 2026-10-08; RESOLUTIONS 9): the background is started
/// by the sign-in trigger, by the update step and by a person's Start Menu start,
/// all through the Task Scheduler. What a relay can do about a missing background is
/// hold the call and, when holding cannot help, say why in a sentence.
/// </para>
/// <para>
/// <b>The engine calls both off its own loop</b>, on the thread pool, so a finder
/// may block briefly: the handshake and the tool list are never behind it.
/// </para>
/// </remarks>
internal interface IBackgroundFinder
{
    /// <summary>
    /// Opens the background's pipe, or answers that there is none.
    /// </summary>
    /// <param name="cancellationToken">Ends the attempt when the relay ends.</param>
    /// <returns>
    /// A connected duplex stream carrying newline-delimited JSON-RPC, which the engine
    /// owns from here and disposes; or <see langword="null"/> when no background serves
    /// the pipe.
    /// </returns>
    Task<Stream?> TryConnectAsync(CancellationToken cancellationToken);

    /// <summary>Says why there is no background to talk to.</summary>
    /// <param name="lastBackgroundPid">
    /// The process id the last background gave in its answer to the relay's greeting,
    /// or <see langword="null"/> when this relay never reached one.
    /// </param>
    /// <returns>The reason, read from the crash record, the processes and the task.</returns>
    BackgroundAbsence Explain(int? lastBackgroundPid);
}

/// <summary>Where the user's scheduled task for BrowserAI stands, read without changing it.</summary>
internal enum TaskState
{
    /// <summary>The task is registered and enabled, and nothing is wrong with it.</summary>
    Ready,

    /// <summary>
    /// Somebody disabled the task. BrowserAI leaves it disabled (D12 b) and says how
    /// to enable it.
    /// </summary>
    Disabled,

    /// <summary>
    /// No task of that name is registered. A person's Start Menu start registers it
    /// again (RESOLUTIONS 9).
    /// </summary>
    Missing,

    /// <summary>The Task Scheduler could not be asked, or did not say.</summary>
    Unknown,

    /// <summary>
    /// This build is installed, but its pack id is unknown, so its task has no name: a
    /// Start Menu start runs nothing (6112), and a reinstall registers the task. Added
    /// 2026-10-10 for round 2 of the texts review, #140, where this was
    /// <see cref="Unknown"/> and the answer sent the person to the Start Menu.
    /// </summary>
    Unnamed,
}

/// <summary>
/// Why a relay has no background to talk to, as <see cref="IBackgroundFinder.Explain"/>
/// reads it.
/// </summary>
/// <remarks>
/// <b>Closed: the eight cases below are every case</b>, and the engine decides from
/// each whether waiting can help. Four answer a call at once, because nothing that
/// happens in the next 150 s changes them (D8 a, R, U2, 9 a): a recorded crash, a root
/// the background refused, a build that is not installed, and an update that is
/// installing; and so does an end the background's own version wrote and cannot read,
/// which is a bug (23.3 b). The others hold it. <i>Corrected 2026-10-10 (previously "the six
/// cases" and "Three answer a call at once"), when a refused root became a case of its
/// own; and again later that day (previously "the seven cases" and "The other three
/// hold it"), when an end this build cannot read became one.</i>
/// </remarks>
internal abstract record BackgroundAbsence
{
    private BackgroundAbsence()
    {
    }

    /// <summary>
    /// The background ended without a clean exit, and a crash is recorded. Only the
    /// person's Start Menu start clears it (R).
    /// </summary>
    /// <param name="At">When it ended.</param>
    /// <param name="ExitCode">Its exit code, or <see langword="null"/> when nothing recorded one.</param>
    /// <param name="LogPath">The log the person reads to find out why.</param>
    internal sealed record Crashed(DateTimeOffset At, int? ExitCode, string LogPath) : BackgroundAbsence;

    /// <summary>
    /// The background would not serve out of its data root or its install root, and
    /// recorded what it refused and the remedy: answer at once (9 a, 2026-10-10).
    /// </summary>
    /// <param name="Refusal">What was refused and how to put it right, or <see langword="null"/> when the record carries no parts.</param>
    /// <param name="LogPath">The log the person reads.</param>
    internal sealed record RootRefused(Hosting.RootRefusal? Refusal, string LogPath) : BackgroundAbsence;

    /// <summary>A background process exists but its pipe is not up yet: hold.</summary>
    /// <param name="ProcessId">The process the record names, for the answer at a held call's deadline (2026-10-10, the texts review's #115).</param>
    internal sealed record Starting(int? ProcessId = null) : BackgroundAbsence;

    /// <summary>No background process runs: hold, and if none appears say what the task says.</summary>
    /// <param name="Task">Where the task stands.</param>
    /// <param name="TaskName">The task's name in the Task Scheduler.</param>
    /// <param name="Detail">What the Task Scheduler said, when that adds anything.</param>
    internal sealed record NotRunning(TaskState Task, string TaskName, string? Detail) : BackgroundAbsence;

    /// <summary>
    /// This build is not installed, so nothing will ever start a background for it
    /// (D11): answer at once.
    /// </summary>
    /// <param name="Executable">This binary's own path.</param>
    /// <param name="DataRoot">The data root the relay serves.</param>
    internal sealed record NotInstalled(string Executable, string DataRoot) : BackgroundAbsence;

    /// <summary>This install's updater runs: answer at once with the update sentence (U2).</summary>
    internal sealed record UpdateInstalling : BackgroundAbsence;

    /// <summary>
    /// The background's record names an end this build cannot read, and who wrote the
    /// record decides what that is.
    /// </summary>
    /// <remarks>
    /// <b>The maintainer's 23.3 b, 2026-10-10, in his words verbatim: <i>"23.3 b"</i></b>, of
    /// the directions put to him once it was explained that only a record written by
    /// another version can name an end a build has no name for. Written by this same
    /// version, it is a bug: answered at once, as a crash is, sending the person to the log
    /// and a bug report. Written by another version, which a relay meets only across an
    /// update or a downgrade, since a relay and its background are one binary and an update
    /// ends every relay: held, as a clean end is, and at the deadline answered with that
    /// version's name. <i>Previously, from earlier that day, an end of any name read as the
    /// clean end it is (<see cref="Coordination.BackgroundEnd.Unrecognised"/>), and the relay named the
    /// task's state.</i>
    /// </remarks>
    /// <param name="Build">The version that wrote the record.</param>
    /// <param name="ThisBuild">Whether that is this relay's own version.</param>
    /// <param name="At">When the record says the background ended, or when the relay found it so.</param>
    /// <param name="LogPath">The log the person reads.</param>
    internal sealed record UnreadableEnd(string Build, bool ThisBuild, DateTimeOffset At, string LogPath) : BackgroundAbsence;

    /// <summary>The background ended cleanly, for example at a stop or a sign-out: hold.</summary>
    internal sealed record CleanEnd : BackgroundAbsence;
}
