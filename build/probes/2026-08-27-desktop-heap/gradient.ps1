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

# The control that says which findings belong to the rig: same command line, the
# desktop the suite itself uses, nothing consumed.
& $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -UseDefaultDesktop -NoFill -Controls 2 -Out (Join-Path $out 'default-desktop') 2>&1

foreach ($cap in @(4637, 4620, 4600, 4550, 4500, 4400, 4200, 4000, 3600)) {
    & $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -Desktop ('BaiG' + $cap) -TitleChars 2048 -FixedCap $cap -Controls 0 -Reps 3 -Out (Join-Path $out ('grad-' + $cap)) 2>&1
}
