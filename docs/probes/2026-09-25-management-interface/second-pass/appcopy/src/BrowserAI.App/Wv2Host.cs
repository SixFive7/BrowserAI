// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#if WEBVIEW2
#pragma warning disable
// Track B scratch: WebView2 hosted from a NativeAOT WinExe through hand-declared
// COM ([GeneratedComInterface] / [GeneratedComClass], the technique this
// repository already uses for the task scheduler). No loopback port anywhere:
// the page arrives through NavigateToString and talks back through postMessage.
//
// The top-level window is created WITHOUT WS_VISIBLE and ShowWindow is never
// called; the launcher also starts this process on a private desktop that is
// never switched to, inside a kill-on-close job.
//
// args: <resultFile> <userDataFolder>
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Wv2Probe;

internal static unsafe partial class Program
{
    public static int Run(string[] args) => MainCore(args[1..]);

    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmTimer = 0x0113;
    private const nint HangTimer = 1;
    private const nint LingerTimer = 2;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<string> Lines = [];
    private static string _resultFile = string.Empty;
    private static nint _hwnd;

#if !NO_WEBVIEW
    // Held so the runtime keeps the wrappers alive for the life of the page.
    internal static ICoreWebView2Environment? Environment;
    internal static ICoreWebView2Controller? Controller;
    internal static ICoreWebView2? WebView;
#endif

    internal const string Html =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>BrowserAI</title>"
        + "<style>:root{color-scheme:light dark}body{font:14px system-ui;margin:24px}</style></head>"
        + "<body><h1>BrowserAI</h1><p>Claude Code: registered. Codex: registered.</p>"
        + "<script>window.chrome.webview.postMessage('ready:' + Math.round(performance.now()) + ':dark=' + matchMedia('(prefers-color-scheme: dark)').matches + ':dpr=' + devicePixelRatio);</script>"
        + "</body></html>";

    private static int MainCore(string[] args)
    {
        _resultFile = args[0];
        var userData = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "udf");
        Mark("main");

        var instance = GetModuleHandleW(0);

        fixed (char* className = "Wv2ProbeWindow")
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = instance,
                lpszClassName = (nint)className,
            };

            if (RegisterClassExW(&wc) == 0)
            {
                Mark("RegisterClassExW failed " + Marshal.GetLastPInvokeError());
                Flush();
                return 2;
            }

            // WS_OVERLAPPEDWINDOW (0x00CF0000) and deliberately NOT WS_VISIBLE.
            _hwnd = CreateWindowExW(0, (nint)className, (nint)className, 0x00CF0000, 0, 0, 1000, 700, 0, 0, instance, 0);
        }

        if (_hwnd == 0)
        {
            Mark("CreateWindowExW failed " + Marshal.GetLastPInvokeError());
            Flush();
            return 2;
        }

        _ = SetTimer(_hwnd, HangTimer, 30000, 0);

#if NO_WEBVIEW
        Mark("window-only build");
        _ = PostMessageW(_hwnd, WmClose, 0, 0);
#else
        var hr = CreateCoreWebView2EnvironmentWithOptions(null, userData, 0, new EnvironmentCompleted());
        Mark($"create-environment-called hr=0x{hr:X8}");

        if (hr < 0)
        {
            Flush();
            return 3;
        }
#endif

        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            _ = TranslateMessage(in msg);
            _ = DispatchMessageW(in msg);
        }

        Mark("exit");
        Flush();
        return 0;
    }

    internal static void Mark(string what)
    {
        Lines.Add(Clock.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + "\t" + what);
    }

    internal static void Flush()
    {
        var temporary = _resultFile + ".tmp";
        File.WriteAllLines(temporary, Lines);
        File.Move(temporary, _resultFile, overwrite: true);
    }

    internal static void Linger() => _ = SetTimer(_hwnd, LingerTimer, 2500, 0);

    internal static nint WindowHandle => _hwnd;

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmTimer when wParam == HangTimer:
                Mark("hang detector fired");
                _ = DestroyWindow(hwnd);
                return 0;

            case WmTimer when wParam == LingerTimer:
                _ = KillTimer(hwnd, LingerTimer);
                Mark("closing controller");
#if !NO_WEBVIEW
                _ = Controller?.Close();
#endif
                _ = DestroyWindow(hwnd);
                return 0;

            case WmClose:
                _ = DestroyWindow(hwnd);
                return 0;

            case WmDestroy:
                PostQuitMessage(0);
                return 0;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

#if !NO_WEBVIEW
    [LibraryImport("WebView2Loader.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateCoreWebView2EnvironmentWithOptions(
        string? browserExecutableFolder,
        string? userDataFolder,
        nint environmentOptions,
        ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler environmentCreatedHandler);
#endif

    [LibraryImport("kernel32.dll")]
    private static partial nint GetModuleHandleW(nint moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, nint className, nint windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    private static partial int TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in MSG msg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    private static partial int PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint SetTimer(nint hwnd, nint id, uint elapse, nint timerProc);

    [LibraryImport("user32.dll")]
    private static partial int KillTimer(nint hwnd, nint id);

    [LibraryImport("user32.dll")]
    private static partial int DestroyWindow(nint hwnd);
}

[StructLayout(LayoutKind.Sequential)]
internal struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public nint lpszMenuName;
    public nint lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public int ptX;
    public int ptY;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

#if !NO_WEBVIEW
[GeneratedComClass]
internal sealed partial class EnvironmentCompleted : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    public int Invoke(int errorCode, ICoreWebView2Environment? result)
    {
        Program.Mark($"environment hr=0x{errorCode:X8}");
        if (errorCode < 0 || result is null)
        {
            Program.Flush();
            return 0;
        }

        Program.Environment = result;
        var hr = result.CreateCoreWebView2Controller(Program.WindowHandle, new ControllerCompleted());
        Program.Mark($"create-controller-called hr=0x{hr:X8}");
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class ControllerCompleted : ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    public int Invoke(int errorCode, ICoreWebView2Controller? result)
    {
        Program.Mark($"controller hr=0x{errorCode:X8}");
        if (errorCode < 0 || result is null)
        {
            Program.Flush();
            return 0;
        }

        Program.Controller = result;
        _ = result.put_Bounds(new RECT { Left = 0, Top = 0, Right = 1000, Bottom = 700 });
        _ = result.get_CoreWebView2(out var webView);
        Program.WebView = webView;

        if (webView is null)
        {
            Program.Mark("no CoreWebView2");
            Program.Flush();
            return 0;
        }

        _ = webView.get_BrowserProcessId(out var pid);
        Program.Mark($"browser-pid {pid}");
        _ = webView.add_NavigationCompleted(new NavigationCompleted(), out _);
        _ = webView.add_WebMessageReceived(new MessageReceived(), out _);
        var hr = webView.NavigateToString(Program.Html);
        Program.Mark($"navigate-to-string hr=0x{hr:X8}");
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class NavigationCompleted : ICoreWebView2NavigationCompletedEventHandler
{
    public int Invoke(ICoreWebView2? sender, ICoreWebView2NavigationCompletedEventArgs? args)
    {
        var ok = 0;
        _ = args?.get_IsSuccess(out ok);
        Program.Mark($"navigation-completed success={ok}");
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class MessageReceived : ICoreWebView2WebMessageReceivedEventHandler
{
    public int Invoke(ICoreWebView2? sender, ICoreWebView2WebMessageReceivedEventArgs? args)
    {
        nint text = 0;
        _ = args?.TryGetWebMessageAsString(out text);
        var message = text == 0 ? "(none)" : Marshal.PtrToStringUni(text);
        if (text != 0)
        {
            Marshal.FreeCoTaskMem(text);
        }

        Program.Mark($"message {message}");
        Program.Flush();
        Program.Linger();
        return 0;
    }
}

[GeneratedComInterface]
[Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
internal partial interface ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    [PreserveSig]
    int Invoke(int errorCode, ICoreWebView2Environment? result);
}

[GeneratedComInterface]
[Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    [PreserveSig]
    int Invoke(int errorCode, ICoreWebView2Controller? result);
}

[GeneratedComInterface]
[Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c")]
internal partial interface ICoreWebView2NavigationCompletedEventHandler
{
    [PreserveSig]
    int Invoke(ICoreWebView2? sender, ICoreWebView2NavigationCompletedEventArgs? args);
}

[GeneratedComInterface]
[Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2")]
internal partial interface ICoreWebView2WebMessageReceivedEventHandler
{
    [PreserveSig]
    int Invoke(ICoreWebView2? sender, ICoreWebView2WebMessageReceivedEventArgs? args);
}

[GeneratedComInterface]
[Guid("b96d755e-0319-4e92-a296-23436f46a1fc")]
internal partial interface ICoreWebView2Environment
{
    [PreserveSig]
    int CreateCoreWebView2Controller(nint parentWindow, ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler);
}

[GeneratedComInterface]
[Guid("4d00c0d1-9434-4eb6-8078-8697a560334f")]
internal partial interface ICoreWebView2Controller
{
    [PreserveSig] int get_IsVisible(out int isVisible);
    [PreserveSig] int put_IsVisible(int isVisible);
    [PreserveSig] int get_Bounds(out RECT bounds);
    [PreserveSig] int put_Bounds(RECT bounds);
    [PreserveSig] int get_ZoomFactor(out double zoomFactor);
    [PreserveSig] int put_ZoomFactor(double zoomFactor);
    [PreserveSig] int add_ZoomFactorChanged(nint handler, out long token);
    [PreserveSig] int remove_ZoomFactorChanged(long token);
    [PreserveSig] int SetBoundsAndZoomFactor(RECT bounds, double zoomFactor);
    [PreserveSig] int MoveFocus(int reason);
    [PreserveSig] int add_MoveFocusRequested(nint handler, out long token);
    [PreserveSig] int remove_MoveFocusRequested(long token);
    [PreserveSig] int add_GotFocus(nint handler, out long token);
    [PreserveSig] int remove_GotFocus(long token);
    [PreserveSig] int add_LostFocus(nint handler, out long token);
    [PreserveSig] int remove_LostFocus(long token);
    [PreserveSig] int add_AcceleratorKeyPressed(nint handler, out long token);
    [PreserveSig] int remove_AcceleratorKeyPressed(long token);
    [PreserveSig] int get_ParentWindow(out nint parentWindow);
    [PreserveSig] int put_ParentWindow(nint parentWindow);
    [PreserveSig] int NotifyParentWindowPositionChanged();
    [PreserveSig] int Close();
    [PreserveSig] int get_CoreWebView2(out ICoreWebView2? coreWebView2);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("76eceacb-0462-4d94-ac83-423a6793775e")]
internal partial interface ICoreWebView2
{
    [PreserveSig] int get_Settings(out nint settings);
    [PreserveSig] int get_Source(out nint uri);
    [PreserveSig] int Navigate(string uri);
    [PreserveSig] int NavigateToString(string htmlContent);
    [PreserveSig] int add_NavigationStarting(nint handler, out long token);
    [PreserveSig] int remove_NavigationStarting(long token);
    [PreserveSig] int add_ContentLoading(nint handler, out long token);
    [PreserveSig] int remove_ContentLoading(long token);
    [PreserveSig] int add_SourceChanged(nint handler, out long token);
    [PreserveSig] int remove_SourceChanged(long token);
    [PreserveSig] int add_HistoryChanged(nint handler, out long token);
    [PreserveSig] int remove_HistoryChanged(long token);
    [PreserveSig] int add_NavigationCompleted(ICoreWebView2NavigationCompletedEventHandler handler, out long token);
    [PreserveSig] int remove_NavigationCompleted(long token);
    [PreserveSig] int add_FrameNavigationStarting(nint handler, out long token);
    [PreserveSig] int remove_FrameNavigationStarting(long token);
    [PreserveSig] int add_FrameNavigationCompleted(nint handler, out long token);
    [PreserveSig] int remove_FrameNavigationCompleted(long token);
    [PreserveSig] int add_ScriptDialogOpening(nint handler, out long token);
    [PreserveSig] int remove_ScriptDialogOpening(long token);
    [PreserveSig] int add_PermissionRequested(nint handler, out long token);
    [PreserveSig] int remove_PermissionRequested(long token);
    [PreserveSig] int add_ProcessFailed(nint handler, out long token);
    [PreserveSig] int remove_ProcessFailed(long token);
    [PreserveSig] int AddScriptToExecuteOnDocumentCreated(nint javaScript, nint handler);
    [PreserveSig] int RemoveScriptToExecuteOnDocumentCreated(nint id);
    [PreserveSig] int ExecuteScript(nint javaScript, nint handler);
    [PreserveSig] int CapturePreview(int imageFormat, nint imageStream, nint handler);
    [PreserveSig] int Reload();
    [PreserveSig] int PostWebMessageAsJson(string webMessageAsJson);
    [PreserveSig] int PostWebMessageAsString(string webMessageAsString);
    [PreserveSig] int add_WebMessageReceived(ICoreWebView2WebMessageReceivedEventHandler handler, out long token);
    [PreserveSig] int remove_WebMessageReceived(long token);
    [PreserveSig] int CallDevToolsProtocolMethod(nint methodName, nint parametersAsJson, nint handler);
    [PreserveSig] int get_BrowserProcessId(out uint value);
}

[GeneratedComInterface]
[Guid("0f99a40c-e962-4207-9e92-e3d542eff849")]
internal partial interface ICoreWebView2WebMessageReceivedEventArgs
{
    [PreserveSig] int get_Source(out nint value);
    [PreserveSig] int get_WebMessageAsJson(out nint value);
    [PreserveSig] int TryGetWebMessageAsString(out nint value);
}

[GeneratedComInterface]
[Guid("30d68b7d-20d9-4752-a9ca-ec8448fbb5c1")]
internal partial interface ICoreWebView2NavigationCompletedEventArgs
{
    [PreserveSig] int get_IsSuccess(out int isSuccess);
    [PreserveSig] int get_WebErrorStatus(out int webErrorStatus);
    [PreserveSig] int get_NavigationId(out ulong navigationId);
}
#endif

#endif
