// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security;

namespace BrowserAI.Runtime;

/// <summary>
/// The Task Scheduler definition for the second sweep trigger: a logon task
/// that looks once and then once more.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two triggers exist because neither can cover the other.</b> BrowserAI's
/// own startup is the primary one — free, no install footprint, and it fires
/// exactly when a stray matters most, because that is the moment something is
/// about to contend for a lock. This task covers what startup cannot: nobody
/// starts a client for a week while a resurrected browser eats memory.
/// </para>
/// <para>
/// <b>Neither trigger tries to win the race with Windows' own app restore, and
/// this one is why the repetition exists.</b> No documented event marks the end
/// of session restore, so instead of guessing, the task simply looks more than
/// once: an immediate pass at logon plus a re-check ten minutes later, expressed
/// as Task Scheduler's <b>native repetition</b> rather than as a sleep inside
/// the process. A sleeping process is a process that can be killed mid-sleep and
/// a process that shows up in a task list; a repetition is state the scheduler
/// keeps.
/// </para>
/// <para>
/// ⚠️ <b><c>InteractiveToken</c> and <c>LeastPrivilege</c> are both load-bearing,
/// and the first is a silent-success trap</b> (race <b>R5</b>).
/// <c>FindWindowExW(HWND_MESSAGE, …)</c> is scoped to a window station and
/// desktop, so a task configured <i>"run whether user is logged on or not"</i>
/// lands in session 0 and sees <b>no message windows at all</b> — it would
/// sweep, find nothing, and report success forever. That is this project's
/// founding failure shape expressed as a task setting, which is why the XML is
/// generated from one place and asserted by the suite rather than authored by
/// hand at install time.
/// </para>
/// <para>
/// <b>Built here, registered at [step 19](../../plan/build-order.md#19-velopack-packaging-and-the-update-lane).</b>
/// There is no install to hook until §G lands, and a product that registered a
/// scheduled task from an ordinary run would be leaving machine state behind
/// every time anybody ran it from a checkout.
/// </para>
/// </remarks>
internal static class LogonSweepTask
{
    /// <summary>The task's folder and name, as it appears in Task Scheduler.</summary>
    public const string TaskPath = @"\BrowserAI\Stray browser sweep";

    /// <summary>
    /// The argument that makes BrowserAI run one sweep and exit rather than
    /// serve stdio.
    /// </summary>
    /// <remarks>
    /// <b>One name, one place in code</b> — this constant is what the task's
    /// action passes and what <c>Program</c> matches, so the task and the
    /// product cannot come to mean different things. The same rule is why the
    /// mutex is <see cref="Sessions.LockScopes.Sweep"/> and not a string the
    /// task carries: a task and a product sweeping under two different names
    /// would both report success while serialising against nothing
    /// (race <b>R4</b>).
    /// </remarks>
    public const string SweepArgument = "--sweep";

    /// <summary>How long after logon the second look happens.</summary>
    public static TimeSpan ReCheckInterval => TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long the repetition window stays open. Long enough for the one
    /// re-check and short enough that it cannot become a third.
    /// </summary>
    public static TimeSpan ReCheckDuration => TimeSpan.FromMinutes(15);

    /// <summary>How long a single pass may run before the scheduler ends it.</summary>
    /// <remarks>
    /// A pass is milliseconds. This is a tripwire for a pass that wedged, not a
    /// budget — and it matters because the alternative to ending a wedged sweep
    /// is a process holding the machine-wide sweep mutex forever, which would
    /// silently disable every other BrowserAI's sweep on the machine.
    /// </remarks>
    public static TimeSpan ExecutionTimeLimit => TimeSpan.FromMinutes(5);

    /// <summary>The task definition, ready for <c>schtasks /Create /XML</c>.</summary>
    /// <remarks>
    /// ⚠️ <b>The principal is a <c>UserId</c> and not a <c>GroupId</c>, and that
    /// was measured rather than chosen.</b> Task Scheduler's schema permits
    /// <c>LogonType</c> only beside a <c>UserId</c>: with a group principal the
    /// element is rejected outright — <c>schtasks /Create</c> answers <i>"The
    /// task XML contains an unexpected node"</i> and names the line. A group
    /// principal would still have run in the user's own session, so the setting
    /// would have been <i>implied</i> by the shape of the file rather than
    /// stated in it — and the whole reason <c>LogonSweepTaskTests</c> asserts on
    /// it is that a sweeper in session 0 finds nothing and reports success
    /// forever.
    /// </remarks>
    /// <param name="executablePath">The absolute path of the BrowserAI binary to run.</param>
    /// <param name="author">Who the task records as its author.</param>
    /// <param name="userId">
    /// Whose session it runs in: a SID, or <c>DOMAIN\user</c>. The installing
    /// user, which for a per-user install is the only one that makes sense.
    /// </param>
    /// <returns>UTF-16 Task Scheduler XML.</returns>
    public static string Xml(string executablePath, string author, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>{SecurityElement.Escape(author)}</Author>
                <Description>Looks for browsers BrowserAI started that no session accounts for, and ends only the ones whose session directory nothing holds. Runs in the signed-in user's own session: a task that ran without a logged-on user would land in session 0, see no browser windows at all, and report success forever.</Description>
                <URI>{SecurityElement.Escape(TaskPath)}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <Repetition>
                    <Interval>{Duration(ReCheckInterval)}</Interval>
                    <Duration>{Duration(ReCheckDuration)}</Duration>
                    <StopAtDurationEnd>true</StopAtDurationEnd>
                  </Repetition>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(userId)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>{Duration(ExecutionTimeLimit)}</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(executablePath)}</Command>
                  <Arguments>{SecurityElement.Escape(SweepArgument)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>An ISO 8601 duration in the shape Task Scheduler accepts.</summary>
    private static string Duration(TimeSpan span) =>
        span.TotalMinutes % 60 is 0 && span.TotalHours >= 1
            ? $"PT{((int)span.TotalHours).ToString(CultureInfo.InvariantCulture)}H"
            : $"PT{((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture)}M";
}
