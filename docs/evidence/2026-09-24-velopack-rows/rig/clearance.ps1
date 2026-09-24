# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig for the 2026-09-24 re-verification of rows 123/124/126/130.
# Reads, never repairs. Mirrors build/Get-ClearanceSnapshot.ps1's readings 1, 2, 4 and 5,
# deliberately WITHOUT `claude mcp get` (it can write ~/.claude.json) and WITHOUT reading ~/.codex.
param([Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference = 'Continue'
$PSStyle.OutputRendering = 'PlainText'
$dir = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\clearance'
if (-not (Test-Path $dir)) { $null = New-Item -ItemType Directory -Force -Path $dir }
$out = [System.Collections.Generic.List[string]]::new()
$out.Add("tag=$Tag at=$((Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fffzzz'))")
$uninstall = 'Software\Microsoft\Windows\CurrentVersion\Uninstall'

# 1. The real key, value by value (kind + exact bytes where it matters) AND as a reg export, hashed.
$real = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$uninstall\BrowserAI.app")
if ($real) {
    $out.Add('ARP BrowserAI.app PRESENT')
    foreach ($name in ($real.GetValueNames() | Sort-Object)) {
        $kind = $real.GetValueKind($name)
        $v = $real.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $out.Add(('  {0} {1} = {2}' -f $name, $kind, $v))
    }
    foreach ($sub in ($real.GetSubKeyNames() | Sort-Object)) { $out.Add("  [subkey] $sub") }
    $real.Dispose()
    $exp = Join-Path $dir "$Tag-arp-BrowserAI.app.reg"
    $null = & reg.exe export 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BrowserAI.app' $exp /y 2>&1
    if (Test-Path $exp) {
        $bytes = [IO.File]::ReadAllBytes($exp)
        # reg export's first line is a header only; hash the whole file AND the body after the header
        $out.Add(('ARP BrowserAI.app reg-export len={0} sha256={1}' -f $bytes.Length, (Get-FileHash $exp -Algorithm SHA256).Hash))
    } else { $out.Add('ARP BrowserAI.app reg-export FAILED') }
} else { $out.Add('ARP BrowserAI.app ABSENT') }

# 2. The suite's test key: must be ABSENT at the end of every install/uninstall cycle.
$test = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$uninstall\BrowserAI.app.test")
if ($test) {
    $out.Add('ARP BrowserAI.app.test: PRESENT')
    foreach ($name in ($test.GetValueNames() | Sort-Object)) {
        $out.Add(('  {0} {1} = {2}' -f $name, $test.GetValueKind($name), $test.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)))
    }
    $test.Dispose()
} else { $out.Add('ARP BrowserAI.app.test: ABSENT') }

# 4. The apply-staging directory of the REAL id.
$staging = Join-Path $env:TEMP 'velopack_BrowserAI.app'
$out.Add('TEMP velopack_BrowserAI.app: ' + $(if (Test-Path -LiteralPath $staging) { 'PRESENT <<< MUST BE ABSENT' } else { 'ABSENT' }))
$stagingTest = Join-Path $env:TEMP 'velopack_BrowserAI.app.test'
$out.Add('TEMP velopack_BrowserAI.app.test: ' + $(if (Test-Path -LiteralPath $stagingTest) { 'PRESENT' } else { 'ABSENT' }))

# 5. The real Start Menu shortcut, by length and SHA-256; plus the suite's own title.
$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
foreach ($n in @('BrowserAI.lnk', 'BrowserAI (suite).lnk')) {
    $p = Join-Path $programs $n
    if (Test-Path -LiteralPath $p) {
        $out.Add(('StartMenu {0} PRESENT len={1} sha256={2} mtime={3}' -f $n, (Get-Item -LiteralPath $p).Length, (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash, (Get-Item -LiteralPath $p).LastWriteTimeUtc.ToString('o')))
    } else { $out.Add("StartMenu $n ABSENT") }
}
$others = Get-ChildItem -LiteralPath $programs -Filter 'BrowserAI*.lnk' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin @('BrowserAI.lnk','BrowserAI (suite).lnk') }
foreach ($o in $others) { $out.Add("StartMenu OTHER $($o.Name) len=$($o.Length)") }

# Context, not a gate: the real install's own servers, by PATH under the real root (observed, never touched).
$realRoot = Join-Path $env:LOCALAPPDATA 'BrowserAI.app\'
$procs = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($realRoot, [StringComparison]::OrdinalIgnoreCase) }
$out.Add(('RealRoot processes: {0} ({1})' -f @($procs).Count, ((@($procs) | Sort-Object ProcessId | ForEach-Object { "$($_.Name):$($_.ProcessId)" }) -join ',')))

$path = Join-Path $dir "$Tag.txt"
[IO.File]::WriteAllText($path, (($out -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Get-Content -LiteralPath $path
