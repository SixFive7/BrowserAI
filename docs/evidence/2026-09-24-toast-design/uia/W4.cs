// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System; using System.Text; using System.Runtime.InteropServices;
public static class W4 {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static string At(int x, int y) {
    SetProcessDPIAware();
    var h = WindowFromPoint(new POINT { X = x, Y = y });
    var root = GetAncestor(h, 2);
    return Describe(h) + "\n root: " + Describe(root);
  }
  public static string Describe(IntPtr h) {
    var c = new StringBuilder(256); GetClassName(h, c, 256); var t = new StringBuilder(256); GetWindowText(h, t, 256); uint pid; GetWindowThreadProcessId(h, out pid); RECT rc; GetWindowRect(h, out rc);
    return String.Format("hwnd=0x{0:X} class='{1}' title='{2}' pid={3} rect=({4},{5})-({6},{7})", h.ToInt64(), c, t, pid, rc.L, rc.T, rc.R, rc.B);
  }
}
