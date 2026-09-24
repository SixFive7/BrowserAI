# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch driver for row 126's second entry point: the root stub -> Update.exe start -> main exe.
param([Parameter(Mandatory)][string]$Stub, [Parameter(Mandatory)][string]$Tag, [int]$Seconds = 60)
$ErrorActionPreference = 'Stop'
$base = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows'
Add-Type -Path "$base\Rig.cs"
$sandbox = "$base\sandbox"
$ov = [System.Collections.Generic.Dictionary[string,string]]::new()
$ov['CLAUDE_CONFIG_DIR'] = "$sandbox\client"; $ov['CODEX_HOME'] = "$sandbox\codex"; $ov['BROWSERAI_ROOT'] = "$sandbox\data"
$ov['VELOPACK_FIRSTRUN'] = $null; $ov['VELOPACK_RESTART'] = $null
$out = "$base\logs\$Tag-stub.txt"
$lines = [System.Collections.Generic.List[string]]::new()
$sw = [Diagnostics.Stopwatch]::StartNew()
function Say($s) { $lines.Add(('{0,9:F1}ms {1}' -f $sw.Elapsed.TotalMilliseconds, $s)); [IO.File]::WriteAllLines($out, $lines) }
$stubProc = [VR.Rig]::StartDetached($Stub, '', (Split-Path $Stub), $ov)
Say "launched stub pid=$($stubProc.Pid) created=$([VR.Rig]::Ft($stubProc.Created))"
$tracked = @{ $stubProc.Pid = $stubProc }; $app = $null; $dlg = $null; $seenAt = $null
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    foreach ($p in [VR.Rig]::Snapshot()) {
        if ($tracked.ContainsKey($p.Pid) -or -not $tracked.ContainsKey($p.Ppid)) { continue }
        if (-not [VR.Rig]::Attach($p)) { continue }
        $tracked[$p.Pid] = $p
        Say "proc+ pid=$($p.Pid) ppid=$($p.Ppid) exe=$($p.Exe) created=$([VR.Rig]::Ft($p.Created)) cmd=$([VR.Rig]::CommandLine($p.Handle))"
        if ($p.Exe -eq 'BrowserAI.exe') { $app = $p }
    }
    if ($app -and -not $dlg) { $dlg = [VR.Rig]::Windows() | Where-Object { $_.Pid -eq $app.Pid -and $_.Visible -and $_.Class -eq '#32770' } | Select-Object -First 1; if ($dlg) { $seenAt = $sw.Elapsed.TotalMilliseconds; Say "app window visible class=$($dlg.Class) title='$($dlg.Title)' size=$($dlg.R-$dlg.L)x$($dlg.B-$dlg.T)" } }
    if ($dlg -and ($sw.Elapsed.TotalMilliseconds - $seenAt) -gt 2500) { break }
    Start-Sleep -Milliseconds 5
}
foreach ($p in $tracked.Values) { [VR.Rig]::Refresh($p); Say "tree pid=$($p.Pid) ppid=$($p.Ppid) exe=$($p.Exe) created=$([VR.Rig]::Ft($p.Created)) exited=$([VR.Rig]::Ft($p.Exited)) code=$($p.ExitCode)" }
if ($dlg) {
    [void][VR.Rig]::PostClose($dlg.Hwnd); Say 'WM_CLOSE posted to the app dialog'
    $t0 = $sw.Elapsed.TotalSeconds
    while ($sw.Elapsed.TotalSeconds - $t0 -lt 30) { [VR.Rig]::Refresh($app); if ($app.Exited -ne 0) { break }; Start-Sleep -Milliseconds 20 }
    Say "app exited=$([VR.Rig]::Ft($app.Exited)) code=$($app.ExitCode)"
}
Say 'DONE'
