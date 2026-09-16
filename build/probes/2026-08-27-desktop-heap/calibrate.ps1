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
foreach ($case in @(@{n='calA'; heap=512; title=0}, @{n='calB'; heap=512; title=2048}, @{n='calC'; heap=2048; title=0})) {
    & $pwshPath -NoProfile -NonInteractive -File $script -Mode Controller -HeapKb $case.heap -TitleChars $case.title -Controls 0 -Reps 0 -MaxFillers 8 -Desktop ('Bai' + $case.n) -Out (Join-Path $out $case.n) 2>&1
}
