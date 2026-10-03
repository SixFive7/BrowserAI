# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# after-check.ps1 -- isolation evidence after all runs, plus removal of the Launcher values this rig's runs added.
# -Remove switches the removal on; without it, it only reports.
param([switch]$Remove)
$root = 'C:\Source\SixFive7\BrowserAI\.work\state-across-close'
$utc = [DateTime]::UtcNow.ToString('o')
$out = New-Object System.Collections.Generic.List[string]
$out.Add("utc=$utc")

# 1. Real ms-playwright server registry, read-only.
$reg = Join-Path $env:LOCALAPPDATA 'ms-playwright\b'
$entries = @(Get-ChildItem -LiteralPath $reg -Force -ErrorAction SilentlyContinue)
$before = Get-Content "$root\baseline\ms-playwright-b-before.txt" | Select-Object -Skip 2 | ForEach-Object { ($_ -split "`t")[0] }
$out.Add("ms-playwright\b before=$($before.Count) after=$($entries.Count)")
foreach ($e in $entries) { if ($before -notcontains $e.Name) { $out.Add("  NEW in real registry: $($e.Name) $($e.LastWriteTimeUtc.ToString('o'))") } }
$mine = @(Get-ChildItem -LiteralPath "$root\registry" -Force -ErrorAction SilentlyContinue)
$out.Add("scratch registry (PWTEST_SERVER_REGISTRY) entries=$($mine.Count)  <- positive control that the variable took")

# 2. Real Local\Mozilla\Firefox, read-only listing.
$moz = Join-Path $env:LOCALAPPDATA 'Mozilla\Firefox'
$m = Get-Item -LiteralPath $moz -Force
$out.Add("Local\Mozilla\Firefox folderLastWriteUtc=$($m.LastWriteTimeUtc.ToString('o'))")
$beforeMoz = Get-Content "$root\baseline\mozilla-local-before.txt"
foreach ($i in Get-ChildItem -LiteralPath $moz -Force) {
  $line = "{0}`t{1}`t{2}`t{3}" -f $i.Name, $i.Mode, $i.Length, $i.LastWriteTimeUtc.ToString('o')
  $was = $beforeMoz | Where-Object { $_ -like "$($i.Name)`t*" }
  $out.Add(("  {0} {1}" -f $(if (-not $was) { 'NEW ' } elseif ($was -ne $line) { 'CHANGED' } else { 'same ' }), $line))
}

# 3. Registry: Launcher values (removal authorised), PreXUL and DllPrefetch (report only).
$launcherKey = 'HKCU:\Software\Mozilla\Firefox\Launcher'
$beforeVals = Get-Content "$root\baseline\launcher-values-before.txt" | Select-Object -Skip 2 | ForEach-Object { ($_ -split "`t")[0] }
$nowVals = (Get-Item -LiteralPath $launcherKey).Property
$added = @($nowVals | Where-Object { $beforeVals -notcontains $_ })
$out.Add("Launcher values before=$($beforeVals.Count) now=$($nowVals.Count) added=$($added.Count)")
foreach ($v in $added) {
  $data = (Get-ItemProperty -LiteralPath $launcherKey -Name $v).$v
  $kind = (Get-Item -LiteralPath $launcherKey).GetValueKind($v)
  $ours = $v -like 'C:\Source\SixFive7\BrowserAI\.work\browsers-cache\firefox-15*\firefox\firefox.exe|*'
  $out.Add("  added: $v [$kind] $data ours=$ours")
  if ($Remove -and $ours) {
    Remove-ItemProperty -LiteralPath $launcherKey -Name $v
    $out.Add("    REMOVED")
  }
}
foreach ($k in 'PreXULSkeletonUISettings', 'DllPrefetchExperiment', 'TaskBarIDs') {
  $snap = Get-Content "$root\baseline\hkcu-mozilla-before.reg.txt" -Encoding Unicode | Out-String
  $props = (Get-Item -LiteralPath "HKCU:\Software\Mozilla\Firefox\$k").Property
  foreach ($p in $props) {
    if ($p -like '*browsers-cache*') { $out.Add("  left in place (not authorised to remove): HKCU\Software\Mozilla\Firefox\$k : $p") }
  }
}

# 4. The shared browsers cache, unchanged?
$bc = 'C:\Source\SixFive7\BrowserAI\.work\browsers-cache'
$now = Get-ChildItem -LiteralPath $bc -Recurse -Force -File | ForEach-Object { "{0}`t{1}`t{2}" -f $_.FullName.Substring($bc.Length), $_.Length, $_.LastWriteTimeUtc.ToString('o') }
$was = Get-Content "$root\baseline\browsers-cache-before.tsv"
$diff = Compare-Object -ReferenceObject $was -DifferenceObject $now
$out.Add("browsers-cache files before=$($was.Count) now=$($now.Count) differing lines=$(@($diff).Count)")
foreach ($d in $diff | Select-Object -First 20) { $out.Add("  $($d.SideIndicator) $($d.InputObject)") }

# 5. Every process this rig launched directly: gone? (pid + start time recorded at launch)
foreach ($l in Get-Content "$root\launched-pids.tsv") {
  $f = $l -split "`t"; $procId = [int]$f[1]; $start = [DateTime]::Parse($f[2]).ToUniversalTime()
  $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
  $state = if (-not $p) { 'gone' } elseif ([Math]::Abs(($p.StartTime.ToUniversalTime() - $start).TotalSeconds) -lt 1) { 'STILL RUNNING' } else { 'gone (pid reused by another process)' }
  $out.Add("launched pid $procId start $($f[2]) : $state")
}

# 6. Scratch size.
$size = (Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum)
$out.Add("scratch: files=$($size.Count) bytes=$($size.Sum) MiB=$([Math]::Round($size.Sum / 1MB, 1))")
$out | Set-Content "$root\results\after-check-$((Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmss')).txt"
$out
