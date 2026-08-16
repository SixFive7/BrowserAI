// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace BrowserAI.TestProbe;

/// <summary>
/// Publishes a message-only window in another process, so the cross-process
/// window read can be measured against a window that is <b>trying to defeat
/// it</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole sweep rests on undocumented behaviour of a documented
/// function</b>, and this is the discriminator that pins it.
/// <c>GetWindowTextW</c>'s contract says a window with no caption returns a null
/// string; a <c>Chrome_MessageWindow</c> has no caption and returns its name
/// anyway, because cross-process the call never sends <c>WM_GETTEXT</c> at all —
/// it reads the kernel-side name set at <c>CreateWindowExW</c>.
/// </para>
/// <para>
/// A window whose WndProc <b>suppresses</b> <c>WM_GETTEXT</c> is the sharpest
/// available proof: same-process the read comes back empty, cross-process it
/// comes back with the real name. Nothing but a bypass of the message queue can
/// produce both answers about one window, and the probe reports the same-process
/// half itself so the test does not have to take the suppression on trust.
/// </para>
/// <para>
/// <b>It also stands in for a browser.</b> Window classes are per-process, so a
/// plain console application can register <c>Chrome_MessageWindow</c> and publish
/// any path it likes — which is simultaneously how the sweep's attribution half
/// is tested without a browser, and why attribution can never be allowed to
/// decide anything on its own.
/// </para>
/// </remarks>
internal static partial class WindowProbe
{
    private const uint WmGetText = 0x000D;
    private const uint WmClose = 0x0010;
    private const uint PmRemove = 0x0001;

    // HWND_MESSAGE: the parent that makes a window message-only -- invisible,
    // never enumerated by EnumWindows, and scoped to this window station and
    // desktop.
    private static readonly nint HwndMessage = -3;

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private static bool _suppressWindowText;

    /// <summary>
    /// Registers a window class, publishes one message-only window titled as
    /// asked, reports, and stays alive.
    /// </summary>
    /// <param name="className">The class to register. Per-process, so any name works.</param>
    /// <param name="title">The window's kernel-side name.</param>
    /// <param name="reportPath">Where to write what was created.</param>
    /// <param name="suppress">
    /// <c>suppress</c> to make the WndProc answer <c>WM_GETTEXT</c> with an empty
    /// string; anything else to let <c>DefWindowProc</c> answer it.
    /// </param>
    /// <returns>Zero once it is asked to stop, or one if the window could not be made.</returns>
    public static unsafe int Publish(string className, string title, string reportPath, string suppress)
    {
        _suppressWindowText = string.Equals(suppress, "suppress", StringComparison.Ordinal);

        var classNameBuffer = Marshal.StringToHGlobalUni(className);
        var titleBuffer = Marshal.StringToHGlobalUni(title);
        var instance = GetModuleHandleW(nint.Zero);

        var registration = new WindowClass
        {
            Size = (uint)Unsafe.SizeOf<WindowClass>(),
            WindowProcedure = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&Procedure,
            Instance = instance,
            ClassName = classNameBuffer,
        };

        if (RegisterClassExW(ref registration) is 0)
        {
            Write(reportPath, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["error"] = $"RegisterClassExW failed with {Marshal.GetLastPInvokeError()}",
            });

            return 1;
        }

        var window = CreateWindowExW(0, classNameBuffer, titleBuffer, 0, 0, 0, 0, 0, HwndMessage, nint.Zero, instance, nint.Zero);

        if (window == nint.Zero)
        {
            Write(reportPath, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["error"] = $"CreateWindowExW failed with {Marshal.GetLastPInvokeError()}",
            });

            return 1;
        }

        Write(reportPath, new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["window"] = window.ToInt64(),
            ["className"] = className,
            ["title"] = title,
            ["suppressing"] = _suppressWindowText,

            // The same-process halves, measured here because they cannot be
            // measured anywhere else: from this thread both of these DO go
            // through the WndProc, so a suppressing one answers "" to both while
            // a reader in another process still sees the real name.
            ["sameProcessGetWindowText"] = SameProcessWindowText(window),
            ["sameProcessSendMessage"] = SameProcessSendMessage(window),
        });

        // A pump, so the window behaves like a real one -- and so that a
        // same-process SendMessage from a test would be answered rather than
        // hanging. The probe exits on its own after Patience whatever happens to
        // the host, on top of the job object the host holds it in.
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < Patience)
        {
            while (PeekMessageW(out var message, nint.Zero, 0, 0, PmRemove))
            {
                if (message.Message is WmClose)
                {
                    return 0;
                }

                _ = DispatchMessageW(ref message);
            }

            Thread.Sleep(10);
        }

        return 0;
    }

    /// <summary>
    /// The WndProc, which lies about <c>WM_GETTEXT</c> when asked to.
    /// </summary>
    /// <remarks>
    /// Returning zero and writing a terminator is what a real hostile or
    /// mid-crash owner does. It is answered from this thread for a same-process
    /// caller and never consulted at all for a cross-process one, which is the
    /// whole measurement.
    /// </remarks>
    [UnmanagedCallersOnly]
    private static unsafe nint Procedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message is WmGetText && _suppressWindowText)
        {
            if (wParam.ToInt64() > 0 && lParam != nint.Zero)
            {
                ((char*)lParam)[0] = '\0';
            }

            return nint.Zero;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private static unsafe string SameProcessWindowText(nint window)
    {
        var buffer = new char[512];

        fixed (char* start = buffer)
        {
            var copied = GetWindowTextW(window, start, buffer.Length);

            return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
        }
    }

    private static unsafe string SameProcessSendMessage(nint window)
    {
        var buffer = new char[512];

        fixed (char* start = buffer)
        {
            var copied = SendMessageW(window, WmGetText, buffer.Length, (nint)start);

            return copied.ToInt64() <= 0 ? string.Empty : new string(buffer, 0, (int)copied.ToInt64());
        }
    }

    private static void Write(string path, JsonObject report)
    {
        var temp = $"{path}.writing";
        File.WriteAllText(temp, report.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WindowClass windowClass);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(
        uint exStyle,
        nint className,
        nint windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out WindowMessage message, nint window, uint filterMin, uint filterMax, uint remove);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(ref WindowMessage message);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static unsafe partial int GetWindowTextW(nint window, char* text, int maxCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetModuleHandleW(nint moduleName);
}
