// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch rig (Q302): job objects, suspended launches, and a look at a stuck
// browser's windows. Everything here acts on pids and handles this rig
// created; nothing selects a process by image name.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class Rig
{
    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic;
        public IO_COUNTERS Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION { public uint ControlFlags; public uint CpuRate; }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateJobObjectW(IntPtr sa, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint len);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info, uint len);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint len, IntPtr ret);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_BASIC_ACCOUNTING_INFORMATION info, uint len, IntPtr ret);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint ResumeThread(IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr p, uint code);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")]
    static extern bool GetExitCodeProcess(IntPtr h, out uint code);

    public const uint KillOnJobClose = 0x2000;
    public const uint JobMemory = 0x200;

    public sealed class Launched
    {
        public IntPtr Job, Process;
        public int Pid;
    }

    public static Launched Launch(string app, string commandLine, string cwd, IDictionary<string, string> env, ulong jobMemoryBytes, uint cpuRate)
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject");
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.Basic.LimitFlags = KillOnJobClose;
        if (jobMemoryBytes > 0)
        {
            info.Basic.LimitFlags |= JobMemory;
            info.JobMemoryLimit = (UIntPtr)jobMemoryBytes;
        }
        if (!SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject(extended)");
        if (cpuRate > 0)
        {
            var cpu = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION { ControlFlags = 0x1 | 0x4, CpuRate = cpuRate };
            if (!SetInformationJobObject(job, 15, ref cpu, (uint)Marshal.SizeOf(typeof(JOBOBJECT_CPU_RATE_CONTROL_INFORMATION))))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject(cpu)");
        }

        var block = new StringBuilder();
        var keys = new List<string>(env.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var k in keys) block.Append(k).Append('=').Append(env[k]).Append('\0');
        block.Append('\0');
        var envPtr = Marshal.StringToHGlobalUni(block.ToString());
        try
        {
            var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)) };
            PROCESS_INFORMATION pi;
            // CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW, inherit nothing.
            if (!CreateProcessW(app, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, 0x4 | 0x400 | 0x08000000, envPtr, cwd, ref si, out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW");
            if (!AssignProcessToJobObject(job, pi.hProcess))
            {
                var err = Marshal.GetLastWin32Error();
                TerminateProcess(pi.hProcess, 1);
                throw new Win32Exception(err, "AssignProcessToJobObject");
            }
            ResumeThread(pi.hThread);
            CloseHandle(pi.hThread);
            return new Launched { Job = job, Process = pi.hProcess, Pid = pi.dwProcessId };
        }
        finally { Marshal.FreeHGlobal(envPtr); }
    }

    public static bool HasExited(Launched l) => WaitForSingleObject(l.Process, 0) == 0;

    public static string JobReport(Launched l)
    {
        var ext = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        QueryInformationJobObject(l.Job, 9, ref ext, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)), IntPtr.Zero);
        var acct = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
        QueryInformationJobObject(l.Job, 1, ref acct, (uint)Marshal.SizeOf(typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)), IntPtr.Zero);
        return string.Format("peakJobCommitMB={0:F1} peakProcessCommitMB={1:F1} cpuUserMs={2:F0} cpuKernelMs={3:F0} totalProcesses={4} activeProcesses={5} pageFaults={6}",
            ext.PeakJobMemoryUsed.ToUInt64() / 1048576.0, ext.PeakProcessMemoryUsed.ToUInt64() / 1048576.0,
            acct.TotalUserTime / 10000.0, acct.TotalKernelTime / 10000.0, acct.TotalProcesses, acct.ActiveProcesses, acct.TotalPageFaultCount);
    }

    public static uint ActiveProcesses(Launched l)
    {
        var acct = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
        QueryInformationJobObject(l.Job, 1, ref acct, (uint)Marshal.SizeOf(typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)), IntPtr.Zero);
        return acct.ActiveProcesses;
    }

    // Closing the job handle terminates every member (KILL_ON_JOB_CLOSE): only
    // processes this rig launched are ever in it.
    public static void Close(Launched l)
    {
        CloseHandle(l.Process);
        CloseHandle(l.Job);
    }

    // ---- windows of one pid, for the "is its main thread pumping" question ----
    delegate bool EnumProc(IntPtr hwnd, IntPtr lp);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hwnd, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, UIntPtr wp, IntPtr lp, uint flags, uint timeout, out UIntPtr result);

    public static string WindowsOf(int pid)
    {
        var found = new List<IntPtr>();
        EnumWindows((h, lp) => { uint p; GetWindowThreadProcessId(h, out p); if (p == (uint)pid) found.Add(h); return true; }, IntPtr.Zero);
        var messageOnly = new IntPtr(-3);
        IntPtr child = IntPtr.Zero;
        while ((child = FindWindowExW(messageOnly, child, null, null)) != IntPtr.Zero)
        {
            uint p; GetWindowThreadProcessId(child, out p); if (p == (uint)pid) found.Add(child);
        }
        var sb = new StringBuilder();
        foreach (var h in found)
        {
            uint p; var tid = GetWindowThreadProcessId(h, out p);
            var cls = new StringBuilder(256); GetClassNameW(h, cls, 256);
            UIntPtr r;
            var t0 = Environment.TickCount64;
            var ok = SendMessageTimeoutW(h, 0 /* WM_NULL */, UIntPtr.Zero, IntPtr.Zero, 0x2 /* SMTO_ABORTIFHUNG */, 3000, out r) != IntPtr.Zero;
            var took = Environment.TickCount64 - t0;
            sb.AppendFormat("hwnd=0x{0:X} tid={1} class={2} hung={3} wmNullAnswered={4} in {5} ms\n", h.ToInt64(), tid, cls, IsHungAppWindow(h), ok, took);
        }
        return sb.ToString();
    }
}
