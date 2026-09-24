// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review scratch prototype, 2026-09-24. Not product code.
// A stand-in for one BrowserAI server: it holds a live marker exactly as LiveInstances.Join does
// (CreateNew, ReadWrite, FileShare.Read, bufferSize 1), publishes the per-server record in one of the
// framings, and optionally answers "describe" on a per-server named pipe.
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static unsafe class Holder
{
    internal static long _n;
    internal static volatile bool _stop;
    internal static long _writes, _writeTicks, _lockContention, _pipeServed, _pipeAllocBytes, _pipeServeTicks, _renameFailures, _renameGaveUp;
    internal static int _lastRenameError;
    private static int _sessions;
    private static string _project = "";

    // holder <dir> <variant> <writesPerSecond> <sessions> <pipe: none|netsync|netasync|raw> <behaviour> <readyEvent> <stopEvent> [<seconds cap>]
    public static int Run(string[] a)
    {
        var dir = a[1];
        var variant = a[2];
        var rate = double.Parse(a[3], CultureInfo.InvariantCulture);
        _sessions = int.Parse(a[4], CultureInfo.InvariantCulture);
        var pipe = a[5];
        var behaviour = a[6];
        using var ready = EventWaitHandle.OpenExisting(a[7]);
        using var stop = EventWaitHandle.OpenExisting(a[8]);
        _project = Environment.CurrentDirectory;

        var guid = Guid.NewGuid().ToString("N");
        var stem = string.Create(CultureInfo.InvariantCulture, $"{Environment.ProcessId}-{guid}");
        var marker = Path.Combine(dir, stem + ".live");

        // EXACTLY LiveInstances.Join's open (src/BrowserAI.Core/Updates/LiveInstances.cs:314).
        using var held = new FileStream(marker, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
        var handle = held.SafeFileHandle;

        var first = Record.Framed(Record.Json(0, Environment.ProcessId, _sessions, _project));
        var region = ((first.Length + Record.SeqLength) / 4096 + 1) * 4096;
        if (variant is "none" or "crc" or "seq" or "lock")
        {
            held.SetLength(region); // set ONCE; never truncated afterwards
            WriteOut(variant, handle, dir, stem, Build(variant, 0, region), 0);
        }
        else if (variant == "posix")
        {
            WriteOut(variant, handle, dir, stem, Build(variant, 0, region), 0);
        }

        var wait = ThreadPool.RegisterWaitForSingleObject(stop, (_, _) => _stop = true, null, Timeout.Infinite, executeOnlyOnce: true);

        Thread? listener = null;
        Task? asyncListener = null;
        IDisposable? pipeObject = null;
        var pipeName = "IpcReview-" + stem;
        if (pipe == "netsync")
        {
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
            pipeObject = server;
            listener = new Thread(() => ServeNetSync(server, behaviour)) { IsBackground = true, Name = "pipe" };
            listener.Start();
        }
        else if (pipe == "netasync")
        {
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            pipeObject = server;
            asyncListener = Task.Run(() => AsyncServe.ServeNetAsync(server, behaviour));
        }
        else if (pipe == "raw")
        {
            var sddl = "D:P(A;;GA;;;" + Native.CurrentUserSid() + ")";
            if (!Native.ConvertStringToSd(sddl, 1, out var sd, out _))
            {
                throw new System.ComponentModel.Win32Exception();
            }

            var sa = new Native.SecurityAttributes { nLength = sizeof(Native.SecurityAttributes), lpSecurityDescriptor = sd, bInheritHandle = 0 };
            var h = Native.CreateNamedPipe(@"\\.\pipe\" + pipeName,
                Native.PIPE_ACCESS_DUPLEX | Native.FILE_FLAG_FIRST_PIPE_INSTANCE,
                Native.PIPE_TYPE_BYTE | Native.PIPE_REJECT_REMOTE_CLIENTS, 1, 65536, 4096, 0, (nint)(&sa));
            Native.LocalFree(sd);
            if (h.IsInvalid)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "CreateNamedPipe");
            }

            pipeObject = h;
            listener = new Thread(() => ServeRaw(h, behaviour)) { IsBackground = true, Name = "pipe" };
            listener.Start();
        }

        ready.Set();

        var clock = Stopwatch.StartNew();
        var period = rate > 0 ? Stopwatch.Frequency / rate : 0;
        long k = 0;
        while (!_stop)
        {
            if (variant == "idle")
            {
                Thread.Sleep(20);
                continue;
            }

            if (rate > 0)
            {
                var due = (long)(k * period);
                var now = clock.ElapsedTicks;
                if (now < due)
                {
                    var ms = (int)((due - now) * 1000 / Stopwatch.Frequency);
                    if (ms > 1)
                    {
                        Thread.Sleep(ms - 1);
                    }
                    else
                    {
                        Thread.SpinWait(50);
                    }

                    continue;
                }

                k++;
            }

            var n = Interlocked.Increment(ref _n);
            var payload = Build(variant, n, region);
            var t0 = Stopwatch.GetTimestamp();
            WriteOut(variant, handle, dir, stem, payload, n);
            _writeTicks += Stopwatch.GetTimestamp() - t0;
            _writes++;
        }

        var elapsed = clock.Elapsed.TotalSeconds;
        using var self = Process.GetCurrentProcess();
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"HOLDER pid={Environment.ProcessId} variant={variant} writes={_writes} rate={_writes / elapsed:F1}/s writeMeanUs={(_writes == 0 ? 0 : _writeTicks * 1e6 / Stopwatch.Frequency / _writes):F2} duty={_writeTicks / (double)Stopwatch.Frequency / elapsed:P3} lockContention={_lockContention} renameFailures={_renameFailures} renameGaveUp={_renameGaveUp} lastRenameError={_lastRenameError} pipeServed={_pipeServed} pipeAllocPerReq={(_pipeServed == 0 ? 0 : _pipeAllocBytes / _pipeServed)} pipeServeMeanUs={(_pipeServed == 0 ? 0 : _pipeServeTicks * 1e6 / Stopwatch.Frequency / _pipeServed):F1} threads={self.Threads.Count} privateKB={self.PrivateMemorySize64 / 1024}"));
        Console.Out.Flush();
        wait.Unregister(null);
        pipeObject?.Dispose();
        return 0;
    }

    private static readonly Native.Overlapped GateTemplate = new() { Offset = 0xFFFFFFFF, OffsetHigh = 0x7FFFFFFF };

    /// <summary>The bytes one publish writes, built BEFORE the timed I/O so the duty cycle is the I/O alone.</summary>
    private static byte[] Build(string variant, long n, int region)
    {
        var json = Record.Json(n, Environment.ProcessId, _sessions, _project);
        switch (variant)
        {
            case "none":
            {
                var block = new byte[region];
                Array.Fill(block, (byte)' ');
                json.CopyTo(block, 0);
                return block;
            }

            case "crc":
            case "lock":
            {
                var block = new byte[region];
                Array.Fill(block, (byte)' ');
                Record.Framed(json).CopyTo(block, 0);
                return block;
            }

            case "seq":
            {
                var block = new byte[region - Record.SeqLength];
                Array.Fill(block, (byte)' ');
                Record.Framed(json).CopyTo(block, 0);
                return block;
            }

            case "posix":
                return Record.Framed(json);

            case "touch":
                return [];

            default:
                return [];
        }
    }

    private static void WriteOut(string variant, SafeFileHandle handle, string dir, string stem, byte[] payload, long n)
    {
        switch (variant)
        {
            case "none":
            case "crc":
                RandomAccess.Write(handle, payload, 0); // ONE WriteFile, length never changes
                break;

            case "seq":
            {
                // Writer: odd before, body, even after. Three WriteFile calls, in that order.
                var s = (ulong)n * 2;
                RandomAccess.Write(handle, Record.SeqLine(s + 1), 0);
                RandomAccess.Write(handle, payload, Record.SeqLength);
                RandomAccess.Write(handle, Record.SeqLine(s + 2), 0);
                break;
            }

            case "lock":
            {
                var o = GateTemplate;
                // Never wait on a reader: fail immediately and try again, as a server must.
                while (!Native.LockFileEx(handle, Native.LOCKFILE_EXCLUSIVE_LOCK | Native.LOCKFILE_FAIL_IMMEDIATELY, 0, 1, 0, ref o))
                {
                    _lockContention++;
                    Thread.Yield();
                    o = GateTemplate;
                }

                try
                {
                    RandomAccess.Write(handle, payload, 0);
                }
                finally
                {
                    o = GateTemplate;
                    Native.UnlockFileEx(handle, 0, 1, 0, ref o);
                }

                break;
            }

            case "touch":
            {
                // FILE_BASIC_INFO: Creation, LastAccess, LastWrite, Change (FILETIME each; 0 = leave), Attributes, pad.
                var info = stackalloc long[5];
                info[0] = 0;
                info[1] = 0;
                info[2] = Record.LastCallFor(n).ToFileTimeUtc();
                info[3] = 0;
                info[4] = 0;
                if (!Native.SetFileInformationByHandle(handle, 0 /* FileBasicInfo */, info, 40))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "SetFileInformationByHandle(FileBasicInfo)");
                }

                break;
            }

            case "posix":
            {
                var target = Path.Combine(dir, stem + ".json");
                var temp = Path.Combine(dir, stem + ".json.tmp");
                using var h = Native.CreateFile(temp, Native.GENERIC_WRITE | Native.DELETE, 0x1 | 0x4 /* FILE_SHARE_READ | FILE_SHARE_DELETE */, 0, Native.CREATE_ALWAYS, 0, 0);
                if (h.IsInvalid)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "CreateFile temp");
                }

                RandomAccess.Write(h, payload, 0);
                var attempts = 0;
                while (true)
                {
                    try
                    {
                        Rename(h, target);
                        break;
                    }
                    catch (System.ComponentModel.Win32Exception e)
                    {
                        _renameFailures++;
                        _lastRenameError = e.NativeErrorCode;
                        if (++attempts > 1000)
                        {
                            _renameGaveUp++;
                            break;
                        }

                        Thread.Sleep(1);
                    }
                }

                break;
            }
        }
    }

    // FILE_RENAME_INFO for FileRenameInfoEx (22): Flags(4) pad(4) RootDirectory(8) FileNameLength(4) FileName[]
    public static void Rename(SafeFileHandle h, string target)
    {
        var nameBytes = target.Length * 2;
        var size = 20 + nameBytes + 2;
        var buffer = stackalloc byte[size];
        new Span<byte>(buffer, size).Clear();
        *(uint*)buffer = 0x1 | 0x2; // FILE_RENAME_FLAG_REPLACE_IF_EXISTS | FILE_RENAME_FLAG_POSIX_SEMANTICS
        *(uint*)(buffer + 16) = (uint)nameBytes;
        fixed (char* p = target)
        {
            Buffer.MemoryCopy(p, buffer + 20, nameBytes, nameBytes);
        }

        if (!Native.SetFileInformationByHandle(h, 22, buffer, (uint)size))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "SetFileInformationByHandle(FileRenameInfoEx)");
        }
    }

    internal static byte[] Describe()
    {
        var n = Interlocked.Read(ref _n);
        return Record.Framed(Record.Json(n, Environment.ProcessId, _sessions, _project));
    }

    private static void ServeNetSync(NamedPipeServerStream server, string behaviour)
    {
        var buffer = new byte[256];
        var served = 0;
        while (!_stop)
        {
            try
            {
                if (behaviour == "hang-listener" && served >= 1)
                {
                    Thread.Sleep(Timeout.Infinite); // disconnected and never listening again
                }

                server.WaitForConnection();
                var t0 = Stopwatch.GetTimestamp();
                var alloc0 = GC.GetAllocatedBytesForCurrentThread();
                ReadRequest(server, buffer);
                if (behaviour == "hang-after-accept")
                {
                    Thread.Sleep(Timeout.Infinite);
                }

                var answer = Describe();
                if (behaviour == "die-mid-answer")
                {
                    server.Write(answer, 0, answer.Length / 2);
                    server.Flush();
                    Thread.Sleep(20); // let the half reach the client's side of the pipe
                    Native.TerminateProcess(Native.GetCurrentProcess(), 99);
                }

                server.Write(answer, 0, answer.Length);
                // Wait for the client to close its end; that is how the server knows the answer was read.
                while (server.Read(buffer, 0, buffer.Length) > 0)
                {
                }

                _pipeAllocBytes += GC.GetAllocatedBytesForCurrentThread() - alloc0;
                _pipeServeTicks += Stopwatch.GetTimestamp() - t0;
                _pipeServed++;
                served++;
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                server.Disconnect();
            }
            catch (InvalidOperationException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static void ReadRequest(Stream s, byte[] buffer)
    {
        var got = 0;
        while (got < buffer.Length)
        {
            var r = s.Read(buffer, got, buffer.Length - got);
            if (r == 0)
            {
                return;
            }

            got += r;
            if (Array.IndexOf(buffer, (byte)'\n', 0, got) >= 0)
            {
                return;
            }
        }
    }

    private static void ServeRaw(SafeFileHandle h, string behaviour)
    {
        var buffer = new byte[256];
        var served = 0;
        while (!_stop)
        {
            if (behaviour == "hang-listener" && served >= 1)
            {
                Thread.Sleep(Timeout.Infinite); // disconnected and never listening again
            }

            if (!Native.ConnectNamedPipe(h, 0))
            {
                var e = Marshal.GetLastPInvokeError();
                if (e != Native.ERROR_PIPE_CONNECTED)
                {
                    if (h.IsClosed)
                    {
                        return;
                    }

                    Native.DisconnectNamedPipe(h);
                    continue;
                }
            }

            var t0 = Stopwatch.GetTimestamp();
            var alloc0 = GC.GetAllocatedBytesForCurrentThread();
            fixed (byte* p = buffer)
            {
                var got = 0;
                while (got < buffer.Length && Native.ReadFile(h, p + got, buffer.Length - got, out var r, 0) && r > 0)
                {
                    got += r;
                    if (Array.IndexOf(buffer, (byte)'\n', 0, got) >= 0)
                    {
                        break;
                    }
                }
            }

            if (behaviour == "hang-after-accept")
            {
                Thread.Sleep(Timeout.Infinite);
            }

            var answer = Describe();
            fixed (byte* p = answer)
            {
                if (behaviour == "die-mid-answer")
                {
                    Native.WriteFile(h, p, answer.Length / 2, out _, 0);
                    Thread.Sleep(20);
                    Native.TerminateProcess(Native.GetCurrentProcess(), 99);
                }

                var sent = 0;
                while (sent < answer.Length && Native.WriteFile(h, p + sent, answer.Length - sent, out var w, 0))
                {
                    sent += w;
                }
            }

            fixed (byte* p = buffer)
            {
                while (Native.ReadFile(h, p, buffer.Length, out var r, 0) && r > 0)
                {
                }
            }

            _pipeAllocBytes += GC.GetAllocatedBytesForCurrentThread() - alloc0;
            _pipeServeTicks += Stopwatch.GetTimestamp() - t0;
            _pipeServed++;
            served++;
            Native.DisconnectNamedPipe(h);
        }
    }
}

internal static class AsyncServe
{
    public static async Task ServeNetAsync(NamedPipeServerStream server, string behaviour)
    {
        var buffer = new byte[256];
        while (!Holder._stop)
        {
            try
            {
                await server.WaitForConnectionAsync().ConfigureAwait(false);
                var t0 = Stopwatch.GetTimestamp();
                var alloc0 = GC.GetTotalAllocatedBytes(false);
                var got = 0;
                while (got < buffer.Length)
                {
                    var r = await server.ReadAsync(buffer.AsMemory(got)).ConfigureAwait(false);
                    if (r == 0)
                    {
                        break;
                    }

                    got += r;
                    if (Array.IndexOf(buffer, (byte)'\n', 0, got) >= 0)
                    {
                        break;
                    }
                }

                var answer = Holder.Describe();
                await server.WriteAsync(answer).ConfigureAwait(false);
                while (await server.ReadAsync(buffer).ConfigureAwait(false) > 0)
                {
                }

                Holder._pipeAllocBytes += GC.GetTotalAllocatedBytes(false) - alloc0;
                Holder._pipeServeTicks += Stopwatch.GetTimestamp() - t0;
                Holder._pipeServed++;
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                server.Disconnect();
            }
            catch (InvalidOperationException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

}
