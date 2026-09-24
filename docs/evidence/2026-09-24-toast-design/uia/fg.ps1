# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class FG {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public static string Now() { var h = GetForegroundWindow(); var t = new StringBuilder(256); GetWindowText(h, t, 256); var c = new StringBuilder(256); GetClassName(h, c, 256); uint pid; GetWindowThreadProcessId(h, out pid); return String.Format("hwnd=0x{0:X} class='{1}' title='{2}' pid={3}", h.ToInt64(), c, t, pid); }
}
"@
$r = [FG]::Now(); $r; $p = [regex]::Match($r,'pid=(\d+)').Groups[1].Value; "process=" + (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName
Get-Process -Name SystemSettings -ErrorAction SilentlyContinue | Select-Object Id, StartTime, MainWindowTitle | Format-List | Out-String
