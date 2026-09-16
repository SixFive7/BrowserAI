# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param(
  [Parameter(Mandatory=$true)][string]$ProbeRoot,
  [Parameter(Mandatory=$true)][string]$Out,
  [int]$Seconds = 900
)

$ErrorActionPreference = 'Stop'

$code = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Probe
{
    public static class Observer
    {
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private sealed class Win
        {
            public string Class = "";
            public string Title = "";
            public uint Pid;
            public bool Visible;
            public double FirstSeen;
            public double LastSeen;
            public bool EverVisible;
        }

        private static StreamWriter _w;
        private static Stopwatch _clock;
        private static readonly Dictionary<uint,string> _names = new Dictionary<uint,string>();

        private static void Emit(string kind, string payload)
        {
            lock (_w)
            {
                _w.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{{\"t\":{0:F3},\"wall\":\"{1}\",\"kind\":\"{2}\",{3}}}",
                    _clock.Elapsed.TotalSeconds, DateTime.Now.ToString("HH:mm:ss.fff"), kind, payload));
                _w.Flush();
            }
        }

        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r"," ").Replace("\n"," ");
        }

        private static string NameOf(uint pid)
        {
            string n;
            if (_names.TryGetValue(pid, out n)) return n;
            n = "?";
            try { using (var p = Process.GetProcessById((int)pid)) n = p.ProcessName; } catch {}
            _names[pid] = n;
            return n;
        }

        public static void Run(string probeRoot, string outPath, int seconds)
        {
            _clock = Stopwatch.StartNew();
            _w = new StreamWriter(new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
            Emit("start", "\"probeRoot\":\"" + Esc(probeRoot) + "\"");

            var windows = new Dictionary<IntPtr, Win>();
            var firstPass = true;

            var tracked = new Dictionary<int, Process>();
            var trackedInfo = new Dictionary<int, string>();
            var startedAt = new Dictionary<int, double>();
            string[] watchNames = new string[] { "BrowserAI", "Update", "Setup", "BrowserAI-win-Setup", "node", "conhost", "OpenConsole", "WindowsTerminal", "chrome", "firefox", "headless_shell" };

            var lastProc = -1000.0;
            while (_clock.Elapsed.TotalSeconds < seconds)
            {
                var seen = new HashSet<IntPtr>();
                EnumWindows((h, l) =>
                {
                    seen.Add(h);
                    var cls = new StringBuilder(256);
                    GetClassNameW(h, cls, 256);
                    uint pid; GetWindowThreadProcessId(h, out pid);
                    var vis = IsWindowVisible(h);
                    Win w;
                    if (!windows.TryGetValue(h, out w))
                    {
                        var ttl = new StringBuilder(512);
                        GetWindowTextW(h, ttl, 512);
                        w = new Win { Class = cls.ToString(), Title = ttl.ToString(), Pid = pid,
                                      Visible = vis, FirstSeen = _clock.Elapsed.TotalSeconds,
                                      LastSeen = _clock.Elapsed.TotalSeconds, EverVisible = vis };
                        windows[h] = w;
                        if (!firstPass)
                            Emit("window+", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "\"hwnd\":\"0x{0:X}\",\"class\":\"{1}\",\"title\":\"{2}\",\"pid\":{3},\"proc\":\"{4}\",\"visible\":{5}",
                                h.ToInt64(), Esc(w.Class), Esc(w.Title), pid, Esc(NameOf(pid)), vis ? "true" : "false"));
                    }
                    else
                    {
                        w.LastSeen = _clock.Elapsed.TotalSeconds;
                        if (vis != w.Visible)
                        {
                            w.Visible = vis;
                            if (vis) w.EverVisible = true;
                            if (!firstPass)
                                Emit("window~", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                    "\"hwnd\":\"0x{0:X}\",\"class\":\"{1}\",\"pid\":{2},\"proc\":\"{3}\",\"visible\":{4}",
                                    h.ToInt64(), Esc(w.Class), pid, Esc(NameOf(pid)), vis ? "true" : "false"));
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                var gone = new List<IntPtr>();
                foreach (var kv in windows) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (var h in gone)
                {
                    var w = windows[h];
                    if (!firstPass)
                        Emit("window-", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "\"hwnd\":\"0x{0:X}\",\"class\":\"{1}\",\"title\":\"{2}\",\"pid\":{3},\"proc\":\"{4}\",\"lifetimeSec\":{5:F3},\"everVisible\":{6}",
                            h.ToInt64(), Esc(w.Class), Esc(w.Title), w.Pid, Esc(NameOf(w.Pid)), w.LastSeen - w.FirstSeen, w.EverVisible ? "true" : "false"));
                    windows.Remove(h);
                }
                firstPass = false;

                if (_clock.Elapsed.TotalSeconds - lastProc > 0.25)
                {
                    lastProc = _clock.Elapsed.TotalSeconds;
                    foreach (var n in watchNames)
                    {
                        Process[] ps;
                        try { ps = Process.GetProcessesByName(n); } catch { continue; }
                        foreach (var p in ps)
                        {
                            if (tracked.ContainsKey(p.Id)) { p.Dispose(); continue; }
                            string path = "?";
                            DateTime st = DateTime.MinValue;
                            try { path = p.MainModule.FileName; } catch {}
                            try { st = p.StartTime; } catch {}
                            try { var _h = p.Handle; } catch {}
                            tracked[p.Id] = p;
                            startedAt[p.Id] = _clock.Elapsed.TotalSeconds;
                            trackedInfo[p.Id] = path;
                            Emit("proc+", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "\"pid\":{0},\"name\":\"{1}\",\"path\":\"{2}\",\"startTime\":\"{3}\"",
                                p.Id, Esc(n), Esc(path), st == DateTime.MinValue ? "?" : st.ToString("HH:mm:ss.fff")));
                        }
                    }
                    var dead = new List<int>();
                    foreach (var kv in tracked)
                    {
                        bool exited = false;
                        try { exited = kv.Value.HasExited; } catch { exited = true; }
                        if (!exited) continue;
                        int code = -999999;
                        try { code = kv.Value.ExitCode; } catch {}
                        double life = _clock.Elapsed.TotalSeconds - startedAt[kv.Key];
                        Emit("proc-", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "\"pid\":{0},\"path\":\"{1}\",\"exitCode\":{2},\"observedLifetimeSec\":{3:F3}",
                            kv.Key, Esc(trackedInfo[kv.Key]), code, life));
                        dead.Add(kv.Key);
                    }
                    foreach (var d in dead) { try { tracked[d].Dispose(); } catch {} tracked.Remove(d); }
                }

                if (File.Exists(outPath + ".stop")) break;
                System.Threading.Thread.Sleep(20);
            }

            foreach (var kv in tracked)
            {
                bool exited = true;
                try { exited = kv.Value.HasExited; } catch {}
                if (!exited)
                    Emit("proc=alive-at-end", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "\"pid\":{0},\"path\":\"{1}\"", kv.Key, Esc(trackedInfo[kv.Key])));
            }
            Emit("stop", "\"reason\":\"end\"");
            _w.Flush();
            _w.Close();
        }
    }
}
'@

Add-Type -TypeDefinition $code -Language CSharp

[Probe.Observer]::Run($ProbeRoot, $Out, $Seconds)
