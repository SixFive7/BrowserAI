# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Read-only "after" snapshot, diffed against baseline\*-before. Changes nothing.
$ErrorActionPreference = 'Stop'
$s = 'C:\Source\SixFive7\BrowserAI\.work\debugger-tools\baseline'
$ts = (Get-Date).ToString('o')
$out = New-Object System.Collections.Generic.List[string]
$out.Add("taken=$ts")

# 1. the real ms-playwright registry
$reg = Join-Path $env:LOCALAPPDATA 'ms-playwright\b'
$top = Join-Path $env:LOCALAPPDATA 'ms-playwright'
$regNow = @(Get-ChildItem -LiteralPath $reg -Force -ErrorAction SilentlyContinue)
$topNow = @(Get-ChildItem -LiteralPath $top -Force -ErrorAction SilentlyContinue)
$regNow | Sort-Object Name | ForEach-Object { "{0}`t{1}`t{2}" -f $_.Name, $_.Length, $_.LastWriteTimeUtc.ToString('o') } | Set-Content -LiteralPath "$s\ms-playwright-b-after.tsv"
$before = @(Get-Content -LiteralPath "$s\ms-playwright-b-before.tsv" | ForEach-Object { ($_ -split "`t")[0] })
$out.Add("ms-playwright\b entries: before=$($before.Count) after=$($regNow.Count)")
$added = @($regNow.Name | Where-Object { $before -notcontains $_ })
$gone = @($before | Where-Object { $regNow.Name -notcontains $_ })
$out.Add("  added: $($added -join ', ')")
$out.Add("  removed: $($gone -join ', ')")
foreach ($a in $added) {
    $t = Get-Content -Raw -LiteralPath (Join-Path $reg $a) -ErrorAction SilentlyContinue
    $mine = if ($t -and $t -match 'debugger-tools') { 'MENTIONS debugger-tools' } else { 'does not mention debugger-tools' }
    $out.Add("  $a : $mine")
}
$out.Add("ms-playwright top-level entries: before=$((Get-Content -LiteralPath "$s\ms-playwright-top-before.tsv").Count) after=$($topNow.Count)")

# 2. Mozilla folders
foreach ($pair in @(@('localappdata-mozilla', (Join-Path $env:LOCALAPPDATA 'Mozilla')), @('appdata-mozilla', (Join-Path $env:APPDATA 'Mozilla')))) {
    $name = $pair[0]; $p = $pair[1]
    $now = if (Test-Path -LiteralPath $p) { @(Get-ChildItem -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue | Sort-Object FullName | ForEach-Object { "{0}`t{1}`t{2}`t{3}" -f $_.FullName.Substring($p.Length), $(if ($_.PSIsContainer) { '<dir>' } else { $_.Length }), $_.LastWriteTimeUtc.ToString('o'), $_.CreationTimeUtc.ToString('o') }) } else { @("ABSENT $p") }
    $now | Set-Content -LiteralPath "$s\$name-after.tsv"
    $b = @(Get-Content -LiteralPath "$s\$name-before.tsv")
    $bPaths = @{}; foreach ($l in $b) { $bPaths[($l -split "`t")[0]] = $l }
    $nPaths = @{}; foreach ($l in $now) { $nPaths[($l -split "`t")[0]] = $l }
    $newPaths = @($nPaths.Keys | Where-Object { -not $bPaths.ContainsKey($_) } | Sort-Object)
    $gonePaths = @($bPaths.Keys | Where-Object { -not $nPaths.ContainsKey($_) } | Sort-Object)
    $changed = @($nPaths.Keys | Where-Object { $bPaths.ContainsKey($_) -and $bPaths[$_] -ne $nPaths[$_] } | Sort-Object)
    $out.Add("$name : before=$($b.Count) after=$($now.Count) new=$($newPaths.Count) gone=$($gonePaths.Count) changed=$($changed.Count)")
    foreach ($x in $newPaths) { $out.Add("  NEW $($nPaths[$x])") }
    foreach ($x in ($gonePaths | Select-Object -First 20)) { $out.Add("  GONE $x") }
    foreach ($x in ($changed | Select-Object -First 20)) { $out.Add("  CHANGED $x :: was $($bPaths[$x] -replace "`t", ' | ') :: now $($nPaths[$x] -replace "`t", ' | ')") }
}

# 3. HKCU Launcher values
$k = 'HKCU:\Software\Mozilla\Firefox\Launcher'
$item = Get-Item -LiteralPath $k
$nowVals = @($item.GetValueNames() | Sort-Object | ForEach-Object { "{0}`t{1}`t{2}" -f $_, $item.GetValueKind($_), ($item.GetValue($_)) })
$nowVals | Set-Content -LiteralPath "$s\hkcu-launcher-after.tsv"
$bVals = @(Get-Content -LiteralPath "$s\hkcu-launcher-before.tsv")
$bNames = @($bVals | ForEach-Object { ($_ -split "`t")[0] })
$newVals = @($nowVals | Where-Object { $bNames -notcontains (($_ -split "`t")[0]) })
$out.Add("HKCU Launcher values: before=$($bVals.Count) after=$($nowVals.Count) new=$($newVals.Count)")
foreach ($v in $newVals) { $out.Add("  NEW $v") }

# 4. the shared browsers cache
$c = 'C:\Source\SixFive7\BrowserAI\.work\browsers-cache'
$cNow = @(Get-ChildItem -LiteralPath $c -Recurse -Force -File | Sort-Object FullName | ForEach-Object { "{0}`t{1}`t{2}" -f $_.FullName.Substring($c.Length), $_.Length, $_.LastWriteTimeUtc.ToString('o') })
$cNow | Set-Content -LiteralPath "$s\browsers-cache-after.tsv"
$cB = @(Get-Content -LiteralPath "$s\browsers-cache-before.tsv")
$diff = Compare-Object -ReferenceObject $cB -DifferenceObject $cNow
$out.Add("browsers-cache files: before=$($cB.Count) after=$($cNow.Count) differing lines=$(@($diff).Count)")
foreach ($d in (@($diff) | Select-Object -First 20)) { $out.Add("  $($d.SideIndicator) $($d.InputObject)") }

$out | Set-Content -LiteralPath "$s\after-diff.txt"
$out
