// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The sign-in step and the path scan it gates on: a staged package is applied
/// only when nothing else runs from the install root.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q282 a and Q285 a, the maintainer's words verbatim: <i>"Q282 a"</i> and
/// <i>"Q285 a"</i>.</b> <c>BrowserAI.exe --sign-in</c> asks Velopack whether a newer
/// package is staged, scans the processes whose image lies under the install root
/// with itself left out -- exactly the set Velopack's kill pass would end -- and
/// hands the package to <c>Update.exe</c>, silent and without a restart, only when
/// that scan is empty.
/// </para>
/// <para>
/// <b>The scan is the product's own and real; the staged package and the apply are
/// seams.</b> What runs from the scratch root is a stand-in, a copy of
/// <c>cmd.exe</c> waiting on a standard input nothing writes to, in a job the arm
/// owns and ends, the shape <see cref="UpdateInProgressTests"/> uses for the
/// updater. It is found by its path and nothing else: a copy of the same file under
/// the same name outside the root is the control that proves it.
/// </para>
/// </remarks>
internal sealed class SignInStepTests
{
    /// <summary>Nothing staged is nothing to do: no scan, no apply.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NothingStagedIsNothingToDoAndNothingIsScanned()
    {
        var staged = new RecordedStaged { Candidate = null };
        var scans = 0;

        var report = SignInStep.Run(
            staged,
            () =>
            {
                scans++;
                return new RootScan([], []);
            },
            NullLogger.Instance);

        await Assert.That(report.Outcome).IsEqualTo(SignInOutcome.NothingStaged);
        await Assert.That(scans).IsEqualTo(0);
        await Assert.That(staged.Applied.Count).IsEqualTo(0);
    }

    /// <summary>A staged package and nothing running from the root is applied, once, with that package.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStagedPackageAndNothingRunningFromTheRootIsAppliedOnce()
    {
        using var root = ScratchDirectory.Create("sign-in-empty");

        var staged = new RecordedStaged { Candidate = Candidate("9.9.9") };

        var report = SignInStep.Run(staged, () => BrowserProcesses.HeldUnder(root.Path, Environment.ProcessId), NullLogger.Instance);

        await Assert.That(report.Outcome).IsEqualTo(SignInOutcome.Applied).Because(report.Why);
        await Assert.That(staged.Applied.Select(candidate => candidate.Version)).IsEquivalentTo(["9.9.9"]);
    }

    /// <summary>
    /// A process running from the install root blocks the apply, and the log names
    /// its pid and its image.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProcessRunningFromTheRootBlocksTheApplyAndIsNamed()
    {
        using var root = ScratchDirectory.Create("sign-in-busy");
        using var job = JobObject.CreateKillOnClose();
        using var standIn = StartAStandIn(job, Path.Combine(root.Path, RegistrationTarget.CurrentDirectoryName));

        var staged = new RecordedStaged { Candidate = Candidate("9.9.9") };

        var report = SignInStep.Run(staged, () => BrowserProcesses.HeldUnder(root.Path, Environment.ProcessId), NullLogger.Instance);

        await Assert.That(report.Outcome).IsEqualTo(SignInOutcome.NotAlone).Because(report.Why);
        await Assert.That(staged.Applied.Count).IsEqualTo(0);
        await Assert.That(report.Why).Contains($"pid {standIn.Id} ");
        await Assert.That(report.Why).Contains(StandInName);
    }

    /// <summary>
    /// The scan finds by full path: the same file under the same name outside the
    /// root is not counted, the pid it is told to leave out is left out, and a
    /// process that has exited is gone from the next scan.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheScanFindsByPathLeavesOutThePidItIsGivenAndDropsAnExitedProcess()
    {
        using var root = ScratchDirectory.Create("sign-in-scan");
        using var outside = ScratchDirectory.Create("sign-in-scan-outside");
        using var insideJob = JobObject.CreateKillOnClose();
        using var job = JobObject.CreateKillOnClose();
        using var inside = StartAStandIn(insideJob, Path.Combine(root.Path, RegistrationTarget.CurrentDirectoryName));
        using var elsewhere = StartAStandIn(job, Path.Combine(outside.Path, RegistrationTarget.CurrentDirectoryName));

        using (var scan = BrowserProcesses.HeldUnder(root.Path, Environment.ProcessId))
        {
            await Assert.That(scan.Held.Select(process => process.ProcessId)).IsEquivalentTo([inside.Id]);
            await Assert.That(scan.Held[0].ImagePath).IsEqualTo(Path.Combine(root.Path, RegistrationTarget.CurrentDirectoryName, StandInName), StringComparison.OrdinalIgnoreCase);
            await Assert.That(scan.Held[0].CreatedFileTime).IsEqualTo(ProcessIdentity.CreationTimeOf(inside.Id));
        }

        // The positive control for "by path": the copy outside is found under its own root.
        using (var theOther = BrowserProcesses.HeldUnder(outside.Path, Environment.ProcessId))
        {
            await Assert.That(theOther.Held.Select(process => process.ProcessId)).IsEquivalentTo([elsewhere.Id]);
        }

        // Left out when it is the pid given, which is how the coordinator leaves itself out.
        using (var itself = BrowserProcesses.HeldUnder(root.Path, inside.Id))
        {
            await Assert.That(itself.Held.Count).IsEqualTo(0);
        }

        // Ended with its own job, and gone from the next scan.
        insideJob.Dispose();
        await Assert.That(await inside.WaitForExitAsync(TestDefaults.InProcessHang)).IsTrue();

        using var after = BrowserProcesses.HeldUnder(root.Path, Environment.ProcessId);

        await Assert.That(after.Held.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The published app's <c>--sign-in</c> start becomes the coordinator, runs the
    /// step, finds nothing staged in a build that is not installed, and exits 0.
    /// </summary>
    /// <remarks>
    /// <b>The wiring, on the published binary</b>: that the mode reaches the step and
    /// the step reaches the log, which the child writes under its scratch data root.
    /// On a private desktop, because this start holds the pipe and a regression that
    /// made it a person's start would open the dialog.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedAppsSignInStartRunsTheStepAndExits()
    {
        PublishedSlice.EnsureAppFresh();

        using var root = ScratchDirectory.Create("sign-in-published");

        var environment = PublishedSlice.InheritedEnvironment();

        environment[BrowserAiPaths.AppRootOverride] = root.Path;
        environment[CodexRegistration.HomeVariable] = Directory.CreateDirectory(Path.Combine(root.Path, "codex")).FullName;

        using var desktop = PrivateDesktop.Create("sign-in-step");
        using var job = JobObject.CreateKillOnClose();
        using var process = desktop.Launch(job, PublishedSlice.AppExecutable, [CoordinatorProtocol.SignInArgument, "$(Arg0)"], root.Path, environment);

        var drained = Task.WhenAll(
            process.StandardOutput.CopyToAsync(Stream.Null),
            process.StandardError.CopyToAsync(Stream.Null));

        await Assert.That(await process.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await drained;
        await Assert.That(process.TryReadExitCode()).IsEqualTo(0);

        var logs = Directory.EnumerateFiles(Path.Combine(root.Path, "logs"), "*.log", SearchOption.AllDirectories).ToList();
        var text = string.Join("\n", await Task.WhenAll(logs.Select(log => File.ReadAllTextAsync(log))));

        await Assert.That(text).Contains($"Sign-in step: {SignInOutcome.NothingStaged}");
        await Assert.That(desktop.TopLevelWindows().Count).IsEqualTo(0);
    }

    /// <summary>The file name every stand-in runs as.</summary>
    private const string StandInName = "BrowserAI.Server.exe";

    /// <summary>Copies <c>cmd.exe</c> into a folder under a name and starts it waiting on its own standard input, in the arm's job.</summary>
    /// <param name="job">The arm's job, which ends it.</param>
    /// <param name="folder">Where the copy goes.</param>
    /// <returns>The running stand-in.</returns>
    private static LaunchedProcess StartAStandIn(JobObject job, string folder)
    {
        var image = Path.Combine(Directory.CreateDirectory(folder).FullName, StandInName);

        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), image);

        // /d: no AutoRun commands; /q: no echo; /k: stay, reading stdin, which is
        // a pipe the launcher holds and nothing writes to.
        return JobLauncher.Start(job, image, ["/d", "/q", "/k"], folder, PublishedSlice.InheritedEnvironment());
    }

    /// <summary>A candidate the step can be handed.</summary>
    /// <param name="version">Its version.</param>
    /// <returns>The candidate.</returns>
    private static UpdateCandidate Candidate(string version) => new()
    {
        Version = version,
        IsDowngrade = false,
        DeltaCount = 0,
        FullPackageSize = 1,
    };

    /// <summary>A staged package the arm controls, and the applies asked of it.</summary>
    private sealed class RecordedStaged : IStagedUpdates
    {
        public UpdateCandidate? Candidate { get; set; }

        public List<UpdateCandidate> Applied { get; } = [];

        public UpdateCandidate? Pending() => Candidate;

        public void ApplyAfterThisProcessExits(UpdateCandidate candidate) => Applied.Add(candidate);
    }
}
