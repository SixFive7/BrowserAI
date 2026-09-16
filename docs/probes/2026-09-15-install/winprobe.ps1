# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param([Parameter(Mandatory=$true)][int]$Pid2)
$ErrorActionPreference = 'Stop'
$code = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WP {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc p, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder t, int c);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder t, int c);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static List<string> Find(int target) {
    var outp = new List<string>();
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != (uint)target) return true;
      var cls = new StringBuilder(256); GetClassNameW(h, cls, 256);
      var ttl = new StringBuilder(512); GetWindowTextW(h, ttl, 512);
      RECT r; GetWindowRect(h, out r);
      int style = GetWindowLong(h, -16);
      int ex = GetWindowLong(h, -20);
      outp.Add(string.Format("hwnd=0x{0:X} class='{1}' title='{2}' visible={3} iconic={4} rect=({5},{6})-({7},{8}) w={9} h={10} style=0x{11:X8} exstyle=0x{12:X8}",
        h.ToInt64(), cls, ttl, IsWindowVisible(h), IsIconic(h), r.Left, r.Top, r.Right, r.Bottom, r.Right-r.Left, r.Bottom-r.Top, style, ex));
      return true;
    }, IntPtr.Zero);
    return outp;
  }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
$res = [WP]::Find($Pid2)
if ($res.Count -eq 0) { Write-Output "  (no top-level window owned by pid $Pid2)" } else { $res | ForEach-Object { Write-Output "  $_" } }
