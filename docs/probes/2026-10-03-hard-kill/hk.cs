// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch rig for the hard-kill survival measurement. Not product code.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class HkWin
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObjectW(IntPtr sa, string name);

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic;
        public IO_COUNTERS Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint len);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(IntPtr job, int cls, IntPtr info, uint len, out uint ret);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr h, uint ms);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder sb, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr h, out uint code);


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);

    // pid -> parent pid, for every process on the machine. Ids only: the image name is never read.
    public static Dictionary<uint, uint> ParentMap()
    {
        var map = new Dictionary<uint, uint>();
        IntPtr snap = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var e = new PROCESSENTRY32W();
            e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));
            if (Process32FirstW(snap, ref e))
            {
                do { map[e.th32ProcessID] = e.th32ParentProcessID; } while (Process32NextW(snap, ref e));
            }
        }
        finally { CloseHandle(snap); }
        return map;
    }

    // The set taskkill /T would walk: every process whose parent chain reaches root.
    public static List<uint> Descendants(uint root)
    {
        var map = ParentMap();
        var children = new Dictionary<uint, List<uint>>();
        foreach (var kv in map)
        {
            if (kv.Key == kv.Value) continue;
            List<uint> l;
            if (!children.TryGetValue(kv.Value, out l)) { l = new List<uint>(); children[kv.Value] = l; }
            l.Add(kv.Key);
        }
        var result = new List<uint>();
        var q = new Queue<uint>();
        q.Enqueue(root);
        var seen = new HashSet<uint> { root };
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            result.Add(p);
            List<uint> l;
            if (children.TryGetValue(p, out l)) foreach (var c in l) if (seen.Add(c)) q.Enqueue(c);
        }
        return result;
    }

    public const uint KillOnJobClose = 0x00002000;

    public static IntPtr CreateKillOnCloseJob()
    {
        var h = CreateJobObjectW(IntPtr.Zero, null);
        if (h == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.Basic.LimitFlags = KillOnJobClose;
        if (!SetInformationJobObject(h, 9, ref info, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
        {
            int e = Marshal.GetLastWin32Error();
            CloseHandle(h);
            throw new Win32Exception(e);
        }
        return h;
    }

    public static uint JobLimitFlags(IntPtr job)
    {
        int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            uint ret;
            if (!QueryInformationJobObject(job, 9, buf, (uint)size, out ret)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = (JOBOBJECT_EXTENDED_LIMIT_INFORMATION)Marshal.PtrToStructure(buf, typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            return info.Basic.LimitFlags;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static void Assign(IntPtr job, Process p)
    {
        if (!AssignProcessToJobObject(job, p.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static uint[] JobPids(IntPtr job)
    {
        int size = 8 + 8 * 4096;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            uint ret;
            if (!QueryInformationJobObject(job, 3, buf, (uint)size, out ret)) throw new Win32Exception(Marshal.GetLastWin32Error());
            int n = Marshal.ReadInt32(buf, 4);
            var r = new uint[n];
            for (int i = 0; i < n; i++) r[i] = (uint)Marshal.ReadInt64(buf, 8 + 8 * i);
            return r;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // JOBOBJECT_BASIC_ACCOUNTING_INFORMATION: 4 x LARGE_INTEGER, then TotalPageFaultCount,
    // TotalProcesses, ActiveProcesses, TotalTerminatedProcesses (DWORD each).
    public static int[] JobAccounting(IntPtr job)
    {
        int size = 48;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            uint ret;
            if (!QueryInformationJobObject(job, 1, buf, (uint)size, out ret)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new[] { Marshal.ReadInt32(buf, 36), Marshal.ReadInt32(buf, 40), Marshal.ReadInt32(buf, 44) };
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public sealed class ProcHandle
    {
        public uint Pid;
        public IntPtr H;
        public long Created;
        public string Image;
    }

    // SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION. Never a terminate right.
    public static ProcHandle Open(uint pid)
    {
        var h = OpenProcess(0x00100000 | 0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        long c, e, k, u;
        GetProcessTimes(h, out c, out e, out k, out u);
        var sb = new StringBuilder(2048);
        uint sz = 2048;
        string img = QueryFullProcessImageNameW(h, 0, sb, ref sz) ? sb.ToString() : null;
        return new ProcHandle { Pid = pid, H = h, Created = c, Image = img };
    }

    public static bool HasExited(IntPtr h) { return WaitForSingleObject(h, 0) == 0; }

    public static bool WaitExit(IntPtr h, uint ms) { return WaitForSingleObject(h, ms) == 0; }

    public static long ExitCode(IntPtr h) { uint c; return GetExitCodeProcess(h, out c) ? (long)c : -1; }

    public static long NowMs() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

    // Waits for every handle to signal, up to a total budget. Returns the pids still alive.
    public static List<uint> WaitAll(List<ProcHandle> handles, int budgetMs)
    {
        var sw = Stopwatch.StartNew();
        var alive = new List<uint>();
        foreach (var p in handles)
        {
            long left = budgetMs - sw.ElapsedMilliseconds;
            if (left < 0) left = 0;
            if (!WaitExit(p.H, (uint)left)) alive.Add(p.Pid);
        }
        return alive;
    }

    public static void CloseAll(List<ProcHandle> handles)
    {
        foreach (var p in handles) if (p != null && p.H != IntPtr.Zero) { CloseHandle(p.H); p.H = IntPtr.Zero; }
    }

    // Opens a file the way @playwright/mcp's isProfileLocked does on Windows (fs.openSync(path, 'r+')):
    // read+write, sharing read|write|delete. Answers "absent", "opened" or the error text.
    public static string TryOpenRw(string path)
    {
        if (!File.Exists(path)) return "absent";
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) { }
            return "opened";
        }
        catch (Exception e) { return "refused: " + e.GetType().Name + ": " + e.Message.Replace("\r", " ").Replace("\n", " "); }
    }
}

public sealed class HkChild
{
    public Process P;
    public readonly BlockingCollection<string> Lines = new BlockingCollection<string>();
    public readonly StringBuilder Err = new StringBuilder();
    public readonly StringBuilder AllOut = new StringBuilder();

    public static HkChild Start(string exe, string[] args, string cwd, IDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = cwd,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment.Clear();
        foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        var c = new HkChild();
        c.P = new Process { StartInfo = psi };
        c.P.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                lock (c.AllOut) c.AllOut.AppendLine(e.Data);
                try { c.Lines.Add(e.Data); } catch (InvalidOperationException) { }
            }
            else
            {
                try { c.Lines.CompleteAdding(); } catch (InvalidOperationException) { }
            }
        };
        c.P.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (c.Err) c.Err.AppendLine(e.Data); };
        c.P.Start();
        c.P.StandardInput.NewLine = "\n";
        c.P.BeginOutputReadLine();
        c.P.BeginErrorReadLine();
        return c;
    }

    public string ReadLine(int timeoutMs)
    {
        string l;
        try { return Lines.TryTake(out l, timeoutMs) ? l : null; }
        catch (InvalidOperationException) { return null; }
    }

    public void WriteLine(string s)
    {
        P.StandardInput.WriteLine(s);
        P.StandardInput.Flush();
    }

    public void CloseStdin()
    {
        try { P.StandardInput.Close(); } catch (Exception) { }
    }

    public string ErrText() { lock (Err) return Err.ToString(); }

    public string OutText() { lock (AllOut) return AllOut.ToString(); }
}
