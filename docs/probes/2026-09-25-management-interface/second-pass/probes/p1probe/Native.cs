// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Win32 declarations for the b2 P1 probe. Scratch only.
using System.Runtime.InteropServices;
using System.Text;

internal static unsafe class W
{
    public const uint HandleFlagInherit = 1;
    public const uint ExtendedStartupInfoPresent = 0x00080000;
    public const uint CreateUnicodeEnvironment = 0x00000400;
    public static readonly nuint ProcThreadAttributeHandleList = 0x00020002;

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public nint lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOW
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public nint lpReserved2;
        public nint hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;

        public override readonly string ToString() => $"{Left},{Top},{Right},{Bottom}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(out nint read, out nint write, ref SECURITY_ATTRIBUTES attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(nint handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returnSize);

    [DllImport("kernel32.dll")]
    public static extern void DeleteProcThreadAttributeList(nint list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessW(string application, char* commandLine, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref STARTUPINFOEXW startup, out PROCESS_INFORMATION info);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(nint process, out uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(nint process, uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(nint handle, byte* buffer, uint size, out uint read, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(nint handle, byte* buffer, uint size, out uint written, nint overlapped);

    public delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint window, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowExW(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(nint window, uint message, nint w, nint l);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SendMessageTimeoutW(nint window, uint message, nint w, nint l, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    public static extern nint GetClassLongPtrW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(nint window, nint dc, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int cx, int cy, uint step, nint brush, uint flags);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    public static extern bool GdiFlush();

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint window, int attribute, out RECT value, int size);

    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid, out nint store);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(nint variant);

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(nint reserved, uint model);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetThreadDesktop(uint thread);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetUserObjectInformationW(nint obj, int index, char* info, uint length, out uint needed);

    public static string ClassOf(nint window)
    {
        var sb = new StringBuilder(256);
        _ = GetClassNameW(window, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string TitleOf(nint window)
    {
        var sb = new StringBuilder(2048);
        _ = GetWindowTextW(window, sb, sb.Capacity);
        return sb.ToString();
    }

    public static uint PidOf(nint window)
    {
        _ = GetWindowThreadProcessId(window, out var pid);
        return pid;
    }

    public static string DesktopName()
    {
        var desk = GetThreadDesktop(GetCurrentThreadId());
        var buffer = stackalloc char[256];
        return GetUserObjectInformationW(desk, 2 /* UOI_NAME */, buffer, 512, out _) ? new string(buffer) : "(unknown)";
    }
}
