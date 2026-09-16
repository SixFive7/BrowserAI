# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

$rig = 'C:\Source\SixFive7\BrowserAI\.work\2026-08-27-desktop-heap'
$script = Join-Path $rig 'Rig.ps1'
$pwshPath = (Get-Process -Id $PID).Path
foreach ($case in @(@{n='calA'; heap=512; title=0}, @{n='calB'; heap=512; title=2048}, @{n='calC'; heap=2048; title=0})) {
    & $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -HeapKb $case.heap -TitleChars $case.title -Controls 0 -Reps 0 -MaxFillers 8 -Desktop ('Bai' + $case.n) -Out (Join-Path $rig $case.n) 2>&1
}
