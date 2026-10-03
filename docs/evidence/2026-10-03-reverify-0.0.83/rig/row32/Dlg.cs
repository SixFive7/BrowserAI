// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

// Row 32's helpers, 2026-10-03: a launched tree by parent pid (never by image
// name), its visible top-level windows, its message-only Chrome windows and their
// titles, a dialog's text, WM_CLOSE, and GetApplicationRestartSettings.
public static class Dlg
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public int dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)] public char[] unread;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetApplicationRestartSettings(IntPtr hProcess, StringBuilder cmd, ref int size, out int flags);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);

    public static HashSet<int> Tree(int root)
    {
        var parent = new Dictionary<int, List<int>>();
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        var e = new PROCESSENTRY32W(); e.dwSize = Marshal.SizeOf(typeof(PROCESSENTRY32W));
        if (Process32FirstW(snap, ref e))
        {
            do { List<int> k; if (!parent.TryGetValue(e.th32ParentProcessID, out k)) { k = new List<int>(); parent[e.th32ParentProcessID] = k; } k.Add(e.th32ProcessID); }
            while (Process32NextW(snap, ref e));
        }
        CloseHandle(snap);
        var set = new HashSet<int> { root }; var st = new Stack<int>(); st.Push(root);
        while (st.Count > 0) { var p = st.Pop(); List<int> k; if (parent.TryGetValue(p, out k)) foreach (var c in k) if (set.Add(c)) st.Push(c); }
        return set;
    }

    static string Cls(IntPtr h) { var c = new StringBuilder(256); GetClassNameW(h, c, 256); return c.ToString(); }
    static string Txt(IntPtr h) { var t = new StringBuilder(1024); GetWindowTextW(h, t, 1024); return t.ToString(); }

    public static List<string> Visible(HashSet<int> pids)
    {
        var r = new List<string>();
        EnumWindows((h, l) => { int p; GetWindowThreadProcessId(h, out p); if (pids.Contains(p) && IsWindowVisible(h)) r.Add("0x" + h.ToInt64().ToString("X") + "|" + p + "|" + Cls(h) + "|" + Txt(h)); return true; }, IntPtr.Zero);
        return r;
    }

    public static IntPtr Dialog(HashSet<int> pids)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => { int p; GetWindowThreadProcessId(h, out p); if (pids.Contains(p) && IsWindowVisible(h) && Cls(h) == "#32770") { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }

    public static string DialogText(IntPtr dlg)
    {
        var parts = new List<string> { "title=" + Txt(dlg) };
        EnumChildWindows(dlg, (h, l) => { var t = Txt(h); if (t.Length > 0) parts.Add(Cls(h) + ":" + t.Replace("\r", " ").Replace("\n", " ")); return true; }, IntPtr.Zero);
        return string.Join(" || ", parts);
    }

    public static List<string> MessageWindows(HashSet<int> pids)
    {
        var r = new List<string>(); IntPtr after = IntPtr.Zero; IntPtr HWND_MESSAGE = new IntPtr(-3);
        while ((after = FindWindowExW(HWND_MESSAGE, after, "Chrome_MessageWindow", null)) != IntPtr.Zero)
        { int p; GetWindowThreadProcessId(after, out p); if (pids.Contains(p)) r.Add(p + "|" + Txt(after)); }
        return r;
    }

    public static bool Close(IntPtr h) { return PostMessageW(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }

    // 0 = registered; anything else is the HRESULT (0x80070490 = not registered).
    public static string Restart(int pid)
    {
        IntPtr h = OpenProcess(0x0400 | 0x0010, false, pid);
        if (h == IntPtr.Zero) return "OpenProcess failed " + Marshal.GetLastWin32Error();
        try
        {
            int size = 2048; var sb = new StringBuilder(size); int flags;
            int hr = GetApplicationRestartSettings(h, sb, ref size, out flags);
            return hr == 0 ? "S_OK registered, " + sb.Length + " chars" : "0x" + hr.ToString("X8");
        }
        finally { CloseHandle(h); }
    }
}
