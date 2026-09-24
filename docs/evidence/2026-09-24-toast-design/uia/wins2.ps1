# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class W2 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static List<string> List(uint[] pids) {
    SetProcessDPIAware();
    var r = new List<string>(); var set = new HashSet<uint>(pids);
    EnumWindows((h, l) => { uint pid; GetWindowThreadProcessId(h, out pid); if (!set.Contains(pid) || !IsWindowVisible(h)) return true;
      var c = new StringBuilder(256); GetClassName(h, c, 256); var t = new StringBuilder(256); GetWindowText(h, t, 256); RECT rc; GetWindowRect(h, out rc);
      r.Add(String.Format("hwnd=0x{0:X} class='{1}' title='{2}' pid={3} rect=({4},{5})-({6},{7})", h.ToInt64(), c, t, pid, rc.L, rc.T, rc.R, rc.B));
      return true; }, IntPtr.Zero);
    return r;
  }
}
"@
$pids = @(Get-Process -Name explorer,ShellHost,ShellExperienceHost,StartMenuExperienceHost -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id })
[W2]::List($pids)
