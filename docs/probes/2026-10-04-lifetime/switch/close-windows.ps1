# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Closes every visible top-level window on THIS process's desktop the way a
# person closes one, with WM_CLOSE, and refuses to run on any desktop whose name
# does not start with "BrowserAI-lifetime-": the desktops HiddenDesktop.ps1
# creates hold nothing but the rig's own processes, so nothing here selects a
# process at all.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class CloseOnDesk
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformationW(IntPtr h, int index, StringBuilder info, int length, out int needed);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    public static string Desktop()
    {
        var sb = new StringBuilder(256); int needed;
        GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 2, sb, 512, out needed);
        return sb.ToString();
    }
    public static List<string> CloseAll()
    {
        var done = new List<string>();
        EnumProc callback = (hwnd, l) =>
        {
            if (IsWindowVisible(hwnd))
            {
                uint pid; GetWindowThreadProcessId(hwnd, out pid);
                var cls = new StringBuilder(256); GetClassNameW(hwnd, cls, 256);
                var title = new StringBuilder(512); GetWindowTextW(hwnd, title, 512);
                bool posted = PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                done.Add(string.Format("pid={0} class={1} title={2} posted={3}", pid, cls, title, posted));
            }
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return done;
    }
}
'@
$desk = [CloseOnDesk]::Desktop()
if (-not $desk.StartsWith('BrowserAI-lifetime-')) { "refused: this process is on desktop '$desk'"; exit 2 }
"desktop=$desk"
[CloseOnDesk]::CloseAll()
