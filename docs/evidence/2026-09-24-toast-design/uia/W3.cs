// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class W3 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static List<string> InRegion(int minLeft, int minTop) {
    SetProcessDPIAware();
    var r = new List<string>();
    EnumWindows((h, l) => { if (!IsWindowVisible(h)) return true; RECT rc; GetWindowRect(h, out rc);
      if (rc.R <= minLeft || rc.B <= minTop || rc.L >= 3840 || rc.T >= 2112) return true;
      if (rc.R - rc.L >= 3000) return true;
      uint pid; GetWindowThreadProcessId(h, out pid);
      var c = new StringBuilder(256); GetClassName(h, c, 256); var t = new StringBuilder(256); GetWindowText(h, t, 256);
      r.Add(String.Format("hwnd=0x{0:X} class='{1}' title='{2}' pid={3} rect=({4},{5})-({6},{7})", h.ToInt64(), c, t, pid, rc.L, rc.T, rc.R, rc.B));
      return true; }, IntPtr.Zero);
    return r;
  }
}
