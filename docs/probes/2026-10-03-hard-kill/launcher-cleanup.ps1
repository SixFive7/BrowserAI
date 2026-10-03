# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: removes from HKCU\Software\Mozilla\Firefox\Launcher only the values that were
# absent from this measurement's baseline (results\..\baseline\hkcu-firefox-launcher-before.tsv)
# AND belong to the one executable this measurement ran:
#   C:\Source\SixFive7\BrowserAI\.work\browsers-cache\firefox-1549\firefox\firefox.exe
# Writes what it removed (name, kind, value) to results\launcher-removed.tsv.
param([switch] $DryRun)
$ErrorActionPreference = 'Stop'
$root = 'C:\Source\SixFive7\BrowserAI\.work\hard-kill'
$exe = 'C:\Source\SixFive7\BrowserAI\.work\browsers-cache\firefox-1549\firefox\firefox.exe'
$baseline = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $root 'baseline\hkcu-firefox-launcher-before.tsv')) {
    if ($line.StartsWith('#')) { continue }
    $baseline[$line.Split("`t")[0]] = $true
}
$keyPath = 'HKCU:\Software\Mozilla\Firefox\Launcher'
$k = Get-Item -LiteralPath $keyPath
$removed = @()
foreach ($n in $k.GetValueNames()) {
    if (-not $n.StartsWith($exe + '|', [StringComparison]::OrdinalIgnoreCase)) { continue }
    if ($baseline.ContainsKey($n)) { continue }
    $removed += "{0}`t{1}`t{2}" -f $n, $k.GetValueKind($n), $k.GetValue($n)
    if (-not $DryRun) { Remove-ItemProperty -LiteralPath $keyPath -Name $n }
}
$hdr = "# $((Get-Date).ToUniversalTime().ToString('o')) dryRun=$DryRun removed=$($removed.Count) valuesAfter=$((Get-Item -LiteralPath $keyPath).GetValueNames().Count)"
@($hdr) + $removed | Set-Content -LiteralPath (Join-Path $root 'results\launcher-removed.tsv') -Encoding utf8
@($hdr) + $removed
