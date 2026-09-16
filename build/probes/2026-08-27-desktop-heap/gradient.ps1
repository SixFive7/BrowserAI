# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

$rig = 'C:\Source\SixFive7\BrowserAI\.work\2026-08-27-desktop-heap'
$script = Join-Path $rig 'Rig.ps1'
$pwshPath = (Get-Process -Id $PID).Path

# The control that says which findings belong to the rig: same command line, the
# desktop the suite itself uses, nothing consumed.
& $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -UseDefaultDesktop -NoFill -Controls 2 -Out (Join-Path $rig 'default-desktop') 2>&1

foreach ($cap in @(4637, 4620, 4600, 4550, 4500, 4400, 4200, 4000, 3600)) {
    & $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -Desktop ('BaiG' + $cap) -TitleChars 2048 -FixedCap $cap -Controls 0 -Reps 3 -Out (Join-Path $rig ('grad-' + $cap)) 2>&1
}
