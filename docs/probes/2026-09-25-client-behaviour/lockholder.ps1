# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Holds .work\installer.lock for the Q304 a install, by the protocol
# tests/BrowserAI.Tests/Harness/InstallerLock.cs describes: created with FileMode.CreateNew,
# one line "pid=<pid> created=<FILETIME>" naming THIS process (so the suite judges it alive
# while it is, and stale if it dies), and deleted when done -- only if it still names this
# process. A lock that already exists is waited for, never taken over or removed.
#   -Lock <path> -Release <path of a file whose appearance releases the lock> -Log <path>
param([Parameter(Mandatory)] [string] $Lock, [Parameter(Mandatory)] [string] $Release, [Parameter(Mandatory)] [string] $Log, [int] $WaitMinutes = 45)
$ErrorActionPreference = 'Stop'
function Note([string] $s) { Add-Content -LiteralPath $Log -Value ("{0} {1}" -f [DateTime]::UtcNow.ToString('o'), $s) }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LockSelf
{
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
    public static long Created() { long c, e, k, u; if (!GetProcessTimes(GetCurrentProcess(), out c, out e, out k, out u)) throw new System.ComponentModel.Win32Exception(); return c; }
}
'@
$token = "pid=$PID created=$([LockSelf]::Created())"
Note "holder started: $token"
$deadline = [DateTime]::UtcNow.AddMinutes($WaitMinutes)
while ($true) {
    try {
        $fs = [System.IO.File]::Open($Lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
        try {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($token)
            $fs.Write($bytes, 0, $bytes.Length)
        }
        finally { $fs.Dispose() }
        Note "TAKEN $Lock"
        break
    }
    catch [System.IO.IOException] {
        if ([DateTime]::UtcNow -gt $deadline) { Note "GAVE UP waiting for $Lock"; exit 3 }
        $held = try { [System.IO.File]::ReadAllText($Lock) } catch { '<unreadable>' }
        Note "WAITING: $Lock exists ($held)"
        Start-Sleep -Seconds 2
    }
}
while (-not (Test-Path -LiteralPath $Release)) { Start-Sleep -Milliseconds 500 }
$now = try { [System.IO.File]::ReadAllText($Lock) } catch { $null }
if ($now -eq $token) { Remove-Item -LiteralPath $Lock -Force; Note "RELEASED $Lock (it still named this holder)" }
else { Note "NOT REMOVED: $Lock now reads '$now', not this holder's '$token'" }
