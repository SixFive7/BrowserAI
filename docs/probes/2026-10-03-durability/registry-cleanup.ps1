# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Removes from the three Firefox keys only the
# values that (a) were absent from this research's own baseline (baseline\before\*.tsv) and
# (b) belong to the one Firefox executable this research ran, its private copy:
#   C:\Source\SixFive7\BrowserAI\.work\durability\browsers\firefox-1553\firefox\firefox.exe
# Writes what it removed (key, name, kind, value) to results\registry-removed.tsv.
param([switch] $DryRun)
$ErrorActionPreference = 'Stop'
$root = 'C:\Source\SixFive7\BrowserAI\.work\durability'
$exe = 'C:\Source\SixFive7\BrowserAI\.work\durability\browsers\firefox-1553\firefox\firefox.exe'
$keys = [ordered]@{
  'ff-launcher'    = 'HKCU:\Software\Mozilla\Firefox\Launcher'
  'ff-dllprefetch' = 'HKCU:\Software\Mozilla\Firefox\DllPrefetchExperiment'
  'ff-skeletonui'  = 'HKCU:\Software\Mozilla\Firefox\PreXULSkeletonUISettings'
}
$removed = @()
$kept = @()
foreach ($name in $keys.Keys) {
  $kp = $keys[$name]
  $baseline = @{}
  foreach ($line in Get-Content -LiteralPath (Join-Path $root "baseline\before\$name.tsv")) {
    if ($line.StartsWith('#')) { continue }
    $baseline[$line.Split("`t")[0]] = $true
  }
  if (-not (Test-Path -LiteralPath $kp)) { continue }
  $k = Get-Item -LiteralPath $kp
  foreach ($n in $k.GetValueNames()) {
    $mine = $n.Equals($exe, [StringComparison]::OrdinalIgnoreCase) -or $n.StartsWith($exe + '|', [StringComparison]::OrdinalIgnoreCase)
    if (-not $mine) { if (-not $baseline.ContainsKey($n)) { $kept += "{0}`t{1}`tnot ours, left" -f $kp, $n }; continue }
    if ($baseline.ContainsKey($n)) { $kept += "{0}`t{1}`tin baseline, left" -f $kp, $n; continue }
    $v = $k.GetValue($n, $null, 'DoNotExpandEnvironmentNames')
    if ($v -is [byte[]]) { $v = 'bytes:' + [Convert]::ToHexString($v) }
    $removed += "{0}`t{1}`t{2}`t{3}" -f $kp, $n, $k.GetValueKind($n), $v
    if (-not $DryRun) { Remove-ItemProperty -LiteralPath $kp -Name $n }
  }
}
$hdr = "# $((Get-Date).ToUniversalTime().ToString('o')) dryRun=$DryRun removed=$($removed.Count)"
@($hdr) + $removed + @('# values added since the baseline that are not ours (left alone):') + $kept | Set-Content -LiteralPath (Join-Path $root 'results\registry-removed.tsv') -Encoding utf8
@($hdr) + $removed + @('# left alone:') + $kept
