# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class Mon {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public delegate bool EnumProc(IntPtr h, IntPtr dc, ref RECT r, IntPtr d);
  [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumProc f, IntPtr d);
  [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  public static List<string> All() { SetProcessDpiAwarenessContext(new IntPtr(-4)); var l = new List<string>();
    EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) => { uint x, y; GetDpiForMonitor(h, 0, out x, out y); l.Add(String.Format("monitor ({0},{1})-({2},{3}) dpi={4} scale={5}%", r.L, r.T, r.R, r.B, x, x * 100 / 96)); return true; }, IntPtr.Zero); return l; }
}
"@
[Mon]::All()
