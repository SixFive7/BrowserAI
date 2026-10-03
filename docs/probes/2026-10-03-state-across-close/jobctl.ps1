# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# jobctl.ps1 -- a job-object holder for the state-across-close rig.
# Reads one JSON command per line on stdin, answers one JSON line on stdout.
# Every process this rig starts for a child lives in the one job created here,
# with KILL_ON_JOB_CLOSE: closing the job (or this process dying, or stdin EOF)
# terminates whatever is left. Nothing here selects a process by image name:
# membership in the job is the only selector.
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class JobCtl
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateJobObjectW(IntPtr attrs, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint len);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint len, out uint ret);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_BASIC_ACCOUNTING_INFORMATION info, uint len, out uint ret);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint len, out uint ret);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder sb, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
    [DllImport("psapi.dll", SetLastError = true)]
    static extern bool GetProcessMemoryInfo(IntPtr h, out PROCESS_MEMORY_COUNTERS_EX pmc, int cb);
    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr h, int cls, ref PROCESS_BASIC_INFORMATION pbi, int len, out int ret);

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS { public ulong R, W, O, RB, WB, OB; }
    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic; public IO_COUNTERS Io;
        public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit; public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb; public uint PageFaultCount; public UIntPtr PeakWorkingSetSize; public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage; public UIntPtr QuotaPagedPoolUsage; public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage; public UIntPtr PagefileUsage; public UIntPtr PeakPagefileUsage; public UIntPtr PrivateUsage;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus; public IntPtr PebBaseAddress; public IntPtr AffinityMask; public IntPtr BasePriority;
        public UIntPtr UniqueProcessId; public UIntPtr InheritedFromUniqueProcessId;
    }

    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    const uint PROCESS_TERMINATE = 0x0001, PROCESS_SET_QUOTA = 0x0100, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    static IntPtr _job = IntPtr.Zero;
    static Thread _watch;
    static volatile bool _watching;
    static int _watchMaxActive;
    static uint _watchTotalAtStart;
    static long _watchSamples;
    static Dictionary<long, string> _watchSeen = new Dictionary<long, string>();
    static readonly object _gate = new object();

    static string Esc(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '\\') b.Append("\\\\"); else if (c == '"') b.Append("\\\""); else if (c < 32) b.AppendFormat("\\u{0:x4}", (int)c); else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    public static string Create()
    {
        if (_job != IntPtr.Zero) return "{\"ok\":false,\"error\":\"job exists\"}";
        _job = CreateJobObjectW(IntPtr.Zero, null);
        if (_job == IntPtr.Zero) return "{\"ok\":false,\"error\":\"CreateJobObject " + Marshal.GetLastWin32Error() + "\"}";
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.Basic.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(_job, 9, ref info, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
            return "{\"ok\":false,\"error\":\"SetInformationJobObject " + Marshal.GetLastWin32Error() + "\"}";
        return "{\"ok\":true}";
    }

    public static string Assign(int pid)
    {
        var h = OpenProcess(PROCESS_TERMINATE | PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "{\"ok\":false,\"error\":\"OpenProcess " + Marshal.GetLastWin32Error() + "\"}";
        try
        {
            if (!AssignProcessToJobObject(_job, h)) return "{\"ok\":false,\"error\":\"AssignProcessToJobObject " + Marshal.GetLastWin32Error() + "\"}";
            long c, e, k, u; GetProcessTimes(h, out c, out e, out k, out u);
            return "{\"ok\":true,\"creation\":" + c + "}";
        }
        finally { CloseHandle(h); }
    }

    static List<long> Pids()
    {
        var list = new List<long>();
        int cap = 1024;
        int size = 8 + cap * IntPtr.Size;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            uint ret;
            if (!QueryInformationJobObject(_job, 3, buf, (uint)size, out ret)) return list;
            int n = Marshal.ReadInt32(buf, 4);
            for (int i = 0; i < n; i++) list.Add(Marshal.ReadIntPtr(buf, 8 + i * IntPtr.Size).ToInt64());
        }
        finally { Marshal.FreeHGlobal(buf); }
        return list;
    }

    static JOBOBJECT_BASIC_ACCOUNTING_INFORMATION Acct()
    {
        var a = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION(); uint ret;
        QueryInformationJobObject(_job, 1, ref a, (uint)Marshal.SizeOf(typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)), out ret);
        return a;
    }

    static string ImageOf(IntPtr h)
    {
        var sb = new StringBuilder(1024); int size = sb.Capacity;
        return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
    }

    public static string Snap()
    {
        if (_job == IntPtr.Zero) return "{\"ok\":false,\"error\":\"no job\"}";
        var a = Acct();
        var ext = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION(); uint r2;
        QueryInformationJobObject(_job, 9, ref ext, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)), out r2);
        var b = new StringBuilder();
        b.Append("{\"ok\":true,\"t\":").Append(DateTime.UtcNow.Ticks)
         .Append(",\"active\":").Append(a.ActiveProcesses)
         .Append(",\"total\":").Append(a.TotalProcesses)
         .Append(",\"terminated\":").Append(a.TotalTerminatedProcesses)
         .Append(",\"peakJobCommit\":").Append(ext.PeakJobMemoryUsed.ToUInt64())
         .Append(",\"procs\":[");
        bool first = true;
        foreach (var pid in Pids())
        {
            var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            string exe = null; ulong ws = 0, priv = 0; long creation = 0; long ppid = -1;
            if (h != IntPtr.Zero)
            {
                try
                {
                    exe = ImageOf(h);
                    PROCESS_MEMORY_COUNTERS_EX m;
                    if (GetProcessMemoryInfo(h, out m, Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX)))) { ws = m.WorkingSetSize.ToUInt64(); priv = m.PrivateUsage.ToUInt64(); }
                    long e, k, u; GetProcessTimes(h, out creation, out e, out k, out u);
                    var pbi = new PROCESS_BASIC_INFORMATION(); int rl;
                    if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)), out rl) == 0) ppid = (long)pbi.InheritedFromUniqueProcessId.ToUInt64();
                }
                finally { CloseHandle(h); }
            }
            if (!first) b.Append(','); first = false;
            b.Append("{\"pid\":").Append(pid).Append(",\"ppid\":").Append(ppid).Append(",\"exe\":").Append(Esc(exe))
             .Append(",\"ws\":").Append(ws).Append(",\"priv\":").Append(priv).Append(",\"creation\":").Append(creation).Append('}');
        }
        return b.Append("]}").ToString();
    }

    public static string WatchStart(int intervalMs)
    {
        if (_watching) return "{\"ok\":false,\"error\":\"already watching\"}";
        lock (_gate) { _watchSeen.Clear(); }
        var a0 = Acct();
        _watchTotalAtStart = a0.TotalProcesses;
        _watchMaxActive = (int)a0.ActiveProcesses;
        _watchSamples = 0;
        _watching = true;
        _watch = new Thread(() =>
        {
            while (_watching)
            {
                var a = Acct();
                if ((int)a.ActiveProcesses > _watchMaxActive) _watchMaxActive = (int)a.ActiveProcesses;
                foreach (var pid in Pids())
                {
                    bool known; lock (_gate) { known = _watchSeen.ContainsKey(pid); }
                    if (!known)
                    {
                        string exe = null;
                        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
                        if (h != IntPtr.Zero) { try { exe = ImageOf(h); } finally { CloseHandle(h); } }
                        lock (_gate) { _watchSeen[pid] = exe; }
                    }
                }
                _watchSamples++;
                Thread.Sleep(intervalMs);
            }
        });
        _watch.IsBackground = true;
        _watch.Start();
        return "{\"ok\":true,\"totalAtStart\":" + _watchTotalAtStart + "}";
    }

    public static string WatchStop()
    {
        _watching = false;
        if (_watch != null) _watch.Join(2000);
        var a = Acct();
        var b = new StringBuilder();
        b.Append("{\"ok\":true,\"totalAtStart\":").Append(_watchTotalAtStart).Append(",\"totalAtStop\":").Append(a.TotalProcesses)
         .Append(",\"created\":").Append(a.TotalProcesses - _watchTotalAtStart)
         .Append(",\"maxActive\":").Append(_watchMaxActive).Append(",\"activeAtStop\":").Append(a.ActiveProcesses)
         .Append(",\"samples\":").Append(_watchSamples).Append(",\"seen\":[");
        bool first = true;
        lock (_gate)
        {
            foreach (var kv in _watchSeen)
            {
                if (!first) b.Append(','); first = false;
                b.Append("{\"pid\":").Append(kv.Key).Append(",\"exe\":").Append(Esc(kv.Value)).Append('}');
            }
        }
        return b.Append("]}").ToString();
    }

    public static string Close()
    {
        if (_job == IntPtr.Zero) return "{\"ok\":true,\"note\":\"no job\"}";
        _watching = false;
        var a = Acct();
        CloseHandle(_job);
        _job = IntPtr.Zero;
        return "{\"ok\":true,\"activeAtClose\":" + a.ActiveProcesses + ",\"total\":" + a.TotalProcesses + "}";
    }
}
'@

[Console]::Out.WriteLine('{"ready":true,"pid":' + $PID + '}')
[Console]::Out.Flush()
while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) { break }
    if ($line.Trim().Length -eq 0) { continue }
    try {
        $cmd = $line | ConvertFrom-Json
        $out = switch ($cmd.op) {
            'create' { [JobCtl]::Create() }
            'assign' { [JobCtl]::Assign([int]$cmd.pid) }
            'snap' { [JobCtl]::Snap() }
            'watchstart' { [JobCtl]::WatchStart([int]($(if ($cmd.interval) { $cmd.interval } else { 2 }))) }
            'watchstop' { [JobCtl]::WatchStop() }
            'close' { [JobCtl]::Close() }
            default { '{"ok":false,"error":"unknown op"}' }
        }
    } catch {
        $out = '{"ok":false,"error":' + ($_.Exception.Message | ConvertTo-Json -Compress) + '}'
    }
    [Console]::Out.WriteLine($out)
    [Console]::Out.Flush()
}
[void][JobCtl]::Close()
