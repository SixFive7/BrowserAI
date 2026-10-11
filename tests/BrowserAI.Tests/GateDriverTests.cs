// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Each gate driver finishes its run while a child the run left behind still holds what
/// the run wrote.
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
/// the three scripts it calls, with a <c>dotnet</c> first on its search path that is the
/// test probe: it writes a line, starts a process that inherits its standard output and
/// holds it for twice <see cref="TestDefaults.ProcessHang"/>, writes that process's pid and
/// creation time, and exits. The wait for the driver is that hang detector, so a driver
/// that waits for the end of a pipe fails it and one that waits for the process is done
/// in seconds. Everything an arm starts is in a job of the arm's own, whose close ends the
/// holder when the arm ends.
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
    }

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
