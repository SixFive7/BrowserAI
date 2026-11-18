// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// What each place that runs RegisterAI gives it, and that every Velopack hook's longest
/// path fits the limit Velopack kills it at.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The maintainer's 174, 2026-11-18, in his words verbatim:</b> <i>"174 rec - also
/// explain to me why velopack even has this limit and cehck if we cannot disable or
/// increase that limit"</i>. Velopack kills <c>--veloapp-install</c> at 30 s,
/// <c>--veloapp-updated</c> at 15 s and <c>--veloapp-uninstall</c> at 60 s, literals in
/// its own code with no option to change them, and until that day every registration
/// had 12 s and a 14 s wait, which with the sign-in task and the drain of RegisterAI's
/// output after a stop could reach 19 s inside the update hook's 15. The recommendation
/// he took: the update hook registers nothing and the background's first start after
/// the update runs the pass with RegisterAI's own 30 s; the install hook gets about 24 s
/// of its 30; the page and the status read get 30 s; the drain is capped by what is left
/// of the budget; and the uninstall's worst case fits with a margin.
/// </para>
/// <para>
/// <b>Planted red 2026-11-18</b>, each arm against the tree before the change, with the
/// members it calls added as stubs that keep the old behaviour; the remarks on each arm
/// say what it read.
/// </para>
/// </remarks>
internal sealed class RegistrationBudgetTests
{
    /// <summary>
    /// The update hook starts no RegisterAI and writes no record, and still does its local
    /// steps: the PATH entry and the sign-in task.
    /// </summary>
    /// <remarks>
    /// Red against the hook as it was: one <c>register</c> call, and a record on disk.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdateHookRunsNoRegistrationAndStillDoesItsLocalSteps()
    {
        using var install = await ScratchDirectory.CreateAsync("budget-update-hook");
        using var data = await ScratchDirectory.CreateAsync("budget-update-hook-data");

        var image = await InstalledLayout.CreateAsync(install.Path);
        var tool = new FakeRegisterAi();

        var outcome = await HookRegistration.RunAsync(
            RegistrationIntent.Update,
            "9.9.9",
            image,
            tool,
            new LocalAppDataPaths(data.Path),
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId);

        await Assert.That(tool.Calls).IsEmpty();
        await Assert.That(outcome.Registrations).IsEmpty();
        await Assert.That(File.Exists(RegistrationRecord.PathFor(data.Path))).IsFalse();
        await Assert.That(outcome.PathEntry).IsNotNull();
        await Assert.That(outcome.SignInTask).IsNotNull();
    }

    /// <summary>
    /// The background's first start after an update runs the update's pass for every
    /// client with RegisterAI's own budget, and writes the record with the update's intent.
    /// </summary>
    /// <remarks>
    /// Red against a stub that ran nothing and wrote nothing, the update's pass as it stood
    /// outside the hook before the change.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheFirstStartAfterAnUpdateRunsThePassWithRegisterAisOwnBudgetAndWritesTheRecord()
    {
        using var install = await ScratchDirectory.CreateAsync("budget-after-update");
        using var data = await ScratchDirectory.CreateAsync("budget-after-update-data");

        var image = await InstalledLayout.CreateAsync(install.Path);
        var tool = new FakeRegisterAi();

        var passes = await AfterUpdateRegistration.RunAsync(image, tool, data.Path, "9.9.9", ScratchLogonTasks.AppId, NullLogger.Instance);

        var call = tool.Calls.Single();

        await Assert.That(call[0]).IsEqualTo("register");
        await Assert.That(FakeRegisterAi.Option(call, "--client")).IsEqualTo("all");
        await Assert.That(FakeRegisterAi.Option(call, "--timeout")).IsEqualTo(Seconds(ProcessBounds.RegisterAiTimeout));
        await Assert.That(tool.Waits.Single()).IsEqualTo(ProcessBounds.RegisterAiBudget);
        await Assert.That(FakeRegisterAi.Command(call)).IsEqualTo(InstalledLayout.ServerIn(install.Path));

        foreach (var pass in passes)
        {
            await Assert.That(pass.Report.Status).IsEqualTo(RegistrationStatus.Registered).Because(pass.Report.Detail);
        }

        var written = await File.ReadAllTextAsync(RegistrationRecord.PathFor(data.Path));

        await Assert.That(written).Contains("\"intent\": \"Update\"");
        await Assert.That(written).Contains("\"browserAiVersion\": \"9.9.9\"");
    }

    /// <summary>
    /// A background runs that pass when the task started it after an update, and at no
    /// other start.
    /// </summary>
    /// <remarks>
    /// <b>What this holds is the decision, and not the call</b>: a background started for
    /// real would run the RegisterAI beside the test host against the person's own
    /// clients. Red against a stub that answered no for every start.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OnlyTheStartAfterAnUpdateRunsThePass()
    {
        await Assert.That(Program.RegistersAfterStarting(BrowserAI.App.PersonStart.StartedAfterAnUpdate)).IsTrue();
        await Assert.That(Program.RegistersAfterStarting(BrowserAI.App.PersonStart.StartedByPerson)).IsFalse();
        await Assert.That(Program.RegistersAfterStarting(BrowserAI.App.PersonStart.StartedByTheInstaller)).IsFalse();
        await Assert.That(Program.RegistersAfterStarting("$(Arg0)")).IsFalse();
        await Assert.That(Program.RegistersAfterStarting(null)).IsFalse();
    }

    /// <summary>The installer's log says where an update's registration went.</summary>
    /// <remarks>Red against the mirror as it was, which wrote nothing for an update with no pass.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstallersLogSaysAnUpdatesRegistrationRunsAfterTheHook()
    {
        var lines = new List<string>();

        VelopackStartup.Mirror(new HookOutcome([], null), RegistrationIntent.Update, "9.9.9", (_, message, _) => lines.Add(message));

        await Assert.That(lines.Any(line => line.Contains("MCP registration (Update)", StringComparison.Ordinal) && line.Contains("first start after this update", StringComparison.Ordinal)))
            .IsTrue().Because(string.Join(Environment.NewLine, lines));
    }

    /// <summary>The install hook gives RegisterAI the install's budget.</summary>
    /// <remarks>Red against the hook as it was: <c>--timeout 12</c> and a 14 s wait.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstallHookGivesRegisterAiTheInstallsBudget()
    {
        using var install = await ScratchDirectory.CreateAsync("budget-install-hook");
        using var data = await ScratchDirectory.CreateAsync("budget-install-hook-data");

        var image = await InstalledLayout.CreateAsync(install.Path);
        var tool = new FakeRegisterAi();

        _ = await HookRegistration.RunAsync(
            RegistrationIntent.Install,
            "9.9.9",
            image,
            tool,
            new LocalAppDataPaths(data.Path),
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId);

        await Assert.That(FakeRegisterAi.Option(tool.Calls.Single(), "--timeout")).IsEqualTo(Seconds(ProcessBounds.RegisterAiInstallTimeout));
        await Assert.That(tool.Waits.Single()).IsEqualTo(ProcessBounds.RegisterAiInstallBudget);
    }

    /// <summary>
    /// The uninstall hook gives RegisterAI what the background's stop left of Velopack's
    /// 60 s, never less than the least budget and never more than RegisterAI's own.
    /// </summary>
    /// <remarks>
    /// With no background to stop, the stop takes nothing and the run gets RegisterAI's own
    /// budget. Red against a fixed budget, which gave <c>--timeout 12</c> whatever the stop
    /// took.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUninstallHookGivesRegisterAiWhatTheStopLeft()
    {
        using var install = await ScratchDirectory.CreateAsync("budget-uninstall-hook");
        using var data = await ScratchDirectory.CreateAsync("budget-uninstall-hook-data");

        var image = await InstalledLayout.CreateAsync(install.Path);
        var tool = new FakeRegisterAi();

        _ = await HookRegistration.RunAsync(
            RegistrationIntent.Uninstall,
            "9.9.9",
            image,
            tool,
            new LocalAppDataPaths(data.Path),
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId);

        await Assert.That(FakeRegisterAi.Option(tool.Calls.Single(), "--timeout")).IsEqualTo(Seconds(ProcessBounds.RegisterAiTimeout));
        await Assert.That(tool.Waits.Single()).IsEqualTo(ProcessBounds.RegisterAiBudget);

        // What is left once the stop has taken some of the 60 s, at three points: none, a
        // stop that leaves exactly the install's wait, and the whole bound and past it.
        await Assert.That(RegistrationBudget.ForUninstallAfter(TimeSpan.Zero)).IsEqualTo(RegistrationBudget.Unbounded);

        var leavesTheInstallsWait = ProcessBounds.VelopackUninstallHookLimit - ProcessBounds.ScheduledTasksCallBound - ProcessBounds.HookMargin - ProcessBounds.RegisterAiInstallBudget;

        await Assert.That(RegistrationBudget.ForUninstallAfter(leavesTheInstallsWait))
            .IsEqualTo(new RegistrationBudget(ProcessBounds.RegisterAiInstallBudget - (ProcessBounds.RegisterAiBudget - ProcessBounds.RegisterAiTimeout), ProcessBounds.RegisterAiInstallBudget));
        await Assert.That(RegistrationBudget.ForUninstallAfter(ProcessBounds.BackgroundStopBound)).IsEqualTo(RegistrationBudget.Uninstall);
        await Assert.That(RegistrationBudget.ForUninstallAfter(ProcessBounds.VelopackUninstallHookLimit)).IsEqualTo(RegistrationBudget.Uninstall);
    }

    /// <summary>
    /// The page's buttons and the status read give RegisterAI its own budget, where nothing
    /// outside BrowserAI limits the wait.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The values are written out</b>, 30 s and a 32 s wait, because they are what the
    /// maintainer's 174 decided: RegisterAI 0.3.0's own default and the margin it keeps. Every
    /// run is held, and a status read also reads the nearest project the walk from its folder
    /// finds, which from a scratch folder under this repository is the repository's own.
    /// </para>
    /// <para>Red against the registrar as it was: <c>--timeout 12</c> and a 14 s wait.</para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageAndTheStatusReadGiveRegisterAiItsOwnBudget()
    {
        using var install = await ScratchDirectory.CreateAsync("budget-page");

        var image = await InstalledLayout.CreateAsync(install.Path);
        var tool = new FakeRegisterAi();

        // What the page's Register does.
        _ = await McpRegistrar.ApplyAsync(RegistrationClient.ClaudeCode, RegistrationIntent.Install, image, tool, NullLogger.Instance, replace: true);

        // What the status read does.
        _ = await RegistrationReader.ReadAsync(tool, RegistrationClient.All, install.Path, InstalledLayout.ServerIn(install.Path), install.Path);

        var verbs = tool.Calls.Select(call => call[0]).ToList();

        await Assert.That(verbs).Contains("register");
        await Assert.That(verbs).Contains("status");
        await Assert.That(tool.Waits.Count).IsEqualTo(tool.Calls.Count);

        foreach (var call in tool.Calls)
        {
            await Assert.That(FakeRegisterAi.Option(call, "--timeout")).IsEqualTo("30").Because(string.Join(' ', call));
        }

        foreach (var wait in tool.Waits)
        {
            await Assert.That(wait).IsEqualTo(TimeSpan.FromSeconds(32));
        }
    }

    /// <summary>
    /// Every hook's longest path fits the limit Velopack kills it at, with the margin to
    /// spare: each bounded step at its bound, in the order the hook takes them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The install</b>: the registration's wait and the sign-in task's one call. <b>The
    /// update</b>: the sign-in task's call alone, since it runs no registration. <b>The
    /// uninstall</b>: the background's stop, at every whole second up to its bound, the
    /// registration's wait for what the stop left, and the task's removal. The PATH entry,
    /// the record, the toasts' activator and the folder links' scheme are registry and file
    /// writes of milliseconds, and the margin is for them and for the process's own start
    /// and end. The uninstall's data-root question is outside it on purpose: it is last,
    /// and it may wait for a person.
    /// </para>
    /// <para>
    /// Red against the budgets as they were: the uninstall's 45 s stop, 14 s wait and 5 s
    /// task call came to 64 s of its 60.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryHooksLongestPathFitsVelopacksLimitWithTheMargin()
    {
        var task = ProcessBounds.ScheduledTasksCallBound;
        var margin = ProcessBounds.HookMargin;

        await Assert.That(RegistrationBudget.Install.Wait + task + margin).IsLessThanOrEqualTo(ProcessBounds.VelopackInstallHookLimit);
        await Assert.That(task + margin).IsLessThanOrEqualTo(ProcessBounds.VelopackUpdateHookLimit);

        for (var stop = TimeSpan.Zero; stop <= ProcessBounds.BackgroundStopBound; stop += TimeSpan.FromSeconds(1))
        {
            await Assert.That(stop + RegistrationBudget.ForUninstallAfter(stop).Wait + task + margin)
                .IsLessThanOrEqualTo(ProcessBounds.VelopackUninstallHookLimit)
                .Because(string.Create(CultureInfo.InvariantCulture, $"a stop of {stop.TotalSeconds:F0} s"));
        }

        // And RegisterAI is always told to answer before BrowserAI stops waiting for it.
        foreach (var budget in new[] { RegistrationBudget.Unbounded, RegistrationBudget.Install, RegistrationBudget.Uninstall, RegistrationBudget.ForUninstallAfter(ProcessBounds.BackgroundStopBound / 2) })
        {
            await Assert.That(budget.Timeout).IsLessThan(budget.Wait).Because(budget.ToString());
        }
    }

    /// <summary>
    /// The drain of RegisterAI's output after a run takes what is left of the run's budget,
    /// never the full five seconds on top of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The arithmetic, and a real run.</b> The run is the suite's probe in its
    /// <c>registerai-lingering</c> shape: it writes a document, starts a copy of itself that
    /// holds the output open for twice the drain, and exits. Given half the drain as its
    /// budget, the run ends with that budget, whether the probe exited in time or was
    /// stopped at it. That the document is read while the copy holds the output is
    /// <c>RegisterAiTests.ADocumentRegisterAiWroteBeforeItExitedIsReadWhileAProcessItLeftHoldsItsOutput</c>,
    /// with the budget a run really gets.
    /// </para>
    /// <para>
    /// Red against the drain as it was, a fixed <see cref="ProcessBounds.RegisterAiOutputDrain"/>
    /// after the run: the probe's run took the whole drain.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDrainTakesWhatIsLeftOfTheRunsBudget()
    {
        var drain = ProcessBounds.RegisterAiOutputDrain;
        var budget = ProcessBounds.RegisterAiBudget;

        await Assert.That(RegisterAiTool.DrainFor(budget, TimeSpan.Zero)).IsEqualTo(drain);
        await Assert.That(RegisterAiTool.DrainFor(budget, budget - (drain / 2))).IsEqualTo(drain / 2);
        await Assert.That(RegisterAiTool.DrainFor(budget, budget)).IsEqualTo(TimeSpan.Zero);
        await Assert.That(RegisterAiTool.DrainFor(budget, budget + drain)).IsEqualTo(TimeSpan.Zero);

        using var scratch = await ScratchDirectory.CreateAsync("budget-drain");

        var documentPath = Path.Combine(scratch.Path, "document.json");
        await File.WriteAllTextAsync(documentPath, """{"tool":"registerai","schema":1,"verb":"status","exitCode":0,"error":null,"results":[]}""");

        var probe = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");
        var hold = (drain * 2).TotalSeconds.ToString(CultureInfo.InvariantCulture);
        var clock = Stopwatch.StartNew();

        _ = await new RegisterAiTool(probe).RunAsync(["registerai-lingering", documentPath, hold], drain / 2);

        clock.Stop();

        await Assert.That(clock.Elapsed).IsLessThan(drain).Because("a run given half the drain as its budget took the whole drain after it");
    }

    /// <summary>A duration as RegisterAI's <c>--timeout</c> spells it.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>Whole seconds.</returns>
    private static string Seconds(TimeSpan duration) => duration.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);
}
