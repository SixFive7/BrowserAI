# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Holds one process by pid AND creation time, waits for it to end, and writes
# its exit code. With -Terminate it ends that same process with the given exit
# code first, which is how the rig plays a kill. The pid and the creation time
# both come from the rig's own census of the processes the BrowserAI.Server.exe
# it started has under it, so nothing here selects a process by its image name,
# and a pid Windows has given to another process is refused.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $ProcessId,
    [Parameter(Mandatory)] [long] $CreatedFileTime,
    [Parameter(Mandatory)] [string] $Out,
    [int] $Terminate = -1,
    [int] $TimeoutSec = 120
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ExitWatch
{
    const uint Synchronize = 0x00100000, QueryLimited = 0x00001000, TerminateRight = 0x0001;
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr h, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public static string Run(int pid, long created, int terminate, int timeoutMs, string armed)
    {
        var h = OpenProcess(Synchronize | QueryLimited | (terminate >= 0 ? TerminateRight : 0), false, (uint)pid);
        if (h == IntPtr.Zero) return "open-failed " + Marshal.GetLastWin32Error();
        try
        {
            long c, e, k, u;
            if (!GetProcessTimes(h, out c, out e, out k, out u)) return "times-failed " + Marshal.GetLastWin32Error();
            // WMI reports a creation time to the microsecond and the kernel to 100 ns,
            // and the two were seen 12 and 14 ticks apart for the same process, so a
            // pid whose creation time is within 5 microseconds is the one recorded.
            if (Math.Abs(c - created) >= 50) return "not-the-process created=" + c + " expected=" + created;
            // Armed: the handle is held, so the process cannot end unseen from here on.
            System.IO.File.WriteAllText(armed, "armed " + DateTime.UtcNow.ToString("o"));
            var started = DateTime.UtcNow;
            if (terminate >= 0 && !TerminateProcess(h, (uint)terminate)) return "terminate-failed " + Marshal.GetLastWin32Error();
            var wait = WaitForSingleObject(h, (uint)timeoutMs);
            if (wait != 0) return "still-running wait=" + wait;
            uint code;
            if (!GetExitCodeProcess(h, out code)) return "exitcode-failed " + Marshal.GetLastWin32Error();
            return string.Format("exited code={0} hex=0x{0:X8} after_ms={1}", code, (long)(DateTime.UtcNow - started).TotalMilliseconds);
        }
        finally { CloseHandle(h); }
    }
}
'@

$started = [DateTime]::UtcNow.ToString('o')
$result = [ExitWatch]::Run($ProcessId, $CreatedFileTime, $Terminate, $TimeoutSec * 1000, "$Out.armed")
Set-Content -LiteralPath $Out -Encoding utf8 -Value ("pid={0} created={1} terminate={2} watchStartedUtc={3} endedUtc={4} {5}" -f $ProcessId, $CreatedFileTime, $Terminate, $started, [DateTime]::UtcNow.ToString('o'), $result)
