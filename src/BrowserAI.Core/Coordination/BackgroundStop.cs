// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;

namespace BrowserAI.Coordination;

/// <summary>How asking the background to stop came out.</summary>
internal enum BackgroundStopOutcome
{
    /// <summary>No background serves the pipe: nothing to stop.</summary>
    NoneRunning,

    /// <summary>It was asked, and it ended within the bound.</summary>
    Ended,

    /// <summary>It was asked, and it had not ended when the bound ran out.</summary>
    StillRunning,

    /// <summary>It took the connection and refused, or did not answer.</summary>
    NotAsked,
}

/// <summary>
/// The uninstall hook's stop: the background is asked through its pipe to close every
/// session cleanly and end, and waited for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never the task's End command</b> (settled by the root session, 2026-10-08, from
/// the step-0 research): End closes the process's top-level windows and terminates it
/// about a second later, exit code <c>0x42B</c>, which ends every browser through the
/// jobs without a clean close. The pipe's <c>stop</c> closes each session within the
/// minute's cap first.
/// </para>
/// <para>
/// <b>Bounded below Velopack's 60 s for the uninstall hook</b> (kb), the same as one
/// close's cap, so a slow close can still be cut short by Velopack's kill pass after
/// the hook; the hook goes on to remove the task and the registrations either way.
/// </para>
/// </remarks>
internal static class BackgroundStop
{
    /// <summary>
    /// How long the uninstall hook waits for the background to end once asked: within
    /// Velopack's 60 s for the hook, with room for the rest of the hook after it.
    /// </summary>
    public static TimeSpan Bound { get; } = TimeSpan.FromSeconds(45);

    /// <summary>Asks the background to stop and waits for it to end.</summary>
    /// <param name="pipeName">The background's pipe.</param>
    /// <param name="recordPath">Its record, which names the process to wait for.</param>
    /// <param name="bound">How long to wait.</param>
    /// <returns>How it came out, and a sentence for the log.</returns>
    public static (BackgroundStopOutcome Outcome, string Detail) AskAndWait(string pipeName, string recordPath, TimeSpan bound)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordPath);

        // Held before it is asked, so the process waited for is the one that was
        // asked and never a later one that took its pid.
        using var held = BackgroundRecord.Read(recordPath) is { Ended: null } record
            ? BrowserProcesses.Hold(record.ProcessId, record.CreatedFileTime)
            : null;

        var answer = BackgroundClient.Ask(pipeName, BackgroundPipe.Stop, null, bound);

        return answer.Outcome switch
        {
            BackgroundAnswerOutcome.NoBackground =>
                (BackgroundStopOutcome.NoneRunning, "No BrowserAI background was running, so none was stopped."),

            BackgroundAnswerOutcome.Answered when held is null =>
                (BackgroundStopOutcome.Ended, "BrowserAI's background was asked to stop; its record named no process to wait for."),

            BackgroundAnswerOutcome.Answered when held!.WaitOne(bound) =>
                (BackgroundStopOutcome.Ended, $"BrowserAI's background, pid {held.ProcessId}, closed its sessions and ended."),

            BackgroundAnswerOutcome.Answered =>
                (BackgroundStopOutcome.StillRunning, $"BrowserAI's background, pid {held!.ProcessId}, was asked to stop and was still closing its sessions when the hook moved on."),

            _ => (BackgroundStopOutcome.NotAsked, $"BrowserAI's background could not be asked to stop: {answer.Sentence}"),
        };
    }
}
