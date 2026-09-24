# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public static List<string> List() {
    var r = new List<string>();
    EnumWindows((h, l) => { var c = new StringBuilder(256); GetClassName(h, c, 256); var t = new StringBuilder(256); GetWindowText(h, t, 256); uint pid; GetWindowThreadProcessId(h, out pid);
      if (c.ToString().Contains("CoreWindow") || t.ToString().Contains("otification")) r.Add(String.Format("hwnd=0x{0:X} class='{1}' title='{2}' pid={3} visible={4}", h.ToInt64(), c, t, pid, IsWindowVisible(h)));
      return true; }, IntPtr.Zero);
    return r;
  }
}
"@
foreach ($l in [W]::List()) { $p = [regex]::Match($l,'pid=(\d+)').Groups[1].Value; $n = (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName; "$l proc=$n" }
