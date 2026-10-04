# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# The positive control for HiddenDesktop.ps1, run as its command. It writes the
# name of the desktop its own thread is on, then shows one window that cannot
# take the foreground and sits far off any monitor (a tool window, no taskbar
# button, shown with SW_SHOWNOACTIVATE at -32000,-32000) for four seconds, so the
# launcher's census has something to find on the private desktop and must find
# nothing on its own.
param([Parameter(Mandatory)] [string] $Out)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DeskProbe
{
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformationW(IntPtr h, int index, StringBuilder info, int length, out int needed);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    public static string DesktopName()
    {
        var sb = new StringBuilder(256); int needed;
        GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 2, sb, 512, out needed);
        return sb.ToString();
    }
    public static IntPtr Show()
    {
        // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE; WS_POPUP; the STATIC class needs no registration.
        var hwnd = CreateWindowExW(0x00000080 | 0x08000000, "STATIC", "lifetime desk probe", 0x80000000, -32000, -32000, 64, 64, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        ShowWindow(hwnd, 4); // SW_SHOWNOACTIVATE
        return hwnd;
    }
    public static void Hide(IntPtr hwnd) { DestroyWindow(hwnd); }
    public static bool IsForeground(IntPtr hwnd) { return GetForegroundWindow() == hwnd; }
}
'@

$name = [DeskProbe]::DesktopName()
$hwnd = [DeskProbe]::Show()
Start-Sleep -Seconds 4
$fg = [DeskProbe]::IsForeground($hwnd)
[DeskProbe]::Hide($hwnd)
Set-Content -LiteralPath $Out -Value ("desktop={0} hwnd=0x{1:x} foregroundOfItsDesktop={2}" -f $name, $hwnd.ToInt64(), $fg) -Encoding utf8
