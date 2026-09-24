# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Starts a pwsh script DETACHED (no console, bInheritHandles=false) so nothing it starts can hold the
# caller's pipe, then polls its output file for DONE.
param(
    [Parameter(Mandatory)][string]$Script,
    [Parameter(Mandatory)][string]$Out,
    [string]$ScriptArgs = '',
    [int]$WaitSeconds = 120
)
$ErrorActionPreference = 'Stop'
$base = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows'
Add-Type -Path "$base\Rig.cs"
if (Test-Path $Out) { Remove-Item -LiteralPath $Out -Force }
$pwsh = (Get-Command pwsh).Source
$p = [VR.Rig]::StartHidden($pwsh, "-NoProfile -File `"$Script`" -Out `"$Out`" $ScriptArgs", $base, $null)
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $WaitSeconds) {
    if ((Test-Path $Out) -and ((Get-Content -LiteralPath $Out -Raw) -match '(?m)^\S+ DONE\s*$')) { break }
    Start-Sleep -Milliseconds 200
}
"driver pid=$($p.Pid) waited=$([math]::Round($sw.Elapsed.TotalSeconds,1))s"
if (Test-Path $Out) { Get-Content -LiteralPath $Out } else { 'NO OUTPUT FILE' }
