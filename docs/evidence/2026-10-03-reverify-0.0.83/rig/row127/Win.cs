// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Row 127's window-time rig, 2026-10-03: start the published configuration app and
// poll EnumWindows without pause from the moment Start() returns until a visible
// #32770 owned by its pid appears; read its rect; post WM_CLOSE to it.
public static class Win
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);

    public static Process Last;

    public static IntPtr FindDialog(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            int p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var c = new StringBuilder(64); GetClassNameW(h, c, 64);
            if (c.ToString() == "#32770") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Returns elapsed ms from just before Start() until the dialog was first seen, or -1 at the bound.
    public static double Run(ProcessStartInfo psi, int boundMs, out int pid, out IntPtr dialog, out string detail)
    {
        var sw = Stopwatch.StartNew();
        var proc = Process.Start(psi);
        Last = proc;
        double started = sw.Elapsed.TotalMilliseconds;
        pid = proc.Id; dialog = IntPtr.Zero; detail = "";
        while (sw.ElapsedMilliseconds < boundMs)
        {
            var h = FindDialog(pid);
            if (h != IntPtr.Zero)
            {
                double at = sw.Elapsed.TotalMilliseconds;
                dialog = h;
                RECT r; GetWindowRect(h, out r);
                var t = new StringBuilder(256); GetWindowTextW(h, t, 256);
                detail = "startReturnedAfterMs=" + started.ToString("F1") + " rect=" + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + " title=" + t;
                return at;
            }
            if (proc.HasExited) { detail = "exited before any dialog, code " + proc.ExitCode; return -1; }
        }
        detail = "no dialog within " + boundMs + " ms";
        return -1;
    }

    public static bool Close(IntPtr h) { return PostMessageW(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
}
