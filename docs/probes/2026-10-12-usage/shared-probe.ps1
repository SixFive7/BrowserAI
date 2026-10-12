# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Exploration only: what PROCESS_MEMORY_COUNTERS_EX2 says for each process of one headless
# Chromium, started by this script in a kill-on-close job of its own, so nothing outside the
# job is read. Prints one line per process and the cost of reading the whole job.
param(
    [Parameter(Mandatory)] [string] $Chrome,
    [Parameter(Mandatory)] [string] $Profile,
    [string] $Url = 'about:blank',
    [int] $SettleSeconds = 8,
    [int] $Rounds = 1000
)

$source = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class UsageProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Counters
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
        public UIntPtr PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimit
    {
        public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Io { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimit
    {
        public BasicLimit Basic; public Io IoInfo; public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Accounting
    {
        public long TotalUserTime; public long TotalKernelTime; public long ThisPeriodTotalUserTime; public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount; public uint TotalProcesses; public uint ActiveProcesses; public uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObjectW(IntPtr a, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int cls, ref ExtendedLimit info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(IntPtr job, int cls, ref Accounting info, int size, out int ret);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryInformationJobObject")] static extern bool QueryIds(IntPtr job, int cls, byte[] info, int size, out int ret);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref StartupInfo si, out ProcessInformation pi);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool K32GetProcessMemoryInfo(IntPtr process, ref Counters counters, int cb);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr p, out long c, out long e, out long k, out long u);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr p, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsProcessInJob(IntPtr p, IntPtr job, out bool result);

    public static IntPtr Job;

    public static int Start(string chrome, string profile, string url)
    {
        Job = CreateJobObjectW(IntPtr.Zero, null);
        var limit = new ExtendedLimit();
        limit.Basic.LimitFlags = 0x2000;
        if (!SetInformationJobObject(Job, 9, ref limit, Marshal.SizeOf(typeof(ExtendedLimit)))) throw new Exception("set " + Marshal.GetLastWin32Error());
        var si = new StartupInfo(); si.cb = Marshal.SizeOf(typeof(StartupInfo));
        var cmd = new StringBuilder("\"" + chrome + "\" --headless=new --no-first-run --no-default-browser-check --user-data-dir=\"" + profile + "\" --remote-debugging-port=0 " + url);
        ProcessInformation pi;
        // CREATE_SUSPENDED | CREATE_NO_WINDOW
        if (!CreateProcessW(chrome, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x4 | 0x08000000, IntPtr.Zero, profile, ref si, out pi)) throw new Exception("create " + Marshal.GetLastWin32Error());
        if (!AssignProcessToJobObject(Job, pi.hProcess)) throw new Exception("assign " + Marshal.GetLastWin32Error());
        ResumeThread(pi.hThread);
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        return pi.dwProcessId;
    }

    public static int[] Members()
    {
        var buffer = new byte[8 + 256 * IntPtr.Size];
        int ret;
        if (!QueryIds(Job, 3, buffer, buffer.Length, out ret)) throw new Exception("ids " + Marshal.GetLastWin32Error());
        var count = BitConverter.ToInt32(buffer, 4);
        var ids = new int[count];
        for (var i = 0; i < count; i++) ids[i] = (int)BitConverter.ToInt64(buffer, 8 + i * 8);
        return ids;
    }

    public static Accounting Account()
    {
        var a = new Accounting(); int ret;
        if (!QueryInformationJobObject(Job, 1, ref a, Marshal.SizeOf(typeof(Accounting)), out ret)) throw new Exception("acct " + Marshal.GetLastWin32Error());
        return a;
    }

    public static string Describe(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return pid + " could not be opened " + Marshal.GetLastWin32Error();
        try
        {
            var c = new Counters(); c.cb = (uint)Marshal.SizeOf(typeof(Counters));
            var ok = K32GetProcessMemoryInfo(h, ref c, (int)c.cb);
            var err = ok ? 0 : Marshal.GetLastWin32Error();
            var sb = new StringBuilder(1024); var size = sb.Capacity;
            QueryFullProcessImageNameW(h, 0, sb, ref size);
            long cr, ex, k, u; GetProcessTimes(h, out cr, out ex, out k, out u);
            const double MB = 1024.0 * 1024.0;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0,6} ok={1} err={2} cb={3} ws={4,8:0.0} pws={5,8:0.0} sharedws={6,8:0.0} sharedcommit={7,8:0.0} private={8,8:0.0} cpu={9,7:0.000}s {10}",
                pid, ok, err, c.cb, (ulong)c.WorkingSetSize / MB, (ulong)c.PrivateWorkingSetSize / MB, ((ulong)c.WorkingSetSize - (ulong)c.PrivateWorkingSetSize) / MB,
                c.SharedCommitUsage / MB, (ulong)c.PrivateUsage / MB, (k + u) / 1e7, System.IO.Path.GetFileName(sb.ToString()));
        }
        finally { CloseHandle(h); }
    }

    // One reading the way the product means to take it: the job's accounting, its members,
    // and for each member an open, a creation time, a membership check, the counters and its image path.
    public static long ReadOnce()
    {
        var a = Account();
        long sum = a.TotalUserTime + a.TotalKernelTime;
        foreach (var pid in Members())
        {
            var h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) continue;
            try
            {
                long cr, ex, k, u; GetProcessTimes(h, out cr, out ex, out k, out u);
                bool inJob; IsProcessInJob(h, Job, out inJob);
                var c = new Counters(); c.cb = (uint)Marshal.SizeOf(typeof(Counters));
                K32GetProcessMemoryInfo(h, ref c, (int)c.cb);
                var sb = new StringBuilder(1024); var size = sb.Capacity;
                QueryFullProcessImageNameW(h, 0, sb, ref size);
                sum += (long)(ulong)c.PrivateWorkingSetSize;
            }
            finally { CloseHandle(h); }
        }
        return sum;
    }

    public static string Time(int rounds)
    {
        var times = new List<double>();
        var watch = new Stopwatch();
        for (var i = 0; i < rounds; i++)
        {
            watch.Restart();
            ReadOnce();
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds * 1000);
        }
        times.Sort();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0} readings of the whole job: median {1:0.0} us, p99 {2:0.0} us, max {3:0.0} us",
            rounds, times[times.Count / 2], times[(int)(times.Count * 0.99)], times[times.Count - 1]);
    }

    public static void End() { CloseHandle(Job); }
}
'@

Add-Type -TypeDefinition $source -Language CSharp
[System.Threading.Thread]::CurrentThread.CurrentCulture = [cultureinfo]::InvariantCulture
New-Item -ItemType Directory -Force $Profile | Out-Null
$pid0 = [UsageProbe]::Start($Chrome, $Profile, $Url)
"started chrome pid $pid0 at $([DateTimeOffset]::UtcNow.ToString('o'))"
Start-Sleep -Seconds $SettleSeconds
try {
    $members = [UsageProbe]::Members()
    "members: $($members.Count)"
    foreach ($m in $members) { [UsageProbe]::Describe($m) }
    $a = [UsageProbe]::Account()
    "job accounting: user $($a.TotalUserTime / 1e7) s kernel $($a.TotalKernelTime / 1e7) s processes total $($a.TotalProcesses) active $($a.ActiveProcesses)"
    [UsageProbe]::Time($Rounds)
}
finally {
    [UsageProbe]::End()
    "job closed at $([DateTimeOffset]::UtcNow.ToString('o'))"
}
