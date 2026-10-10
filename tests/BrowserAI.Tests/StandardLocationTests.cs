// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Relay;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Win32;
using Velopack.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The one folder the shipping install may be in: a copy in another one sets nothing up,
/// says why at every start, and its uninstall touches nothing of the standard install's.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 21 of 2026-10-10, verbatim: <i>"21 refusing installing into a
/// non-standard folder so the project specific setups always resolve on every dev's
/// pc."</i></b>, and of the way to do it the same day, relayed in his words verbatim,
/// <i>"that proposal sounds go"</i>. Velopack's Setup takes <c>--installto</c> and a hook
/// that fails undoes nothing, measured at 1.2.161, so the refusal is the hook's and every
/// start's.
/// </para>
/// <para>
/// <b>In process, with every machine-wide thing a seam</b>: the standard folder is a
/// scratch one the hook is handed, the PATH, the task scheduler and RegisterAI are the
/// suite's fakes, and no box is shown, only the decision to show one.
/// </para>
/// </remarks>
internal sealed class StandardLocationTests
{
    /// <summary>
    /// A shipping copy outside the standard folder is refused, and the standard folder in
    /// any spelling, the suite's test pack and a build that is not installed are not.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a judgement that refused nothing, which is the
    /// rule absent: the copy outside the standard folder came back allowed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AShippingCopyOutsideTheStandardFolderIsRefusedAndNothingElseIs()
    {
        using var standard = ScratchDirectory.Create("standard-location");
        using var elsewhere = ScratchDirectory.Create("standard-location-elsewhere");

        await Assert.That(StandardLocation.ShippingPackId).IsEqualTo(ReleaseLayout.PackId);

        var refused = StandardLocation.Judge(ReleaseLayout.PackId, elsewhere.Path, standard.Path);

        await Assert.That(refused).IsNotNull();
        await Assert.That(refused!.Sentence).Contains($"is in '{elsewhere.Path}'");
        await Assert.That(refused.Sentence).Contains($"installs only into '{standard.Path}'");
        await Assert.That(refused.Sentence).EndsWith(StandardLocation.Remedy);
        await Assert.That(StandardLocation.Remedy).IsEqualTo("Uninstall this copy, then run BrowserAI.exe again without --installto.");

        // The parts a relay answers with: the install root, why, and the remedy as a clause.
        var parts = refused.AsRootRefusal();

        await Assert.That(parts.Which).IsEqualTo(JudgedRoot.Install);
        await Assert.That(parts.Root).IsEqualTo(elsewhere.Path);
        await Assert.That(parts.Remedy).IsEqualTo("uninstall this copy, then run BrowserAI.exe again without --installto.");

        // The standard folder, in another case and with a trailing separator, is the standard folder.
        await Assert.That(StandardLocation.Judge(ReleaseLayout.PackId, standard.Path, standard.Path)).IsNull();
        await Assert.That(StandardLocation.Judge(ReleaseLayout.PackId, standard.Path.ToUpperInvariant() + "\\", standard.Path)).IsNull();

        // The suite's test pack keeps its scratch folders, and a build that is not installed has no install root.
        await Assert.That(StandardLocation.Judge(ReleaseLayout.TestPackId, elsewhere.Path, standard.Path)).IsNull();
        await Assert.That(StandardLocation.Judge(null, elsewhere.Path, standard.Path)).IsNull();
        await Assert.That(StandardLocation.Judge(ReleaseLayout.PackId, null, standard.Path)).IsNull();

        // And the shipping install keeps its data in the default root whatever it is handed.
        await Assert.That(StandardLocation.DataRootFor(ReleaseLayout.PackId, elsewhere.Path)).IsNull();
        await Assert.That(StandardLocation.DataRootFor(ReleaseLayout.TestPackId, elsewhere.Path)).IsEqualTo(elsewhere.Path);
        await Assert.That(StandardLocation.DataRootFor(null, elsewhere.Path)).IsEqualTo(elsewhere.Path);
    }

    /// <summary>
    /// A refused copy's install, update and uninstall hooks set nothing up and ask no
    /// client, and its uninstall leaves the data root and the toasts' activator alone; what
    /// an older build of the same copy put on the PATH and into the scheduler is taken off.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against the hook without the judgement: the install
    /// asked RegisterAI to register both clients and put the copy's folder on the PATH, and
    /// the uninstall asked whether to delete the data root.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusedCopysHooksSetNothingUpAndItsUninstallTouchesNeitherTheDataRootNorTheClients()
    {
        using var install = ScratchDirectory.Create("refused-copy");
        using var standard = ScratchDirectory.Create("refused-copy-standard");
        using var data = ScratchDirectory.Create("refused-copy-data");

        var image = InstalledLayout.Create(install.Path);
        var kept = Path.Combine(data.Path, "the-standard-installs.txt");
        await File.WriteAllTextAsync(kept, "browsers, sessions and a log");

        var tool = new FakeRegisterAi();
        var store = new ScratchUserPath();
        var tasks = new ScratchLogonTasks();
        var activated = new List<RegistrationIntent>();
        var asked = 0;

        HookOutcome hook(RegistrationIntent intent, string standardRoot) =>
            HookRegistration.Run(
                intent,
                "9.9.9",
                image,
                tool,
                new LocalAppDataPaths(data.Path),
                store,
                tasks,
                ReleaseLayout.PackId,
                silent: false,
                ask: _ =>
                {
                    asked++;
                    return true;
                },
                toastActivator: (asking, _) =>
                {
                    activated.Add(asking);
                    return "registered";
                },
                standardRoot: standardRoot);

        foreach (var intent in new[] { RegistrationIntent.Install, RegistrationIntent.Update, RegistrationIntent.Uninstall })
        {
            var outcome = hook(intent, standard.Path);

            await Assert.That(outcome.NotSetUp).IsNotNull().Because($"{intent}");
            await Assert.That(outcome.Registrations).IsEmpty().Because($"{intent}");
            await Assert.That(outcome.Disposal).IsNull().Because($"{intent}");
        }

        await Assert.That(tool.Calls).IsEmpty();
        await Assert.That(activated).IsEmpty();
        await Assert.That(asked).IsEqualTo(0);
        await Assert.That(store.Value).IsNull();
        await Assert.That(tasks.Registered).IsEmpty();
        await Assert.That(string.Join(" | ", Directory.EnumerateFileSystemEntries(data.Path).Select(entry => Path.GetFileName(entry)))).IsEqualTo("the-standard-installs.txt");

        // What an older build of the same copy set up of its own is taken off.
        var folder = Path.GetDirectoryName(image)!;
        store.Value = new UserPathValue(folder, RegistryValueKind.ExpandString);
        _ = tasks.Register(SignInTask.NameFor(ReleaseLayout.PackId, install.Path), "<Task/>");

        _ = hook(RegistrationIntent.Uninstall, standard.Path);

        await Assert.That(store.Value?.Text ?? string.Empty).DoesNotContain(folder);
        await Assert.That(tasks.Registered).IsEmpty();
        await Assert.That(tool.Calls).IsEmpty();

        // The positive control: the same copy, judged against its own folder as the standard
        // one, is set up as any install is.
        var setUp = hook(RegistrationIntent.Install, install.Path);

        await Assert.That(setUp.NotSetUp).IsNull();
        await Assert.That(tool.Calls).IsNotEmpty();
        await Assert.That(activated).IsEquivalentTo([RegistrationIntent.Install]);
        await Assert.That(store.Value?.Text ?? string.Empty).Contains(folder);
    }

    /// <summary>
    /// An install hook that set nothing up ends with its own exit code and says why in the
    /// installer's log; an update or an uninstall of such a copy, and every hook that set
    /// things up, end with Velopack's zero.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a hook that ended every run with Velopack's
    /// zero: Setup's own log then said the hook succeeded.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallThatSetNothingUpEndsWithItsOwnCodeAndNoOtherHookDoes()
    {
        var refused = new StandardLocationRefusal(@"D:\Tools\BrowserAI.app", @"C:\Users\someone\AppData\Local\BrowserAI.app");
        var notSetUp = new HookOutcome([], null, NotSetUp: refused);
        var setUp = new HookOutcome([], null);

        await Assert.That(VelopackStartup.ExitCodeFor(RegistrationIntent.Install, notSetUp)).IsEqualTo(StandardLocation.RefusedExitCode);
        await Assert.That(VelopackStartup.ExitCodeFor(RegistrationIntent.Update, notSetUp)).IsNull();
        await Assert.That(VelopackStartup.ExitCodeFor(RegistrationIntent.Uninstall, notSetUp)).IsNull();
        await Assert.That(VelopackStartup.ExitCodeFor(RegistrationIntent.Install, setUp)).IsNull();

        var lines = new List<(VelopackLogLevel Level, string Message)>();
        VelopackStartup.Mirror(notSetUp, RegistrationIntent.Install, "9.9.9", (level, message, _) => lines.Add((level, message)));

        await Assert.That(lines.Single().Level).IsEqualTo(VelopackLogLevel.Warning);
        await Assert.That(lines.Single().Message).IsEqualTo($"BrowserAI 9.9.9 -- not set up (Install): {refused.Sentence}");
    }

    /// <summary>
    /// A refused copy tells a person in a box at a Start Menu start, a page's start and the
    /// installer's own start after a non-silent install, and never at a start nobody is
    /// watching.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a start that told nobody, which is where every
    /// start of such a copy went until that day: no task, no background, and nothing on the
    /// screen.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusedCopyIsToldInABoxAtAPersonsStartAndNowhereElse()
    {
        await Assert.That(App.Program.TellsThePersonItWasNotSetUp([], restarted: false)).IsTrue();
        await Assert.That(App.Program.TellsThePersonItWasNotSetUp([BrowserAI.Coordination.CoordinatorProtocol.SessionsArgument], restarted: false)).IsTrue();

        await Assert.That(App.Program.TellsThePersonItWasNotSetUp([App.Program.ReportArgument, @"C:\report.json"], restarted: false)).IsFalse();
        await Assert.That(App.Program.TellsThePersonItWasNotSetUp([], restarted: true)).IsFalse();
        await Assert.That(App.Program.TellsThePersonItWasNotSetUp([AfterUpdate.Argument, "1.2.0"], restarted: false)).IsFalse();
        await Assert.That(App.Program.TellsThePersonItWasNotSetUp([ToastActivatorRegistration.Argument, "-Embedding"], restarted: false)).IsFalse();
    }

    /// <summary>
    /// A relay in a refused copy answers every call at once with why nothing was set up and
    /// what puts it right, where it would have met the sentence for a task that is missing.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a finder that did not read the setting: it
    /// answered that BrowserAI's background was not running.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARelayInARefusedCopyAnswersEveryCallAtOnceWithWhyAndTheRemedy()
    {
        using var data = ScratchDirectory.Create("refused-copy-relay");

        var refused = new StandardLocationRefusal(@"D:\Tools\BrowserAI.app", @"C:\Users\someone\AppData\Local\BrowserAI.app");
        var log = Path.Combine(data.Path, "logs", "browserai.log");

        using var finder = new BackgroundFinder(
            new BackgroundFinderSettings
            {
                PipeName = $"browserai-refused-{Guid.NewGuid():N}",
                RecordPath = Path.Combine(data.Path, "background.json"),
                InstallRoot = refused.InstallRoot,
                DataRoot = data.Path,
                TaskName = "BrowserAI.app sign-in refused",
                Executable = @"D:\Tools\BrowserAI.app\current\BrowserAI.exe",
                Build = "9.9.9",
                LogPath = log,
                NotSetUp = refused.AsRootRefusal(),
                ReadTask = static _ => throw new InvalidOperationException("a refused copy's relay reads no task"),
                FindUpdater = static _ => throw new InvalidOperationException("a refused copy's relay looks for no updater"),
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var absence = finder.Explain(null);

        await Assert.That(absence).IsTypeOf<BackgroundAbsence.RootRefused>();

        var said = RelayErrors.RootRefused("browser_navigate", ((BackgroundAbsence.RootRefused)absence).Refusal, log);

        await Assert.That(said).Contains($"install root '{refused.InstallRoot}'");
        await Assert.That(said).Contains("'browser_navigate' was NOT run");
        await Assert.That(said).Contains("The person at this computer needs to uninstall this copy, then run BrowserAI.exe again without --installto.");
        await Assert.That(said).Contains("until it is put right");
    }

    /// <summary>
    /// The root judgement every background and sweep makes refuses a shipping copy outside
    /// the standard folder before it asks the profile, in the standard location's words.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against the judgement that read the profile alone: a
    /// scratch folder inside the profile was served.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRootJudgementRefusesARefusedCopyBeforeItAsksTheProfile()
    {
        // Both under the profile, so that only the standard folder can refuse them.
        using var install = ScratchDirectory.CreateUnderProfile("refused-copy-scope");
        using var data = ScratchDirectory.CreateUnderProfile("refused-copy-scope-data");

        var refused = InstallRootScope.Judge(data.Path, install.Path, ReleaseLayout.PackId);

        await Assert.That(refused.MayServe).IsFalse();
        await Assert.That(refused.Refusal!).EndsWith(StandardLocation.Remedy);
        await Assert.That(refused.Detail!.Which).IsEqualTo(JudgedRoot.Install);
        await Assert.That(refused.Detail.Remedy).StartsWith("uninstall this copy");

        // The test pack in the same folder is judged against the profile alone, and served.
        await Assert.That(InstallRootScope.Judge(data.Path, install.Path, ReleaseLayout.TestPackId).MayServe).IsTrue();
    }
}
