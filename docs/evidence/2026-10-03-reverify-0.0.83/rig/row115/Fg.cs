// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Row 115's rig, 2026-10-03: a browser launched through CreateProcessW with a
// hand-built STARTUPINFOW, with and without STARTF_USESHOWWINDOW + SW_SHOWNOACTIVATE,
// the foreground read every 250 ms and attributed to a pid, and the launched tree's
// visible top-level windows recorded on every poll as the control. The tree is
// found by parent pid from the pid CreateProcessW returned, never by image name.
public static class Fg
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public int dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)] public char[] path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string cwd, ref STARTUPINFOW si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfoW(uint action, uint param, out uint value, uint winIni);

    public static uint ForegroundLockTimeout()
    {
        uint v; SystemParametersInfoW(0x2000, 0, out v, 0); return v;
    }

    public static string Describe(IntPtr h)
    {
        if (h == IntPtr.Zero) return "hwnd=0";
        int pid; GetWindowThreadProcessId(h, out pid);
        var c = new StringBuilder(256); GetClassNameW(h, c, 256);
        var t = new StringBuilder(256); GetWindowTextW(h, t, 256);
        return "hwnd=0x" + h.ToInt64().ToString("X") + " pid=" + pid + " class=" + c + " title=" + t;
    }

    public static int ForegroundPid()
    {
        int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid;
    }

    public static HashSet<int> Tree(int root)
    {
        var parent = new Dictionary<int, List<int>>();
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        var e = new PROCESSENTRY32W(); e.dwSize = Marshal.SizeOf(typeof(PROCESSENTRY32W));
        if (Process32FirstW(snap, ref e))
        {
            do
            {
                List<int> kids; if (!parent.TryGetValue(e.th32ParentProcessID, out kids)) { kids = new List<int>(); parent[e.th32ParentProcessID] = kids; }
                kids.Add(e.th32ProcessID);
            } while (Process32NextW(snap, ref e));
        }
        CloseHandle(snap);
        var set = new HashSet<int> { root }; var stack = new Stack<int>(); stack.Push(root);
        while (stack.Count > 0) { var p = stack.Pop(); List<int> kids; if (parent.TryGetValue(p, out kids)) foreach (var k in kids) if (set.Add(k)) stack.Push(k); }
        return set;
    }

    public static List<string> VisibleWindowsOf(HashSet<int> pids)
    {
        var found = new List<string>();
        EnumWindows((h, l) => { int pid; GetWindowThreadProcessId(h, out pid); if (pids.Contains(pid) && IsWindowVisible(h)) found.Add(Describe(h)); return true; }, IntPtr.Zero);
        return found;
    }

    // Launches, polls every 250 ms for the given span, and returns one line per poll.
    public static List<string> Arm(string exe, string args, string cwd, bool flag, int pollMs, out int rootPid)
    {
        var lines = new List<string>();
        var si = new STARTUPINFOW(); si.cb = Marshal.SizeOf(typeof(STARTUPINFOW));
        if (flag) { si.dwFlags = 0x1; si.wShowWindow = 4; }
        PROCESS_INFORMATION pi;
        var cmd = new StringBuilder("\"" + exe + "\" " + args);
        var sw = Stopwatch.StartNew();
        if (!CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, cwd, ref si, out pi))
        {
            rootPid = 0; lines.Add("CreateProcessW failed " + Marshal.GetLastWin32Error()); return lines;
        }
        rootPid = pi.dwProcessId; CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        lines.Add("t=0 launched pid=" + rootPid + " flag=" + flag);
        var known = new HashSet<int> { rootPid };
        while (sw.ElapsedMilliseconds < pollMs)
        {
            System.Threading.Thread.Sleep(250);
            foreach (var p in Tree(rootPid)) known.Add(p);
            var fgw = GetForegroundWindow(); int fgp; GetWindowThreadProcessId(fgw, out fgp);
            var vis = VisibleWindowsOf(known);
            lines.Add("t=" + sw.ElapsedMilliseconds + " tree=" + known.Count + " fgInTree=" + known.Contains(fgp) + " fg=[" + Describe(fgw) + "] visible=" + vis.Count + (vis.Count > 0 ? " first=[" + vis[0] + "]" : ""));
        }
        lines.Add("TREE " + string.Join(",", known));
        return lines;
    }
}
