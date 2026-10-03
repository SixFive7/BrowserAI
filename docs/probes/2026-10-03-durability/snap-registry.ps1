# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Read-only snapshot of the registry keys and folders
# a Firefox or Chrome for Testing run may write outside the scratch. Writes one TSV per key.
param([Parameter(Mandatory)] [string] $Label)
$ErrorActionPreference = 'Continue'
$out = Join-Path 'C:\Source\SixFive7\BrowserAI\.work\durability\baseline' $Label
[void][System.IO.Directory]::CreateDirectory($out)
$t = (Get-Date).ToUniversalTime().ToString('o')
$keys = [ordered]@{
  'ff-launcher'        = 'HKCU:\Software\Mozilla\Firefox\Launcher'
  'ff-dllprefetch'     = 'HKCU:\Software\Mozilla\Firefox\DllPrefetchExperiment'
  'ff-skeletonui'      = 'HKCU:\Software\Mozilla\Firefox\PreXULSkeletonUISettings'
  'cft-root'           = 'HKCU:\Software\Google\Chrome for Testing'
  'cft-blbeacon'       = 'HKCU:\Software\Google\Chrome for Testing\BLBeacon'
  'cft-prefmacs'       = 'HKCU:\Software\Google\Chrome for Testing\PreferenceMACs\Default'
  'cft-stability'      = 'HKCU:\Software\Google\Chrome for Testing\StabilityMetrics'
  'cft-thirdparty'     = 'HKCU:\Software\Google\Chrome for Testing\ThirdParty'
}
foreach ($name in $keys.Keys) {
  $kp = $keys[$name]
  $lines = @("# $t $kp")
  if (Test-Path -LiteralPath $kp) {
    $k = Get-Item -LiteralPath $kp
    $lines += "# exists=True values=$($k.ValueCount) subkeys=$($k.SubKeyCount) subkeyNames=$((($k.GetSubKeyNames()) -join ','))"
    foreach ($n in $k.GetValueNames()) {
      $v = $k.GetValue($n, $null, 'DoNotExpandEnvironmentNames')
      if ($v -is [byte[]]) { $v = 'bytes:' + [Convert]::ToHexString($v) }
      $lines += "{0}`t{1}`t{2}" -f $n, $k.GetValueKind($n), $v
    }
  } else { $lines += '# exists=False' }
  $lines | Set-Content -LiteralPath (Join-Path $out "$name.tsv") -Encoding utf8
}
$dirs = [ordered]@{
  'appdata-mozilla-firefox'      = (Join-Path $env:APPDATA 'Mozilla\Firefox')
  'localappdata-mozilla-firefox' = (Join-Path $env:LOCALAPPDATA 'Mozilla\Firefox')
  'localappdata-ms-playwright'   = (Join-Path $env:LOCALAPPDATA 'ms-playwright')
}
foreach ($name in $dirs.Keys) {
  $d = $dirs[$name]
  $lines = @("# $t $d")
  if (Test-Path -LiteralPath $d) {
    foreach ($e in Get-ChildItem -LiteralPath $d -Force -Recurse -Depth 1 -ErrorAction SilentlyContinue) {
      $lines += "{0}`tcreated={1}`tmtime={2}`t{3}" -f $e.FullName.Substring($d.Length), $e.CreationTimeUtc.ToString('o'), $e.LastWriteTimeUtc.ToString('o'), $(if ($e.PSIsContainer) { 'D' } else { $e.Length })
    }
  } else { $lines += '# absent' }
  $lines | Set-Content -LiteralPath (Join-Path $out "$name.tsv") -Encoding utf8
}
"snapshot $Label at $t -> $out"
