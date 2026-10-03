# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Removes ONLY the HKCU\Software\Mozilla\Firefox\Launcher values this probe's
# Firefox runs added: names that start with the shared cache's Firefox path and
# were absent from the snapshot taken before the first run. Without -Apply it
# lists them and changes nothing.
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$s = 'C:\Source\SixFive7\BrowserAI\.work\debugger-tools\baseline'
$k = 'HKCU:\Software\Mozilla\Firefox\Launcher'
# firefox-1549 only: the firefox-1553 values appeared at 03:47 before this probe
# had launched 1553 at all (baseline\hkcu-launcher-mid.tsv), so they are another
# agent's and are not touched.
$prefix = 'C:\Source\SixFive7\BrowserAI\.work\browsers-cache\firefox-1549\'
$before = @(Get-Content -LiteralPath "$s\hkcu-launcher-before.tsv" | ForEach-Object { ($_ -split "`t")[0] })
$item = Get-Item -LiteralPath $k
$candidates = @($item.GetValueNames() | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and ($before -notcontains $_) } | Sort-Object)
$lines = foreach ($n in $candidates) { "{0}`t{1}`t{2}" -f $n, $item.GetValueKind($n), $item.GetValue($n) }
"candidates=$($candidates.Count)"
$lines
if ($Apply -and $candidates.Count) {
    $lines | Set-Content -LiteralPath "$s\hkcu-launcher-removed.tsv"
    foreach ($n in $candidates) { Remove-ItemProperty -LiteralPath $k -Name $n -Confirm:$false }
    $left = @((Get-Item -LiteralPath $k).GetValueNames() | Where-Object { $candidates -contains $_ })
    "removed=$($candidates.Count - $left.Count) left=$($left.Count)"
}
