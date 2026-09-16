# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param([Parameter(Mandatory)][int] $Pid2)
$ErrorActionPreference = 'Stop'
Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class DlgRead {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumWindowsProc p, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder t, int c);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder t, int c);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

  public static List<string> Tops(int target) {
    var outp = new List<string>();
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != (uint)target) return true;
      var cls = new StringBuilder(256); GetClassNameW(h, cls, 256);
      var ttl = new StringBuilder(512); GetWindowTextW(h, ttl, 512);
      RECT r; GetWindowRect(h, out r);
      outp.Add(string.Format("TOP hwnd=0x{0:X} class='{1}' title='{2}' visible={3} rect=({4},{5})-({6},{7}) w={8} h={9}",
        h.ToInt64(), cls, ttl, IsWindowVisible(h), r.Left, r.Top, r.Right, r.Bottom, r.Right-r.Left, r.Bottom-r.Top));
      EnumChildWindows(h, (c, l2) => {
        var ccls = new StringBuilder(256); GetClassNameW(c, ccls, 256);
        var cttl = new StringBuilder(2048); GetWindowTextW(c, cttl, 2048);
        RECT cr; GetWindowRect(c, out cr);
        outp.Add(string.Format("    CHILD hwnd=0x{0:X} id={1} class='{2}' visible={3} rect=({4},{5})-({6},{7}) text='{8}'",
          c.ToInt64(), GetDlgCtrlID(c), ccls, IsWindowVisible(c), cr.Left, cr.Top, cr.Right, cr.Bottom, cttl));
        return true;
      }, IntPtr.Zero);
      return true;
    }, IntPtr.Zero);
    return outp;
  }
}
'@
[DlgRead]::Tops($Pid2) | ForEach-Object { $_ }
