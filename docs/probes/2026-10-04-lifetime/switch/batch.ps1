# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Runs a plan of rig runs, each on a private desktop of its own through
# HiddenDesktop.ps1, under the machine-wide suite lock, with Firefox's launcher
# registry key exported before and after.
param(
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $Plan
)

$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\lifetime'
$L = 'C:\Source\SixFive7\BrowserAI\.work\locks\suite'
$rig = "$S\rig"
$node = 'C:\Source\SixFive7\BrowserAI\.work\wt\lifetime\payload\node\node.exe'
$pwsh = (Get-Command pwsh).Source
$log = "$S\runs\$Name.batch.log"

function Log([string] $m) { Add-Content -LiteralPath $log -Value ('{0} {1}' -f [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'), $m) -Encoding utf8 }

$env:TEMP = "$S\tmp"
$env:TMP = "$S\tmp"
$env:PLAYWRIGHT_SKIP_BROWSER_GC = '1'
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1'

Log "batch ${Name}: waiting for the suite lock"
while ($true) {
    try { New-Item -ItemType Directory -Path $L -ErrorAction Stop | Out-Null; break }
    catch { Start-Sleep -Seconds 30 }
}

try {
    Set-Content -LiteralPath "$L\owner.txt" -Encoding utf8 -Value @(
        'lane: lifetime',
        ('started: {0}' -f [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')),
        "running: lifetime review measurements, batch $Name (published BrowserAI.Server.exe and browsers, each run on a private desktop)",
        "log: $log")
    Log 'lock taken'
    & reg.exe export 'HKCU\Software\Mozilla\Firefox\Launcher' "$S\registry\$Name-before.reg" /y | Out-Null
    Log "registry before: exit $LASTEXITCODE"

    $runs = Get-Content -LiteralPath $Plan -Raw | ConvertFrom-Json
    foreach ($r in $runs) {
        $out = "$S\runs\$Name\$($r.id)"
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $cmd = "$node $rig\$($r.script) " + ($r.args -join ' ')
        if ($r.script -ne 'row152\fp.mjs') { $cmd += " out=$out\rig sessions=$S\sessions" }
        Log "BEGIN $($r.id): $cmd"
        & $pwsh -NoProfile -File "$rig\HiddenDesktop.ps1" -Purpose ($r.id -replace '[^A-Za-z0-9-]', '') -App $node -CommandLine $cmd -WorkingDirectory $rig -Log "$out\desktop.log" -TimeoutSec $r.timeout
        $code = $LASTEXITCODE
        Log "END $($r.id): launcher exit $code"
        if ($code -eq 3) { Log 'LEAK: a window of the job reached the screen; the batch stops here'; break }
        if ($r.PSObject.Properties.Name -contains 'gate' -and $r.gate) {
            $verdict = & python "$rig\gate_check.py" "$out\rig\result.json" 2>&1
            $gateCode = $LASTEXITCODE
            foreach ($line in $verdict) { Log "GATE $($r.id): $line" }
            if ($gateCode -ne 0) { Log "GATE FAILED after $($r.id); the batch stops here and releases the lock"; break }
        }
    }
}
catch {
    Log ('ERROR {0}' -f $_.Exception.Message)
}
finally {
    & reg.exe export 'HKCU\Software\Mozilla\Firefox\Launcher' "$S\registry\$Name-after.reg" /y | Out-Null
    Log "registry after: exit $LASTEXITCODE"
    Remove-Item -LiteralPath "$L\owner.txt" -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $L -ErrorAction SilentlyContinue
    Log 'lock released'
}
