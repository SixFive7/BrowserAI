# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Takes or lets go of `.work/installer.lock` on behalf of a gate driver.

.DESCRIPTION
    Q291, decided 2026-09-24 by the maintainer, verbatim: "Q291 a". The suite takes
    the lock itself when a session starts (tests/BrowserAI.Tests/Harness/
    InstallerLock.cs), and a gate driver takes it before its first clearance
    snapshot and declares itself in BROWSERAI_INSTALLER_LOCK_HELD, so the test host
    it starts finds the holder it was told about and neither waits for it nor lets
    it go. This script is that driver half, shared by the four drivers because the
    protocol is one protocol; the drive-letter spelling each driver forces is not in
    here and stays in each driver.

    THE PROTOCOL, and the suite's reader holds the same one:
      - the file is created with CreateNew and carries one line naming the holder's
        pid and its creation time as a FILETIME;
      - a file whose holder is gone is stale and is taken over;
      - a live holder is waited for, and past the wait this script exits 1 naming it;
      - the holder deletes the file when it is done, and only a file that still
        names it.

    `-Take` prints the declaration token on success and nothing else on stdout, so a
    driver can capture it. The holder is a pid the caller names: a PowerShell driver
    passes its own `$PID`, a Git Bash driver passes the Windows pid of its own bash
    process, because that is the process that lives for the whole gate.

.PARAMETER Take
    Take the lock for -HolderPid, waiting for a live holder.

.PARAMETER Release
    Let the lock go, when it still names -HolderPid.

.PARAMETER HolderPid
    The Windows pid of the process that holds it.

.PARAMETER Path
    The lock file. Defaults to the main checkout's `.work/installer.lock`.

.PARAMETER WaitSeconds
    How long to wait for a live holder. Defaults to the suite's own
    TestDefaults.InstallerLockWait, thirty minutes.

.EXAMPLE
    $token = & build/InstallerLock.ps1 -Take -HolderPid $PID
#>
[CmdletBinding()]
param(
    [switch] $Take,
    [switch] $Release,
    [Parameter(Mandatory)] [int] $HolderPid,
    [string] $Path,
    [int] $WaitSeconds = 1800)

$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'

if (-not $Path) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

    # A linked worktree's .git is a file naming its git directory, whose
    # `commondir` names the main checkout's: one lock for every checkout.
    $dotGit = Join-Path $root '.git'
    if (Test-Path -LiteralPath $dotGit -PathType Leaf) {
        $pointer = (Get-Content -LiteralPath $dotGit -Raw).Trim()
        if ($pointer -match '^gitdir:\s*(?<dir>.+)$') {
            $gitDirectory = [System.IO.Path]::GetFullPath($Matches['dir'].Trim(), $root)
            $commonFile = Join-Path $gitDirectory 'commondir'
            if (Test-Path -LiteralPath $commonFile) {
                $common = [System.IO.Path]::GetFullPath((Get-Content -LiteralPath $commonFile -Raw).Trim(), $gitDirectory)
                $root = Split-Path -Parent ([System.IO.Path]::TrimEndingDirectorySeparator($common))
            }
        }
    }

    $Path = Join-Path $root '.work' 'installer.lock'
}

function Get-CreationFileTime([int] $ProcessId) {
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $process) { return $null }
    try { return $process.StartTime.ToFileTimeUtc() } catch { return $null }
}

function Read-Holder([string] $Text) {
    if ($Text -notmatch '\bpid(?:=|\s)(?<pid>\d+)') { return $null }
    $holder = [pscustomobject]@{ Pid = [int] $Matches['pid']; Created = $null }
    if ($Text -match '\bcreated=(?<created>\d+)') { $holder.Created = [long] $Matches['created'] }
    return $holder
}

function Test-Alive($Holder) {
    $created = Get-CreationFileTime $Holder.Pid
    if ($null -eq $created) { return $false }
    if ($null -eq $Holder.Created) { return $true }
    return $created -eq $Holder.Created
}

function Read-Lock {
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
        try { return (New-Object System.IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Dispose() }
    }
    catch [System.IO.FileNotFoundException] { return $null }
    catch [System.IO.DirectoryNotFoundException] { return $null }
}

$created = Get-CreationFileTime $HolderPid
if ($null -eq $created) {
    [Console]::Error.WriteLine("There is no live process $HolderPid to hold the installer lock.")
    exit 2
}

$token = "pid=$HolderPid created=$created"

if ($Release) {
    $text = Read-Lock
    if ($null -ne $text) {
        $holder = Read-Holder $text
        if ($holder -and $holder.Pid -eq $HolderPid -and $holder.Created -eq $created) {
            Remove-Item -LiteralPath $Path -Force
            [Console]::Error.WriteLine("Let go of $Path ($token).")
        }
        else {
            [Console]::Error.WriteLine("$Path names '$($text.Trim())' and not $token, so it is not this holder's to let go.")
        }
    }
    exit 0
}

if (-not $Take) {
    [Console]::Error.WriteLine('Pass -Take or -Release.')
    exit 2
}

$deadline = (Get-Date).AddSeconds($WaitSeconds)
$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path)

while ($true) {
    $text = Read-Lock

    if ($null -eq $text) {
        try {
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read -bor [System.IO.FileShare]::Delete)
            try {
                $bytes = [System.Text.Encoding]::UTF8.GetBytes("holder=gate $token at=$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))`n")
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally {
                $stream.Dispose()
            }

            [Console]::Error.WriteLine("Took $Path ($token).")
            Write-Output $token
            exit 0
        }
        catch [System.IO.IOException] {
            continue
        }
    }

    $holder = Read-Holder $text

    if ($holder -and $holder.Pid -eq $HolderPid -and $holder.Created -eq $created) {
        # Already this holder's: taking it twice is taking it once.
        Write-Output $token
        exit 0
    }

    # Gone between the read and here: look again.
    $item = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if (-not $item) { continue }

    # A file with no holder in it is a holder mid-write for a moment, and debris
    # after ten seconds: the suite's reader gives it the same grace.
    $age = (Get-Date).ToUniversalTime() - $item.LastWriteTimeUtc
    $stale = if ($holder) { -not (Test-Alive $holder) } else { $age.TotalSeconds -ge 10 }

    if ($stale) {
        if ((Read-Lock) -eq $text) {
            Remove-Item -LiteralPath $Path -Force
            [Console]::Error.WriteLine("Removed a stale $Path ('$($text.Trim())'): its holder is gone.")
        }
        continue
    }

    if ((Get-Date) -ge $deadline) {
        [Console]::Error.WriteLine("'$($text.Trim())' held $Path for the whole wait of $WaitSeconds s. Nothing was run.")
        exit 1
    }

    Start-Sleep -Seconds 2
}
