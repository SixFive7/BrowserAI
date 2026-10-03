# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Batch 2 of lane stale, 2026-10-03: row 103, the three rename rigs as stored,
# with only the revision constants (chromium-1247, firefox-1553) and the scratch
# profile and working directory moved. Headless. Run under the suite lock.
$ErrorActionPreference = 'Continue'
$S = 'C:\Source\SixFive7\BrowserAI\.work\stale-scratch'
$L = Join-Path $S 'out\batch2'
New-Item -ItemType Directory -Force $L, (Join-Path $S 'out\row103') | Out-Null
$browsers = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers'
function Stamp { (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
"before: $((Get-ChildItem $browsers -Directory | ForEach-Object Name) -join ' ')" | Tee-Object -Append (Join-Path $L 'batch2.log')
foreach ($rig in 'rename-under-chromium.ps1', 'rename-under-firefox.ps1', 'rename-shared-components.ps1') {
  "=== $(Stamp) BEGIN $rig" | Tee-Object -Append (Join-Path $L 'batch2.log')
  & pwsh -NoProfile -NonInteractive -File (Join-Path $S "rigs\row103\$rig") *> (Join-Path $L "$rig.log")
  "=== $(Stamp) END $rig exit=$LASTEXITCODE" | Tee-Object -Append (Join-Path $L 'batch2.log')
}
"after: $((Get-ChildItem $browsers -Directory | ForEach-Object Name) -join ' ')" | Tee-Object -Append (Join-Path $L 'batch2.log')
"=== $(Stamp) BATCH2 COMPLETE" | Tee-Object -Append (Join-Path $L 'batch2.log')
