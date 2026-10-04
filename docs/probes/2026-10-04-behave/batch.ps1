# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Runs a plan of rig runs, each on a private desktop of its own through
# HiddenDesktop.ps1, under the machine-wide suite lock, with Firefox's launcher
# registry key exported before and after.
param(
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $Plan,
    [Parameter(Mandatory)] [string] $Server
)

$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\wt\behave\.work'
$L = 'C:\Source\SixFive7\BrowserAI\.work\locks\suite'
$rig = "$S\rig"
$node = 'C:\Source\SixFive7\BrowserAI\.work\wt\behave\payload\node\node.exe'
$pwsh = (Get-Command pwsh).Source
New-Item -ItemType Directory -Force -Path "$S\runs", "$S\registry", "$S\sessions", "$S\tmp" | Out-Null
$log = "$S\runs\$Name.batch.log"

function Log([string] $m) { Add-Content -LiteralPath $log -Value ('{0} {1}' -f [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'), $m) -Encoding utf8 }

$env:TEMP = "$S\tmp"
$env:TMP = "$S\tmp"

Log "batch ${Name}: waiting for the suite lock"
while ($true) {
    try { New-Item -ItemType Directory -Path $L -ErrorAction Stop | Out-Null; break }
    catch { Start-Sleep -Seconds 30 }
}

try {
    Set-Content -LiteralPath "$L\owner.txt" -Encoding utf8 -Value @(
        'lane: behave',
        ('started: {0}' -f [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')),
        "running: behave lane measurements, batch $Name (a published BrowserAI.Server.exe and browsers, each run on a private desktop)",
        "log: $log")
    Log 'lock taken'
    & reg.exe export 'HKCU\Software\Mozilla\Firefox\Launcher' "$S\registry\$Name-before.reg" /y | Out-Null
    Log "registry before: exit $LASTEXITCODE"

    $runs = Get-Content -LiteralPath $Plan -Raw | ConvertFrom-Json
    foreach ($r in $runs) {
        $out = "$S\runs\$Name\$($r.id)"
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $purpose = $r.id -replace '[^A-Za-z0-9-]', ''
        if ($r.PSObject.Properties.Name -contains 'probe' -and $r.probe) {
            # The private desktop's positive control: the probe shows one window
            # there, and the census must find it there and on no other desktop.
            $cmd = "`"$pwsh`" -NoProfile -File `"$rig\desk-probe.ps1`" -Out `"$out\probe.txt`""
            Log "BEGIN $($r.id): $cmd"
            & $pwsh -NoProfile -File "$rig\HiddenDesktop.ps1" -Purpose $purpose -App $pwsh -CommandLine $cmd -WorkingDirectory $rig -Log "$out\desktop.log" -TimeoutSec $r.timeout
        }
        elseif (($r.PSObject.Properties.Name -contains 'raw' -and $r.raw) -or ($r.PSObject.Properties.Name -contains 'raw2' -and $r.raw2)) {
            $script = if ($r.PSObject.Properties.Name -contains 'raw2' -and $r.raw2) { 'raw2.mjs' } else { 'raw.mjs' }
            $cmd = "$node $rig\$script payload=C:\Source\SixFive7\BrowserAI\.work\wt\behave\payload out=$out\rig"
            Log "BEGIN $($r.id): $cmd"
            & $pwsh -NoProfile -File "$rig\HiddenDesktop.ps1" -Purpose $purpose -App $node -CommandLine $cmd -WorkingDirectory $rig -Log "$out\desktop.log" -TimeoutSec $r.timeout
        }
        else {
            $cmd = "$node $rig\behave.mjs server=$Server " + ($r.args -join ' ') + " out=$out\rig sessions=$S\sessions"
            Log "BEGIN $($r.id): $cmd"
            & $pwsh -NoProfile -File "$rig\HiddenDesktop.ps1" -Purpose $purpose -App $node -CommandLine $cmd -WorkingDirectory $rig -Log "$out\desktop.log" -TimeoutSec $r.timeout
        }
        $code = $LASTEXITCODE
        Log "END $($r.id): launcher exit $code"
        if ($code -eq 3) { Log 'LEAK: a window of the job reached the screen; the batch stops here'; break }
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
