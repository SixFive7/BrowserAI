// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Each gate driver finishes its run while a child the run left behind still holds what
/// the run wrote, and copies every kept folder into its run's records.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>64 c (a), the maintainer's words verbatim: <i>"64 c now"</i>.</b> Until
/// 2026-10-11 every driver read <c>dotnet test</c> through a pipe, a <c>Tee-Object</c> in
/// PowerShell and a <c>tee</c> in Git Bash, and a pipe ends only when every process that
/// holds its write end has closed it. Twice that day a frozen child of a test host held it
/// after the run had printed its summary, and the driver waited with nothing in any log to
/// say why: a suspended git a real Claude Code's git launcher left behind, pid 102292, and a
/// suspended <c>cmd.exe</c> of a test host that had crashed, pid 23920 (HAZARDS.md, the
/// frozen-child wedge). Since then a driver hands the run a file, waits for the run's own
/// process, and copies the file into its own output as the file grows.
/// </para>
/// <para>
/// <b>Each arm runs the real driver</b>, copied into a scratch tree beside stand-ins for
/// the three scripts it calls, with a <c>dotnet</c> of its own first on its search path.
/// The first arm's is the test probe: it writes a line, starts a process that inherits its
/// standard output and holds it for twice <see cref="TestDefaults.ProcessHang"/>, writes
/// that process's pid and creation time, and exits. The wait for the driver is that hang
/// detector, so a driver that waits for the end of a pipe fails it and one that waits for
/// the process is done in seconds. Everything an arm starts is in a job of the arm's own,
/// whose close ends the holder when the arm ends. The second arm's keeps a folder, as a red
/// arm does, and exits.
/// </para>
/// </remarks>
internal sealed class GateDriverTests
{
    /// <summary>The test probe, which plays the run.</summary>
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// A driver finishes, says it is done and has copied what the run wrote, while the
    /// process the run left behind is still alive and still holds the run's output.
    /// </summary>
    /// <param name="driver">The driver's file name under <c>build/</c>.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("Invoke-OrdinaryGate.ps1")]
    [Arguments("Invoke-ReleaseGate.ps1")]
    [Arguments("invoke-ordinary-gate.sh")]
    [Arguments("invoke-release-gate.sh")]
    public async Task ADriverFinishesWhileAChildItsRunLeftBehindStillHoldsTheRunsOutput(string driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        var bash = driver.EndsWith(".sh", StringComparison.Ordinal);

        if (bash)
        {
            await SuiteEnvironment.RequireGitAsync();
        }

        var shell = bash ? Shells.GitBash : Shells.PowerShell;

        await Assert.That(shell).IsNotNull()
            .Because(bash
                ? "git is on the search path and no bin\\bash.exe was found beside it, so Git Bash's half of the gate cannot be run here"
                : "no pwsh.exe is on the search path, and every PowerShell driver runs under it");

        using var sandbox = await ScratchDirectory.CreateAsync("gate-driver");

        var build = Directory.CreateDirectory(Path.Combine(sandbox.Path, "build")).FullName;
        var standIn = Directory.CreateDirectory(Path.Combine(sandbox.Path, bash ? "bin-bash" : "bin")).FullName;
        var document = Path.Combine(sandbox.Path, "document.txt");
        var holderReport = Path.Combine(sandbox.Path, "holder.txt");
        var marker = $"the stand-in run of {driver}, {Guid.NewGuid():N}";
        var hold = (TestDefaults.ProcessHang * 2).TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);

        await File.WriteAllTextAsync(document, marker + "\n");
        await CopyAsync(Path.Combine(RepositoryLayout.Root.FullName, "build", driver), Path.Combine(build, driver));
        await WriteStandInsAsync(build);

        if (bash)
        {
            // LF only: MSYS reads a carriage return as part of the last argument.
            await File.WriteAllTextAsync(
                Path.Combine(standIn, "dotnet"),
                "#!/usr/bin/env bash\n"
                + $"\"{ProbePath.Replace('\\', '/')}\" output-holder \"{document}\" {hold} \"{holderReport}\"\n");
        }
        else
        {
            await File.WriteAllTextAsync(
                Path.Combine(standIn, "dotnet.cmd"),
                $"@\"{ProbePath}\" output-holder \"{document}\" {hold} \"{holderReport}\"\r\n");
        }

        var release = driver.Contains("Release", StringComparison.OrdinalIgnoreCase);
        var tag = "gate-arm";
        var runTag = release ? $"{tag}-1" : tag;
        var script = Path.Combine(build, driver);

        string[] arguments = (bash, release) switch
        {
            (false, false) => ["-NoProfile", "-NonInteractive", "-File", script, "-Tag", tag],
            (false, true) => ["-NoProfile", "-NonInteractive", "-File", script, "-Runs", "1", "-Prefix", tag],
            (true, false) => ["--noprofile", "--norc", script.Replace('\\', '/'), tag],
            (true, true) => ["--noprofile", "--norc", script.Replace('\\', '/'), "1", tag],
        };

        var environment = ChildEnvironment.Build(
        [
            new("PATH", standIn + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")),
        ]);

        using var scope = new JobObjectScope();
        var running = await scope.LaunchWithAsync(shell!, sandbox.Path, environment, arguments);

        var finished = await running.WaitForExitAsync(TestDefaults.ProcessHang);
        var holder = File.Exists(holderReport) ? await File.ReadAllTextAsync(holderReport) : null;
        var said = await scope.SaidByAsync(running.Id);

        await Assert.That(holder).IsNotNull()
            .Because($"the stand-in dotnet never wrote '{holderReport}', so the run this arm is about did not happen. The driver said: {said}");

        var (pid, created) = Identity(holder!);

        await Assert.That(finished).IsTrue()
            .Because($"{driver} had not finished {TestDefaults.ProcessHang.TotalMinutes:F0} minutes after it started, while process {pid} its run left behind held the run's output; a driver that waits for the end of a pipe waits for that process. It said: {said}");

        // ⚠️ THE PREMISE, AFTER THE DRIVER HAS FINISHED: the process holding the run's
        // output was still alive, or the driver finishing proves nothing about it.
        await Assert.That(ProcessLiveness.IsAlive(pid, created)).IsTrue()
            .Because($"the holder, pid {pid}, had already gone when {driver} finished, so nothing held the run's output");

        await Assert.That(said).Contains(Outcome(driver));
        await Assert.That(said).Contains(marker).Because("a driver copies what its run writes into its own output as the run goes");

        var log = Path.Combine(sandbox.Path, ".work", "suite", $"{runTag}.log");

        await Assert.That(File.Exists(log)).IsTrue().Because($"{driver} keeps each run's output in .work/suite/{runTag}.log");
        await Assert.That(await ReadSharedAsync(log)).Contains(marker);

        // A run that kept nothing says so, 119: this tree has no .work\kept at all.
        await Assert.That(said).Contains(@"kept folders: none under .work\kept").Because(said);
    }

    /// <summary>
    /// A driver copies every folder under <c>.work\kept</c> into its run's records, beside
    /// the run's log, and names each one in its own output and in the run's log, with
    /// whether its run kept it, and names a folder it could not copy whole as well.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The maintainer's 119, 2026-10-12, in his words verbatim:</b> <i>"119 your
    /// recommendation"</i>, which has the drivers collect what a red arm keeps
    /// (<see cref="KeptOnRed"/>). Until then nothing did, so a kept folder reached a gate's
    /// records only when somebody read the failure and copied the folder it named.
    /// </para>
    /// <para>
    /// <b>Three folders</b>: one kept before the driver started; one the stand-in run keeps
    /// while it runs, as a red arm does; and one kept before whose one file this arm holds
    /// open with no sharing while the driver runs, so its copy fails. Planted red against
    /// the four drivers before they copied anything.
    /// </para>
    /// </remarks>
    /// <param name="driver">The driver's file name under <c>build/</c>.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("Invoke-OrdinaryGate.ps1")]
    [Arguments("Invoke-ReleaseGate.ps1")]
    [Arguments("invoke-ordinary-gate.sh")]
    [Arguments("invoke-release-gate.sh")]
    public async Task ADriverCopiesEveryKeptFolderIntoItsRunsRecordsAndNamesEachOne(string driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        var bash = driver.EndsWith(".sh", StringComparison.Ordinal);

        if (bash)
        {
            await SuiteEnvironment.RequireGitAsync();
        }

        var shell = bash ? Shells.GitBash : Shells.PowerShell;

        await Assert.That(shell).IsNotNull()
            .Because(bash
                ? "git is on the search path and no bin\\bash.exe was found beside it, so Git Bash's half of the gate cannot be run here"
                : "no pwsh.exe is on the search path, and every PowerShell driver runs under it");

        using var sandbox = await ScratchDirectory.CreateAsync("gate-driver-kept");

        var build = Directory.CreateDirectory(Path.Combine(sandbox.Path, "build")).FullName;
        var standIn = Directory.CreateDirectory(Path.Combine(sandbox.Path, bash ? "bin-bash" : "bin")).FullName;
        var kept = Path.Combine(sandbox.Path, ".work", "kept");
        var unique = Guid.NewGuid().ToString("N")[..8];
        var before = $"20261012T000000Z-1-before-{unique}";
        var during = $"20261012T000001Z-2-during-{unique}";
        var held = $"20261012T000002Z-3-held-{unique}";
        var marker = $"the stand-in run of {driver}, {unique}";

        foreach (var name in new[] { before, held })
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(RecordIn(kept, name))!);
            await File.WriteAllTextAsync(RecordIn(kept, name), $"the record of {name}");
        }

        await CopyAsync(Path.Combine(RepositoryLayout.Root.FullName, "build", driver), Path.Combine(build, driver));
        await WriteStandInsAsync(build);

        var keepsDuring = Path.GetDirectoryName(RecordIn(kept, during))!;

        if (bash)
        {
            // LF only: MSYS reads a carriage return as part of the last argument.
            await File.WriteAllTextAsync(
                Path.Combine(standIn, "dotnet"),
                "#!/usr/bin/env bash\n"
                + $"mkdir -p \"{keepsDuring.Replace('\\', '/')}\"\n"
                + $"printf '%s' 'the record of {during}' > \"{RecordIn(kept, during).Replace('\\', '/')}\"\n"
                + $"echo '{marker}'\n");
        }
        else
        {
            await File.WriteAllTextAsync(
                Path.Combine(standIn, "dotnet.cmd"),
                $"@mkdir \"{keepsDuring}\"\r\n"
                + $"@echo the record of {during}> \"{RecordIn(kept, during)}\"\r\n"
                + $"@echo {marker}\r\n");
        }

        var release = driver.Contains("Release", StringComparison.OrdinalIgnoreCase);
        var tag = "gate-arm";
        var runTag = release ? $"{tag}-1" : tag;
        var script = Path.Combine(build, driver);

        string[] arguments = (bash, release) switch
        {
            (false, false) => ["-NoProfile", "-NonInteractive", "-File", script, "-Tag", tag],
            (false, true) => ["-NoProfile", "-NonInteractive", "-File", script, "-Runs", "1", "-Prefix", tag],
            (true, false) => ["--noprofile", "--norc", script.Replace('\\', '/'), tag],
            (true, true) => ["--noprofile", "--norc", script.Replace('\\', '/'), "1", tag],
        };

        var environment = ChildEnvironment.Build(
        [
            new("PATH", standIn + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")),
        ]);

        bool finished;
        string said;

        // The held folder's one file, open with no sharing for as long as the driver runs.
        await using (new FileStream(RecordIn(kept, held), FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            using var scope = new JobObjectScope();
            var running = await scope.LaunchWithAsync(shell!, sandbox.Path, environment, arguments);

            finished = await running.WaitForExitAsync(TestDefaults.ProcessHang);
            said = await scope.SaidByAsync(running.Id);
        }

        await Assert.That(finished).IsTrue()
            .Because($"{driver} had not finished {TestDefaults.ProcessHang.TotalMinutes:F0} minutes after it started. It said: {said}");
        await Assert.That(said).Contains(Outcome(driver)).Because(said);
        await Assert.That(said).Contains(marker).Because(said);

        // Named from the driver's tree, as the driver names them.
        var records = Path.Combine(".work", "suite", $"{runTag}-kept");
        var log = await ReadSharedAsync(Path.Combine(sandbox.Path, ".work", "suite", $"{runTag}.log"));

        foreach (var (name, when) in new[] { (before, "kept before this run"), (during, "kept during this run") })
        {
            var copied = $"kept folder copied: {name}, {when}, to {Path.Combine(records, name)}";

            await Assert.That(said).Contains(copied).Because(said);
            await Assert.That(log).Contains(copied).Because(log);
            await Assert.That((await File.ReadAllTextAsync(RecordIn(Path.Combine(sandbox.Path, records), name))).Trim()).IsEqualTo($"the record of {name}");
            await Assert.That(File.Exists(RecordIn(kept, name))).IsTrue().Because("a kept folder is copied and stays where it was kept");
        }

        var notWhole = $"kept folder {held}, kept before this run, could not be copied whole to {Path.Combine(records, held)}: ";

        await Assert.That(said).Contains(notWhole).Because(said);
        await Assert.That(log).Contains(notWhole).Because(log);
    }

    /// <summary>The one file of a kept folder these arms write: its session's record.</summary>
    /// <param name="kept">The folder the kept folder is in.</param>
    /// <param name="name">The kept folder's name.</param>
    /// <returns>The file's path.</returns>
    private static string RecordIn(string kept, string name) => Path.Combine(kept, name, "a-session", "browserai.data");

    /// <summary>The line a driver ends a clean gate with.</summary>
    /// <param name="driver">The driver's file name.</param>
    /// <returns>The line.</returns>
    private static string Outcome(string driver) => driver switch
    {
        "Invoke-OrdinaryGate.ps1" => "ORDINARY-PS-DONE",
        "Invoke-ReleaseGate.ps1" => "RELEASE-PS-DONE",
        "invoke-ordinary-gate.sh" => "ORDINARY-BASH-DONE",
        _ => "RELEASE-BASH-DONE",
    };

    /// <summary>A process's pid and creation time, as the probe wrote them.</summary>
    /// <param name="line">The probe's line.</param>
    /// <returns>The two numbers.</returns>
    private static (int Pid, long Created) Identity(string line)
    {
        var parts = line.Trim().Split(' ');

        return (int.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    /// <summary>Copies one file, asynchronously at both ends.</summary>
    /// <param name="source">The file to copy.</param>
    /// <param name="destination">Where the copy goes; it must not exist yet.</param>
    /// <returns>The copy.</returns>
    private static async Task CopyAsync(string source, string destination)
    {
        await using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        await using var to = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);

        await from.CopyToAsync(to);
    }

    /// <summary>A file another process may still hold, read without taking it.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// The three scripts a driver calls, as stand-ins that take nothing and change nothing
    /// outside the scratch tree.
    /// </summary>
    /// <remarks>
    /// <b>The installer lock is the suite's own while this host runs</b>, so a real take would
    /// wait for this very process; the test pack needs a publish this arm has no reason to
    /// make; and the clearance snapshot reads the machine. The stand-ins answer the way the
    /// real ones answer a clean gate: a token, a pack that worked, and two snapshots that are
    /// the same.
    /// </remarks>
    /// <param name="build">The scratch tree's <c>build</c> directory.</param>
    /// <returns>The writes.</returns>
    private static async Task WriteStandInsAsync(string build)
    {
        await File.WriteAllTextAsync(
            Path.Combine(build, "InstallerLock.ps1"),
            "param([switch] $Take, [switch] $Release, [int] $HolderPid)\n"
            + "if ($Take) { \"pid=$HolderPid created=0\" }\n"
            + "exit 0\n");

        await File.WriteAllTextAsync(
            Path.Combine(build, "New-Release.ps1"),
            "param([switch] $TestPackOnly, [switch] $FromReleasePublish)\nexit 0\n");

        await File.WriteAllTextAsync(
            Path.Combine(build, "Get-ClearanceSnapshot.ps1"),
            "param([string] $Tag)\n"
            + "$directory = Join-Path ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) '.work' 'clearance'\n"
            + "$null = New-Item -ItemType Directory -Force -Path $directory\n"
            + "Set-Content -LiteralPath (Join-Path $directory \"$Tag.txt\") -Value 'the same machine'\n");
    }
}
