# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

$rig = 'C:\Source\SixFive7\BrowserAI\.work\2026-08-27-desktop-heap'
$script = Join-Path $rig 'Rig.ps1'
$pwshPath = (Get-Process -Id $PID).Path

# Do not overlap with the gradient: two rigs at once would contend for the same
# machine and confound both.
try { Wait-Process -Id 81864 -Timeout 1800 -ErrorAction Stop } catch { }

# The threshold, finely: the gradient put it between 4620 (lives) and 4637 (dies).
foreach ($cap in @(4636, 4635, 4634, 4632, 4630, 4626)) {
    & $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -Desktop ('BaiF' + $cap) -TitleChars 2048 -FixedCap $cap -Controls 0 -Reps 3 -BrowserWaitMs 8000 -Out (Join-Path $rig ('fine-' + $cap)) 2>&1
}

# The same heap spent at a different granularity: ~52,000 small windows instead
# of ~4,637 large ones. A heap full of small blocks is a different heap.
& $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -Desktop 'BaiSmall' -TitleChars 0 -Controls 0 -Reps 5 -MaxFillers 10 -BrowserWaitMs 8000 -Out (Join-Path $rig 'small-windows') 2>&1
