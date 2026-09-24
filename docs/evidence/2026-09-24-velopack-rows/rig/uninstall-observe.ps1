# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch driver: a SILENT uninstall of one scratch root, headless, observed.
param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Tag, [int]$Seconds = 120)
$ErrorActionPreference = 'Stop'
$base = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows'
Add-Type -Path "$base\Rig.cs"
$sandbox = "$base\sandbox"
$ov = [System.Collections.Generic.Dictionary[string,string]]::new()
$ov['CLAUDE_CONFIG_DIR'] = "$sandbox\client"; $ov['CODEX_HOME'] = "$sandbox\codex"; $ov['BROWSERAI_ROOT'] = "$sandbox\data"
$ov['VELOPACK_FIRSTRUN'] = $null; $ov['VELOPACK_RESTART'] = $null
$update = Join-Path $Root 'Update.exe'
if (-not (Test-Path -LiteralPath $update)) { "NO Update.exe at $update"; return }
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = [VR.Rig]::StartDetached($update, '--uninstall --silent', $Root, $ov)
"[$Tag] launched Update.exe --uninstall --silent pid=$($p.Pid) created=$([VR.Rig]::Ft($p.Created))"
$shown = @{}
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    foreach ($w in [VR.Rig]::Windows()) { if ($w.Pid -eq $p.Pid -and $w.Visible -and -not $shown.ContainsKey("$($w.Hwnd)")) { $shown["$($w.Hwnd)"] = 1; "[$Tag] VISIBLE WINDOW class=$($w.Class) title='$($w.Title)'" } }
    [VR.Rig]::Refresh($p); if ($p.Exited -ne 0) { break }; Start-Sleep -Milliseconds 20
}
"[$Tag] Update.exe exited=$([VR.Rig]::Ft($p.Exited)) code=$($p.ExitCode) lifeMs=$([math]::Round([VR.Rig]::Ms($p.Created, $p.Exited),1)) visibleWindows=$($shown.Count)"
$cur = Join-Path $Root 'current'
$t0 = $sw.Elapsed.TotalSeconds
while ((Test-Path -LiteralPath $cur) -and ($sw.Elapsed.TotalSeconds - $t0) -lt 60) { Start-Sleep -Milliseconds 250 }
"[$Tag] current\ present after wait: $(Test-Path -LiteralPath $cur); root entries now: $((Get-ChildItem -LiteralPath $Root -Force -ErrorAction SilentlyContinue | ForEach-Object Name) -join ', ')"
"[$Tag] PID=$($p.Pid)"
