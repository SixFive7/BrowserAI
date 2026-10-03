// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A minimal Chrome DevTools Protocol client over the two inherited pipe handles.
// Messages are UTF-8 JSON, each terminated by one NUL byte, in both directions
// (content/browser/devtools/devtools_pipe_handler.cc, PipeReaderASCIIZ / PipeWriterASCIIZ).
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

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
