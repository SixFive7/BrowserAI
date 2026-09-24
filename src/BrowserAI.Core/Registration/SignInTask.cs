// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Security;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

/// <summary>What the hooks did to the per-user logon task.</summary>
/// <param name="Name">The task's name, or <see langword="null"/> when no name could be composed.</param>
/// <param name="Change">What happened.</param>
/// <param name="Detail">A sentence for the installer's log.</param>
internal sealed record SignInTaskReport(string? Name, TaskChange Change, string Detail);

/// <summary>
/// The per-user logon task: what it is called, what it runs, and what each
/// installer hook does to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q282 a, decided 2026-09-24 by the maintainer, in his words: <i>"Q282 a"</i>.</b>
/// The install and update hooks register one task per install root and the
/// uninstall hook removes it. Its trigger is the installing user's logon, with no
/// delay: a non-elevated token may register a trigger scoped to the user and is
/// refused one for any user, <c>0x80070005</c>, measured 2026-09-24
/// ([kb](../../../kb/windows/processes.md#a-logon-task-registers-without-elevation-when-its-trigger-names-the-user)).
/// It runs <c>&lt;install root&gt;\current\BrowserAI.exe --sign-in $(Arg0)</c>: at
/// sign-in the placeholder stays as it is written, and a blocked server runs the
/// same task on demand with <c>--coordinate</c> in its place (Q283 a), measured
/// 2026-09-25.
/// </para>
/// <para>
/// <b>Named for the pack id and the install root</b>: <c>BrowserAI.app sign-in</c>
/// and the root's key, the same key the census gate and the coordinator's pipe
/// end in. The suite's test pack therefore registers <c>BrowserAI.app.test sign-in
/// ...</c> under a scratch root's key and can never touch the real install's task,
/// and two roots of one pack id have two tasks.
/// </para>
/// <para>
/// <b>A registration that fails never fails a hook</b>, as the maintainer's brief
/// for this step has it; the report goes to BrowserAI's log and to the installer's.
/// The cost of a missing task is a staged update that waits for the next blocked
/// server to start the coordinator, and the sign-in step that would have applied
/// it.
/// </para>
/// </remarks>
internal static class SignInTask
{
    /// <summary>The action's arguments, with the placeholder a started-on-demand run fills.</summary>
    public const string Arguments = CoordinatorProtocol.SignInArgument + " $(Arg0)";

    /// <summary>The task's name for one pack id and one install root.</summary>
    /// <param name="appId">The Velopack pack id: <c>BrowserAI.app</c>, or the suite's <c>BrowserAI.app.test</c>.</param>
    /// <param name="installRoot">The install root.</param>
    /// <returns>The name, in the scheduler's root folder.</returns>
    public static string NameFor(string appId, string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        return $"{appId} sign-in {LiveInstances.RootKeyFor(installRoot)}";
    }

    /// <summary>The task's Task Scheduler 1.2 definition.</summary>
    /// <remarks>
    /// <para>
    /// <b>Every setting that differs from the scheduler's default is written, and why
    /// is here.</b> <c>MultipleInstancesPolicy</c> is <c>Parallel</c>, where the
    /// default ignores a start while an instance runs: the coordinator's pipe decides
    /// which start is the coordinator, and an ignored start could be the only one a
    /// blocked server makes. <c>DisallowStartIfOnBatteries</c> is false, where the
    /// default would skip the sign-in step on a laptop on battery.
    /// <c>ExecutionTimeLimit</c> is <c>PT0S</c>, no limit, where the default ends the
    /// process after three days: the coordinator waits for every server to exit, and
    /// that can take longer. <c>Priority</c> is 5, a normal-priority process, where
    /// the default 7 is below normal: the coordinator opens the configuration window
    /// when a person asks for it.
    /// </para>
    /// <para>
    /// <b>The user is named by SID</b> in the trigger and the principal, the SID of
    /// the token the hook runs with; the scheduler stores the trigger's as
    /// <c>DOMAIN\user</c>, measured 2026-09-25.
    /// </para>
    /// </remarks>
    /// <param name="appImage">The configuration app the task starts: <c>&lt;install root&gt;\current\BrowserAI.exe</c>.</param>
    /// <param name="userSid">The installing user's SID.</param>
    /// <param name="installRoot">The install root, for the description.</param>
    /// <returns>The XML.</returns>
    public static string DefinitionFor(string appImage, string userSid, string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var image = SecurityElement.Escape(appImage);
        var sid = SecurityElement.Escape(userSid);
        var root = SecurityElement.Escape(installRoot);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>BrowserAI</Author>
                <Description>Starts BrowserAI when you sign in, to install an update it has already downloaded if nothing else runs from {root}. A BrowserAI server whose update is waiting starts it too.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{sid}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{sid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <StartWhenAvailable>false</StartWhenAvailable>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{image}</Command>
                  <Arguments>{Arguments}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>What one installer hook does to the task: register it, or remove it.</summary>
    /// <param name="intent">Which hook is running.</param>
    /// <param name="target">The install the hook runs in.</param>
    /// <param name="appId">The pack id, or <see langword="null"/> when the locator gave none.</param>
    /// <param name="tasks">The scheduler.</param>
    /// <param name="logger">Where the outcome is recorded.</param>
    /// <returns>What happened.</returns>
    public static SignInTaskReport Apply(
        RegistrationIntent intent,
        RegistrationTarget target,
        string? appId,
        ILogonTasks tasks,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(logger);

        SignInTaskReport report;

        if (appId is not { Length: > 0 })
        {
            report = new SignInTaskReport(
                null,
                TaskChange.Failed,
                "The pack id is unknown, so the sign-in task has no name and was not changed. BrowserAI works without it; a staged update then waits for a server to start the coordinator.");
        }
        else
        {
            var name = NameFor(appId, target.InstallRoot);

            try
            {
                var outcome = intent is RegistrationIntent.Uninstall
                    ? tasks.Remove(name)
                    : tasks.Register(
                        name,
                        DefinitionFor(
                            Path.Combine(target.InstallRoot, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName),
                            NamedPipes.CurrentUserSid(),
                            target.InstallRoot));

                report = new SignInTaskReport(name, outcome.Change, outcome.Detail);
            }
#pragma warning disable CA1031 // A hook must not fail on the task: a sentence in the installer's log, and the install goes on.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                report = new SignInTaskReport(name, TaskChange.Failed, $"The sign-in task '{name}' was not changed: {failure.Message}");
            }
        }

        SignInTaskLog.Changed(logger, report.Change, report.Detail);

        return report;
    }
}

/// <summary>Source-generated log messages for the sign-in task.</summary>
internal static partial class SignInTaskLog
{
    /// <summary>What a hook did to the task.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="change">What changed.</param>
    /// <param name="detail">The sentence.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Sign-in task: {Change}. {Detail}")]
    public static partial void Changed(ILogger logger, TaskChange change, string detail);
}
