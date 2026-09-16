# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Corrected 2026-09-16 (previously
# $rig = 'C:\Source\SixFive7\BrowserAI\.work\2026-08-27-desktop-heap').
# The rig moved out of the scratch directory when it was wiped; the run's own
# output still goes to a scratch directory, which is what $out is for.
$rig = $PSScriptRoot
$out = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')) '.work/2026-08-27-desktop-heap'
$null = New-Item -ItemType Directory -Force -Path $out
$script = Join-Path $rig 'Rig.ps1'
$pwshPath = (Get-Process -Id $PID).Path
$n = 0
foreach ($delay in @(0, 20, 40, 60, 80, 100, 120, 150, 200, 250, 300, 400)) {
    $n++
    & $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -Desktop ('BaiT' + $n) -TitleChars 2048 -FixedCap 4637 -Controls 0 -Reps 1 -ReleaseAfterMs $delay -BrowserWaitMs 8000 -Out (Join-Path $out ('trans-' + $delay)) 2>&1
}
