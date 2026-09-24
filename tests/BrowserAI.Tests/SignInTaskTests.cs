// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Xml.Linq;
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
    /// a principal that runs as that user, and an action that starts the app with
    /// the sign-in argument and the placeholder a started-on-demand run fills.
    /// </summary>
    /// <remarks>
    /// <b>Read as XML and not as text</b>, so the arm holds what the scheduler will
    /// read. The app's path carries an ampersand, which is the positive control for
    /// the escaping: an unescaped one makes the document fail to parse.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDefinitionIsAUserScopedLogonTriggerWithNoDelayThatStartsTheAppForTheSignInStep()
    {
        const string Image = @"C:\Users\someone\AppData\Local\Tom & Jerry\current\BrowserAI.exe";

        var sid = NamedPipes.CurrentUserSid();
        var task = XDocument.Parse(SignInTask.DefinitionFor(Image, sid, @"C:\Users\someone\AppData\Local\Tom & Jerry"));

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
        await Assert.That(one("Arguments").Value).IsEqualTo("--sign-in $(Arg0)");
        await Assert.That(task.Descendants().Count(element => element.Name.LocalName == "Exec")).IsEqualTo(1);

        await Assert.That(one("MultipleInstancesPolicy").Value).IsEqualTo("Parallel");
        await Assert.That(one("DisallowStartIfOnBatteries").Value).IsEqualTo("false");
        await Assert.That(one("ExecutionTimeLimit").Value).IsEqualTo("PT0S");
        await Assert.That(one("AllowStartOnDemand").Value).IsEqualTo("true");
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
    /// the uninstall hook removes it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstallAndUpdateHooksRegisterTheTaskAndTheUninstallHookRemovesIt()
    {
        using var install = ScratchDirectory.Create("sign-in-hooks");
        using var data = ScratchDirectory.Create("sign-in-hooks-data");

        var image = InstalledLayout.Create(install.Path);
        var tasks = new ScratchLogonTasks();
        var name = SignInTask.NameFor(ScratchLogonTasks.AppId, install.Path);

        var installed = Hook(RegistrationIntent.Install, image, data.Path, tasks);

        await Assert.That(installed.SignInTask!.Change).IsEqualTo(TaskChange.Registered);
        await Assert.That(installed.SignInTask.Name).IsEqualTo(name);

        var definition = XDocument.Parse(tasks.Registered[name]);

        await Assert.That(definition.Descendants().Single(element => element.Name.LocalName == "Command").Value)
            .IsEqualTo(Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName));

        var updated = Hook(RegistrationIntent.Update, image, data.Path, tasks);

        await Assert.That(updated.SignInTask!.Change).IsEqualTo(TaskChange.Registered);

        var removed = Hook(RegistrationIntent.Uninstall, image, data.Path, tasks);

        await Assert.That(removed.SignInTask!.Change).IsEqualTo(TaskChange.Removed);
        await Assert.That(tasks.Registered.IsEmpty).IsTrue();
        await Assert.That(string.Join(", ", tasks.Calls)).IsEqualTo($"register {name}, register {name}, remove {name}");
    }

    /// <summary>
    /// A task the scheduler will not register fails nothing: the hook returns, the
    /// rest of what it did stands, and the installer's own log carries a warning
    /// naming the task.
    /// </summary>
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

    /// <summary>Runs one hook against a scratch install, a scratch data root, no client and a scratch PATH.</summary>
    /// <param name="intent">Which hook.</param>
    /// <param name="image">The installed app's image.</param>
    /// <param name="data">The data root.</param>
    /// <param name="tasks">The scheduler.</param>
    /// <returns>What the hook did.</returns>
    private static HookOutcome Hook(RegistrationIntent intent, string image, string data, ILogonTasks tasks) =>
        HookRegistration.Run(
            intent,
            "9.9.9",
            image,
            new FakeClientCommandLine(),
            new LocalAppDataPaths(data),
            new ScratchUserPath(),
            tasks,
            ScratchLogonTasks.AppId,
            silent: true,
            ask: _ => false,
            clients: []);
}
