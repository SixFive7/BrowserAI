// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#if P1_CDP
#pragma warning disable
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text;
// A minimal Chrome DevTools Protocol client over the two inherited pipe handles.
// Messages are UTF-8 JSON, each terminated by one NUL byte, in both directions
// (content/browser/devtools/devtools_pipe_handler.cc, PipeReaderASCIIZ / PipeWriterASCIIZ).

internal static class J
{
    public static string Str(string value) => "\"" + JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode(value) + "\"";
}

internal sealed unsafe class CdpPipe : IDisposable
{
    private readonly nint _write;
    private readonly nint _read;
    private readonly Thread _reader;
    private readonly Lock _writeGate = new();
    private readonly ConcurrentDictionary<int, Slot> _pending = new();
    private readonly Stopwatch _clock;
    private int _nextId;
    private bool _writeClosed;

    public CdpPipe(nint writeToBrowser, nint readFromBrowser, Stopwatch clock)
    {
        _write = writeToBrowser;
        _read = readFromBrowser;
        _clock = clock;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "cdp reader" };
    }

    /// <summary>Called on the reader thread for every event: method, params, sessionId.</summary>
    public Action<string, JsonElement, string?>? OnEvent { get; set; }

    public volatile bool ReadEnded;

    public double ReadEndedAtMs { get; private set; } = -1;

    public int ReadEndError { get; private set; }

    public long BytesIn { get; private set; }

    public long MessagesIn { get; private set; }

    public void Start() => _reader.Start();

    public int Post(string method, string paramsJson = "{}", string? sessionId = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        Send(id, method, paramsJson, sessionId);
        return id;
    }

    public JsonDocument Call(string method, string paramsJson = "{}", string? sessionId = null, int timeoutMs = 20000)
    {
        var id = Interlocked.Increment(ref _nextId);
        var slot = new Slot();
        _pending[id] = slot;
        Send(id, method, paramsJson, sessionId);

        if (!slot.Done.Wait(timeoutMs))
        {
            _ = _pending.TryRemove(id, out _);
            throw new TimeoutException($"{method} got no answer in {timeoutMs} ms (read ended: {ReadEnded}).");
        }

        var doc = slot.Document!;
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"{method} failed: {error.GetRawText()}");
        }

        return doc;
    }

    private void Send(int id, string method, string paramsJson, string? sessionId)
    {
        var text = sessionId is null
            ? $"{{\"id\":{id},\"method\":{J.Str(method)},\"params\":{paramsJson}}}"
            : $"{{\"id\":{id},\"method\":{J.Str(method)},\"params\":{paramsJson},\"sessionId\":{J.Str(sessionId)}}}";
        var bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        _ = Encoding.UTF8.GetBytes(text, bytes);

        lock (_writeGate)
        {
            if (_writeClosed)
            {
                throw new IOException("The pipe to the browser is closed.");
            }

            fixed (byte* p = bytes)
            {
                var done = 0u;
                while (done < bytes.Length)
                {
                    if (!W.WriteFile(_write, p + done, (uint)bytes.Length - done, out var written, 0))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteFile to the browser's pipe");
                    }

                    done += written;
                }
            }
        }
    }

    /// <summary>Closes our write end, which the browser sees as its read failing.</summary>
    public void CloseWriteEnd()
    {
        lock (_writeGate)
        {
            if (!_writeClosed)
            {
                _writeClosed = true;
                _ = W.CloseHandle(_write);
            }
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[1 << 16];
        var pending = new MemoryStream();

        fixed (byte* p = buffer)
        {
            while (true)
            {
                if (!W.ReadFile(_read, p, (uint)buffer.Length, out var read, 0) || read == 0)
                {
                    ReadEndError = Marshal.GetLastWin32Error();
                    ReadEndedAtMs = _clock.Elapsed.TotalMilliseconds;
                    ReadEnded = true;
                    foreach (var slot in _pending.Values)
                    {
                        slot.Document = JsonDocument.Parse("{\"error\":{\"message\":\"pipe closed\"}}");
                        slot.Done.Set();
                    }

                    return;
                }

                BytesIn += read;
                var start = 0;
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != 0)
                    {
                        continue;
                    }

                    pending.Write(buffer, start, i - start);
                    Dispatch(pending.ToArray());
                    pending.SetLength(0);
                    start = i + 1;
                }

                pending.Write(buffer, start, (int)read - start);
            }
        }
    }

    private void Dispatch(byte[] message)
    {
        MessagesIn++;
        var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        if (root.TryGetProperty("id", out var idElement))
        {
            if (_pending.TryRemove(idElement.GetInt32(), out var slot))
            {
                slot.Document = doc;
                slot.Done.Set();
            }

            return;
        }

        if (root.TryGetProperty("method", out var method))
        {
            var session = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
            var parameters = root.TryGetProperty("params", out var p) ? p : default;
            try
            {
                OnEvent?.Invoke(method.GetString()!, parameters, session);
            }
            catch (Exception failure)
            {
                Console.Error.WriteLine($"event handler for {method.GetString()} threw: {failure.Message}");
            }
        }
    }

    public void Dispose()
    {
        CloseWriteEnd();
        _ = W.CloseHandle(_read);
    }

    private sealed class Slot
    {
        public ManualResetEventSlim Done { get; } = new(false);

        public JsonDocument? Document { get; set; }
    }
}

/// <summary>Starts Chromium with the DevTools pipe handed over through --remote-debugging-io-pipes.</summary>
internal static unsafe class Chromium
{
    public sealed record Started(W.PROCESS_INFORMATION Process, nint WriteToBrowser, nint ReadFromBrowser, string CommandLine, uint ChildReadValue, uint ChildWriteValue);

    public static Started Launch(string chrome, IEnumerable<string> arguments)
    {
        var attributes = new W.SECURITY_ATTRIBUTES { nLength = (uint)sizeof(W.SECURITY_ATTRIBUTES), bInheritHandle = 1 };

        // Pipe 1: we write, the browser reads. Pipe 2: the browser writes, we read.
        if (!W.CreatePipe(out var browserRead, out var ourWrite, ref attributes, 0)
            || !W.CreatePipe(out var ourRead, out var browserWrite, ref attributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe");
        }

        // Our ends must not be inherited: the browser holding our write end would never see EOF.
        if (!W.SetHandleInformation(ourWrite, W.HandleFlagInherit, 0) || !W.SetHandleInformation(ourRead, W.HandleFlagInherit, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation");
        }

        var line = new StringBuilder();
        Append(line, chrome);
        Append(line, "--remote-debugging-pipe");
        Append(line, $"--remote-debugging-io-pipes={(uint)browserRead},{(uint)browserWrite}");
        foreach (var argument in arguments)
        {
            Append(line, argument);
        }

        var commandLine = line.ToString();
        var buffer = (commandLine + "\0").ToCharArray();

        // The exact inherited set: the two pipe ends and nothing else.
        nuint size = 0;
        _ = W.InitializeProcThreadAttributeList(0, 1, 0, ref size);
        var list = Marshal.AllocHGlobal((nint)size);
        var handles = Marshal.AllocHGlobal(nint.Size * 2);
        try
        {
            if (!W.InitializeProcThreadAttributeList(list, 1, 0, ref size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList");
            }

            Marshal.WriteIntPtr(handles, 0, browserRead);
            Marshal.WriteIntPtr(handles, nint.Size, browserWrite);
            if (!W.UpdateProcThreadAttribute(list, 0, W.ProcThreadAttributeHandleList, handles, (nuint)(nint.Size * 2), 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute(HANDLE_LIST)");
            }

            var startup = default(W.STARTUPINFOEXW);
            startup.StartupInfo.cb = sizeof(W.STARTUPINFOEXW);
            startup.lpAttributeList = list;

            W.PROCESS_INFORMATION pi;
            fixed (char* p = buffer)
            {
                if (!W.CreateProcessW(chrome, p, 0, 0, true, W.ExtendedStartupInfoPresent | W.CreateUnicodeEnvironment, 0, Path.GetDirectoryName(chrome), ref startup, out pi))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW(chrome)");
                }
            }

            var readValue = (uint)browserRead;
            var writeValue = (uint)browserWrite;

            // The browser has its copies; ours of ITS ends must close, or its exit never reaches us as EOF.
            _ = W.CloseHandle(browserRead);
            _ = W.CloseHandle(browserWrite);

            return new Started(pi, ourWrite, ourRead, commandLine, readValue, writeValue);
        }
        finally
        {
            W.DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            Marshal.FreeHGlobal(handles);
        }
    }

    private static void Append(StringBuilder b, string argument)
    {
        if (b.Length != 0)
        {
            b.Append(' ');
        }

        if (argument.Length != 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            b.Append(argument);
            return;
        }

        b.Append('"');
        for (var i = 0; i < argument.Length; i++)
        {
            var slashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                i++;
                slashes++;
            }

            if (i == argument.Length)
            {
                b.Append('\\', slashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                b.Append('\\', (slashes * 2) + 1).Append('"');
            }
            else
            {
                b.Append('\\', slashes).Append(argument[i]);
            }
        }

        b.Append('"');
    }
}

// Win32 declarations for the b2 P1 probe. Scratch only.

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


internal static class P1Host
{
    public static int Run(string[] args)
    {
        var clock = Stopwatch.StartNew();
        const string Origin = "https://app.browserai.invalid";
        const string Html = "<!doctype html><title>BrowserAI</title><script src=\"/app.js\" defer></script><p id=\"state\">loading</p>";
        const string Js = "window.browseraiHost(JSON.stringify({ready:true}));";
        var started = Chromium.Launch(args[1], [$"--user-data-dir={args[2]}", "--app=data:text/html,", "--no-first-run"]);
        using var cdp = new CdpPipe(started.WriteToBrowser, started.ReadFromBrowser, clock);
        var ready = new ManualResetEventSlim(false);
        cdp.OnEvent = (method, p, sid) =>
        {
            if (method == "Fetch.requestPaused")
            {
                var id = p.GetProperty("requestId").GetString()!;
                var url = p.GetProperty("request").GetProperty("url").GetString()!;
                var body = url == Origin + "/" ? Html : url == Origin + "/app.js" ? Js : null;
                _ = body is null
                    ? cdp.Post("Fetch.failRequest", $"{{\"requestId\":{J.Str(id)},\"errorReason\":\"BlockedByClient\"}}", sid)
                    : cdp.Post("Fetch.fulfillRequest", $"{{\"requestId\":{J.Str(id)},\"responseCode\":200,\"body\":{J.Str(Convert.ToBase64String(Encoding.UTF8.GetBytes(body)))}}}", sid);
            }
            else if (method == "Runtime.bindingCalled")
            {
                ready.Set();
            }
        };
        cdp.Start();
        using var targets = cdp.Call("Target.getTargets");
        string? target = null;
        foreach (var t in targets.RootElement.GetProperty("result").GetProperty("targetInfos").EnumerateArray())
        {
            if (t.GetProperty("type").GetString() == "page")
            {
                target = t.GetProperty("targetId").GetString();
            }
        }

        using var attached = cdp.Call("Target.attachToTarget", $"{{\"targetId\":{J.Str(target!)},\"flatten\":true}}");
        var session = attached.RootElement.GetProperty("result").GetProperty("sessionId").GetString();
        _ = cdp.Call("Runtime.addBinding", "{\"name\":\"browseraiHost\"}", session);
        _ = cdp.Call("Fetch.enable", "{\"patterns\":[{\"urlPattern\":\"*\"}]}", session);
        _ = cdp.Call("Page.navigate", $"{{\"url\":{J.Str(Origin + "/")}}}", session);
        ready.Wait(30000);
        _ = cdp.Call("Runtime.evaluate", "{\"expression\":\"1+1\",\"returnByValue\":true}", session);
        _ = cdp.Call("Browser.close");
        return 0;
    }
}

#endif
