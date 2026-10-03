# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch supervisor for the debugger-tools probe. Starts one rig process inside a
# job object this script creates (KILL_ON_JOB_CLOSE), so everything the rig starts
# -- the @playwright/mcp child and its browser -- dies when the job handle closes,
# and nothing is ever selected by image name. Polls the job's active process count
# to a TSV so a run can be read against it afterwards.
param(
    [Parameter(Mandatory)][string]$Node,
    [Parameter(Mandatory)][string]$Script,
    [Parameter(Mandatory)][string]$OutDir,
    [Parameter(Mandatory)][string[]]$RigArgs,
    [int]$MaxSeconds = 240
)

$ErrorActionPreference = 'Stop'

if (-not ('DbgJob' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DbgJob {
  [StructLayout(LayoutKind.Sequential)]
  public struct BASIC { public long a; public long b; public uint LimitFlags; public UIntPtr c; public UIntPtr d; public uint e; public UIntPtr f; public uint g; public uint h; }
  [StructLayout(LayoutKind.Sequential)]
  public struct IO { public ulong a, b, c, d, e, f; }
  [StructLayout(LayoutKind.Sequential)]
  public struct EXT { public BASIC Basic; public IO Io; public UIntPtr a; public UIntPtr b; public UIntPtr c; public UIntPtr d; }
  [StructLayout(LayoutKind.Sequential)]
  public struct ACCT { public long a, b, c, d; public uint PageFaults, TotalProcesses, ActiveProcesses, TotalTerminated; }
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObjectW(IntPtr attrs, string name);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int cls, ref EXT info, int len);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(IntPtr job, int cls, out ACCT info, int len, IntPtr ret);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
  public static IntPtr Create() {
    IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
    if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
    EXT info = new EXT();
    info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(EXT)))) throw new System.ComponentModel.Win32Exception();
    return job;
  }
  public static void Assign(IntPtr job, IntPtr process) { if (!AssignProcessToJobObject(job, process)) throw new System.ComponentModel.Win32Exception(); }
  public static string Counts(IntPtr job) { ACCT a; if (!QueryInformationJobObject(job, 1, out a, Marshal.SizeOf(typeof(ACCT)), IntPtr.Zero)) return "-1\t-1"; return a.ActiveProcesses + "\t" + a.TotalProcesses; }
  public static void Close(IntPtr job) { CloseHandle(job); }
}
'@
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$supLog = Join-Path $OutDir 'supervisor.log'
$activeTsv = Join-Path $OutDir 'job-active.tsv'
"epochMs`tactive`ttotal" | Set-Content -LiteralPath $activeTsv
function Log([string]$m) { "[{0}] {1}" -f ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()), $m | Add-Content -LiteralPath $supLog }

$job = [DbgJob]::Create()
Log "job created"

$psi = [System.Diagnostics.ProcessStartInfo]::new($Node)
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.WorkingDirectory = $OutDir
$psi.ArgumentList.Add($Script)
foreach ($a in $RigArgs) { $psi.ArgumentList.Add($a) }

$p = [System.Diagnostics.Process]::Start($psi)
$started = $p.StartTime.ToUniversalTime().ToString('o')
[DbgJob]::Assign($job, $p.Handle)
Log ("rig pid={0} start={1} assigned to job" -f $p.Id, $started)
$outTask = $p.StandardOutput.ReadToEndAsync()
$errTask = $p.StandardError.ReadToEndAsync()
Set-Content -LiteralPath (Join-Path $OutDir 'go.flag') -Value 'go'

$deadline = [DateTime]::UtcNow.AddSeconds($MaxSeconds)
while (-not $p.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    "{0}`t{1}" -f ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()), [DbgJob]::Counts($job) | Add-Content -LiteralPath $activeTsv
    Start-Sleep -Milliseconds 500
}
if (-not $p.HasExited) {
    Log "TIMEOUT after $MaxSeconds s: closing the job (kills the rig and everything it started)"
}
else {
    Log ("rig exited code={0}" -f $p.ExitCode)
}
# What was left in the job after the rig itself finished.
for ($i = 0; $i -lt 6; $i++) {
    "{0}`t{1}" -f ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()), [DbgJob]::Counts($job) | Add-Content -LiteralPath $activeTsv
    Start-Sleep -Milliseconds 500
}
Log ("left in job before close: {0}" -f [DbgJob]::Counts($job))
[DbgJob]::Close($job)
Log "job closed"
try { [void]$outTask.Wait(5000); Set-Content -LiteralPath (Join-Path $OutDir 'rig.stdout.txt') -Value $outTask.Result } catch { Log "stdout read failed: $_" }
try { [void]$errTask.Wait(5000); Set-Content -LiteralPath (Join-Path $OutDir 'rig.stderr.txt') -Value $errTask.Result } catch { Log "stderr read failed: $_" }
