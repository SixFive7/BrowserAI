# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

$rig = 'C:\Source\SixFive7\BrowserAI\.work\2026-08-27-desktop-heap'
$script = Join-Path $rig 'Rig.ps1'
$pwshPath = (Get-Process -Id $PID).Path
$n = 0
foreach ($delay in @(0, 20, 40, 60, 80, 100, 120, 150, 200, 250, 300, 400)) {
    $n++
    & $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -Desktop ('BaiT' + $n) -TitleChars 2048 -FixedCap 4637 -Controls 0 -Reps 1 -ReleaseAfterMs $delay -BrowserWaitMs 8000 -Out (Join-Path $rig ('trans-' + $delay)) 2>&1
}
