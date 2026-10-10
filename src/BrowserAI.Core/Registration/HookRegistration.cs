// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

/// <summary>One client's pass, carrying enough to name it without it.</summary>
/// <remarks>
/// <b>The key and the display name are copied and not a reference to the
/// <see cref="RegistrationClient"/>.</b> This travels into the record on disk,
/// where a reader has neither the type nor the process that produced it, and a
/// record that named a client only by object identity would name it nowhere.
/// </remarks>
/// <param name="Key">The client's stable key, as the record on disk spells it.</param>
/// <param name="DisplayName">What to call it in a sentence a person reads.</param>
/// <param name="Report">What that client's pass concluded.</param>
internal sealed record ClientRegistration(string Key, string DisplayName, RegistrationReport Report);

/// <summary>Everything one Velopack lifecycle hook did.</summary>
/// <remarks>
/// <para>
/// <b>Two answers instead of one, since 2026-09-15.</b> A hook used to do
/// exactly one thing -- point a client at this build, or unpoint it -- and the
/// uninstall hook now also decides what becomes of the data root. The second
/// answer is returned and not only logged because the log it would be
/// written into is inside the directory it is about: on a removal that file is
/// gone, so the outcome goes to <c>VelopackStartup</c>, which mirrors it into
/// the installer's own log.
/// </para>
/// <para>
/// ⚠️ <b>A LIST SINCE 2026-09-24, AND THE PLURAL IS THE POINT (Q258 step 2).</b>
/// One hook registers every client in
/// <see cref="RegistrationClient.All"/> -- each with its own ownership check and
/// its own entry -- so a failure against one is legible without guessing which.
/// There is deliberately no single <c>Registration</c> property any more: a
/// caller that wants one client must name it, because the one that read
/// <i>the</i> registration silently meant Claude Code's and would have gone on
/// meaning it after a second client existed.
/// </para>
/// </remarks>
/// <param name="Registrations">What each client's pass concluded, in <see cref="RegistrationClient.All"/> order.</param>
/// <param name="Disposal">
/// What was decided about the data root, or <see langword="null"/> when the hook
/// was not an uninstall or could not get far enough to ask.
/// </param>
/// <param name="PathEntry">
/// What became of the install's folder on the user's PATH (Q294 b), or
/// <see langword="null"/> when the hook could not get far enough to know which
/// folder that is.
/// </param>
/// <param name="SignInTask">
/// What became of the per-user logon task (Q282 a), or <see langword="null"/> when
/// the hook could not get far enough to know which install it runs in.
/// </param>
internal sealed record HookOutcome(
    IReadOnlyList<ClientRegistration> Registrations,
    DataRootDisposalReport? Disposal,
    UserPathReport? PathEntry = null,
    SignInTaskReport? SignInTask = null)
{
    /// <summary>
    /// Whether every client's pass did what was asked of it.
    /// </summary>
    /// <remarks>
    /// <b>The one reduction across clients, and it is a boolean for a reason.</b>
    /// An absent client is <see cref="RegistrationStatus.ClientNotFound"/> and
    /// therefore still <i>what was asked for</i> -- a machine with no Codex on it
    /// is a machine BrowserAI installs correctly on. What makes this false is a
    /// refusal or a failure, which are the two outcomes a person has to act on.
    /// </remarks>
    public bool IsWhatWasAskedFor => Registrations.All(pass => pass.Report.IsWhatWasAskedFor);

    /// <summary>One client's pass, by key.</summary>
    /// <param name="key">The client's <see cref="RegistrationClient.Key"/>.</param>
    /// <returns>What that client's pass concluded.</returns>
    /// <exception cref="InvalidOperationException">No client with that key ran.</exception>
    public RegistrationReport For(string key) =>
        Registrations.Single(pass => string.Equals(pass.Key, key, StringComparison.Ordinal)).Report;
}

/// <summary>
/// The whole body of a Velopack lifecycle hook: open a log, register or
/// unregister, write down what happened, and get out.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>A hook opens its own log instead of using the process's.</b>
/// <c>Program.Main</c> buffers Velopack's own records and replays them once the
/// install root is known -- which works for an ordinary start and cannot work for
/// a hook, because <c>VelopackApp.Run()</c> <b>exits the process</b> when it has
/// served one. Anything a hook merely buffers is discarded at that exit. So the
/// destination is established here, inside the hook, and every record is on disk
/// before the callback returns.
/// </para>
/// <para>
/// ⚠️ <b>The log and the record go to the DATA root, and the image path decides
/// only what gets registered -- corrected 2026-09-15 (previously "the install
/// root is derived from the running image ... so the path that is registered and
/// the directory the record lands in cannot disagree").</b> They cannot
/// disagree, and they were both wrong: the install root is the directory
/// <c>Setup.exe</c> renames aside and deletes and uninstall empties, so a
/// registration record written there is destroyed by exactly the events somebody
/// would read it after. The data root is
/// <see cref="Hosting.LocalAppDataPaths.Default"/> -- reachable from a hook
/// without a locator, which is the property that made it possible to stop
/// deriving one. The image path is still what
/// <see cref="RegistrationTarget"/> judges, because what a client is pointed at
/// is a fact about the binary.
/// </para>
/// <para>
/// <b>Consulting <c>VelopackLocator</c> inside a fast-exit callback would add an
/// unproven dependency to the one code path that must not have any</b>, and it
/// is not needed for either question: the registered path comes from
/// <see cref="Environment.ProcessPath"/> and the data root is a constant.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> A hook that throws breaks the installer; a hook
/// that swallows leaves a product nobody can reach and nothing to say so. The
/// third option is what this is: catch, log, record, return.
/// </para>
/// </remarks>
internal static class HookRegistration
{
    /// <summary>
    /// Runs one pass on behalf of a hook, against the real client and the real
    /// filesystem.
    /// </summary>
    /// <param name="intent">Which hook is asking.</param>
    /// <param name="version">The version Velopack handed the callback.</param>
    /// <returns>What happened.</returns>
    public static HookOutcome Run(RegistrationIntent intent, string version) =>
        Run(
            intent,
            version,
            InstallerSettings.Read(RegistrationTarget.TryResolve(Environment.ProcessPath, out var target, out _) ? target!.InstallRoot : null));

    /// <summary>Runs one pass with what the installer's environment named, read once.</summary>
    /// <param name="intent">Which hook is asking.</param>
    /// <param name="version">The version Velopack handed the callback.</param>
    /// <param name="overridden">The hooks' one read of the installer's environment, the override every start is then handed.</param>
    /// <returns>What happened.</returns>
    private static HookOutcome Run(RegistrationIntent intent, string version, InstallerSettings overridden) =>
        Run(
            intent,
            version,
            Environment.ProcessPath,
            RegisterAiTool.Beside(Environment.ProcessPath),
            new LocalAppDataPaths(overridden.DataRoot),
            RegistryUserPathStore.User,
            ScheduledTasks.Instance,
            InstallLocation.AppId,
            DataRootDisposal.IsSilent(ProcessLiveness.ParentCommandLine()),
            message => UserPrompt.AskYesNo(DataRootDisposal.PromptTitle, message),
            settings: overridden,
            toastActivator: ToastActivatorStep.Apply);

    /// <summary>
    /// Runs one pass against a supplied image path and client seam.
    /// </summary>
    /// <param name="intent">Which hook is asking.</param>
    /// <param name="version">The version Velopack handed the callback.</param>
    /// <param name="imagePath">The running image, or what stands in for it.</param>
    /// <param name="tool">
    /// RegisterAI, which registers with every client in one run. The hook passes the
    /// one beside the running image; the suite passes a fake. <i>Until 2026-10-03 this
    /// was the seam over starting each client's own command line.</i>
    /// </param>
    /// <param name="paths">
    /// Where the log and the record go, and -- on an uninstall -- what is offered
    /// for deletion. <b>Required, not defaulted</b>: a test that forgot
    /// it would write into the developer's own data root and offer to delete it.
    /// </param>
    /// <param name="userPath">
    /// Where the user's PATH is, which the install and update hooks put this
    /// install's folder on and the uninstall hook takes it off (Q294 b).
    /// <b>Required, not defaulted</b>, for the reason <paramref name="paths"/> is:
    /// a test that forgot it would write the developer's own PATH.
    /// </param>
    /// <param name="tasks">
    /// The task scheduler, where the install and update hooks register the per-user
    /// logon task and the uninstall hook removes it (Q282 a). <b>Required, not
    /// defaulted</b>, for the same reason: a test that forgot it would register a
    /// task in the developer's own scheduler.
    /// </param>
    /// <param name="appId">
    /// The pack id the task is named for; the hook passes the locator's, and the
    /// suite the test pack's.
    /// </param>
    /// <param name="silent">
    /// Whether the uninstall that started this hook was unattended. Ignored by
    /// every other intent.
    /// </param>
    /// <param name="ask">The question seam. See <see cref="DataRootDisposal.Choose"/>.</param>
    /// <param name="clients">
    /// Which clients to register with, or <see langword="null"/> for
    /// <see cref="RegistrationClient.All"/>.
    /// </param>
    /// <param name="settings">
    /// What the installer's environment named, read once by the caller
    /// (<see cref="InstallerSettings.Read"/>), or <see langword="null"/> for nothing.
    /// </param>
    /// <param name="toastActivator">
    /// Registers or removes the toasts' activator for an install root and says what it
    /// did, or <see langword="null"/> to leave it alone: the suite's in-process hooks
    /// write no class into the user's registry.
    /// </param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// <para>
    /// The overload the suite drives. It is the same body: the only things a
    /// test replaces are the ones that need an installed Velopack layout,
    /// somebody else's executable, and a human at the screen.
    /// </para>
    /// <para>
    /// ⚠️ <b>The client set is one of those things, added 2026-09-24.</b> It
    /// defaults to every client, and the hook itself never passes one. <i>Since
    /// 2026-10-03 a missing client is RegisterAI's answer and the fake gives it, so
    /// the set only narrows which clients a pass is about.</i>
    /// </para>
    /// </remarks>
    public static HookOutcome Run(
        RegistrationIntent intent,
        string version,
        string? imagePath,
        IRegisterAi tool,
        IAppPaths paths,
        IUserPathStore userPath,
        ILogonTasks tasks,
        string? appId,
        bool silent = true,
        Func<string, bool>? ask = null,
        IReadOnlyList<RegistrationClient>? clients = null,
        InstallerSettings? settings = null,
        Func<RegistrationIntent, string, string>? toastActivator = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(userPath);
        ArgumentNullException.ThrowIfNull(tasks);

        var who = clients ?? RegistrationClient.All;

        try
        {
            IReadOnlyList<ClientRegistration> passes = [];
            DataRootDisposalReport? disposal = null;
            UserPathReport? pathEntry = null;
            SignInTaskReport? signIn = null;

            // The log's own scope, closed before anything is deleted: the file is
            // inside the data root, and a handle this process still holds would
            // be the one node a removal could not remove.
            using (var log = ProcessLog.Create(paths, LogLevel.Information))
            {
                var logger = log.Factory.CreateLogger("BrowserAI.Registration");

                RegistrationHookLog.HookRunning(logger, intent, version, imagePath ?? "<unknown>");

                // ⚠️ THE BACKGROUND STOPS FIRST, AND THROUGH ITS PIPE -- 2026-10-08, S a.
                // An uninstall asks a running background to close every session and
                // end, never through the task's End, which would end every browser
                // with no clean close, and waits for it within its bound before the
                // task, the registrations and the PATH entry go.
                if (intent is RegistrationIntent.Uninstall && RegistrationTarget.TryResolve(imagePath, out var installing, out _))
                {
                    var pipe = BackgroundPipe.NameFor(installing!.InstallRoot, paths.RootAppDir);
                    var (_, detail) = BackgroundStop.AskAndWait(pipe, BackgroundRecord.PathFor(paths.RootAppDir, pipe), BackgroundStop.Bound);

                    RegistrationHookLog.BackgroundStopped(logger, detail);
                }

                // ⚠️ EVERY CLIENT, AND ONE ANSWER EACH -- 2026-09-24, Q258 step 2.
                // Each carries its own ownership read, so a foreign entry in one
                // client's configuration refuses that client and says nothing
                // about the other. Since 2026-10-03 the pass is one run of
                // RegisterAI for all of them, bounded as a whole, and
                // McpRegistrar.Apply still never throws.
                // ⚠️ THE INSTALLER'S SETTINGS, AS ARGUMENTS -- 2026-10-08. A running
                // BrowserAI reads no BROWSERAI_ variable, so the data root the
                // installer named travels in the registration's own arguments, and
                // the relay a client starts finds the background for that root.
                var installed = settings ?? InstallerSettings.None;

                passes = McpRegistrar.Apply(who, intent, imagePath, tool, logger, commandArguments: installed.RelayArguments);

                WriteRecord(paths.RootAppDir, passes, intent, version, logger);

                // ⚠️ THE INSTALL'S FOLDER ON THE USER'S PATH -- Q294 b, 2026-09-24. A
                // Codex project entry names `BrowserAI.exe --mcp` alone (since
                // 2026-10-08, previously `BrowserAI.Server.exe`), because
                // Codex expands no variable in a command, and Codex finds it through
                // the PATH it hands the server. The install and update hooks put this
                // install's own `current\` there and the uninstall hook takes exactly
                // that entry off; another install root's entry is never touched. After
                // the record, so a failure here cannot cost the registration its
                // account; before the data root's question, which is the one step that
                // may wait for a human.
                pathEntry = ChangeThePath(intent, imagePath, userPath, logger);

                // ⚠️ THE PER-USER LOGON TASK -- Q282 a, 2026-09-25. The install and
                // update hooks register one task per install root, named for the
                // pack id and the root, and the uninstall hook removes it. After the
                // PATH for the same reason the PATH is after the record: a failure
                // here costs this step its own sentence and nothing before it, and
                // SignInTask.Apply never throws.
                signIn = RegistrationTarget.TryResolve(imagePath, out var target, out _)
                    ? SignInTask.Apply(intent, target!, appId, tasks, logger, SignInTask.ArgumentsFor(installed.DataRoot, installed.UpdateSource))
                    : null;

                // ⚠️ THE TOASTS' ACTIVATOR -- T, decided 2026-10-08. A click on one of
                // BrowserAI's toasts reaches a COM class registered under the user's own
                // classes for this install's application id, which the install and
                // update hooks register and the uninstall hook removes. It never fails
                // a hook: a toast with no activator shows and opens nothing.
                if (target is not null && toastActivator is not null)
                {
                    var activator = toastActivator(intent, target.InstallRoot);
                    RegistrationHookLog.ToastActivator(logger, activator);
                }

                // ⚠️ LAST, AND ONLY ON AN UNINSTALL. It is the only part of a
                // hook that may wait for a human, so everything an uninstall must
                // not skip is already on disk above it: a hook killed at its
                // 60-second budget for want of an answer has still unregistered
                // the client and still written its record, and the data root is
                // kept by the same default every other unanswered case takes.
                if (intent is RegistrationIntent.Uninstall)
                {
                    disposal = DataRootDisposal.Choose(paths, silent, ask ?? (_ => false), logger);
                }
            }

            return new HookOutcome(passes, disposal is null ? null : DataRootDisposal.Remove(disposal), pathEntry, signIn);
        }
#pragma warning disable CA1031 // The outermost boundary of a fast-exit callback. Nothing may escape into the installer, including a failure to open a log.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // The log itself could not be opened, so there is nowhere to say
            // this. The record is the only remaining channel and it is tried
            // anyway -- a silent install is the one outcome this whole mechanism
            // exists to prevent.
            //
            // ⚠️ THE SAME SENTENCE FOR EVERY CLIENT, and that is honest rather
            // than lazy: this path is reached before any client was asked, so
            // nothing is known about any of them individually.
            var failed = who
                .Select(client => new ClientRegistration(
                    client.Key,
                    client.DisplayName,
                    new RegistrationReport(
                        RegistrationStatus.Failed,
                        $"The registration hook could not even open its log: {failure.Message}. BrowserAI may be installed and unregistered with {client.DisplayName}.",
                        null,
                        imagePath)))
                .ToList();

            TryWriteWithoutALogger(paths.RootAppDir, failed, intent, version);

            // The data root is KEPT on this path and nothing is asked. A hook
            // that could not open a log is a hook that cannot report what it
            // deleted, and an unreportable deletion of somebody's browsers is the
            // one outcome that must not be reachable.
            return new HookOutcome(failed, null);
        }
    }

    /// <summary>Puts this install's folder on the user's PATH, or takes it off.</summary>
    /// <param name="intent">Which hook is asking.</param>
    /// <param name="imagePath">The running image, which decides the folder.</param>
    /// <param name="userPath">Where the PATH is.</param>
    /// <param name="logger">Where the change is reported.</param>
    /// <returns>What happened, or <see langword="null"/> when the image is not an install.</returns>
    private static UserPathReport? ChangeThePath(RegistrationIntent intent, string? imagePath, IUserPathStore userPath, ILogger logger)
    {
        // The same refusal the registration took: an image that is not an install
        // has no folder of its own to put anywhere, and it said why already.
        if (!RegistrationTarget.TryResolve(imagePath, out var target, out _))
        {
            return null;
        }

        var entry = UserPath.EntryFor(target!);
        var report = intent is RegistrationIntent.Uninstall
            ? UserPath.Remove(userPath, entry)
            : UserPath.Add(userPath, entry);

        RegistrationHookLog.PathChanged(logger, report.Detail);

        return report;
    }

    private static void WriteRecord(string root, IReadOnlyList<ClientRegistration> passes, RegistrationIntent intent, string version, ILogger logger)
    {
        var path = RegistrationRecord.PathFor(root);

        try
        {
            _ = RegistrationRecord.Write(root, passes, intent, version, DateTimeOffset.Now);

            foreach (var pass in passes)
            {
                RegistrationLog.RecordWritten(logger, path, pass.Key, pass.Report.Status);
            }
        }
#pragma warning disable CA1031 // A record that cannot be written must not turn an otherwise successful registration into a failed install.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            RegistrationLog.RecordNotWritten(logger, path, failure);
        }
    }

    private static void TryWriteWithoutALogger(string root, IReadOnlyList<ClientRegistration> passes, RegistrationIntent intent, string version)
    {
        try
        {
            _ = RegistrationRecord.Write(root, passes, intent, version, DateTimeOffset.Now);
        }
#pragma warning disable CA1031 // Last resort. If this fails too there is nothing left that could report it, and an installer must still succeed.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}

/// <summary>Source-generated log messages for the hook itself.</summary>
internal static partial class RegistrationHookLog
{
    /// <summary>
    /// The first line a hook writes, and often the only evidence that a hook ran
    /// at all.
    /// </summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="intent">Which hook.</param>
    /// <param name="version">The version Velopack passed.</param>
    /// <param name="imagePath">The image the hook is running as, which is what gets registered.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Velopack's {Intent} hook is running for BrowserAI {Version}, as {ImagePath}.")]
    public static partial void HookRunning(ILogger logger, RegistrationIntent intent, string version, string imagePath);

    /// <summary>What became of the install's folder on the user's PATH.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="detail">The sentence.</param>
    /// <remarks>
    /// <i>Corrected 2026-10-10, the texts polish, page #176 (previously "User PATH: {Change}.
    /// {Detail}", with the member's name, <c>Added</c> or <c>AlreadyThere</c>, before a
    /// sentence that already says it)</i>.
    /// </remarks>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "User PATH: {Detail}")]
    public static partial void PathChanged(ILogger logger, string detail);

    /// <summary>What asking the background to stop came to, at an uninstall.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="detail">What happened, as a sentence a person reads.</param>
    /// <remarks>
    /// <b>The sentence alone since 2026-10-10</b>, round 2 of the texts review, #168
    /// (previously "Background stop: {Outcome}. {Detail}", which put the outcome's .NET
    /// name, <c>NoneRunning</c> or <c>NotWaitedFor</c>, in front of a sentence that
    /// already says it in words). Written to the process log only.
    /// </remarks>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Background stop: {Detail}")]
    public static partial void BackgroundStopped(ILogger logger, string detail);

    /// <summary>What the hook did to the toasts' activator.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="detail">
    /// A sentence for the process log, the one place it is written. <i>Corrected
    /// 2026-10-10, round 2 of the texts review, first page 142 (previously "A sentence for
    /// the installer's log.")</i>: the installer's log carries no activator line.
    /// </param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Toast activator: {Detail}")]
    public static partial void ToastActivator(ILogger logger, string detail);
}
