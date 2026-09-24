// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch prototype, 2026-09-24: single instance by named mutex, "show yourself" signal to the running
// instance through a MESSAGE-ONLY window (and, for comparison, a named event), AllowSetForegroundWindow
// from the second start. Headless by construction: no visible window is created, nothing is activated,
// SetForegroundWindow is never called. Not product code.
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

internal static unsafe partial class Program
{
    private static string _log = "";
    private static uint _showMsg;
    private static uint _quitMsg;
    private static int _received;
    private static IntPtr _event;

    private static void Log(string s)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.ffffff} pid={Environment.ProcessId} ts={Stopwatch.GetTimestamp()} {s}";
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (typeof(Program))
        {
            // Several processes append to one file: share it, and retry a sharing violation instead of dying on it.
            for (var i = 0; i < 200; i++)
            {
                try
                {
                    using var fs = new FileStream(_log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    fs.Write(bytes);
                    return;
                }
                catch (IOException) { Thread.Sleep(2); }
            }
        }
    }

    private static int Main(string[] args)
    {
        var t0 = Stopwatch.GetTimestamp();
        var mode = args.Length > 0 ? args[0] : "auto";
        var name = args.Length > 1 ? args[1] : "default";
        _log = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "probe.log");

        if (mode == "fg-info")
        {
            uint timeout = 0;
            var ok = SystemParametersInfoW(0x2000 /* SPI_GETFOREGROUNDLOCKTIMEOUT */, 0, &timeout, 0);
            var fg = GetForegroundWindow();
            uint fgPid = 0;
            _ = GetWindowThreadProcessId(fg, &fgPid);
            string fgImage = "";
            try { fgImage = fgPid == 0 ? "" : Process.GetProcessById((int)fgPid).ProcessName; } catch { fgImage = "<unreadable>"; }
            // Granting to OURSELVES only: TRUE means this process already holds foreground rights, FALSE with
            // ERROR_ACCESS_DENIED means it does not. ASFW_ANY is deliberately never passed.
            var asfw = AllowSetForegroundWindow((uint)Environment.ProcessId);
            var err = Marshal.GetLastPInvokeError();
            Log($"FGINFO SPI_GETFOREGROUNDLOCKTIMEOUT ok={ok} ms={timeout} foregroundPid={fgPid} foregroundImage={fgImage} AllowSetForegroundWindow(self)={asfw} lastError={err}");
            return 0;
        }

        var mutexName = $"Local\\CLProbe-{name}";
        var mutex = CreateMutexW(IntPtr.Zero, 0, mutexName);
        var mutexErr = Marshal.GetLastPInvokeError();
        var already = mutexErr == 183; // ERROR_ALREADY_EXISTS
        _showMsg = RegisterWindowMessageW($"CLProbe-Show-{name}");
        _quitMsg = RegisterWindowMessageW($"CLProbe-Quit-{name}");
        var className = $"CLProbe-{name}";
        var startupUs = (Stopwatch.GetTimestamp() - t0) * 1_000_000 / Stopwatch.Frequency;

        if (!already && mode != "second")
        {
            // PRIMARY: the hidden coordinator.
            _event = CreateEventW(IntPtr.Zero, 0, 0, $"Local\\CLProbe-Event-{name}");
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandleW(null),
            };
            fixed (char* cn = className)
            {
                wc.lpszClassName = cn;
                if (RegisterClassExW(&wc) == 0) { Log($"PRIMARY RegisterClassExW failed {Marshal.GetLastPInvokeError()}"); return 3; }
                var hwnd = CreateWindowExW(0, cn, cn, 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) { Log($"PRIMARY CreateWindowExW failed {Marshal.GetLastPInvokeError()}"); return 4; }
                Log($"PRIMARY READY mutex=created startupUs={startupUs} hwnd=0x{hwnd:X} messageOnly=true visible={IsWindowVisible(hwnd)} showMsg=0x{_showMsg:X}");
            }
            var waiter = new Thread(() =>
            {
                while (true)
                {
                    var r = WaitForSingleObject(_event, 0xFFFFFFFF);
                    if (r != 0) break;
                    Log("PRIMARY EVENT signalled");
                }
            }) { IsBackground = true };
            waiter.Start();
            MSG msg;
            while (GetMessageW(&msg, IntPtr.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(&msg);
                _ = DispatchMessageW(&msg);
            }
            Log($"PRIMARY EXIT received={_received}");
            GC.KeepAlive(mutex);
            return 0;
        }

        // SECONDARY: a second start, as a Start Menu click would make it.
        IntPtr target;
        fixed (char* cn = className) target = FindWindowExW(new IntPtr(-3), IntPtr.Zero, cn, null);
        uint targetPid = 0;
        if (target != IntPtr.Zero) _ = GetWindowThreadProcessId(target, &targetPid);
        var asfw2 = AllowSetForegroundWindow(targetPid);
        var asfwErr = Marshal.GetLastPInvokeError();
        var sentTs = Stopwatch.GetTimestamp();
        var posted = mode == "quit"
            ? PostMessageW(target, _quitMsg, IntPtr.Zero, IntPtr.Zero)
            : PostMessageW(target, _showMsg, new IntPtr(sentTs), new IntPtr(Environment.ProcessId));
        var evTs = Stopwatch.GetTimestamp();
        var ev = OpenEventW(0x0002 /* EVENT_MODIFY_STATE */, 0, $"Local\\CLProbe-Event-{name}");
        var set = ev != IntPtr.Zero && SetEvent(ev) != 0;
        Log($"SECONDARY mutex={(already ? "ALREADY_EXISTS" : "created")} startupUs={startupUs} target=0x{target:X} targetPid={targetPid} AllowSetForegroundWindow={asfw2} lastError={asfwErr} posted={posted} sentTs={sentTs} eventSetTs={evTs} eventSet={set}");
        GC.KeepAlive(mutex);
        return 0;
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _showMsg && _showMsg != 0)
        {
            var now = Stopwatch.GetTimestamp();
            _received++;
            var us = (now - (long)wParam) * 1_000_000 / Stopwatch.Frequency;
            Log($"PRIMARY SHOW from pid={(long)lParam} latencyUs={us} n={_received} (the real app would show its window and call SetForegroundWindow here; not executed)");
            return IntPtr.Zero;
        }
        if (msg == _quitMsg && _quitMsg != 0)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateMutexW(IntPtr attrs, int initialOwner, string name);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateEventW(IntPtr attrs, int manualReset, int initialState, string name);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenEventW(uint access, int inherit, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetEvent(IntPtr h);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(IntPtr h, uint ms);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? name);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, char* className, char* windowName);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, uint* pid);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint pid);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(MSG* msg, IntPtr hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    private static partial int TranslateMessage(MSG* msg);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DispatchMessageW(MSG* msg);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint action, uint param, void* pv, uint winIni);
}
