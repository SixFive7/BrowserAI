# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: a read-only snapshot of the state outside the scratch that Firefox or Playwright
# might touch. Appends one block to results\external-snapshots.txt. Changes nothing.
param([string] $Label = 'snapshot')
$ErrorActionPreference = 'Continue'
$out = 'C:\Source\SixFive7\BrowserAI\.work\hard-kill\results\external-snapshots.txt'
$t = (Get-Date).ToUniversalTime().ToString('o')
$lines = @("=== $Label $t")
$moz = Join-Path $env:LOCALAPPDATA 'Mozilla\Firefox'
$d = Get-Item -LiteralPath $moz -Force
$lines += "LOCALAPPDATA\Mozilla\Firefox folder mtime=$($d.LastWriteTimeUtc.ToString('o'))"
foreach ($e in Get-ChildItem -LiteralPath $moz -Force) {
    $lines += "  {0}`tcreated={1}`tmtime={2}`t{3}" -f $e.Name, $e.CreationTimeUtc.ToString('o'), $e.LastWriteTimeUtc.ToString('o'), $(if ($e.PSIsContainer) { 'D' } else { $e.Length })
}
$roam = Join-Path $env:APPDATA 'Mozilla\Firefox'
$r = Get-Item -LiteralPath $roam -Force
$lines += "APPDATA\Mozilla\Firefox folder mtime=$($r.LastWriteTimeUtc.ToString('o'))"
foreach ($e in Get-ChildItem -LiteralPath $roam -Force) {
    $lines += "  {0}`tcreated={1}`tmtime={2}`t{3}" -f $e.Name, $e.CreationTimeUtc.ToString('o'), $e.LastWriteTimeUtc.ToString('o'), $(if ($e.PSIsContainer) { 'D' } else { $e.Length })
}
$reg = Join-Path $env:LOCALAPPDATA 'ms-playwright\b'
$files = if (Test-Path -LiteralPath $reg) { @(Get-ChildItem -LiteralPath $reg -Force -File) } else { @() }
$newest = ($files | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1)
$lines += "LOCALAPPDATA\ms-playwright\b files=$($files.Count) newest=$(if ($newest) { $newest.Name + ' ' + $newest.LastWriteTimeUtc.ToString('o') } else { '-' })"
$k = Get-Item -LiteralPath 'HKCU:\Software\Mozilla\Firefox\Launcher'
$names = $k.GetValueNames()
$lines += "HKCU\Software\Mozilla\Firefox\Launcher values=$($names.Count)"
foreach ($n in ($names | Where-Object { $_ -like '*\.work\browsers-cache\*' })) { $lines += "  {0}`t{1}`t{2}" -f $n, $k.GetValueKind($n), $k.GetValue($n) }
Add-Content -LiteralPath $out -Value $lines -Encoding utf8
$lines
