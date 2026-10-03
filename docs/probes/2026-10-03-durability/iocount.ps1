# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Runs iocount.js in a kill-on-close job this script
# creates, three times per mode, and prints the job's write counters before and after the writes.
$ErrorActionPreference = 'Stop'
$root = 'C:\Source\SixFive7\BrowserAI\.work\durability'
Add-Type -Path (Join-Path $root 'rig\hk.cs')
$node = Join-Path $root 'payload\node\node.exe'
$js = Join-Path $root 'iocount\iocount.js'
foreach ($rep in 1..3) {
  foreach ($mode in 'none', 'pipe', 'file') {
    $target = Join-Path $root "iocount\out-$mode-$rep.bin"
    $psi = [System.Diagnostics.ProcessStartInfo]::new($node)
    foreach ($a in @($js, $mode, $target)) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $job = [HkWin]::CreateKillOnCloseJob(); [HkWin]::Assign($job, $p)
    $sink = $p.StandardOutput.BaseStream.CopyToAsync([System.IO.Stream]::Null)
    $errTask = $p.StandardError.ReadToEndAsync()
    Start-Sleep -Milliseconds 300
    $io0 = [HkWin]::JobIo($job)
    $p.StandardInput.WriteLine('GO'); $p.StandardInput.Flush()
    [void]$p.WaitForExit(60000); $p.WaitForExit()
    [void]$sink.Wait(10000)
    $io1 = [HkWin]::JobIo($job)
    [void][HkWin]::CloseHandle($job)
    '{0} rep {1}: writeBytes +{2:N0}  writeOps +{3:N0}  readBytes +{4:N0}  stderr={5}' -f $mode, $rep, ($io1[4] - $io0[4]), ($io1[1] - $io0[1]), ($io1[3] - $io0[3]), $errTask.Result.Trim()
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
  }
}
