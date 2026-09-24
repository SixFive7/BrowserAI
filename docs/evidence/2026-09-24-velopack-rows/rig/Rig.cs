// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch rig for the 2026-09-24 re-verification of rows 123/124/126/130. Not a maintained tool.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VR
{
    public sealed class Proc
    {
        public int Pid;
        public int Ppid;
        public string Exe = "";
        public string Path = "";
        public long Created;   // FILETIME
        public long Exited;    // FILETIME, 0 while alive
        public int ExitCode = int.MinValue;
        public IntPtr Handle;  // kept open so a short-lived process can still be read after it exits
    }

    public sealed class Win
    {
        public IntPtr Hwnd;
        public int Pid;
        public string Class = "";
        public string Title = "";
        public bool Visible;
        public int L, T, R, B;
    }

    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public uint dwProcessId; public uint dwThreadId; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public int dwX; public int dwY; public int dwXSize; public int dwYSize;
            public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute;
            public int dwFlags; public short wShowWindow; public short cbReserved2;
            public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
            IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetProcessTimes(IntPtr h, out long c, out long e, out long k, out long u);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetExitCodeProcess(IntPtr h, out int code);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32
        {
            public uint dwSize; public uint cntUsage; public uint th32ProcessID; public IntPtr th32DefaultHeapID;
            public uint th32ModuleID; public uint cntThreads; public uint th32ParentProcessID; public int pcPriClassBase;
            public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc p, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder t, int c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder t, int c);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    }

    public static class Rig
    {
        public const uint DETACHED_PROCESS = 0x00000008;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint SYNCHRONIZE = 0x00100000;

        /// Starts a process with no console of its own (DETACHED_PROCESS), inheriting NO handles,
        /// with the current environment plus overrides (a null value removes a variable).
        public static Proc StartDetached(string exe, string args, string workDir, IDictionary<string, string> overrides) { return StartWith(exe, args, workDir, overrides, DETACHED_PROCESS | CREATE_UNICODE_ENVIRONMENT); }
        /// Same, but with a console of its own that has NO window (CREATE_NO_WINDOW), still inheriting no handles.
        public static Proc StartHidden(string exe, string args, string workDir, IDictionary<string, string> overrides) { return StartWith(exe, args, workDir, overrides, 0x08000000 | CREATE_UNICODE_ENVIRONMENT); }
        public static Proc StartWith(string exe, string args, string workDir, IDictionary<string, string> overrides, uint flags)
        {
            var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
                env[(string)kv.Key] = (string)kv.Value;
            if (overrides != null)
                foreach (var kv in overrides)
                {
                    if (kv.Value == null) env.Remove(kv.Key); else env[kv.Key] = kv.Value;
                }
            var block = new StringBuilder();
            foreach (var kv in env) block.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
            block.Append('\0');
            var envPtr = Marshal.StringToHGlobalUni(block.ToString());
            try
            {
                var si = new Native.STARTUPINFO { cb = Marshal.SizeOf(typeof(Native.STARTUPINFO)) };
                Native.PROCESS_INFORMATION pi;
                var cmd = new StringBuilder("\"" + exe + "\"" + (string.IsNullOrEmpty(args) ? "" : " " + args));
                if (!Native.CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, flags,
                        envPtr, workDir, ref si, out pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                Native.CloseHandle(pi.hThread);
                var p = new Proc { Pid = (int)pi.dwProcessId, Exe = System.IO.Path.GetFileName(exe), Path = exe, Handle = pi.hProcess };
                Refresh(p);
                return p;
            }
            finally { Marshal.FreeHGlobal(envPtr); }
        }

        public static void Refresh(Proc p)
        {
            if (p.Handle == IntPtr.Zero) return;
            long c, e, k, u;
            if (Native.GetProcessTimes(p.Handle, out c, out e, out k, out u))
            {
                p.Created = c;
                int code;
                if (Native.WaitForSingleObject(p.Handle, 0) == 0)
                {
                    p.Exited = e;
                    if (Native.GetExitCodeProcess(p.Handle, out code)) p.ExitCode = code;
                }
            }
        }

        public static List<Proc> Snapshot()
        {
            var list = new List<Proc>();
            var snap = Native.CreateToolhelp32Snapshot(0x2, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
            try
            {
                var e = new Native.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(Native.PROCESSENTRY32)) };
                if (Native.Process32FirstW(snap, ref e))
                {
                    do { list.Add(new Proc { Pid = (int)e.th32ProcessID, Ppid = (int)e.th32ParentProcessID, Exe = e.szExeFile }); }
                    while (Native.Process32NextW(snap, ref e));
                }
            }
            finally { Native.CloseHandle(snap); }
            return list;
        }

        /// Opens a pid for query+synchronize, fills path and creation time, and KEEPS the handle.
        public static bool Attach(Proc p)
        {
            var h = Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, p.Pid);
            if (h == IntPtr.Zero) return false;
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (Native.QueryFullProcessImageNameW(h, 0, sb, ref size)) p.Path = sb.ToString();
            p.Handle = h;
            Refresh(p);
            return true;
        }

        public static List<Win> Windows()
        {
            var list = new List<Win>();
            Native.EnumWindows((h, l) =>
            {
                int pid; Native.GetWindowThreadProcessId(h, out pid);
                var cls = new StringBuilder(256); Native.GetClassNameW(h, cls, 256);
                var ttl = new StringBuilder(1024); Native.GetWindowTextW(h, ttl, 1024);
                Native.RECT r; Native.GetWindowRect(h, out r);
                list.Add(new Win { Hwnd = h, Pid = pid, Class = cls.ToString(), Title = ttl.ToString(), Visible = Native.IsWindowVisible(h), L = r.Left, T = r.Top, R = r.Right, B = r.Bottom });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool PostClose(IntPtr hwnd) { return Native.PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buf, int len, out int ret);

        /// ProcessCommandLineInformation (60): needs only PROCESS_QUERY_LIMITED_INFORMATION.
        public static string CommandLine(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            int len;
            NtQueryInformationProcess(h, 60, IntPtr.Zero, 0, out len);
            if (len <= 0) return null;
            var buf = Marshal.AllocHGlobal(len);
            try
            {
                if (NtQueryInformationProcess(h, 60, buf, len, out len) != 0) return null;
                var chars = Marshal.ReadInt16(buf); // UNICODE_STRING.Length in bytes
                var ptr = Marshal.ReadIntPtr(buf, IntPtr.Size);
                return Marshal.PtrToStringUni(ptr, (ushort)chars / 2);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static string Ft(long ft) { return ft == 0 ? "" : DateTime.FromFileTimeUtc(ft).ToLocalTime().ToString("HH:mm:ss.fffffff"); }
        public static double Ms(long from, long to) { return (to - from) / 10000.0; }
    }
}
