// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch probe for 76 b, place 3: can ReOpenFile with FILE_FLAG_OVERLAPPED reopen the handle kinds a
// stdio server is handed, and what is a relay actually handed. Shows no window, starts no process.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static unsafe class Program
{
    private static string _results = "";

    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "standins";
        _results = args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "results");
        Directory.CreateDirectory(_results);

        JsonNode result = mode switch
        {
            "standins" => StandIns(),
            "relays" => Relays(args[2], args.Contains("--stdin-mode")),
            _ => throw new ArgumentException(mode),
        };

        var path = Path.Combine(_results, $"{mode}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.json");
        File.WriteAllText(path, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.Out.WriteLine(path);
        return 0;
    }

    // ------------------------------------------------------------------ describing a handle

    private static JsonObject Describe(IntPtr h)
    {
        var o = new JsonObject();
        uint ft = N.GetFileType(h);
        int ftErr = Marshal.GetLastPInvokeError();
        o["fileType"] = ft switch { 1 => "disk", 2 => "char", 3 => "pipe", 0 => $"unknown (error {ftErr})", _ => ft.ToString() };
        o["mode"] = QueryUlong(h, N.FileModeInformation, out var mode) ?? ModeText(mode);
        o["access"] = QueryUlong(h, N.FileAccessInformation, out var acc) ?? $"0x{acc:X}";
        if (ft == 3)
        {
            o["pipe"] = PipeLocal(h);
            o["name"] = Name(h);
        }

        return o;
    }

    private static string? QueryUlong(IntPtr h, int cls, out uint value)
    {
        uint v = 0;
        int st = N.NtQueryInformationFile(h, out _, (byte*)&v, 4, cls);
        value = v;
        return st == 0 ? null : $"NTSTATUS 0x{st:X8}";
    }

    private static string ModeText(uint mode)
    {
        var parts = new List<string>();
        if ((mode & 0x10) != 0) parts.Add("FILE_SYNCHRONOUS_IO_ALERT");
        if ((mode & 0x20) != 0) parts.Add("FILE_SYNCHRONOUS_IO_NONALERT");
        if ((mode & 0x30) == 0) parts.Add("asynchronous, no FILE_SYNCHRONOUS_IO flag");
        if ((mode & 0x2) != 0) parts.Add("FILE_WRITE_THROUGH");
        if ((mode & 0x4) != 0) parts.Add("FILE_SEQUENTIAL_ONLY");
        if ((mode & 0x8) != 0) parts.Add("FILE_NO_INTERMEDIATE_BUFFERING");
        return $"0x{mode:X}: " + string.Join(" | ", parts);
    }

    private static JsonNode PipeLocal(IntPtr h)
    {
        uint* b = stackalloc uint[10];
        int st = N.NtQueryInformationFile(h, out _, (byte*)b, 40, N.FilePipeLocalInformation);
        if (st != 0) return $"NTSTATUS 0x{st:X8}";
        return new JsonObject
        {
            ["type"] = b[0] == 0 ? "byte stream" : "message",
            ["configuration"] = b[1] switch { 0 => "inbound", 1 => "outbound", 2 => "full duplex", _ => b[1].ToString() },
            ["maximumInstances"] = b[2],
            ["currentInstances"] = b[3],
            ["inboundQuota"] = b[4],
            ["readDataAvailable"] = b[5],
            ["outboundQuota"] = b[6],
            ["writeQuotaAvailable"] = b[7],
            ["state"] = b[8] switch { 1 => "disconnected", 2 => "listening", 3 => "connected", 4 => "closing", _ => b[8].ToString() },
            ["end"] = b[9] == 0 ? "client end" : "server end",
        };
    }

    private static string Name(IntPtr h)
    {
        byte* buf = stackalloc byte[4 + 2048];
        if (!N.GetFileInformationByHandleEx(h, N.FileNameInfo, buf, 4 + 2048)) return $"<error {Marshal.GetLastPInvokeError()}>";
        int len = *(int*)buf;
        return len == 0 ? "(empty: an unnamed pipe)" : new string((char*)(buf + 4), 0, len / 2);
    }

    private static string Win32(int e) => $"error {e}: {new Win32Exception(e).Message}";

    private static string Nt(int st) => $"NTSTATUS 0x{st:X8} ({Win32((int)N.RtlNtStatusToDosError(st))})";

    // ------------------------------------------------------------------ reads and writes

    private readonly record struct Io(bool Ok, int Error, uint Bytes, bool TimedOut, double Ms, string Text);

    private static Io OverlappedRead(IntPtr h, int timeoutMs, long offset = 0)
    {
        var buf = new byte[256];
        var ev = N.CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
        var ov = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
        ov->OffsetLow = (int)offset;
        ov->OffsetHigh = (int)(offset >> 32);
        ov->EventHandle = ev;
        var sw = Stopwatch.StartNew();
        try
        {
            fixed (byte* p = buf)
            {
                bool ok = N.ReadFile(h, p, (uint)buf.Length, out uint n, ov);
                int err = ok ? 0 : Marshal.GetLastPInvokeError();
                if (!ok && err != 997) return new Io(false, err, 0, false, sw.Elapsed.TotalMilliseconds, "");
                if (!ok)
                {
                    if (N.WaitForSingleObject(ev, (uint)timeoutMs) != 0)
                    {
                        N.CancelIoEx(h, ov);
                        N.GetOverlappedResult(h, ov, out _, true);
                        return new Io(false, 997, 0, true, sw.Elapsed.TotalMilliseconds, "");
                    }
                }

                bool r = N.GetOverlappedResult(h, ov, out uint got, false);
                int e2 = r ? 0 : Marshal.GetLastPInvokeError();
                return new Io(r, e2, got, false, sw.Elapsed.TotalMilliseconds, Encoding.ASCII.GetString(buf, 0, (int)got));
            }
        }
        finally
        {
            NativeMemory.Free(ov);
            N.CloseHandle(ev);
        }
    }

    private static Io OverlappedWrite(IntPtr h, byte[] data, int timeoutMs, long offset = 0)
    {
        var ev = N.CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
        var ov = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
        ov->OffsetLow = (int)offset;
        ov->OffsetHigh = (int)(offset >> 32);
        ov->EventHandle = ev;
        var sw = Stopwatch.StartNew();
        try
        {
            fixed (byte* p = data)
            {
                bool ok = N.WriteFile(h, p, (uint)data.Length, out _, ov);
                int err = ok ? 0 : Marshal.GetLastPInvokeError();
                if (!ok && err != 997) return new Io(false, err, 0, false, sw.Elapsed.TotalMilliseconds, "");
                if (!ok && N.WaitForSingleObject(ev, (uint)timeoutMs) != 0)
                {
                    N.CancelIoEx(h, ov);
                    N.GetOverlappedResult(h, ov, out _, true);
                    return new Io(false, 997, 0, true, sw.Elapsed.TotalMilliseconds, "");
                }

                bool r = N.GetOverlappedResult(h, ov, out uint got, false);
                return new Io(r, r ? 0 : Marshal.GetLastPInvokeError(), got, false, sw.Elapsed.TotalMilliseconds, "");
            }
        }
        finally
        {
            NativeMemory.Free(ov);
            N.CloseHandle(ev);
        }
    }

    private static Io SyncWrite(IntPtr h, byte[] data)
    {
        var sw = Stopwatch.StartNew();
        fixed (byte* p = data)
        {
            bool ok = N.WriteFile(h, p, (uint)data.Length, out uint n, null);
            return new Io(ok, ok ? 0 : Marshal.GetLastPInvokeError(), n, false, sw.Elapsed.TotalMilliseconds, "");
        }
    }

    /// <summary>A read of a synchronous pipe end that never blocks: it waits for data with PeekNamedPipe first.</summary>
    private static Io PeekedRead(IntPtr h, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (!N.PeekNamedPipe(h, IntPtr.Zero, 0, IntPtr.Zero, out uint avail, IntPtr.Zero))
                return new Io(false, Marshal.GetLastPInvokeError(), 0, false, sw.Elapsed.TotalMilliseconds, "");
            if (avail > 0)
            {
                var buf = new byte[Math.Min(avail, 256u)];
                fixed (byte* p = buf)
                {
                    bool ok = N.ReadFile(h, p, (uint)buf.Length, out uint n, null);
                    return new Io(ok, ok ? 0 : Marshal.GetLastPInvokeError(), n, false, sw.Elapsed.TotalMilliseconds, Encoding.ASCII.GetString(buf, 0, (int)n));
                }
            }

            if (sw.ElapsedMilliseconds > timeoutMs) return new Io(false, 0, 0, true, sw.Elapsed.TotalMilliseconds, "");
            Thread.Sleep(5);
        }
    }

    private static JsonObject Show(Io io) => new()
    {
        ["ok"] = io.Ok,
        ["error"] = io.Error == 0 ? null : Win32(io.Error),
        ["bytes"] = io.Bytes,
        ["timedOut"] = io.TimedOut,
        ["ms"] = Math.Round(io.Ms, 1),
        ["text"] = io.Text.Length == 0 ? null : io.Text.Replace("\n", "\\n"),
    };

    // ------------------------------------------------------------------ the arms

    private sealed class Arm
    {
        public required string Id;
        public required string Kind;
        public required string Source;
        public required IntPtr Child;
        public IntPtr Peer;
        public bool PeerOverlapped;
        public bool ChildOverlapped;
        public bool ChildReads;
        public required uint ReopenAccess;
        public string? Win32Name;
        public string Category = "pipe";
        public string? FilePath;
        public List<IntPtr> Extra = [];
    }

    private static IntPtr _pipeFs;

    private static (IntPtr server, IntPtr client, string name) LibuvPair(bool clientOverlapped)
    {
        ulong random = ulong.MaxValue;
        while (true)
        {
            string name = $@"\\?\pipe\uv\{random}-{Environment.ProcessId}";
            IntPtr s = N.CreateNamedPipeW(name, N.PIPE_ACCESS_INBOUND | N.PIPE_ACCESS_OUTBOUND | N.FILE_FLAG_OVERLAPPED | N.WRITE_DAC | N.FILE_FLAG_FIRST_PIPE_INSTANCE,
                0, 1, 65536, 65536, 0, IntPtr.Zero);
            if (s == N.Invalid)
            {
                int e = Marshal.GetLastPInvokeError();
                if (e is 231 or 5)
                {
                    random++;
                    continue;
                }

                throw new Win32Exception(e);
            }

            IntPtr c = N.CreateFileW(name, N.GENERIC_READ | N.GENERIC_WRITE | N.WRITE_DAC, 0, IntPtr.Zero, N.OPEN_EXISTING, clientOverlapped ? N.FILE_FLAG_OVERLAPPED : 0, IntPtr.Zero);
            if (c == N.Invalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            return (s, c, name);
        }
    }

    private static (IntPtr ours, IntPtr theirs) RustPair(bool oursReadable)
    {
        if (_pipeFs == IntPtr.Zero)
        {
            const string root = @"\Device\NamedPipe\";
            fixed (char* p = root)
            {
                var us = new N.UnicodeString { Length = (ushort)(root.Length * 2), MaximumLength = (ushort)(root.Length * 2), Buffer = (IntPtr)p };
                var oa = new N.ObjectAttributes { Length = sizeof(N.ObjectAttributes), ObjectName = (IntPtr)(&us) };
                int st = N.NtOpenFile(out _pipeFs, N.SYNCHRONIZE | N.GENERIC_READ, ref oa, out _, N.FILE_SHARE_READ | N.FILE_SHARE_WRITE, N.FILE_SYNCHRONOUS_IO_NONALERT);
                if (st != 0) throw new InvalidOperationException(Nt(st));
            }
        }

        var empty = default(N.UnicodeString);
        var oa2 = new N.ObjectAttributes { Length = sizeof(N.ObjectAttributes), RootDirectory = _pipeFs, ObjectName = (IntPtr)(&empty) };
        long timeout = -50L * 10000;
        int s1 = N.NtCreateNamedPipeFile(out IntPtr ours, N.SYNCHRONIZE | (oursReadable ? N.GENERIC_READ : N.GENERIC_WRITE), ref oa2, out _,
            oursReadable ? N.FILE_SHARE_WRITE : N.FILE_SHARE_READ, N.FILE_CREATE, 0, 0, 0, 0, 1, 65536, 65536, ref timeout);
        if (s1 != 0) throw new InvalidOperationException(Nt(s1));
        var oa3 = new N.ObjectAttributes { Length = sizeof(N.ObjectAttributes), RootDirectory = ours, ObjectName = (IntPtr)(&empty) };
        int s2 = N.NtOpenFile(out IntPtr theirs, N.SYNCHRONIZE | (oursReadable ? N.GENERIC_WRITE | N.FILE_READ_ATTRIBUTES : N.GENERIC_READ), ref oa3, out _, 0,
            N.FILE_NON_DIRECTORY_FILE | N.FILE_SYNCHRONOUS_IO_NONALERT);
        if (s2 != 0) throw new InvalidOperationException(Nt(s2));
        return (ours, theirs);
    }

    private static List<Arm> MakeArms(JsonArray notMade)
    {
        var arms = new List<Arm>();

        var (s1, c1, n1) = LibuvPair(clientOverlapped: false);
        arms.Add(new Arm
        {
            Id = "A1", Kind = "Claude Code's stdin kind: a libuv named pipe, the client end, duplex, opened with no FILE_FLAG_OVERLAPPED",
            Source = "libuv uv__create_stdio_pipe_pair with Bun's flags for fds 0 to 2, UV_CREATE_PIPE | UV_READABLE_PIPE | UV_WRITABLE_PIPE",
            Child = c1, Peer = s1, PeerOverlapped = true, ChildReads = true, ReopenAccess = N.GENERIC_READ, Win32Name = n1,
        });

        var (s2, c2, n2) = LibuvPair(clientOverlapped: false);
        arms.Add(new Arm
        {
            Id = "A2", Kind = "Claude Code's stdout kind: the same pipe kind, written by the child",
            Source = "as A1",
            Child = c2, Peer = s2, PeerOverlapped = true, ChildReads = false, ReopenAccess = N.GENERIC_WRITE, Win32Name = n2,
        });

        var (s3, c3, n3) = LibuvPair(clientOverlapped: true);
        arms.Add(new Arm
        {
            Id = "A3", Kind = "Control: a libuv client end opened WITH FILE_FLAG_OVERLAPPED, which Bun does only for extra fds",
            Source = "libuv with UV_NONBLOCK_PIPE (UV_OVERLAPPED_PIPE)",
            Child = c3, Peer = s3, PeerOverlapped = true, ChildOverlapped = true, ChildReads = true, ReopenAccess = N.GENERIC_READ, Win32Name = n3,
        });

        var (o4, t4) = RustPair(oursReadable: false);
        arms.Add(new Arm
        {
            Id = "A4", Kind = "Codex's stdin kind: a Rust std child pipe, unnamed, the client end, FILE_SYNCHRONOUS_IO_NONALERT",
            Source = "rust-lang/rust library/std/src/sys/process/windows/child_pipe.rs, child_pipe(ours_readable: false)",
            Child = t4, Peer = o4, PeerOverlapped = true, ChildReads = true, ReopenAccess = N.GENERIC_READ,
        });

        var (o5, t5) = RustPair(oursReadable: true);
        arms.Add(new Arm
        {
            Id = "A5", Kind = "Codex's stdout kind: a Rust std child pipe, unnamed, the client end, FILE_SYNCHRONOUS_IO_NONALERT",
            Source = "child_pipe(ours_readable: true)",
            Child = t5, Peer = o5, PeerOverlapped = true, ChildReads = false, ReopenAccess = N.GENERIC_WRITE,
        });

        if (!N.CreatePipe(out var r6, out var w6, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        arms.Add(new Arm
        {
            Id = "A6", Kind = "CreatePipe's read end, a child's stdin under .NET's Process or a shell pipeline",
            Source = "kernel32 CreatePipe",
            Child = r6, Peer = w6, PeerOverlapped = false, ChildReads = true, ReopenAccess = N.GENERIC_READ,
        });

        if (!N.CreatePipe(out var r7, out var w7, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        arms.Add(new Arm
        {
            Id = "A7", Kind = "CreatePipe's write end, a child's stdout under the same",
            Source = "kernel32 CreatePipe",
            Child = w7, Peer = r7, PeerOverlapped = false, ChildReads = false, ReopenAccess = N.GENERIC_WRITE,
        });

        var files = Path.Combine(_results, "files");
        Directory.CreateDirectory(files);
        var inFile = Path.Combine(files, "stdin.txt");
        File.WriteAllText(inFile, "file-content\n");
        IntPtr f8 = N.CreateFileW(inFile, N.GENERIC_READ, N.FILE_SHARE_READ | N.FILE_SHARE_WRITE, IntPtr.Zero, N.OPEN_EXISTING, 0, IntPtr.Zero);
        arms.Add(new Arm
        {
            Id = "A8", Kind = "A disk file opened synchronously for reading, shared for reading and writing: stdin redirected from a file",
            Source = "CreateFileW, no flags", Child = f8, ChildReads = true, ReopenAccess = N.GENERIC_READ, Category = "file", FilePath = inFile,
        });

        var outFile9 = Path.Combine(files, "stdout-shared.txt");
        IntPtr f9 = N.CreateFileW(outFile9, N.GENERIC_WRITE, N.FILE_SHARE_READ | N.FILE_SHARE_WRITE, IntPtr.Zero, N.CREATE_ALWAYS, 0, IntPtr.Zero);
        arms.Add(new Arm
        {
            Id = "A9", Kind = "A disk file opened synchronously for writing, shared for reading and writing: cmd's > redirect",
            Source = "CreateFileW, no flags", Child = f9, ChildReads = false, ReopenAccess = N.GENERIC_WRITE, Category = "file", FilePath = outFile9,
        });

        var outFile10 = Path.Combine(files, "stdout-readshare.txt");
        IntPtr f10 = N.CreateFileW(outFile10, N.GENERIC_WRITE, N.FILE_SHARE_READ, IntPtr.Zero, N.CREATE_ALWAYS, 0, IntPtr.Zero);
        arms.Add(new Arm
        {
            Id = "A10", Kind = "A disk file opened synchronously for writing, shared for reading only",
            Source = "CreateFileW, no flags", Child = f10, ChildReads = false, ReopenAccess = N.GENERIC_WRITE, Category = "file", FilePath = outFile10,
        });

        IntPtr ci = N.CreateFileW("CONIN$", N.GENERIC_READ | N.GENERIC_WRITE, N.FILE_SHARE_READ | N.FILE_SHARE_WRITE, IntPtr.Zero, N.OPEN_EXISTING, 0, IntPtr.Zero);
        if (ci == N.Invalid) notMade.Add($"A11 CONIN$: {Win32(Marshal.GetLastPInvokeError())}");
        else arms.Add(new Arm { Id = "A11", Kind = "The console's input, CONIN$: a person running the server in a terminal", Source = "CreateFileW", Child = ci, ChildReads = true, ReopenAccess = N.GENERIC_READ, Category = "console" });

        IntPtr co = N.CreateFileW("CONOUT$", N.GENERIC_READ | N.GENERIC_WRITE, N.FILE_SHARE_READ | N.FILE_SHARE_WRITE, IntPtr.Zero, N.OPEN_EXISTING, 0, IntPtr.Zero);
        if (co == N.Invalid) notMade.Add($"A12 CONOUT$: {Win32(Marshal.GetLastPInvokeError())}");
        else arms.Add(new Arm { Id = "A12", Kind = "The console's output, CONOUT$", Source = "CreateFileW", Child = co, ChildReads = false, ReopenAccess = N.GENERIC_WRITE, Category = "console" });

        IntPtr nul = N.CreateFileW("NUL", N.GENERIC_READ, N.FILE_SHARE_READ | N.FILE_SHARE_WRITE, IntPtr.Zero, N.OPEN_EXISTING, 0, IntPtr.Zero);
        arms.Add(new Arm { Id = "A13", Kind = "NUL: a client that hands no input", Source = "CreateFileW", Child = nul, ChildReads = true, ReopenAccess = N.GENERIC_READ, Category = "nul" });

        return arms;
    }

    private static readonly byte[] Payload = "probe-roundtrip\n"u8.ToArray();

    private static Io PeerWrite(Arm arm) => arm.PeerOverlapped ? OverlappedWrite(arm.Peer, Payload, 3000) : SyncWrite(arm.Peer, Payload);

    private static Io PeerRead(Arm arm) => arm.PeerOverlapped ? OverlappedRead(arm.Peer, 3000) : PeekedRead(arm.Peer, 3000);

    private static JsonNode RoundTrip(Arm arm, IntPtr r, bool overlapped)
    {
        switch (arm.Category)
        {
            case "pipe" when arm.ChildReads:
            {
                var w = PeerWrite(arm);
                var got = overlapped ? OverlappedRead(r, 3000) : PeekedRead(r, 3000);
                return new JsonObject { ["peerWrote"] = Show(w), ["reopenedRead"] = Show(got), ["sameConnection"] = got.Text == "probe-roundtrip\n" };
            }
            case "pipe":
            {
                var w = overlapped ? OverlappedWrite(r, Payload, 3000) : SyncWrite(r, Payload);
                var got = PeerRead(arm);
                return new JsonObject { ["reopenedWrote"] = Show(w), ["peerRead"] = Show(got), ["sameConnection"] = got.Text == "probe-roundtrip\n" };
            }
            case "file" when arm.ChildReads:
            {
                var got = overlapped ? OverlappedRead(r, 3000, offset: 0) : PeekedFileRead(r);
                return new JsonObject { ["reopenedRead"] = Show(got) };
            }
            case "file":
            {
                var w = overlapped ? OverlappedWrite(r, Payload, 3000, offset: 0) : SyncWrite(r, Payload);
                return new JsonObject { ["reopenedWrote"] = Show(w) };
            }
            case "nul":
            {
                var got = overlapped ? OverlappedRead(r, 3000) : PeekedFileRead(r);
                return new JsonObject { ["reopenedRead"] = Show(got) };
            }
            default:
                return "not attempted: a console read waits for a key, and a console write goes to a screen buffer";
        }
    }

    private static Io PeekedFileRead(IntPtr h)
    {
        var buf = new byte[256];
        var sw = Stopwatch.StartNew();
        fixed (byte* p = buf)
        {
            bool ok = N.ReadFile(h, p, (uint)buf.Length, out uint n, null);
            return new Io(ok, ok ? 0 : Marshal.GetLastPInvokeError(), n, false, sw.Elapsed.TotalMilliseconds, Encoding.ASCII.GetString(buf, 0, (int)n));
        }
    }

    private static JsonNode OriginalStillWorks(Arm arm)
    {
        if (arm.Category != "pipe") return "not checked";
        if (arm.ChildReads)
        {
            var w = PeerWrite(arm);
            var got = arm.ChildOverlapped ? OverlappedRead(arm.Child, 3000) : PeekedRead(arm.Child, 3000);
            return new JsonObject { ["peerWrote"] = Show(w), ["originalRead"] = Show(got) };
        }
        else
        {
            var w = arm.ChildOverlapped ? OverlappedWrite(arm.Child, Payload, 3000) : SyncWrite(arm.Child, Payload);
            var got = PeerRead(arm);
            return new JsonObject { ["originalWrote"] = Show(w), ["peerRead"] = Show(got) };
        }
    }

    private static JsonObject TryReopen(Arm arm, uint share, uint flags)
    {
        var o = new JsonObject { ["access"] = $"0x{arm.ReopenAccess:X}", ["share"] = share, ["flags"] = $"0x{flags:X}" };
        IntPtr r = N.ReOpenFile(arm.Child, arm.ReopenAccess, share, flags);
        if (r == N.Invalid || r == IntPtr.Zero)
        {
            o["result"] = "failed, " + Win32(Marshal.GetLastPInvokeError());
            return o;
        }

        o["result"] = "a new handle";
        o["reopened"] = Describe(r);
        o["roundTrip"] = RoundTrip(arm, r, overlapped: (flags & N.FILE_FLAG_OVERLAPPED) != 0);
        N.CloseHandle(r);
        o["originalAfterTheNewHandleClosed"] = OriginalStillWorks(arm);
        return o;
    }

    /// <summary>Runs a call on a thread of its own and says whether it returned within the time.</summary>
    private static JsonObject Guarded(string what, Func<string> call, int timeoutMs, List<Thread> stuck)
    {
        string? result = null;
        var sw = Stopwatch.StartNew();
        double doneAt = -1;
        var t = new Thread(() =>
        {
            result = call();
            doneAt = sw.Elapsed.TotalMilliseconds;
        }) { IsBackground = true };
        t.Start();
        bool done = t.Join(timeoutMs);
        if (!done) stuck.Add(t);
        return new JsonObject { ["call"] = what, ["returnedWithin"] = done ? $"{Math.Round(doneAt, 1)} ms" : $"still waiting after {timeoutMs} ms", ["result"] = done ? result : null };
    }

    private static JsonObject PendingReadArms(Arm arm)
    {
        var o = new JsonObject();

        // 1. Queries made while another thread is parked in a synchronous read of the handle.
        {
            Io parked = default;
            var t = new Thread(() =>
            {
                var buf = new byte[256];
                var sw = Stopwatch.StartNew();
                fixed (byte* p = buf)
                {
                    bool ok = N.ReadFile(arm.Child, p, (uint)buf.Length, out uint n, null);
                    parked = new Io(ok, ok ? 0 : Marshal.GetLastPInvokeError(), n, false, sw.Elapsed.TotalMilliseconds, Encoding.ASCII.GetString(buf, 0, (int)n));
                }
            }) { IsBackground = true };
            t.Start();
            Thread.Sleep(300);
            var stuck = new List<Thread>();
            var calls = new JsonArray
            {
                Guarded("NtQueryInformationFile FileModeInformation", () => QueryUlong(arm.Child, N.FileModeInformation, out var m) ?? ModeText(m), 1500, stuck),
                Guarded("NtQueryInformationFile FileAccessInformation", () => QueryUlong(arm.Child, N.FileAccessInformation, out var a) ?? $"0x{a:X}", 1500, stuck),
                Guarded("GetFileType", () => N.GetFileType(arm.Child).ToString(), 1500, stuck),
                Guarded("NtQueryInformationFile FilePipeLocalInformation", () => PipeLocal(arm.Child).ToJsonString(), 1500, stuck),
                Guarded("PeekNamedPipe", () => N.PeekNamedPipe(arm.Child, IntPtr.Zero, 0, IntPtr.Zero, out uint av, IntPtr.Zero) ? $"ok, {av} bytes available" : Win32(Marshal.GetLastPInvokeError()), 1500, stuck),
                Guarded("NtQueryObject ObjectBasicInformation", () => ObjectBasic(arm.Child), 1500, stuck),
            };
            var w = PeerWrite(arm);
            bool readDone = t.Join(3000);
            bool allReturned = stuck.All(s => s.Join(3000));
            o["queriesWhileASynchronousReadIsPending"] = new JsonObject
            {
                ["calls"] = calls,
                ["peerThenWrote"] = Show(w),
                ["theParkedRead"] = readDone ? Show(parked) : "still parked",
                ["theWaitingQueriesReturnedOnceTheReadCompleted"] = allReturned,
            };
            // Whatever the waiting queries' reads took is now in the pipe or consumed; drain what is left.
            _ = PeekedRead(arm.Child, 200);
        }

        // 2. An OVERLAPPED passed to ReadFile on the synchronous handle.
        {
            Io got = default;
            var t = new Thread(() => got = OverlappedRead(arm.Child, 5000)) { IsBackground = true };
            var sw = Stopwatch.StartNew();
            t.Start();
            Thread.Sleep(500);
            bool returnedBeforeData = !t.IsAlive;
            var w = PeerWrite(arm);
            t.Join(5000);
            o["anOverlappedPassedOnTheSynchronousHandle"] = new JsonObject
            {
                ["returnedBeforeAnyDataWasWritten"] = returnedBeforeData,
                ["peerWroteAt"] = "500 ms",
                ["read"] = Show(got),
            };
        }

        // 3. CancelSynchronousIo on a thread parked in the read.
        {
            Io parked = default;
            uint tid = 0;
            var t = new Thread(() =>
            {
                tid = N.GetCurrentThreadId();
                var buf = new byte[256];
                var sw = Stopwatch.StartNew();
                fixed (byte* p = buf)
                {
                    bool ok = N.ReadFile(arm.Child, p, (uint)buf.Length, out uint n, null);
                    parked = new Io(ok, ok ? 0 : Marshal.GetLastPInvokeError(), n, false, sw.Elapsed.TotalMilliseconds, "");
                }
            }) { IsBackground = true };
            t.Start();
            Thread.Sleep(300);
            IntPtr th = N.OpenThread(0x0001, false, tid);
            bool cancelled = N.CancelSynchronousIo(th);
            int cancelError = cancelled ? 0 : Marshal.GetLastPInvokeError();
            N.CloseHandle(th);
            bool ended = t.Join(3000);
            o["cancelSynchronousIoOnTheParkedReader"] = new JsonObject
            {
                ["cancelSynchronousIo"] = cancelled ? "TRUE" : "FALSE, " + Win32(cancelError),
                ["theReadEnded"] = ended,
                ["theRead"] = ended ? Show(parked) : null,
            };
            if (!ended)
            {
                _ = PeerWrite(arm);
                t.Join(3000);
            }
        }

        return o;
    }

    private static JsonObject PendingOverlappedQueries(Arm arm)
    {
        Io got = default;
        var t = new Thread(() => got = OverlappedRead(arm.Child, 10000)) { IsBackground = true };
        t.Start();
        Thread.Sleep(300);
        var stuck = new List<Thread>();
        var calls = new JsonArray
        {
            Guarded("NtQueryInformationFile FileModeInformation", () => QueryUlong(arm.Child, N.FileModeInformation, out var m) ?? ModeText(m), 1500, stuck),
            Guarded("NtQueryInformationFile FilePipeLocalInformation", () => PipeLocal(arm.Child).ToJsonString(), 1500, stuck),
            Guarded("PeekNamedPipe", () => N.PeekNamedPipe(arm.Child, IntPtr.Zero, 0, IntPtr.Zero, out uint av, IntPtr.Zero) ? $"ok, {av} bytes available" : Win32(Marshal.GetLastPInvokeError()), 1500, stuck),
        };
        var w = PeerWrite(arm);
        t.Join(5000);
        stuck.ForEach(s => s.Join(3000));
        return new JsonObject { ["calls"] = calls, ["peerThenWrote"] = Show(w), ["thePendingRead"] = Show(got) };
    }

    private static string ObjectBasic(IntPtr h)
    {
        uint* b = stackalloc uint[14];
        int st = N.NtQueryObject(h, 0, b, 56, out _);
        return st == 0 ? $"granted access 0x{b[1]:X}, handle count {b[2]}" : $"NTSTATUS 0x{st:X8}";
    }

    private static JsonNode StandIns()
    {
        var root = new JsonObject
        {
            ["what"] = "ReOpenFile with FILE_FLAG_OVERLAPPED over the handle kinds a stdio server is handed, each made the way its source makes it",
            ["os"] = Environment.OSVersion.VersionString,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["when"] = DateTime.UtcNow.ToString("O"),
            ["pid"] = Environment.ProcessId,
        };
        var notMade = new JsonArray();
        var arms = MakeArms(notMade);
        root["notMade"] = notMade;
        var results = new JsonArray();
        foreach (var arm in arms)
        {
            var o = new JsonObject { ["id"] = arm.Id, ["kind"] = arm.Kind, ["source"] = arm.Source, ["childUses"] = arm.ChildReads ? "reads it, as stdin" : "writes it, as stdout" };
            o["described"] = Describe(arm.Child);
            if (arm.Peer != IntPtr.Zero) o["peer"] = Describe(arm.Peer);
            o["reopenOverlappedSharedReadWrite"] = TryReopen(arm, N.FILE_SHARE_READ | N.FILE_SHARE_WRITE, N.FILE_FLAG_OVERLAPPED);
            o["reopenOverlappedUnshared"] = TryReopen(arm, 0, N.FILE_FLAG_OVERLAPPED);
            o["reopenSynchronousSharedReadWrite"] = TryReopen(arm, N.FILE_SHARE_READ | N.FILE_SHARE_WRITE, 0);
            if (arm.Win32Name is { } name)
            {
                IntPtr byName = N.CreateFileW(name, arm.ReopenAccess, 0, IntPtr.Zero, N.OPEN_EXISTING, N.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                o["openTheNamedPipeAgainByItsNameOverlapped"] = byName == N.Invalid ? "failed, " + Win32(Marshal.GetLastPInvokeError()) : "a new handle";
                if (byName != N.Invalid) N.CloseHandle(byName);
            }

            if (arm.Category == "pipe" && arm.ChildReads && !arm.ChildOverlapped) o["withAReadPending"] = PendingReadArms(arm);
            if (arm.Category == "pipe" && arm.ChildReads && arm.ChildOverlapped)
            {
                var w = PeerWrite(arm);
                o["controlOverlappedReadOnTheHandleAsHanded"] = new JsonObject { ["peerWrote"] = Show(w), ["read"] = Show(OverlappedRead(arm.Child, 3000)) };
                o["controlQueriesWhileAnOverlappedReadIsPending"] = PendingOverlappedQueries(arm);
            }

            IntPtr port = N.CreateIoCompletionPort(arm.Child, IntPtr.Zero, UIntPtr.Zero, 0);
            o["bindToACompletionPort"] = port == IntPtr.Zero ? "failed, " + Win32(Marshal.GetLastPInvokeError()) : "bound";
            if (port != IntPtr.Zero) N.CloseHandle(port);

            uint zero = 0;
            int st = N.NtSetInformationFile(arm.Child, out _, (byte*)&zero, 4, N.FileModeInformation);
            o["clearTheSynchronousModeInPlace"] = st == 0 ? "succeeded" : Nt(st);
            o["modeAfterwards"] = QueryUlong(arm.Child, N.FileModeInformation, out var after) ?? ModeText(after);

            results.Add(o);
            N.CloseHandle(arm.Child);
            if (arm.Peer != IntPtr.Zero) N.CloseHandle(arm.Peer);
            Console.Out.WriteLine($"{arm.Id} done");
        }

        root["arms"] = results;
        return root;
    }

    // ------------------------------------------------------------------ reading real relays, by reading only

    private const uint QueryLimited = 0x1000, VmRead = 0x0010, DupHandle = 0x0040;

    private static string Image(IntPtr h)
    {
        char* buf = stackalloc char[1024];
        uint size = 1024;
        return N.QueryFullProcessImageNameW(h, 0, buf, ref size) ? new string(buf, 0, (int)size) : $"<error {Marshal.GetLastPInvokeError()}>";
    }

    private static uint ParentOf(IntPtr h)
    {
        var pbi = default(N.ProcessBasicInformation);
        int st = N.NtQueryInformationProcess(h, 0, &pbi, sizeof(N.ProcessBasicInformation), out _);
        return st == 0 ? (uint)pbi.InheritedFromUniqueProcessId : 0;
    }

    private static long Created(IntPtr h) => N.GetProcessTimes(h, out long c, out _, out _, out _) ? c : 0;

    private static JsonNode Relays(string installedExe, bool queryStdinMode)
    {
        var root = new JsonObject { ["when"] = DateTime.UtcNow.ToString("O"), ["owned"] = installedExe, ["stdinFileQueries"] = queryStdinMode };

        var ancestors = new JsonArray();
        var ancestorPids = new HashSet<uint>();
        uint pid = (uint)Environment.ProcessId;
        long childCreated = long.MaxValue;
        for (int i = 0; i < 24 && pid != 0; i++)
        {
            IntPtr h = N.OpenProcess(QueryLimited, false, pid);
            if (h == IntPtr.Zero) break;
            long created = Created(h);
            if (created > childCreated)
            {
                N.CloseHandle(h);
                break;
            }

            ancestors.Add(new JsonObject { ["pid"] = pid, ["image"] = Image(h) });
            ancestorPids.Add(pid);
            uint ppid = ParentOf(h);
            N.CloseHandle(h);
            childCreated = created;
            pid = ppid;
        }

        root["ancestors"] = ancestors;

        var found = new JsonArray();
        foreach (var p in Process.GetProcesses())
        {
            uint id = (uint)p.Id;
            p.Dispose();
            IntPtr q = N.OpenProcess(QueryLimited, false, id);
            if (q == IntPtr.Zero) continue;
            string image = Image(q);
            if (!string.Equals(image, installedExe, StringComparison.OrdinalIgnoreCase))
            {
                N.CloseHandle(q);
                continue;
            }

            var o = new JsonObject { ["pid"] = id, ["created"] = DateTime.FromFileTimeUtc(Created(q)).ToString("O") };
            uint ppid = ParentOf(q);
            long created = Created(q);
            N.CloseHandle(q);
            o["parentPid"] = ppid;
            o["parentIsAnAncestorOfThisProbe"] = ancestorPids.Contains(ppid);
            IntPtr pq = N.OpenProcess(QueryLimited, false, ppid);
            if (pq != IntPtr.Zero)
            {
                o["parentImage"] = Created(pq) <= created ? Image(pq) : "(that pid was reused by a later process)";
                N.CloseHandle(pq);
            }
            else
            {
                o["parentImage"] = "(gone or not openable)";
            }

            IntPtr h = N.OpenProcess(QueryLimited | VmRead | DupHandle, false, id);
            if (h == IntPtr.Zero)
            {
                o["read"] = "failed to open, " + Win32(Marshal.GetLastPInvokeError());
                found.Add(o);
                continue;
            }

            try
            {
                var pbi = default(N.ProcessBasicInformation);
                N.NtQueryInformationProcess(h, 0, &pbi, sizeof(N.ProcessBasicInformation), out _);
                IntPtr pp;
                N.ReadProcessMemory(h, pbi.PebBaseAddress + 0x20, &pp, 8, out _);
                IntPtr* std = stackalloc IntPtr[3];
                N.ReadProcessMemory(h, pp + 0x20, std, 24, out _);
                o["stdin"] = Remote(h, std[0], fileQueries: queryStdinMode, stdin: IntPtr.Zero);
                IntPtr stdinLocal = IntPtr.Zero;
                if (std[0] != IntPtr.Zero && std[0] != N.Invalid) N.DuplicateHandle(h, std[0], N.GetCurrentProcess(), out stdinLocal, 0, false, 2);
                o["stdout"] = Remote(h, std[1], fileQueries: true, stdin: stdinLocal);
                o["stderr"] = Remote(h, std[2], fileQueries: true, stdin: stdinLocal);
                if (stdinLocal != IntPtr.Zero) N.CloseHandle(stdinLocal);
            }
            finally
            {
                N.CloseHandle(h);
            }

            found.Add(o);
        }

        root["processesAtTheOwnedPath"] = found;
        return root;
    }

    [DllImport("kernelbase.dll")]
    private static extern bool CompareObjectHandles(IntPtr a, IntPtr b);

    private static JsonObject Remote(IntPtr proc, IntPtr remote, bool fileQueries, IntPtr stdin)
    {
        var o = new JsonObject { ["value"] = $"0x{remote.ToInt64():X}" };
        if (remote == IntPtr.Zero || remote == N.Invalid)
        {
            o["kind"] = "none";
            return o;
        }

        // DUPLICATE_SAME_ACCESS only: never DUPLICATE_CLOSE_SOURCE.
        if (!N.DuplicateHandle(proc, remote, N.GetCurrentProcess(), out IntPtr local, 0, false, 2))
        {
            o["duplicate"] = "failed, " + Win32(Marshal.GetLastPInvokeError());
            return o;
        }

        o["object"] = ObjectBasic(local);
        if (stdin != IntPtr.Zero && CompareObjectHandles(stdin, local))
        {
            // The same file object as stdin, whose reading thread holds its lock: answer only what does not wait.
            o["sameObjectAsStdin"] = true;
            fileQueries = false;
        }

        if (!fileQueries)
        {
            // Only the calls measured to answer at once while a synchronous read is parked on the handle.
            uint ft = N.GetFileType(local);
            o["fileType"] = ft switch { 1 => "disk", 2 => "char", 3 => "pipe", _ => ft.ToString() };
            o["access"] = QueryUlong(local, N.FileAccessInformation, out var acc) ?? $"0x{acc:X}";
            o["file"] = "no call that waits behind a parked synchronous read was made";
            N.CloseHandle(local);
            return o;
        }

        var stuck = new List<Thread>();
        var d = Guarded("describe", () => Describe(local).ToJsonString(), 3000, stuck);
        o["file"] = d;
        if (stuck.Count == 0) N.CloseHandle(local);
        return o;
    }
}
