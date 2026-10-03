# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch: the ordinary two-shell gate on the rv worktree's HEAD, under the machine-wide suite lock.
# Publishes both slices first, runs the PowerShell half and then the Git Bash half, each through
# the tree's own driver, with vpk 1.2.161 from the review lane's scratch tool path first on PATH.
param([string] $Round = '1')
$ErrorActionPreference = 'Continue'
$wt = 'C:\Source\SixFive7\BrowserAI\.work\wt\rv'
$s = 'C:\Source\SixFive7\BrowserAI\.work\rv-scratch'
$lock = 'C:\Source\SixFive7\BrowserAI\.work\locks\suite'
$log = Join-Path $s "logs\gate-$Round.log"
function Log([string]$m) { "[{0}] {1}" -f [datetime]::UtcNow.ToString('o'), $m | Add-Content -LiteralPath $log }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'
$env:PATH = "$s\vpk-1.2.161;" + $env:PATH

Log "gate round $Round waiting for the suite lock"
while ($true) {
    try { New-Item -ItemType Directory -Path $lock -ErrorAction Stop | Out-Null; break }
    catch { Start-Sleep -Seconds 60 }
}
$head = (git -C $wt rev-parse --short HEAD)
"lane: rv`nstarted: $([datetime]::UtcNow.ToString('o'))`nrunning: the ordinary two-shell gate (build/Invoke-OrdinaryGate.ps1, then build/invoke-ordinary-gate.sh) on .work\wt\rv at $head, after publishing both slices; starts browsers, BrowserAI.Server.exe, BrowserAI.exe and the suite's installer" | Set-Content -LiteralPath (Join-Path $lock 'owner.txt') -Encoding utf8NoBOM
Log "lock taken; HEAD $head"
try {
    & pwsh -NoProfile -File (Join-Path $s 'list-browsers.ps1') -Tag "10-gate$Round-before" | Out-Null
    Log 'publishing both slices'
    & dotnet publish (Join-Path $wt 'src\BrowserAI\BrowserAI.csproj') -c Release -r win-x64 --self-contained *> (Join-Path $s "logs\gate-$Round-publish-server.log")
    Log "server publish exit $LASTEXITCODE"
    & dotnet publish (Join-Path $wt 'src\BrowserAI.App\BrowserAI.App.csproj') -c Release -r win-x64 --self-contained *> (Join-Path $s "logs\gate-$Round-publish-app.log")
    Log "app publish exit $LASTEXITCODE"

    Log 'PowerShell half starting'
    & pwsh -NoProfile -File (Join-Path $wt 'build\Invoke-OrdinaryGate.ps1') -Tag "rv-ps-$Round" *> (Join-Path $s "logs\gate-$Round-ps-driver.log")
    Log "PowerShell half exit $LASTEXITCODE"

    Log 'Git Bash half starting'
    $bash = 'C:\Program Files\Git\bin\bash.exe'
    $cmd = "export PATH=/c/Source/SixFive7/BrowserAI/.work/rv-scratch/vpk-1.2.161:`$PATH; bash /c/Source/SixFive7/BrowserAI/.work/wt/rv/build/invoke-ordinary-gate.sh rv-bash-$Round"
    & $bash -c $cmd *> (Join-Path $s "logs\gate-$Round-bash-driver.log")
    Log "Git Bash half exit $LASTEXITCODE"

    & pwsh -NoProfile -File (Join-Path $s 'list-browsers.ps1') -Tag "11-gate$Round-after" | Out-Null
    Log ("ms-playwright-mcp after: " + (Test-Path (Join-Path $env:LOCALAPPDATA 'ms-playwright-mcp')))
}
finally {
    $owner = Get-Content -LiteralPath (Join-Path $lock 'owner.txt') -Raw -ErrorAction SilentlyContinue
    if ($owner -match 'lane: rv') { Remove-Item -LiteralPath $lock -Recurse -Force; Log 'lock released' }
    else { Log "lock not released: owner is not rv: $owner" }
}
Log 'GATE-DONE'
