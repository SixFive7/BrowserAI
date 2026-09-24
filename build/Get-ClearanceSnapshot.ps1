# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Writes the clearance snapshot the gate compares either side of every run.

.DESCRIPTION
    The suite installs a real pack under a test id, and the six things below are
    the ones a run must not disturb. They are read before and after each run and
    compared; a difference stops the gate instead of being reported at the end.

      1. HKCU Uninstall\BrowserAI.app -- the maintainer's own Add/Remove entry,
         every value, because Velopack writes one key per pack id per user and an
         install under --installto still rewrites it.
      2. HKCU Uninstall\BrowserAI.app.test -- the suite's own, which must be
         ABSENT: every key under the test id is one the suite wrote, so one that
         outlives a run is a run that did not clean up.
      3. `claude mcp get browserai` -- the real registration, which the install
         and uninstall hooks rewrite.
      4. %TEMP%\velopack_BrowserAI.app -- present means an apply was interrupted.
      5. The Start Menu shortcut, by length and SHA-256, because Velopack names
         it after the pack TITLE and removes shortcuts by target.
      6. ~\.codex\config.toml, by length and SHA-256 -- ADDED 2026-09-24 with the
         Codex half of registration. The hooks register with Codex now, and
         `codex mcp add` has no scope flag: it writes whichever configuration
         CODEX_HOME names, so a suite arm that forgot to move it writes the
         maintainer's own. Read as a FILE and not through `codex mcp get`,
         because the thing that must not change is the file, and the reading has
         to work on a machine with no CLI on it.

    ⚠️ IT READS AND NEVER REPAIRS. A snapshot that fixed what it found would
    destroy the evidence of the run that broke it. On a difference the gate
    stops and a human looks; TESTING.md says how to clear a dangling key.

.PARAMETER Tag
    Names the snapshot file under `.work/clearance/`.

.EXAMPLE
    pwsh -File build/Get-ClearanceSnapshot.ps1 -Tag ord-ps-1-before
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $Tag)

# Continue and not Stop: a snapshot that throws half way through reports
# nothing, and every reading below is allowed to be absent.
$ErrorActionPreference = 'Continue'
$PSStyle.OutputRendering = 'PlainText'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out = @()

$uninstall = 'Software\Microsoft\Windows\CurrentVersion\Uninstall'

$real = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$uninstall\BrowserAI.app")
if ($real) {
    $out += 'ARP BrowserAI.app PRESENT'
    foreach ($name in ($real.GetValueNames() | Sort-Object)) {
        $out += ('  {0} {1} = {2}' -f $name, $real.GetValueKind($name), $real.GetValue($name))
    }
}
else {
    $out += 'ARP BrowserAI.app ABSENT'
}

$test = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$uninstall\BrowserAI.app.test")
$out += ('ARP BrowserAI.app.test: ' + $(if ($test) { 'PRESENT <<< MUST BE ABSENT' } else { 'ABSENT' }))

$out += '--- claude mcp get browserai ---'
$out += ((claude mcp get browserai 2>&1 | Out-String) -split "`r?`n" | Where-Object { $_ -notmatch '^\s*$' })

$staging = Join-Path $env:TEMP 'velopack_BrowserAI.app'
$out += ('TEMP velopack_BrowserAI.app: ' + $(if (Test-Path $staging) { 'PRESENT <<< MUST BE ABSENT' } else { 'ABSENT' }))

$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\BrowserAI.lnk'
if (Test-Path $shortcut) {
    $out += ('StartMenu BrowserAI.lnk PRESENT len={0} sha256={1}' -f (Get-Item $shortcut).Length, (Get-FileHash $shortcut -Algorithm SHA256).Hash)
}
else {
    $out += 'StartMenu BrowserAI.lnk ABSENT'
}

$codex = Join-Path $env:USERPROFILE '.codex\config.toml'
if (Test-Path $codex) {
    $out += ('Codex config.toml PRESENT len={0} sha256={1}' -f (Get-Item $codex).Length, (Get-FileHash $codex -Algorithm SHA256).Hash)
}
else {
    $out += 'Codex config.toml ABSENT'
}

$directory = Join-Path $root '.work' 'clearance'
if (-not (Test-Path $directory)) { $null = New-Item -ItemType Directory -Force -Path $directory }

$path = Join-Path $directory "$Tag.txt"
[System.IO.File]::WriteAllText($path, (($out -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
Write-Host "clearance snapshot -> $path"
