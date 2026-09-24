# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Writes the clearance snapshot the gate compares either side of every run.

.DESCRIPTION
    The suite installs a real pack under a test id, and the eight things below are
    the ones a run must not disturb. They are read before and after each run and
    compared; a difference stops the gate instead of being reported at the end.

      1. HKCU Uninstall\BrowserAI.app -- the maintainer's own Add/Remove entry,
         every value, because Velopack writes one key per pack id per user and an
         install under --installto still rewrites it.
      2. HKCU Uninstall\BrowserAI.app.test -- the suite's own, which must be
         ABSENT: every key under the test id is one the suite wrote, so one that
         outlives a run is a run that did not clean up.
      3. The real registration, which the install and uninstall hooks rewrite:
         the `browserai` entry of `mcpServers` in ~/.claude.json, READ AS A FILE
         AND PARSED, never asked of the client. Q281, decided 2026-09-24 by the
         maintainer, verbatim: "Q281 a". Until then this reading ran the client's
         own `mcp get` verb, which starts the client, health-checks the server it
         names, and may write ~/.claude.json, the one file every run has to be
         shown not to change. `SuiteCoverageTests.
         TheClearanceSnapshotReadsTheRegistrationWithoutStartingTheClient` holds
         it that way.
      4. %TEMP%\velopack_BrowserAI.app -- present means an apply was interrupted.
      5. The Start Menu shortcut, by length and SHA-256, because Velopack names
         it after the pack TITLE and removes shortcuts by target.
      6. ~\.codex\config.toml -- ADDED 2026-09-24 with the Codex half of
         registration. The hooks register with Codex now, and `codex mcp add`
         has no scope flag: it writes whichever configuration CODEX_HOME names,
         so a suite arm that forgot to move it writes the maintainer's own. Read
         as a FILE and not through `codex mcp get`, so the reading works on a
         machine with no CLI on it.
         ⚠️ THE ENTRY AND NOT THE FILE since 2026-09-24 -- Q292, the
         maintainer's words verbatim: "Q292 a - Same for claude code".
         (Previously "by length and SHA-256".) The Codex desktop app rewrites
         this file when it starts, so a whole-file hash stopped a gate on a
         change that was not BrowserAI's. What is compared now is the
         `[mcp_servers.browserai]` entry, line for line, and reading 3 is the
         same rule for Claude Code: the `browserai` entry and no other part of
         ~/.claude.json. `SuiteCoverageTests.
         TheClearanceComparesOnlyEachClientsBrowserAiEntry` runs this script
         against a scratch profile and holds both halves.

      7. HKCU\Environment\Path, read raw -- ADDED 2026-09-24 with Q294 b, the
         maintainer's words verbatim: "Q294 b". The hooks put the install's
         `current\` folder on the user's PATH and take it off again, and the test
         pack runs them in every gate, so the value must come out of every run
         byte-identical. Kind, length and SHA-256 of the unexpanded text, and the
         entries that name BrowserAI; `SuiteCoverageTests.
         TheClearanceReadsTheUserPathByteForByte` holds the line against the test
         host's own read.

      8. The Task Scheduler's root folder -- ADDED 2026-09-25 with Q282 a, the
         maintainer's words verbatim: "Q282 a". The install and update hooks
         register a per-user logon task named `<pack id> sign-in <root key>` and
         the uninstall hook removes it, and the test pack runs those hooks from
         scratch roots in every gate: each `BrowserAI.app sign-in` task by name
         and the SHA-256 of its stored definition, which must not move, and every
         `BrowserAI.app.test` task, of which there must be none.
         `SuiteCoverageTests.TheClearanceNamesATestPackTaskLeftBehind` holds it.

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

# 3. The registration, out of the client's own file and never by starting the
# client (Q281 a). Opened for reading with every sharing mode, so a client
# writing the file at the same moment is neither blocked nor raced by a lock.
# -AsHashtable because the file can carry keys that differ only by case, and it
# returns an ordered table, so two snapshots print the keys in the same order.
$claudeJson = Join-Path $env:USERPROFILE '.claude.json'
$out += '--- ~/.claude.json mcpServers.browserai, parsed read-only ---'
if (Test-Path -LiteralPath $claudeJson) {
    try {
        $stream = [System.IO.File]::Open($claudeJson, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            $parsed = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable
        }
        finally {
            $stream.Dispose()
        }

        $servers = $parsed['mcpServers']
        if ($servers -and $servers.Contains('browserai')) {
            $entry = $servers['browserai']
            foreach ($key in ($entry.Keys | Sort-Object)) {
                $out += ('  {0} = {1}' -f $key, (ConvertTo-Json -InputObject $entry[$key] -Compress -Depth 10))
            }
        }
        else {
            $out += '  browserai ABSENT at user scope'
        }
    }
    catch {
        $out += "  UNREADABLE: $($_.Exception.Message)"
    }
}
else {
    $out += '  ~/.claude.json ABSENT'
}

$staging = Join-Path $env:TEMP 'velopack_BrowserAI.app'
$out += ('TEMP velopack_BrowserAI.app: ' + $(if (Test-Path $staging) { 'PRESENT <<< MUST BE ABSENT' } else { 'ABSENT' }))

$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\BrowserAI.lnk'
if (Test-Path $shortcut) {
    $out += ('StartMenu BrowserAI.lnk PRESENT len={0} sha256={1}' -f (Get-Item $shortcut).Length, (Get-FileHash $shortcut -Algorithm SHA256).Hash)
}
else {
    $out += 'StartMenu BrowserAI.lnk ABSENT'
}

# 6. The Codex registration: the browserai entry of ~/.codex/config.toml and
# nothing else in that file (Q292 a). The whole file was hashed until
# 2026-09-24, and the Codex desktop app rewrites it when it starts, so a gate
# that spanned a Codex start stopped on a change that was not BrowserAI's.
# Read as text, every line of the entry verbatim, with the file opened under
# every sharing mode so a client writing it is neither blocked nor raced:
#   - a `[mcp_servers.browserai]` table and any `[mcp_servers.browserai.*]` sub-table,
#     which is the shape `codex mcp add` writes;
#   - a `browserai` key inside a `[mcp_servers]` table, and a top-level
#     `mcp_servers.browserai` key, which a person may write by hand.
# A value that runs over several lines is followed until its brackets close.
$codex = Join-Path $env:USERPROFILE '.codex\config.toml'
$out += '--- ~/.codex/config.toml [mcp_servers.browserai], read as text ---'
if (Test-Path -LiteralPath $codex) {
    try {
        $stream = [System.IO.File]::Open($codex, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            $toml = $reader.ReadToEnd()
        }
        finally {
            $stream.Dispose()
        }

        $name = '(?:browserai|"browserai"|''browserai'')'
        $entryTable = "^\s*\[\s*mcp_servers\s*\.\s*$name\s*(?:\.[^\]]*)?\]\s*(?:#.*)?$"
        $serversTable = '^\s*\[\s*mcp_servers\s*\]\s*(?:#.*)?$'
        $anyTable = '^\s*\['
        $keyInServers = "^\s*$name\s*[.=]"
        $keyAtTop = "^\s*mcp_servers\s*\.\s*$name\s*[.=]"

        $table = ''
        $open = 0
        $entry = @()

        foreach ($line in ($toml -split "`r?`n")) {
            if ($open -gt 0) {
                # Inside a value that began on an earlier line of the entry.
                $entry += '  ' + $line.Trim()
                $open += ([regex]::Matches($line, '[\[{]').Count - [regex]::Matches($line, '[\]}]').Count)
                continue
            }

            if ($line -match $anyTable) {
                $table = if ($line -match $entryTable) { 'entry' } elseif ($line -match $serversTable) { 'servers' } else { 'other' }
                if ($table -eq 'entry') { $entry += '  ' + $line.Trim() }
                continue
            }

            $trimmed = $line.Trim()
            if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) { continue }

            $belongs = ($table -eq 'entry') -or
                ($table -eq 'servers' -and $line -match $keyInServers) -or
                ($table -eq '' -and $line -match $keyAtTop)

            if ($belongs) {
                $entry += '  ' + $trimmed
                $open = [regex]::Matches($line, '[\[{]').Count - [regex]::Matches($line, '[\]}]').Count
                if ($open -lt 0) { $open = 0 }
            }
        }

        if ($entry.Count -gt 0) {
            $out += $entry
        }
        else {
            $out += '  browserai ABSENT from config.toml'
        }
    }
    catch {
        $out += "  UNREADABLE: $($_.Exception.Message)"
    }
}
else {
    $out += '  ~/.codex/config.toml ABSENT'
}

# 7. The user's own PATH, read raw (Q294 b). The install and update hooks put
# the install's `current\` folder on HKCU\Environment\Path and the uninstall hook
# takes exactly that entry off, and the suite's test pack runs both hooks from
# scratch roots in every gate: the value has to come out of every run exactly as
# it went in. Its kind, its length and the SHA-256 of its unexpanded UTF-16 text,
# and every entry naming BrowserAI -- not the whole value, which lists the
# software installed on this machine.
$out += '--- HKCU\Environment Path, read raw ---'
$environment = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
if ($environment -and ($environment.GetValueNames() -contains 'Path')) {
    $kind = $environment.GetValueKind('Path')
    $text = [string] $environment.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    $hash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::Unicode.GetBytes($text)))
    $out += ('  kind={0} chars={1} sha256={2}' -f $kind, $text.Length, $hash)
    $ours = @($text.Split(';') | Where-Object { $_ -match 'BrowserAI' })
    $out += '  entries naming BrowserAI: ' + $(if ($ours.Count -gt 0) { $ours -join ' | ' } else { 'none' })
}
else {
    $out += '  Path ABSENT'
}

# 8. The per-user logon task (Q282 a). The install and update hooks register one
# task per install root in the scheduler's root folder, named for the pack id and
# the root's key, and the uninstall hook removes it; the suite's test pack runs
# those hooks from scratch roots in every gate. So the real install's task, if
# there is one, must come out of a run byte-identical, and no task under the test
# pack's id may be left. Read through the scheduler's own COM object, which only
# reads here; each task by name and the SHA-256 of the definition it stores.
$out += '--- Task Scheduler root folder, the sign-in tasks ---'
try {
    $scheduler = New-Object -ComObject Schedule.Service
    $scheduler.Connect()
    $all = @($scheduler.GetFolder('\').GetTasks(1))
    $realTasks = @($all | Where-Object { $_.Name -like 'BrowserAI.app sign-in *' } | Sort-Object Name)
    if ($realTasks.Count -eq 0) {
        $out += '  BrowserAI.app sign-in: none'
    }
    foreach ($task in $realTasks) {
        $digest = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::Unicode.GetBytes([string] $task.Xml)))
        $out += ('  {0} sha256={1}' -f $task.Name, $digest)
    }
    $testTasks = @($all | Where-Object { $_.Name -like 'BrowserAI.app.test *' } | Sort-Object Name)
    $out += '  BrowserAI.app.test tasks: ' + $(if ($testTasks.Count -gt 0) { ($testTasks.Name -join ' | ') + ' <<< MUST BE ABSENT' } else { 'none' })
}
catch {
    $out += "  UNREADABLE: $($_.Exception.Message)"
}

$directory = Join-Path $root '.work' 'clearance'
if (-not (Test-Path $directory)) { $null = New-Item -ItemType Directory -Force -Path $directory }

$path = Join-Path $directory "$Tag.txt"
[System.IO.File]::WriteAllText($path, (($out -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
Write-Host "clearance snapshot -> $path"
