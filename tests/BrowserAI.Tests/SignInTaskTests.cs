// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Xml.Linq;
using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Velopack.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The per-user logon task: what it is, what each installer hook does to it, and
/// what the real task scheduler keeps of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q282 a, the maintainer's words verbatim: <i>"Q282 a"</i>.</b> The install and
/// update hooks register one task per install root, with a logon trigger scoped to
/// the current user and no delay, running
/// <c>&lt;install root&gt;\current\BrowserAI.exe --sign-in $(Arg0)</c>; the
/// uninstall hook removes it; a registration that fails never fails a hook.
/// </para>
/// <para>
/// <b>Two layers.</b> The hook is driven in process against a scheduler that
/// records, the way every other hook arm drives it against a scratch PATH. The real
/// scheduler is asked once, with a task named for the test pack's id under a scratch
/// root's key, which no real install can share; the real-installer arms add what an
/// installed test pack's hooks leave behind.
/// </para>
/// </remarks>
internal sealed class SignInTaskTests
{
    /// <summary>
    /// The definition is a logon trigger scoped to the current user, with no delay,
    /// a principal that runs as that user, an action that starts the background and
    /// says who started it through the placeholder a started-on-demand run fills, a
    /// second start ignored while one runs, and a description that says what disabling
    /// and deleting the task do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read as XML and not as text</b>, so the arm holds what the scheduler will
    /// read. The app's path carries an ampersand, which is the positive control for
    /// the escaping: an unescaped one makes the document fail to parse.
    /// <b>Changed 2026-10-08 with S a</b> (previously "--sign-in $(Arg0)" and
    /// <c>Parallel</c>): the task starts the one resident background, and
    /// <c>IgnoreNew</c> is the second of its two guards against a second one, the
    /// first being its pipe's first instance.
    /// </para>
    /// <para>
    /// <b>Extended 2026-10-09 by addition</b>: the priority, the description, which is
    /// the design's (deleting or disabling the task stops BrowserAI until a person
    /// enables it or starts BrowserAI from the Start Menu), and an action carrying the
    /// installer's settings, escaped like the rest. Planted red that day against a
    /// definition that ran a second instance in parallel, the setting before S a.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDefinitionIsAUserScopedLogonTriggerWithNoDelayThatStartsOneBackground()
    {
        const string Root = @"C:\Users\someone\AppData\Local\Tom & Jerry";
        const string Image = Root + @"\current\BrowserAI.exe";

        var sid = NamedPipes.CurrentUserSid();
        var task = XDocument.Parse(SignInTask.DefinitionFor(Image, sid, Root));

        XElement one(string name) => task.Descendants().Single(element => element.Name.LocalName == name);

        var trigger = one("LogonTrigger");

        await Assert.That(trigger.Elements().Single(element => element.Name.LocalName == "UserId").Value).IsEqualTo(sid);
        await Assert.That(task.Descendants().Any(element => element.Name.LocalName == "Delay")).IsFalse();
        await Assert.That(task.Descendants().Count(element => element.Name.LocalName is "LogonTrigger" or "BootTrigger" or "TimeTrigger" or "CalendarTrigger" or "EventTrigger")).IsEqualTo(1);

        var principal = one("Principal");

        await Assert.That(principal.Elements().Single(element => element.Name.LocalName == "UserId").Value).IsEqualTo(sid);
        await Assert.That(principal.Elements().Single(element => element.Name.LocalName == "LogonType").Value).IsEqualTo("InteractiveToken");
        await Assert.That(principal.Elements().Single(element => element.Name.LocalName == "RunLevel").Value).IsEqualTo("LeastPrivilege");

        await Assert.That(one("Command").Value).IsEqualTo(Image);
        await Assert.That(one("Arguments").Value).IsEqualTo("--background --started-by $(Arg0)");
        await Assert.That(one("Arguments").Value).IsEqualTo(SignInTask.Arguments);
        await Assert.That(task.Descendants().Count(element => element.Name.LocalName == "Exec")).IsEqualTo(1);

        await Assert.That(one("MultipleInstancesPolicy").Value).IsEqualTo("IgnoreNew");
        await Assert.That(one("DisallowStartIfOnBatteries").Value).IsEqualTo("false");
        await Assert.That(one("ExecutionTimeLimit").Value).IsEqualTo("PT0S");
        await Assert.That(one("AllowStartOnDemand").Value).IsEqualTo("true");
        await Assert.That(one("Priority").Value).IsEqualTo("5");

        var description = one("Description").Value;

        await Assert.That(description).Contains($"installed in {Root}");
        await Assert.That(description).Contains("Disabling this task stops BrowserAI until it is enabled again");
        await Assert.That(description).Contains("deleting it stops BrowserAI until BrowserAI is started from the Start Menu, which registers it again");

        // The installer's settings travel in the same action, escaped like the rest.
        var named = XDocument.Parse(SignInTask.DefinitionFor(Image, sid, Root, SignInTask.ArgumentsFor(Root + @"\data", @"D:\feeds\BrowserAI")));

        await Assert.That(named.Descendants().Single(element => element.Name.LocalName == "Arguments").Value)
            .IsEqualTo($"--background --data-root \"{Root}\\data\" --update-source \"D:\\feeds\\BrowserAI\" --started-by $(Arg0)");
    }

    /// <summary>
    /// The installer's settings reach the task's action as plain arguments, each path
    /// or source quoted with its trailing separators taken off, so the program the task
    /// starts reads them back exactly, the placeholder included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The design's settings rule</b>: a running BrowserAI reads no <c>BROWSERAI_</c>
    /// variable, so the install and update hooks write what the installer named into
    /// the action, and never inside <c>$(Arg0)</c>. A backslash before a closing quote
    /// would escape it on the way to the process's own command line, which is why the
    /// separators come off; the round trip below splits the action the way the C
    /// runtime does and hands it to the program's own readers.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against an action that kept a trailing separator
    /// inside the data root's quotes.
    /// </para>
    /// </remarks>
    /// <param name="dataRoot">The data root the installer named.</param>
    /// <param name="updateSource">The update source the installer named.</param>
    /// <param name="readRoot">The data root the program reads back.</param>
    /// <param name="readSource">The update source the program reads back.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(null, null, null, null)]
    [Arguments(@"C:\Users\some one\BrowserAI data", null, @"C:\Users\some one\BrowserAI data", null)]
    [Arguments(@"C:\Users\some one\BrowserAI data\", null, @"C:\Users\some one\BrowserAI data", null)]
    [Arguments(@"C:\Users\someone\data\\", null, @"C:\Users\someone\data", null)]
    [Arguments(null, @"D:\feeds\BrowserAI\", null, @"D:\feeds\BrowserAI")]
    [Arguments(null, "https://example.invalid/feeds/browserai/", null, "https://example.invalid/feeds/browserai")]
    [Arguments(@"C:\Users\someone\data/", @"\\server\share\feed\", @"C:\Users\someone\data", @"\\server\share\feed")]
    public async Task TheInstallersSettingsReachTheActionQuotedAndTrimmedAndTheProgramReadsThemBack(string? dataRoot, string? updateSource, string? readRoot, string? readSource)
    {
        var action = SignInTask.ArgumentsFor(dataRoot, updateSource);

        await Assert.That(action).StartsWith(SignInTask.BackgroundArgument + " ");
        await Assert.That(action).EndsWith(" " + SignInTask.StartedByArgument + " $(Arg0)");

        // What the program the task starts receives, with the placeholder filled as a
        // person's start fills it.
        var args = Relay.ClientRecognition.Split(@"""C:\Users\someone\BrowserAI\current\BrowserAI.exe"" " + action.Replace("$(Arg0)", "person", StringComparison.Ordinal))
            .Skip(1)
            .ToList();

        await Assert.That(Program.IsTheTasksStart(args)).IsTrue();
        await Assert.That(Program.ValueOf(args, Program.DataRootArgument)).IsEqualTo(readRoot);
        await Assert.That(Program.ValueOf(args, UpdateSource.Argument)).IsEqualTo(readSource);
        await Assert.That(Program.ValueOf(args, Program.StartedByArgument)).IsEqualTo("person");
        await Assert.That(args.Count).IsEqualTo(3 + (readRoot is null ? 0 : 2) + (readSource is null ? 0 : 2)).Because(string.Join(" | ", args));
    }

    /// <summary>
    /// The task is named for the pack id and the install root's key, the same key
    /// the census gate and the coordinator's pipe end in.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheTaskIsNamedForThePackIdAndTheInstallRootsKey()
    {
        using var root = ScratchDirectory.Create("sign-in-name");

        var key = LiveInstances.RootKeyFor(root.Path);

        await Assert.That(SignInTask.NameFor("BrowserAI.app", root.Path)).IsEqualTo($"BrowserAI.app sign-in {key}");
        await Assert.That(SignInTask.NameFor(ReleaseLayout.TestPackId, root.Path)).IsEqualTo($"{ReleaseLayout.TestPackId} sign-in {key}");
        await Assert.That(SignInTask.NameFor("BrowserAI.app", root.Path.ToUpperInvariant())).IsEqualTo(SignInTask.NameFor("BrowserAI.app", root.Path));
        await Assert.That(Coordination.CoordinatorProtocol.NameFor(root.Path).EndsWith(key, StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// The install and update hooks register the task, for this install's app, and
    /// save the definition they registered beside the install; the uninstall hook
    /// removes both, after it has found no background to stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>RESOLUTIONS 9</b>: a person's start registers a missing task again from the
    /// definition the hooks saved, because the settings the install hook read out of the
    /// installer's environment exist nowhere else once the installer has gone. So what
    /// is saved must be what was registered, byte for byte, and an uninstall must take
    /// it away with the task.
    /// </para>
    /// <para>
    /// ⚠️ <b>Extended 2026-10-09</b> (previously the arm held the registrations alone).
    /// <b>Planted red 2026-10-09</b> against a hook that registered the task and saved
    /// nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstallAndUpdateHooksRegisterTheTaskAndTheUninstallHookRemovesIt()
    {
        using var install = ScratchDirectory.Create("sign-in-hooks");
        using var data = ScratchDirectory.Create("sign-in-hooks-data");

        var image = InstalledLayout.Create(install.Path);
        var tasks = new ScratchLogonTasks();
        var name = SignInTask.NameFor(ScratchLogonTasks.AppId, install.Path);

        await Assert.That(SignInTask.SavedDefinition(install.Path)).IsNull();

        var installed = Hook(RegistrationIntent.Install, image, data.Path, tasks);

        await Assert.That(installed.SignInTask!.Change).IsEqualTo(TaskChange.Registered);
        await Assert.That(installed.SignInTask.Name).IsEqualTo(name);

        var definition = XDocument.Parse(tasks.Registered[name]);

        await Assert.That(definition.Descendants().Single(element => element.Name.LocalName == "Command").Value)
            .IsEqualTo(Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName));
        await Assert.That(SignInTask.SavedDefinition(install.Path)).IsEqualTo(tasks.Registered[name])
            .Because("the definition a person's start registers again is not the one the hook registered");

        var updated = Hook(RegistrationIntent.Update, image, data.Path, tasks);

        await Assert.That(updated.SignInTask!.Change).IsEqualTo(TaskChange.Registered);
        await Assert.That(SignInTask.SavedDefinition(install.Path)).IsEqualTo(tasks.Registered[name]);

        var removed = Hook(RegistrationIntent.Uninstall, image, data.Path, tasks);

        await Assert.That(removed.SignInTask!.Change).IsEqualTo(TaskChange.Removed);
        await Assert.That(tasks.Registered.IsEmpty).IsTrue();
        await Assert.That(string.Join(", ", tasks.Calls)).IsEqualTo($"register {name}, register {name}, remove {name}");
        await Assert.That(SignInTask.SavedDefinition(install.Path)).IsNull();
        await Assert.That(File.Exists(Path.Combine(install.Path, SignInTask.SavedDefinitionFileName))).IsFalse();
        await Assert.That(HookLog(data.Path)).Contains($"Background stop: {BackgroundStopOutcome.NoneRunning}.");
    }

    /// <summary>
    /// A task the scheduler will not register fails nothing: the hook returns, the
    /// rest of what it did stands, the installer's own log carries a warning naming the
    /// task, and the definition is saved all the same, for a person's start to register
    /// the missing task from.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-10-09</b> (previously "and no definition is saved for a task
    /// that is not there", with the remark "The saved definition is extended 2026-10-09,
    /// and planted red that day against a hook that saved the definition whatever the
    /// scheduler answered"). Lane ARCH's helper T1 held that no definition is saved and
    /// watched it red against a hook that saved one anyway; lane ARCH chose the saved
    /// file the same day. A person's start registers a missing task only from that file
    /// (RESOLUTIONS 9), and a task the hook could not register is the one that will be
    /// missing (hazard row 331). The assertion now holds the file there, and
    /// <see cref="ATaskTheSchedulerRefusedStillLeavesItsDefinitionForAPersonsStart"/>
    /// holds what it carries.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATaskTheSchedulerRefusesNeverFailsTheHookAndTheInstallersLogSaysSo()
    {
        using var install = ScratchDirectory.Create("sign-in-refused");
        using var data = ScratchDirectory.Create("sign-in-refused-data");

        var image = InstalledLayout.Create(install.Path);
        var tasks = new ScratchLogonTasks { FailWith = "The task scheduler could not register it: 0x80070005, Access is denied." };

        var outcome = Hook(RegistrationIntent.Install, image, data.Path, tasks);

        await Assert.That(outcome.SignInTask!.Change).IsEqualTo(TaskChange.Failed);
        await Assert.That(outcome.IsWhatWasAskedFor).IsTrue();
        await Assert.That(outcome.PathEntry).IsNotNull();
        await Assert.That(SignInTask.SavedDefinition(install.Path)).IsNotNull()
            .Because("a person's start registers the missing task from the saved definition, and none was saved for a task the scheduler refused");

        var lines = new List<(VelopackLogLevel Level, string Message)>();

        VelopackStartup.Mirror(outcome, RegistrationIntent.Install, "9.9.9", (level, message, _) => lines.Add((level, message)));

        var (level, message) = lines.Single(entry => entry.Message.Contains("sign-in task", StringComparison.Ordinal));

        await Assert.That(level).IsEqualTo(VelopackLogLevel.Warning);
        await Assert.That(message).Contains("Access is denied");

        // And a registration that worked is information, not a warning.
        var fine = Hook(RegistrationIntent.Install, image, data.Path, new ScratchLogonTasks());

        lines.Clear();
        VelopackStartup.Mirror(fine, RegistrationIntent.Install, "9.9.9", (level, message, _) => lines.Add((level, message)));

        await Assert.That(lines.Single(entry => entry.Message.Contains("sign-in task", StringComparison.Ordinal)).Level).IsEqualTo(VelopackLogLevel.Information);
    }

    /// <summary>
    /// What the installer named reaches the processes that use it as arguments, read
    /// once by the hook: the data root into every client's registration of the relay,
    /// and the data root and the update source into the task's action. With nothing
    /// named, each carries its defaults alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The design's "Settings travel as arguments and over the pipe"</b>: the
    /// installer's hooks are the one place a <c>BROWSERAI_</c> variable is read, and
    /// they write what they find into the task's action and into the registration's
    /// arguments, so the relay a client starts finds the background for that root.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a hook that registered the relay with
    /// <c>--mcp</c> alone whatever the installer named.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHooksCarryTheInstallersSettingsIntoTheRelaysRegistrationAndTheTasksAction()
    {
        using var install = ScratchDirectory.Create("sign-in-settings");
        using var data = ScratchDirectory.Create("sign-in-settings-data");

        const string Root = @"C:\Users\someone\BrowserAI data";
        const string Source = @"D:\feeds\BrowserAI";

        var image = InstalledLayout.Create(install.Path);
        var name = SignInTask.NameFor(ScratchLogonTasks.AppId, install.Path);

        var tool = new FakeRegisterAi();
        var tasks = new ScratchLogonTasks();

        _ = Hook(RegistrationIntent.Install, image, data.Path, tasks, tool, RegistrationClient.All, new InstallerSettings(Root, Source));

        await Assert.That(RegisteredCommand(tool)).IsEqualTo($"{image} | {Program.McpArgument} | {Program.DataRootArgument} | {Root}");
        await Assert.That(ActionOf(tasks.Registered[name])).IsEqualTo(SignInTask.ArgumentsFor(Root, Source));

        // Named nothing: the defaults alone, in both.
        var plainTool = new FakeRegisterAi();
        var plainTasks = new ScratchLogonTasks();

        _ = Hook(RegistrationIntent.Update, image, data.Path, plainTasks, plainTool, RegistrationClient.All, settings: null);

        await Assert.That(RegisteredCommand(plainTool)).IsEqualTo($"{image} | {Program.McpArgument}");
        await Assert.That(ActionOf(plainTasks.Registered[name])).IsEqualTo(SignInTask.Arguments);
    }

    /// <summary>
    /// The toasts' activator is registered by the install and update hooks and removed
    /// by the uninstall hook, each for this install's root, and the hook's log says what
    /// came of it; with no activator handed in, nothing is asked and nothing is said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>T, decided 2026-10-08</b>: a click on one of BrowserAI's toasts reaches a COM
    /// class under the user's own classes, which the hooks keep in step with the install.
    /// The suite hands in a stand-in, because a class written into the developer's own
    /// registry by an arm is exactly what the seam exists to prevent.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against an uninstall hook that left the activator
    /// registered.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheToastActivatorFollowsTheInstallAndIsLeftAloneWithoutTheSeam()
    {
        using var install = ScratchDirectory.Create("sign-in-activator");
        using var data = ScratchDirectory.Create("sign-in-activator-data");
        using var quiet = ScratchDirectory.Create("sign-in-activator-quiet");

        var image = InstalledLayout.Create(install.Path);
        var asked = new List<string>();

        string activator(RegistrationIntent intent, string root)
        {
            asked.Add($"{intent} {root}");
            return $"the suite's activator, at the {intent} hook";
        }

        foreach (var intent in new[] { RegistrationIntent.Install, RegistrationIntent.Update, RegistrationIntent.Uninstall })
        {
            _ = Hook(intent, image, data.Path, new ScratchLogonTasks(), toastActivator: activator);
        }

        await Assert.That(string.Join(" | ", asked))
            .IsEqualTo($"{RegistrationIntent.Install} {install.Path} | {RegistrationIntent.Update} {install.Path} | {RegistrationIntent.Uninstall} {install.Path}");

        var log = HookLog(data.Path);

        await Assert.That(log).Contains($"Toast activator: the suite's activator, at the {RegistrationIntent.Install} hook");
        await Assert.That(log).Contains($"Toast activator: the suite's activator, at the {RegistrationIntent.Uninstall} hook");

        // No seam, no question and no line.
        _ = Hook(RegistrationIntent.Install, image, quiet.Path, new ScratchLogonTasks(), toastActivator: null);

        await Assert.That(HookLog(quiet.Path)).DoesNotContain("Toast activator:");
        await Assert.That(asked.Count).IsEqualTo(3);
    }

    /// <summary>
    /// An uninstall asks the background serving this install's pipe to stop, through
    /// that pipe, and only then removes the task; an install and an update leave a
    /// running background alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The design's "A person's start, the tab and the hooks"</b>: uninstall asks a
    /// running background through its pipe to close its sessions and exit, never
    /// through the task's End, then removes the task, the registrations and the PATH
    /// entry. The background here is the product's own server in this process, on the
    /// pipe the hook composes from the install root and the data root.
    /// </para>
    /// <para>
    /// <b>The order is read from the hook's own log</b>, which it writes in the order it
    /// works: the stop's line before the task's. The background's record names no
    /// process here, so the hook does not wait on one.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against an uninstall hook that asked nothing of the
    /// background.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallAsksTheBackgroundToStopThroughItsPipeBeforeTheTaskGoes()
    {
        using var install = ScratchDirectory.Create("sign-in-stop");
        using var data = ScratchDirectory.Create("sign-in-stop-data");

        var image = InstalledLayout.Create(install.Path);
        var name = SignInTask.NameFor(ScratchLogonTasks.AppId, install.Path);
        var tasks = new ScriptedLogonTasks();

        await using var background = BackgroundServerRig.Start(BackgroundPipe.NameFor(install.Path, data.Path), data.Path);

        _ = Hook(RegistrationIntent.Install, image, data.Path, tasks);
        _ = Hook(RegistrationIntent.Update, image, data.Path, tasks);

        await Assert.That(background.Verbs.Stops).IsEqualTo(0).Because("an install or an update stopped the background");

        var removed = Hook(RegistrationIntent.Uninstall, image, data.Path, tasks);

        await Assert.That(removed.SignInTask!.Change).IsEqualTo(TaskChange.Removed);

        await Assert.That(string.Join(" | ", tasks.Events)).IsEqualTo($"register {name} | register {name} | remove {name}");

        var log = HookLog(data.Path);
        var stopped = log.IndexOf($"Background stop: {BackgroundStopOutcome.Ended}.", StringComparison.Ordinal);
        var taskGone = log.IndexOf($"Sign-in task: {TaskChange.Removed}.", StringComparison.Ordinal);

        await Assert.That(stopped).IsGreaterThanOrEqualTo(0).Because(log);
        await Assert.That(taskGone).IsGreaterThan(stopped).Because("the task went before the background was asked to stop");

        // The background's own stop runs once its answer is written, so it is waited
        // for, not assumed.
        await BackgroundServerRig.WaitUntilAsync(() => background.Verbs.Stops is 1, "the background was never asked to stop");
    }

    /// <summary>
    /// A task the scheduler refused still leaves its definition beside the install, so
    /// a person's start can register the task once the scheduler takes it.
    /// </summary>
    /// <remarks>
    /// <b>Hazard row 331, added 2026-10-09.</b> A person's start registers a missing task
    /// from <see cref="SignInTask.SavedDefinitionFileName"/>, and until that day the hook
    /// wrote the file only once the scheduler had registered the task, so a task the hook
    /// could not register, the one that is missing, had no definition to register it from
    /// and the start could only say so in its log. The real-installer arm holds the rest
    /// of the path through the real scheduler.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATaskTheSchedulerRefusedStillLeavesItsDefinitionForAPersonsStart()
    {
        using var install = ScratchDirectory.Create("sign-in-refused-saved");
        using var data = ScratchDirectory.Create("sign-in-refused-saved-data");

        var image = InstalledLayout.Create(install.Path);
        var tasks = new ScratchLogonTasks { FailWith = "The task scheduler could not register it: 0x80070005, Access is denied." };

        var outcome = Hook(RegistrationIntent.Install, image, data.Path, tasks);

        await Assert.That(outcome.SignInTask!.Change).IsEqualTo(TaskChange.Failed);

        var saved = SignInTask.SavedDefinition(install.Path);

        await Assert.That(saved).IsNotNull().Because("the definition is what a person's start registers the missing task from");

        var definition = XDocument.Parse(saved!);

        await Assert.That(definition.Descendants().Single(element => element.Name.LocalName == "Command").Value)
            .IsEqualTo(Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName));
        await Assert.That(definition.Descendants().Single(element => element.Name.LocalName == "Arguments").Value)
            .IsEqualTo(SignInTask.Arguments);
    }

    /// <summary>
    /// The real task scheduler registers the definition, keeps a logon trigger with
    /// no delay and the sign-in arguments, removes it, and says a second removal
    /// found nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named for the test pack's id under a scratch root's key</b>, so the task
    /// cannot be anybody's, and removed in a <c>finally</c>; the gate's clearance
    /// reading fails a run that leaves a test-pack task behind.
    /// </para>
    /// <para>
    /// <b>What the scheduler keeps is read back, because it rewrites.</b> Measured
    /// 2026-09-25: it stores the trigger's user as <c>DOMAIN\user</c> where the
    /// definition gave the SID, and drops every element equal to its default. The
    /// action is never started here, so its path need not exist.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRealSchedulerKeepsTheTaskAndRemovesItAgain()
    {
        using var root = ScratchDirectory.Create("sign-in-real");

        var name = SignInTask.NameFor(ReleaseLayout.TestPackId, root.Path);
        var image = Path.Combine(root.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);

        try
        {
            var registered = ScheduledTasks.Instance.Register(name, SignInTask.DefinitionFor(image, NamedPipes.CurrentUserSid(), root.Path));

            await Assert.That(registered.Change).IsEqualTo(TaskChange.Registered).Because(registered.Detail);

            var kept = XDocument.Parse(ScheduledTasks.DefinitionOf(name)!);

            await Assert.That(kept.Descendants().Count(element => element.Name.LocalName == "LogonTrigger")).IsEqualTo(1);
            await Assert.That(kept.Descendants().Single(element => element.Name.LocalName == "LogonTrigger").Elements().Single(element => element.Name.LocalName == "UserId").Value)
                .IsEqualTo($@"{Environment.UserDomainName}\{Environment.UserName}", StringComparison.OrdinalIgnoreCase);
            await Assert.That(kept.Descendants().Any(element => element.Name.LocalName == "Delay")).IsFalse();
            await Assert.That(kept.Descendants().Single(element => element.Name.LocalName == "Command").Value).IsEqualTo(image);
            await Assert.That(kept.Descendants().Single(element => element.Name.LocalName == "Arguments").Value).IsEqualTo(SignInTask.Arguments);

            // Registering again replaces it, which is what the update hook does.
            await Assert.That(ScheduledTasks.Instance.Register(name, SignInTask.DefinitionFor(image, NamedPipes.CurrentUserSid(), root.Path)).Change)
                .IsEqualTo(TaskChange.Registered);
        }
        finally
        {
            var removed = ScheduledTasks.Instance.Remove(name);

            await Assert.That(removed.Change).IsEqualTo(TaskChange.Removed).Because(removed.Detail);
        }

        await Assert.That(ScheduledTasks.DefinitionOf(name)).IsNull();
        await Assert.That(ScheduledTasks.Instance.Remove(name).Change).IsEqualTo(TaskChange.Absent);
        await Assert.That(ScheduledTasks.Instance.Run(name, Coordination.CoordinatorProtocol.CoordinateArgument).Change).IsEqualTo(TaskChange.NotRegistered);
    }

    /// <summary>Runs one hook against a scratch install, a scratch data root and a scratch PATH.</summary>
    /// <param name="intent">Which hook.</param>
    /// <param name="image">The installed app's image.</param>
    /// <param name="data">The data root.</param>
    /// <param name="tasks">The scheduler.</param>
    /// <param name="tool">RegisterAI, or <see langword="null"/> for a fake that registers nothing it is not asked to.</param>
    /// <param name="clients">The clients, or <see langword="null"/> for none.</param>
    /// <param name="settings">What the installer named, or <see langword="null"/> for nothing.</param>
    /// <param name="toastActivator">The toasts' activator step, or <see langword="null"/> to leave it alone.</param>
    /// <returns>What the hook did.</returns>
    private static HookOutcome Hook(
        RegistrationIntent intent,
        string image,
        string data,
        ILogonTasks tasks,
        IRegisterAi? tool = null,
        IReadOnlyList<RegistrationClient>? clients = null,
        InstallerSettings? settings = null,
        Func<RegistrationIntent, string, string>? toastActivator = null) =>
        HookRegistration.Run(
            intent,
            "9.9.9",
            image,
            tool ?? new FakeRegisterAi(),
            new LocalAppDataPaths(data),
            new ScratchUserPath(),
            tasks,
            ScratchLogonTasks.AppId,
            silent: true,
            ask: _ => false,
            clients: clients ?? [],
            settings: settings,
            toastActivator: toastActivator);

    /// <summary>The command and arguments the one registration RegisterAI was asked for names, joined.</summary>
    /// <param name="tool">The fake RegisterAI.</param>
    /// <returns>Everything after <c>--</c>.</returns>
    private static string RegisteredCommand(FakeRegisterAi tool)
    {
        var call = tool.Calls.Single(arguments => arguments[0] is "register");

        return string.Join(" | ", call.Skip(call.ToList().IndexOf("--") + 1));
    }

    /// <summary>The action's arguments in a task definition.</summary>
    /// <param name="definition">The definition's XML.</param>
    /// <returns>The arguments.</returns>
    private static string ActionOf(string definition) =>
        XDocument.Parse(definition).Descendants().Single(element => element.Name.LocalName == "Arguments").Value;

    /// <summary>Everything the hooks logged under a data root, in the order the files were written.</summary>
    /// <param name="data">The data root.</param>
    /// <returns>The text.</returns>
    private static string HookLog(string data) =>
        string.Join(
            "\n",
            Directory.EnumerateFiles(Path.Combine(data, "logs"), "*.log")
                .Order(StringComparer.Ordinal)
                .Select(static path =>
                {
                    // Shared, as the log itself opens it.
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }));
}
