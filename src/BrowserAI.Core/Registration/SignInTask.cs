// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Security;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BrowserAI.Hosting;
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
internal static partial class SignInTask
{
    /// <summary>
    /// The argument the action starts the one executable with: the background
    /// (S a, 2026-10-08).
    /// </summary>
    public const string BackgroundArgument = "--background";

    /// <summary>The argument that carries the data root the install was made for.</summary>
    public const string DataRootArgument = "--data-root";

    /// <summary>The argument that carries who asked the task for a start, filled from <c>$(Arg0)</c>.</summary>
    public const string StartedByArgument = "--started-by";

    /// <summary>
    /// The action's arguments with no setting the installer named: the background,
    /// and the placeholder a started-on-demand run fills.
    /// </summary>
    /// <remarks>
    /// ⚠️ <i>Corrected 2026-10-08 (previously <c>--sign-in $(Arg0)</c>, the
    /// coordinator's)</i>: the task starts the resident background, and the
    /// placeholder stays literal at sign-in, which is how the background tells the
    /// trigger's start from a person's.
    /// </remarks>
    public const string Arguments = BackgroundArgument + " " + StartedByArgument + " $(Arg0)";

    /// <summary>The file beside the install that keeps the definition the hooks registered, or tried to.</summary>
    /// <remarks>
    /// <para>
    /// <b>A person's start registers a missing task again from it</b> (RESOLUTIONS 9):
    /// the arguments the install hook read out of the installer's environment exist
    /// nowhere else once the installer has gone, and a definition composed without
    /// them would point an install that takes its updates from a folder (H2 a) back
    /// at GitHub. Under the install root and outside <c>current\</c>, so an update
    /// keeps it until the update hook writes it again.
    /// </para>
    /// <para>
    /// ⚠️ <i>Corrected 2026-10-09 by addition (previously "The file beside the install
    /// that keeps the definition the hooks registered.")</i>: the hooks write it whether
    /// or not the scheduler took the task, because a task the hook could not register
    /// is the one a person's start finds missing (hazard row 331).
    /// </para>
    /// </remarks>
    public const string SavedDefinitionFileName = "background-task.xml";

    /// <summary>The task's name for one pack id and one install root.</summary>
    /// <param name="appId">The Velopack pack id: <c>BrowserAI.app</c>, or the suite's <c>BrowserAI.app.test</c>.</param>
    /// <param name="installRoot">The install root.</param>
    /// <returns>The name, in the scheduler's root folder.</returns>
    public static string NameFor(string appId, string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        return $"{appId} sign-in {RootKey.For(installRoot)}";
    }

    /// <summary>The task's Task Scheduler 1.2 definition.</summary>
    /// <remarks>
    /// <para>
    /// <b>Every setting that differs from the scheduler's default is written, and why
    /// is here.</b> <c>MultipleInstancesPolicy</c> is <c>IgnoreNew</c>, the scheduler's
    /// default, written out: the task never starts a second background while one runs
    /// (S a). Step 0 measured it on 2026-10-08 with stand-ins: twenty requests at one
    /// moment started one instance, 10 of 10 rounds, and none started while one ran.
    /// ⚠️ <i>Corrected 2026-10-08 (previously <c>Parallel</c>, "the coordinator's pipe
    /// decides which start is the coordinator, and an ignored start could be the only
    /// one a blocked server makes")</i>: no server runs the task since S a, and the
    /// background's pipe admits one background besides.
    /// <c>DisallowStartIfOnBatteries</c> is false, where the
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
    /// <param name="arguments">The action's arguments: <see cref="ArgumentsFor"/>, or <see cref="Arguments"/> when the installer named no setting.</param>
    /// <returns>The XML.</returns>
    public static string DefinitionFor(string appImage, string userSid, string installRoot, string arguments = Arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var image = SecurityElement.Escape(appImage);
        var sid = SecurityElement.Escape(userSid);
        var root = SecurityElement.Escape(installRoot);
        var action = SecurityElement.Escape(arguments);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>BrowserAI</Author>
                <Description>Starts BrowserAI, installed in {root}, when you sign in, when you start it from the Start Menu and after an update. BrowserAI runs in the background from then on and holds its browser sessions, its page and its updates. Disabling this task stops BrowserAI until it is enabled again; deleting it stops BrowserAI until BrowserAI is started from the Start Menu, which registers it again.</Description>
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
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
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
                  <Arguments>{action}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>The action's arguments for the settings the installer named.</summary>
    /// <remarks>
    /// <b>The design's settings rule</b>: a running BrowserAI reads no
    /// <c>BROWSERAI_</c> variable, so the install and update hooks read the
    /// installer's environment once and write what they find here, as plain arguments
    /// and never inside <c>$(Arg0)</c>. A path or a source is quoted, with any trailing
    /// separator taken off, because a backslash before the closing quote would escape
    /// it on the way to the process's own command line.
    /// </remarks>
    /// <param name="dataRoot">The data root the installer named, or <see langword="null"/> for the default.</param>
    /// <param name="updateSource">The update source the installer named, or <see langword="null"/> for the production feed.</param>
    /// <returns>The arguments.</returns>
    public static string ArgumentsFor(string? dataRoot, string? updateSource)
    {
        var arguments = BackgroundArgument;

        if (dataRoot is { Length: > 0 })
        {
            arguments += $" {DataRootArgument} \"{WithoutTrailingSeparators(dataRoot)}\"";
        }

        if (updateSource is { Length: > 0 })
        {
            arguments += $" {UpdateSource.Argument} \"{updateSource.TrimEnd('\\', '/')}\"";
        }

        return arguments + " " + StartedByArgument + " $(Arg0)";
    }

    /// <summary>A path with every trailing separator taken off, down to its root and never into it.</summary>
    /// <remarks>
    /// <b>Every one, and not the last alone</b>: <c>C:\data\\</c> trimmed once still ends
    /// in a backslash, which escapes the closing quote and carries the rest of the action
    /// into the data root, <c>--started-by</c> included. <i>Corrected 2026-10-09
    /// (previously one <see cref="Path.TrimEndingDirectorySeparator(string)"/>).</i> A
    /// root keeps its own separator, because <c>C:</c> alone means the current directory
    /// on that drive.
    /// </remarks>
    /// <param name="path">The path.</param>
    /// <returns>The path, ending in no separator unless it is a root.</returns>
    private static string WithoutTrailingSeparators(string path)
    {
        var trimmed = path;

        while (Path.TrimEndingDirectorySeparator(trimmed) is var shorter && shorter.Length < trimmed.Length)
        {
            trimmed = shorter;
        }

        return trimmed;
    }

    /// <summary>The data root a definition's action names, or <see langword="null"/> for the default.</summary>
    /// <remarks>
    /// <b>How a start that was handed no <c>--data-root</c> finds its install's</b>
    /// (step 5 of the one-binary build, 2026-10-08): a person's start from the Start
    /// Menu and Velopack's start after an update carry no arguments of ours, and the
    /// background's pipe is named for the data root, so they read the root the hooks
    /// wrote into the task (<see cref="ArgumentsFor"/>) from the definition the hooks
    /// saved beside the install. A file the hooks wrote, never a variable. A root that
    /// is not fully qualified is the default, as in the hooks' read.
    /// </remarks>
    /// <param name="definition">The task's XML, or <see langword="null"/>.</param>
    /// <returns>The data root.</returns>
    public static string? DataRootIn(string? definition) =>
        QuotedIn(definition, SavedDataRoot()) is { } root && Path.IsPathFullyQualified(root) ? root : null;

    /// <summary>The update source a definition's action names, or <see langword="null"/> for the production feed.</summary>
    /// <remarks>
    /// <b>H2 a's folder survives an update this way</b>: the update hook runs under
    /// <c>Update.exe</c>, which the background started and whose environment the Task
    /// Scheduler built, so it carries no <c>BROWSERAI_UPDATE_FEED</c>, and the source
    /// the install hook wrote is read back from the definition it saved.
    /// </remarks>
    /// <param name="definition">The task's XML, or <see langword="null"/>.</param>
    /// <returns>The update source.</returns>
    public static string? UpdateSourceIn(string? definition) => QuotedIn(definition, SavedUpdateSource());

    /// <summary>One quoted value of a definition's action, as <see cref="ArgumentsFor"/> writes it.</summary>
    /// <param name="definition">The task's XML, or <see langword="null"/>.</param>
    /// <param name="argument">The argument and its value.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static string? QuotedIn(string? definition, Regex argument)
    {
        if (definition is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var arguments = XDocument.Parse(definition).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Arguments")?.Value;

            return arguments is not null && argument.Match(arguments) is { Success: true } named
                ? named.Groups["value"].Value
                : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The data root in an action's arguments, quoted as <see cref="ArgumentsFor"/> writes it.</summary>
    [GeneratedRegex(@"(^|\s)--data-root\s+""(?<value>[^""]+)""")]
    private static partial Regex SavedDataRoot();

    /// <summary>The update source in an action's arguments, quoted as <see cref="ArgumentsFor"/> writes it.</summary>
    [GeneratedRegex(@"(^|\s)--update-source\s+""(?<value>[^""]+)""")]
    private static partial Regex SavedUpdateSource();

    /// <summary>
    /// The definition the hooks last wrote for an install, whether or not the scheduler
    /// took it, or <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// ⚠️ <i>Corrected 2026-10-09 by addition (previously "The definition the hooks last
    /// registered for an install, or null when there is none.")</i>: what the hooks last
    /// wrote, registered or not. A person's start registers a missing task from it, and
    /// <see cref="InstallerSettings.Read"/> reads the data root and the update source
    /// back from it for a hook that runs without the installer's environment.
    /// </remarks>
    /// <param name="installRoot">The install root.</param>
    /// <returns>The XML.</returns>
    public static string? SavedDefinition(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        try
        {
            return File.ReadAllText(Path.Combine(installRoot, SavedDefinitionFileName));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>What one installer hook does to the task: register it, or remove it.</summary>
    /// <param name="intent">Which hook is running.</param>
    /// <param name="target">The install the hook runs in.</param>
    /// <param name="appId">The pack id, or <see langword="null"/> when the locator gave none.</param>
    /// <param name="tasks">The scheduler.</param>
    /// <param name="logger">Where the outcome is recorded.</param>
    /// <param name="arguments">The action's arguments (<see cref="ArgumentsFor"/>).</param>
    /// <returns>What happened.</returns>
    public static SignInTaskReport Apply(
        RegistrationIntent intent,
        RegistrationTarget target,
        string? appId,
        ILogonTasks tasks,
        ILogger logger,
        string arguments = Arguments)
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
                "The pack id is unknown, so the task that starts BrowserAI has no name and was not changed. BrowserAI then starts only once it is installed again.");
        }
        else
        {
            var name = NameFor(appId, target.InstallRoot);
            var saved = Path.Combine(target.InstallRoot, SavedDefinitionFileName);

            try
            {
                TaskReport outcome;

                if (intent is RegistrationIntent.Uninstall)
                {
                    outcome = tasks.Remove(name);
                    File.Delete(saved);
                }
                else
                {
                    var definition = DefinitionFor(
                        Path.Combine(target.InstallRoot, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName),
                        NamedPipes.CurrentUserSid(),
                        target.InstallRoot,
                        arguments);

                    outcome = tasks.Register(name, definition);

                    // ⚠️ SAVED WHATEVER THE SCHEDULER SAID -- 2026-10-09. Corrected
                    // (previously written only once the scheduler had registered the
                    // task): a person's start registers a missing task from this file,
                    // and a task the hook could not register is the one that will be
                    // missing, so the file was absent in exactly the case it is for
                    // (hazard row 331).
                    File.WriteAllText(saved, definition);
                }

                report = new SignInTaskReport(name, outcome.Change, outcome.Detail);
            }
#pragma warning disable CA1031 // A hook must not fail on the task: a sentence in the installer's log, and the install goes on.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                report = new SignInTaskReport(name, TaskChange.Failed, $"The task '{name}' was not changed: {failure.Message}");
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
