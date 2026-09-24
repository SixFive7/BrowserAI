// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch rig for the coordinator-lifecycle research, 2026-09-24. Not product code.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CL
{
    public sealed class Spawned
    {
        public string Label;
        public Process P;
        public int Pid;
        public long CreatedFt;
        public string ImageAtStart;
        public StreamWriter Stdin;
        public string Error;
        public long SpawnWallTicks;
    }

    public sealed class Snap
    {
        public int Pid;
        public int Ppid;
        public string Exe;
    }

    public static class Rig
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeProcess(IntPtr h, out uint code);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        public static string Ft(long ft) => ft == 0 ? "" : DateTime.FromFileTimeUtc(ft).ToString("HH:mm:ss.fff");

        public static string Now() => DateTime.UtcNow.ToString("HH:mm:ss.fff");

        public static long Created(IntPtr h)
        {
            GetProcessTimes(h, out var c, out _, out _, out _);
            return c;
        }

        public static long Exited(IntPtr h)
        {
            GetProcessTimes(h, out _, out var e, out _, out _);
            return e;
        }

        public static string Image(IntPtr h)
        {
            var sb = new StringBuilder(32768);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : "<err " + Marshal.GetLastWin32Error() + ">";
        }

        // Observation only: parent/child mapping by pid, never used to select a process to act on.
        public static List<Snap> Snapshot()
        {
            var list = new List<Snap>();
            var s = CreateToolhelp32Snapshot(0x2, 0);
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (Process32FirstW(s, ref e))
            {
                do { list.Add(new Snap { Pid = (int)e.th32ProcessID, Ppid = (int)e.th32ParentProcessID, Exe = e.szExeFile }); }
                while (Process32NextW(s, ref e));
            }
            CloseHandle(s);
            return list;
        }

        // Opens a handle to a pid we already know is our own child (verified by parent pid AND creation time after the parent's).
        public static IntPtr OpenForObservation(int pid) => OpenProcess(0x00100000 | 0x1000, false, pid); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION

        public static uint ExitCode(IntPtr h)
        {
            GetExitCodeProcess(h, out var c);
            return c;
        }

        public static Spawned Start(string label, string exe, string args, string cwd, IDictionary<string, string> env, string outDir, bool keepStdin)
        {
            var sp = new Spawned { Label = label, SpawnWallTicks = DateTime.UtcNow.Ticks };
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = cwd,
                    RedirectStandardInput = keepStdin,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var kv in env)
                {
                    if (kv.Value == null) psi.Environment.Remove(kv.Key); else psi.Environment[kv.Key] = kv.Value;
                }
                var p = new Process { StartInfo = psi };
                var outW = new StreamWriter(Path.Combine(outDir, label + ".stdout.txt"), false, new UTF8Encoding(false)) { AutoFlush = true };
                var errW = new StreamWriter(Path.Combine(outDir, label + ".stderr.txt"), false, new UTF8Encoding(false)) { AutoFlush = true };
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (outW) outW.WriteLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errW) errW.WriteLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                sp.P = p;
                sp.Pid = p.Id;
                sp.CreatedFt = Created(p.Handle);
                sp.ImageAtStart = Image(p.Handle);
                if (keepStdin) sp.Stdin = p.StandardInput;
            }
            catch (Exception ex)
            {
                sp.Error = ex.GetType().Name + ": " + ex.Message;
            }
            return sp;
        }
    }
}
