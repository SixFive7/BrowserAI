# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Runs one command on a desktop of its own that nobody is looking at, inside a
    kill-on-close job, and records every top-level window the job's processes
    own: on that desktop, and on the desktop this script runs on.

.DESCRIPTION
    The desktop is created with CreateDesktopW in this process's window station
    and without DESKTOP_SWITCHDESKTOP, so nothing here can put it on the screen.
    The command is created suspended with STARTUPINFO.lpDesktop naming it,
    assigned to the job, and resumed. Every process it starts inherits the
    desktop and joins the job.

    Once a second the script reads the job's process ids and enumerates the
    top-level windows of BOTH desktops, keeping the visible ones whose owning
    pid is in the job. A visible window of the job on this script's own desktop
    is a window on the person's screen: it is recorded as LEAK and the job is
    terminated at once. The same pid filter run over the private desktop is the
    positive control: it is what finds the browser's own window there.

    Every observation is keyed on pids the job reports, never on an image name.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Purpose,
    [Parameter(Mandatory)] [string] $App,
    [Parameter(Mandatory)] [string] $CommandLine,
    [Parameter(Mandatory)] [string] $WorkingDirectory,
    [Parameter(Mandatory)] [string] $Log,
    [int] $TimeoutSec = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class LifetimeDesk
{
    const uint DesktopReadObjects = 0x0001, DesktopCreateWindow = 0x0002, DesktopCreateMenu = 0x0004,
        DesktopHookControl = 0x0008, DesktopEnumerate = 0x0040, DesktopWriteObjects = 0x0080, ReadControl = 0x00020000;
    const uint CreateSuspended = 0x00000004, CreateUnicodeEnvironment = 0x00000400, CreateNoWindow = 0x08000000;
    const int JobObjectBasicProcessIdList = 3, JobObjectExtendedLimitInformation = 9;
    const uint KillOnJobClose = 0x00002000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFOW
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [StructLayout(LayoutKind.Sequential)]
    struct BASIC_LIMIT
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    struct EXTENDED_LIMIT
    {
        public BASIC_LIMIT Basic; public IO_COUNTERS Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr devmode, uint flags, uint access, IntPtr sa);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder info, int length, out int needed);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr desktop, EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObjectW(IntPtr sa, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int cls, ref EXTENDED_LIMIT info, int length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(IntPtr job, int cls, IntPtr info, int length, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateJobObject(IntPtr job, uint code);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcessW(string app, StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string cwd, ref STARTUPINFOW startup, out PROCESS_INFORMATION info);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);

    public static IntPtr Desktop, Job, Process;
    public static string DesktopName, StartupName;
    public static int ProcessId;

    public static void Create(string purpose)
    {
        DesktopName = "BrowserAI-lifetime-" + purpose + "-" + Guid.NewGuid().ToString("N");
        Desktop = CreateDesktopW(DesktopName, IntPtr.Zero, IntPtr.Zero, 0,
            DesktopReadObjects | DesktopCreateWindow | DesktopCreateMenu | DesktopHookControl | DesktopEnumerate | DesktopWriteObjects | ReadControl,
            IntPtr.Zero);
        if (Desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDesktopW");
        var station = new StringBuilder(256);
        int needed;
        if (!GetUserObjectInformationW(GetProcessWindowStation(), 2, station, station.Capacity * 2, out needed))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetUserObjectInformationW");
        StartupName = station.ToString() + "\\" + DesktopName;

        Job = CreateJobObjectW(IntPtr.Zero, null);
        if (Job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObjectW");
        var limit = new EXTENDED_LIMIT();
        limit.Basic.LimitFlags = KillOnJobClose;
        if (!SetInformationJobObject(Job, JobObjectExtendedLimitInformation, ref limit, Marshal.SizeOf(typeof(EXTENDED_LIMIT))))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject");
    }

    public static void Start(string app, string commandLine, string cwd)
    {
        var startup = new STARTUPINFOW();
        startup.cb = Marshal.SizeOf(typeof(STARTUPINFOW));
        startup.lpDesktop = StartupName;
        PROCESS_INFORMATION info;
        if (!CreateProcessW(app, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow, IntPtr.Zero, cwd, ref startup, out info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW");
        if (!AssignProcessToJobObject(Job, info.hProcess))
        {
            var error = Marshal.GetLastWin32Error();
            TerminateJobObject(Job, 1);
            throw new Win32Exception(error, "AssignProcessToJobObject");
        }
        ResumeThread(info.hThread);
        CloseHandle(info.hThread);
        Process = info.hProcess;
        ProcessId = info.dwProcessId;
    }

    public static bool Wait(uint ms) { return WaitForSingleObject(Process, ms) == 0; }

    public static uint ExitCode() { uint code; GetExitCodeProcess(Process, out code); return code; }

    public static HashSet<uint> JobPids()
    {
        var pids = new HashSet<uint>();
        int size = 8 + 8 * 4096;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            int returned;
            if (QueryInformationJobObject(Job, JobObjectBasicProcessIdList, buffer, size, out returned))
            {
                int count = Marshal.ReadInt32(buffer, 4);
                for (int i = 0; i < count; i++) pids.Add((uint)Marshal.ReadIntPtr(buffer, 8 + 8 * i).ToInt64());
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return pids;
    }

    public static List<string> Windows(bool privateDesktop, HashSet<uint> pids)
    {
        var found = new List<string>();
        EnumProc callback = (hwnd, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pids.Contains(pid) && IsWindowVisible(hwnd))
            {
                var cls = new StringBuilder(256); GetClassNameW(hwnd, cls, 256);
                var title = new StringBuilder(512); GetWindowTextW(hwnd, title, 512);
                RECT r; GetWindowRect(hwnd, out r);
                found.Add(string.Format("pid={0} hwnd=0x{1:x} class={2} rect={3},{4},{5},{6} title={7}",
                    pid, hwnd.ToInt64(), cls, r.Left, r.Top, r.Right, r.Bottom, title.ToString().Replace("\r", " ").Replace("\n", " ")));
            }
            return true;
        };
        if (privateDesktop) EnumDesktopWindows(Desktop, callback, IntPtr.Zero); else EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    public static void End()
    {
        if (Job != IntPtr.Zero) { TerminateJobObject(Job, 1); CloseHandle(Job); Job = IntPtr.Zero; }
        if (Process != IntPtr.Zero) { CloseHandle(Process); Process = IntPtr.Zero; }
        if (Desktop != IntPtr.Zero) { CloseDesktop(Desktop); Desktop = IntPtr.Zero; }
    }
}
'@

function Write-Record([string] $kind, [string] $text) {
    $line = '{0} {1} {2}' -f ([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')), $kind, $text
    Add-Content -LiteralPath $Log -Value $line -Encoding utf8
}

$exitCode = 255
$leaked = $false
try {
    [LifetimeDesk]::Create($Purpose)
    Write-Record 'DESKTOP' ([LifetimeDesk]::StartupName)
    [LifetimeDesk]::Start($App, $CommandLine, $WorkingDirectory)
    Write-Record 'START' ("pid={0} app={1} cmd={2}" -f [LifetimeDesk]::ProcessId, $App, $CommandLine)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    $lastPrivate = ''
    $lastScreen = ''
    $maxPids = 0
    $privateSeen = 0
    $ticks = 0
    while ($true) {
        $done = [LifetimeDesk]::Wait(1000)
        $ticks++
        $pids = [LifetimeDesk]::JobPids()
        if ($pids.Count -gt $maxPids) { $maxPids = $pids.Count }
        $screen = @([LifetimeDesk]::Windows($false, $pids))
        $private = @([LifetimeDesk]::Windows($true, $pids))
        if ($private.Count -gt 0) { $privateSeen++ }
        $privateText = ($private | Sort-Object) -join ' | '
        $screenText = ($screen | Sort-Object) -join ' | '
        if ($privateText -ne $lastPrivate) { Write-Record 'PRIVATE' ("pids={0} windows={1} {2}" -f $pids.Count, $private.Count, $privateText); $lastPrivate = $privateText }
        if ($screen.Count -gt 0) {
            Write-Record 'LEAK' ("pids={0} windows={1} {2}" -f $pids.Count, $screen.Count, $screenText)
            $leaked = $true
            [LifetimeDesk]::End()
            break
        }
        if ($done) { $exitCode = [int][LifetimeDesk]::ExitCode(); break }
        if ([DateTime]::UtcNow -gt $deadline) { Write-Record 'TIMEOUT' "after $TimeoutSec s"; break }
    }
    Write-Record 'SUMMARY' ("exit={0} ticks={1} maxJobPids={2} ticksWithPrivateWindows={3} leaked={4}" -f $exitCode, $ticks, $maxPids, $privateSeen, $leaked)
}
catch {
    Write-Record 'ERROR' $_.Exception.ToString().Replace("`r", ' ').Replace("`n", ' ')
}
finally {
    $left = 0
    try { $left = [LifetimeDesk]::JobPids().Count } catch { }
    Write-Record 'END' ("processes still in the job before it closed: {0}" -f $left)
    [LifetimeDesk]::End()
}
if ($leaked) { exit 3 }
exit $exitCode
