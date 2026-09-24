// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review scratch prototype, 2026-09-24. Not product code.
// The coordinator side: reads what the stand-in servers publish, and measures it.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static unsafe class Bench
{
    public static string Self => Environment.ProcessPath!;

    // ---------------------------------------------------------------- holders
    public sealed class Holder : IDisposable
    {
        public required Process Process { get; init; }
        public required EventWaitHandle Stop { get; init; }
        public required string Marker { get; init; }
        public required string Stem { get; init; }
        public string PipeName => "IpcReview-" + Stem;
        public string Output { get; private set; } = "";
        private bool _stopped;

        public string StopAndCollect(int timeoutMs = 20000)
        {
            if (_stopped)
            {
                return Output;
            }

            _stopped = true;
            Stop.Set();
            var read = Process.StandardOutput.ReadToEndAsync();
            if (!Process.WaitForExit(timeoutMs))
            {
                Process.Kill(); // only ever a process this bench started
            }

            Process.WaitForExit();
            Output = read.Wait(5000) ? read.Result.Trim() : "<no output>";
            return Output;
        }

        public void Dispose()
        {
            StopAndCollect();
            Process.Dispose();
            Stop.Dispose();
        }
    }

    public static Holder StartHolder(string dir, string variant, double rate, int sessions, string pipe, string behaviour, string? cwd = null)
    {
        Directory.CreateDirectory(dir);
        var token = Guid.NewGuid().ToString("N");
        var readyName = @"Local\IpcReview-ready-" + token;
        var stopName = @"Local\IpcReview-stop-" + token;
        var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        var stop = new EventWaitHandle(false, EventResetMode.ManualReset, stopName);
        var psi = new ProcessStartInfo(Self)
        {
            UseShellExecute = false,
            CreateNoWindow = true, // the house rule: every launch suppresses the console window
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            WorkingDirectory = cwd ?? dir,
        };
        foreach (var arg in new[] { "holder", dir, variant, rate.ToString(CultureInfo.InvariantCulture), sessions.ToString(CultureInfo.InvariantCulture), pipe, behaviour, readyName, stopName })
        {
            psi.ArgumentList.Add(arg);
        }

        var p = Process.Start(psi)!;
        if (!ready.WaitOne(30000))
        {
            p.Kill();
            throw new TimeoutException("holder did not become ready");
        }

        ready.Dispose();
        var marker = Directory.GetFiles(dir, p.Id.ToString(CultureInfo.InvariantCulture) + "-*.live").Single();
        return new Holder { Process = p, Stop = stop, Marker = marker, Stem = Path.GetFileNameWithoutExtension(marker) };
    }

    // ---------------------------------------------------------------- readers
    public enum Outcome { Ok, Detected, WrongAccepted, CaughtByParser, Empty }

    private static readonly Native.Overlapped GateTemplate = new() { Offset = 0xFFFFFFFF, OffsetHigh = 0x7FFFFFFF };

    public static Outcome ReadOnce(string variant, string marker, byte[] buffer, SafeFileHandle? keep = null)
    {
        SafeFileHandle Open() => keep ?? File.OpenHandle(marker, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        void Done(SafeFileHandle h)
        {
            if (!ReferenceEquals(h, keep))
            {
                h.Dispose();
            }
        }

        switch (variant)
        {
            case "none":
            {
                int got;
                var h = Open();
                try
                {
                    got = RandomAccess.Read(h, buffer, 0);
                }
                finally
                {
                    Done(h);
                }

                var json = Record.TrimPadding(buffer.AsSpan(0, got));
                if (json.Length == 0)
                {
                    return Outcome.Empty;
                }

                return Record.Check(json, out _) switch
                {
                    Record.Consistency.Whole => Outcome.Ok,
                    Record.Consistency.Mixed => Outcome.WrongAccepted,
                    _ => Outcome.CaughtByParser,
                };
            }

            case "crc":
            {
                int got;
                var h = Open();
                try
                {
                    got = RandomAccess.Read(h, buffer, 0);
                }
                finally
                {
                    Done(h);
                }

                if (got == 0)
                {
                    return Outcome.Empty;
                }

                if (!Record.TryUnframe(buffer.AsSpan(0, got), out var json, out var crcOk) || !crcOk)
                {
                    return Outcome.Detected;
                }

                return Record.Check(json, out _) == Record.Consistency.Whole ? Outcome.Ok : Outcome.WrongAccepted;
            }

            case "seq":
            {
                var h = Open();
                using var release = new Releaser(() => Done(h));
                var first = new byte[Record.SeqLength];
                var second = new byte[Record.SeqLength];
                if (RandomAccess.Read(h, first, 0) != Record.SeqLength || !Record.TryParseSeq(first, out var s1) || (s1 & 1) != 0)
                {
                    return Outcome.Detected; // a write is in progress
                }

                var got = RandomAccess.Read(h, buffer, Record.SeqLength);
                if (RandomAccess.Read(h, second, 0) != Record.SeqLength || !Record.TryParseSeq(second, out var s2) || s2 != s1)
                {
                    return Outcome.Detected; // a write started while we read
                }

                // Accepted by the protocol. Audit it with the crc the protocol never looked at.
                if (!Record.TryUnframe(buffer.AsSpan(0, got), out var json, out var crcOk) || !crcOk)
                {
                    return Outcome.WrongAccepted;
                }

                return Record.Check(json, out _) == Record.Consistency.Whole ? Outcome.Ok : Outcome.WrongAccepted;
            }

            case "lock":
            {
                var h = Open();
                using var release = new Releaser(() => Done(h));
                var o = GateTemplate;
                if (!Native.LockFileEx(h, Native.LOCKFILE_FAIL_IMMEDIATELY, 0, 1, 0, ref o)) // shared
                {
                    return Outcome.Detected; // the writer holds the gate
                }

                int got;
                try
                {
                    got = RandomAccess.Read(h, buffer, 0);
                }
                finally
                {
                    o = GateTemplate;
                    Native.UnlockFileEx(h, 0, 1, 0, ref o);
                }

                if (!Record.TryUnframe(buffer.AsSpan(0, got), out var json, out var crcOk) || !crcOk)
                {
                    return Outcome.WrongAccepted;
                }

                return Record.Check(json, out _) == Record.Consistency.Whole ? Outcome.Ok : Outcome.WrongAccepted;
            }

            case "posix":
            {
                var sidecar = Path.ChangeExtension(marker, ".json");
                int got;
                try
                {
                    using var h = File.OpenHandle(sidecar, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    got = RandomAccess.Read(h, buffer, 0);
                }
                catch (FileNotFoundException)
                {
                    return Outcome.Detected;
                }
                catch (IOException)
                {
                    return Outcome.Detected; // sharing violation against the writer's still-open handle
                }

                if (!Record.TryUnframe(buffer.AsSpan(0, got), out var json, out var crcOk) || !crcOk)
                {
                    return Outcome.WrongAccepted;
                }

                return Record.Check(json, out _) == Record.Consistency.Whole ? Outcome.Ok : Outcome.WrongAccepted;
            }

            default:
                throw new ArgumentException(variant);
        }
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    // ---------------------------------------------------------------- torn reads
    // torn <variant> <writesPerSecond> <reads> <secondsCap>
    public static int Torn(string[] a)
    {
        var variant = a[1];
        var rate = double.Parse(a[2], CultureInfo.InvariantCulture);
        var reads = int.Parse(a[3], CultureInfo.InvariantCulture);
        var cap = double.Parse(a[4], CultureInfo.InvariantCulture);
        var persistent = a.Length > 5 && a[5] == "persistent";
        var dir = Path.Combine(RunRoot, "torn-" + variant + "-" + Guid.NewGuid().ToString("N")[..8]);
        using var holder = StartHolder(dir, variant, rate, 3, "none", "normal");
        var buffer = new byte[65536];
        var counts = new long[5];
        var ticks = new List<long>(reads);
        var clock = Stopwatch.StartNew();
        var capTicks = (long)(cap * Stopwatch.Frequency);
        var done = 0;
        using var keep = persistent && variant != "posix"
            ? File.OpenHandle(holder.Marker, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
            : null;
        while (done < reads && clock.ElapsedTicks < capTicks)
        {
            var t0 = Stopwatch.GetTimestamp();
            var outcome = ReadOnce(variant, holder.Marker, buffer, keep);
            ticks.Add(Stopwatch.GetTimestamp() - t0);
            counts[(int)outcome]++;
            done++;
        }

        var elapsed = clock.Elapsed.TotalSeconds;
        var h = holder.StopAndCollect();
        ticks.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"TORN variant={variant} handle={(keep is null ? "reopen" : "persistent")} writeRate={(rate == 0 ? "max" : rate.ToString(CultureInfo.InvariantCulture))} reads={done} in {elapsed:F1}s ok={counts[0]} detected={counts[1]} WRONG_ACCEPTED={counts[2]} caughtByParser={counts[3]} empty={counts[4]} readUs p50={Us(ticks, 0.50):F1} p99={Us(ticks, 0.99):F1} max={Us(ticks, 1.0):F1}"));
        Console.WriteLine("  " + h);
        TryDeleteTree(dir);
        return 0;
    }


    // ---------------------------------------------------------------- open+read+close latency
    // readlat <variant> <writesPerSecond> <reads> <intervalMs>
    public static int ReadLat(string[] a)
    {
        var variant = a[1];
        var rate = double.Parse(a[2], CultureInfo.InvariantCulture);
        var reads = int.Parse(a[3], CultureInfo.InvariantCulture);
        var interval = int.Parse(a[4], CultureInfo.InvariantCulture);
        var withProbe = !(a.Length > 5 && a[5] == "noprobe");
        var dir = Path.Combine(RunRoot, "readlat-" + Guid.NewGuid().ToString("N")[..8]);
        using var holder = StartHolder(dir, variant, rate, 3, "none", "normal");
        var buffer = new byte[65536];
        var open = new List<long>();
        var read = new List<long>();
        var close = new List<long>();
        var probe = new List<long>();
        var attr = new List<long>();
        var touchedSeen = 0;
        for (var i = 0; i < reads; i++)
        {
            if (variant == "touch")
            {
                var a0 = Stopwatch.GetTimestamp();
                var lastWrite = File.GetLastWriteTimeUtc(holder.Marker);
                attr.Add(Stopwatch.GetTimestamp() - a0);
                if (lastWrite >= Record.Epoch && lastWrite < Record.Epoch.AddDays(1))
                {
                    touchedSeen++;
                }
            }

            var t0 = Stopwatch.GetTimestamp();
            if (withProbe)
            {
                try
                {
                    using var p = new FileStream(holder.Marker, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
                }
                catch (IOException)
                {
                }
            }

            var t1 = Stopwatch.GetTimestamp();
            var h = File.OpenHandle(holder.Marker, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var t2 = Stopwatch.GetTimestamp();
            RandomAccess.Read(h, buffer, 0);
            var t3 = Stopwatch.GetTimestamp();
            h.Dispose();
            var t4 = Stopwatch.GetTimestamp();
            probe.Add(t1 - t0);
            open.Add(t2 - t1);
            read.Add(t3 - t2);
            close.Add(t4 - t3);
            if (interval > 0)
            {
                Thread.Sleep(interval);
            }
        }

        holder.StopAndCollect();
        foreach (var l in new[] { probe, open, read, close, attr })
        {
            l.Sort();
        }

        if (variant == "touch")
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"READLAT touch: GetFileAttributesEx (File.GetLastWriteTimeUtc) us p50/p99/max={Us(attr, .5):F0}/{Us(attr, .99):F0}/{Us(attr, 1):F0}; reads that saw a published call time: {touchedSeen}/{reads}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"READLAT variant={variant} probe={withProbe} writeRate={rate}/s reads={reads} intervalMs={interval} us p50/p99/max: censusProbe={Us(probe, .5):F0}/{Us(probe, .99):F0}/{Us(probe, 1):F0} open={Us(open, .5):F0}/{Us(open, .99):F0}/{Us(open, 1):F0} read={Us(read, .5):F0}/{Us(read, .99):F0}/{Us(read, 1):F0} close={Us(close, .5):F0}/{Us(close, .99):F0}/{Us(close, 1):F0}"));
        TryDeleteTree(dir);
        return 0;
    }

    // ---------------------------------------------------------------- page refresh over N servers
    // walk <variant|pipe:netsync|pipe:netasync|pipe:raw|derive> <holders> <writesPerSecond> <rounds> [parallel]
    public static int Walk(string[] a)
    {
        var what = a[1];
        var count = int.Parse(a[2], CultureInfo.InvariantCulture);
        var rate = double.Parse(a[3], CultureInfo.InvariantCulture);
        var rounds = int.Parse(a[4], CultureInfo.InvariantCulture);
        var parallel = a.Length > 5 && a[5] == "parallel";
        var dir = Path.Combine(RunRoot, "walk-" + what.Replace(':', '-') + "-" + Guid.NewGuid().ToString("N")[..8]);
        var isPipe = what.StartsWith("pipe:", StringComparison.Ordinal);
        var variant = isPipe ? "crc" : what == "derive" || what == "census" ? "idle" : what;
        var pipe = isPipe ? what[5..] : "none";

        var holders = new List<Holder>();
        var startClock = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var cwd = Path.Combine(dir, "project-" + i.ToString("D3", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(cwd);
            holders.Add(StartHolder(dir, variant, rate, 3, pipe, "normal", cwd));
        }

        var startSeconds = startClock.Elapsed.TotalSeconds;
        Thread.Sleep(500);

        var perRound = new List<long>();
        var perServer = new ConcurrentBag<long>();
        long failures = 0, detected = 0, wrong = 0;
        var buffer = new byte[65536];
        for (var r = 0; r < rounds; r++)
        {
            var t0 = Stopwatch.GetTimestamp();
            var markers = Directory.GetFiles(dir, "*.live");
            void One(string m, byte[] buf)
            {
                var s0 = Stopwatch.GetTimestamp();
                // The census probe, exactly LiveInstances.Probe: a sharing violation means held.
                var held = false;
                try
                {
                    using var probe = new FileStream(m, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
                }
                catch (IOException e) when ((e.HResult & 0xFFFF) is 32 or 33)
                {
                    held = true;
                }

                if (!held)
                {
                    Interlocked.Increment(ref failures);
                    return;
                }

                if (isPipe)
                {
                    var stem = Path.GetFileNameWithoutExtension(m);
                    try
                    {
                        var answer = PipeDescribe("IpcReview-" + stem, 2000, 2000, out var serverPid);
                        if (answer is null || serverPid.ToString(CultureInfo.InvariantCulture) != stem.Split('-')[0])
                        {
                            Interlocked.Increment(ref failures);
                        }
                        else if (!Record.TryUnframe(answer, out var json, out var ok) || !ok || Record.Check(json, out _) != Record.Consistency.Whole)
                        {
                            Interlocked.Increment(ref wrong);
                        }
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
                else if (what == "derive")
                {
                    var pid = int.Parse(Path.GetFileNameWithoutExtension(m).Split('-')[0], CultureInfo.InvariantCulture);
                    var d = Derive(pid);
                    if (d.Cwd is null || d.ParentImage is null)
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
                else if (what == "touch")
                {
                    var lastWrite = File.GetLastWriteTimeUtc(m);
                    if (lastWrite < Record.Epoch)
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
                else if (what != "census")
                {
                    var tries = 0;
                    while (true)
                    {
                        var outcome = ReadOnce(variant, m, buf);
                        if (outcome == Outcome.Ok)
                        {
                            break;
                        }

                        if (outcome == Outcome.WrongAccepted)
                        {
                            Interlocked.Increment(ref wrong);
                            break;
                        }

                        Interlocked.Increment(ref detected);
                        if (++tries > 100)
                        {
                            Interlocked.Increment(ref failures);
                            break;
                        }
                    }
                }

                perServer.Add(Stopwatch.GetTimestamp() - s0);
            }

            if (parallel)
            {
                Parallel.ForEach(markers, new ParallelOptions { MaxDegreeOfParallelism = 8 }, m => One(m, new byte[65536]));
            }
            else
            {
                foreach (var m in markers)
                {
                    One(m, buffer);
                }
            }

            perRound.Add(Stopwatch.GetTimestamp() - t0);
            Thread.Sleep(100);
        }

        // The cost side, read off the holders before they are stopped.
        long threads = 0, privateKb = 0;
        foreach (var h in holders)
        {
            h.Process.Refresh();
            threads += h.Process.Threads.Count;
            privateKb += h.Process.PrivateMemorySize64 / 1024;
        }

        var outputs = holders.Select(h => h.StopAndCollect()).ToList();
        foreach (var h in holders)
        {
            h.Dispose();
        }

        perRound.Sort();
        var servers = perServer.ToList();
        servers.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"WALK what={what} servers={count} writeRate={rate}/s rounds={rounds} {(parallel ? "parallel8" : "sequential")} roundMs p50={Us(perRound, 0.5) / 1000:F2} p99={Us(perRound, 0.99) / 1000:F2} max={Us(perRound, 1) / 1000:F2} perServerUs p50={Us(servers, 0.5):F1} p99={Us(servers, 0.99):F1} failures={failures} retries={detected} wrongAccepted={wrong} holderThreadsMean={threads / (double)count:F1} holderPrivateKBMean={privateKb / count} startAllSeconds={startSeconds:F1}"));
        Console.WriteLine("  sample holder: " + outputs[0]);
        TryDeleteTree(dir);
        return 0;
    }

    // ---------------------------------------------------------------- pipe client
    /// <summary>Asks one server to describe itself. Null when it did not answer in time.</summary>
    public static byte[]? PipeDescribe(string pipeName, int connectTimeoutMs, int readTimeoutMs, out uint serverPid)
    {
        serverPid = 0;
        using var c = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
        c.Connect(connectTimeoutMs);
        Native.GetNamedPipeServerProcessId(c.SafePipeHandle, out serverPid);
        c.Write("describe\n"u8);
        using var cts = new CancellationTokenSource(readTimeoutMs);
        var header = new byte[Record.HeaderLength];
        if (!ReadExactly(c, header, cts.Token))
        {
            return null;
        }

        var len = int.Parse(Encoding.ASCII.GetString(header, 24, 8), CultureInfo.InvariantCulture);
        var all = new byte[Record.HeaderLength + len];
        header.CopyTo(all, 0);
        if (!ReadExactly(c, all.AsMemory(Record.HeaderLength), cts.Token))
        {
            return null; // truncated: the server went away mid-answer
        }

        return all;
    }

    private static bool ReadExactly(Stream s, Memory<byte> into, CancellationToken token)
    {
        var got = 0;
        while (got < into.Length)
        {
            var r = s.ReadAsync(into[got..], token).AsTask().GetAwaiter().GetResult();
            if (r == 0)
            {
                return false;
            }

            got += r;
        }

        return true;
    }

    // ---------------------------------------------------------------- pipe failure modes
    // pipefail <impl: netsync|raw>
    public static int PipeFail(string[] a)
    {
        var impl = a[1];
        var dir = Path.Combine(RunRoot, "pipefail-" + impl + "-" + Guid.NewGuid().ToString("N")[..8]);

        // 1. the server dies half-way through its answer
        for (var run = 0; run < 3; run++)
        {
            using var h = StartHolder(dir, "idle", 0, 3, impl, "die-mid-answer");
            var t0 = Stopwatch.GetTimestamp();
            string result;
            try
            {
                var answer = PipeDescribe(h.PipeName, 2000, 5000, out _);
                result = answer is null ? "null (truncation detected by the length prefix)" : "WHOLE ANSWER (unexpected)";
            }
            catch (Exception e)
            {
                result = e.GetType().Name + ": " + e.Message.Split('\n')[0];
            }

            var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            h.Process.WaitForExit(5000);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PIPEFAIL impl={impl} die-mid-answer run={run} client saw: {result} after {ms:F1} ms; server exit={(h.Process.HasExited ? h.Process.ExitCode : -1)}"));
        }

        // 2. the server accepts and never answers: the client's own timeout is the only bound
        foreach (var timeout in new[] { 250, 1000 })
        {
            using var h = StartHolder(dir, "idle", 0, 3, impl, "hang-after-accept");
            var t0 = Stopwatch.GetTimestamp();
            string result;
            try
            {
                var answer = PipeDescribe(h.PipeName, 2000, timeout, out _);
                result = answer is null ? "null" : "answer";
            }
            catch (Exception e)
            {
                result = e.GetType().Name;
            }

            var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PIPEFAIL impl={impl} hang-after-accept readTimeout={timeout} client saw: {result} after {ms:F1} ms"));
            h.Process.Kill(); // our own child, hung on purpose
        }

        // 3. the listener answers once and then never listens again
        {
            using var h = StartHolder(dir, "idle", 0, 3, impl, "hang-listener");
            var first = PipeDescribe(h.PipeName, 2000, 2000, out _) is not null;
            Thread.Sleep(100);
            var t0 = Stopwatch.GetTimestamp();
            string result;
            try
            {
                var answer = PipeDescribe(h.PipeName, 500, 500, out _);
                result = answer is null ? "null" : "answer";
            }
            catch (Exception e)
            {
                result = e.GetType().Name + ": " + e.Message.Split('\n')[0];
            }

            var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PIPEFAIL impl={impl} hang-listener first={first} second client (connect timeout 500) saw: {result} after {ms:F1} ms"));
            h.Process.Kill();
        }

        // 4. two coordinators at once against one instance: the second waits, it is not refused
        {
            using var h = StartHolder(dir, "idle", 0, 3, impl, "normal");
            var results = new ConcurrentBag<string>();
            var gate = new ManualResetEventSlim();
            var threads = Enumerable.Range(0, 8).Select(i => new Thread(() =>
            {
                gate.Wait();
                var t0 = Stopwatch.GetTimestamp();
                try
                {
                    var ok = PipeDescribe(h.PipeName, 2000, 2000, out _) is not null;
                    results.Add(string.Create(CultureInfo.InvariantCulture, $"{(ok ? "ok" : "null")}:{(Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency:F2}ms"));
                }
                catch (Exception e)
                {
                    results.Add(e.GetType().Name);
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            gate.Set();
            threads.ForEach(t => t.Join());
            Console.WriteLine($"PIPEFAIL impl={impl} 8 simultaneous clients, maxInstances=1: " + string.Join(" ", results.OrderBy(x => x, StringComparer.Ordinal)));
        }

        // 5. the server process is gone entirely: what does a connect say, and how fast
        {
            var h = StartHolder(dir, "idle", 0, 3, impl, "normal");
            var name = h.PipeName;
            h.Process.Kill();
            h.Process.WaitForExit();
            var t0 = Stopwatch.GetTimestamp();
            string result;
            try
            {
                result = PipeDescribe(name, 500, 500, out _) is null ? "null" : "answer";
            }
            catch (Exception e)
            {
                result = e.GetType().Name + ": " + e.Message.Split('\n')[0];
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PIPEFAIL impl={impl} server killed, connect(500 ms) saw: {result} after {(Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency:F1} ms"));
            h.Process.Dispose();
            h.Stop.Dispose();
        }

        TryDeleteTree(dir);
        return 0;
    }

    // ---------------------------------------------------------------- derive from outside
    public sealed record Derived(long Creation, string? Image, string? CommandLine, int ParentPid, string? ParentImage, string? ParentVersion, string? Cwd);

    public static Derived Derive(int pid)
    {
        using var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h.IsInvalid)
        {
            return new Derived(0, null, null, 0, null, null, null);
        }

        Native.GetProcessTimes(h, out var creation, out _, out _, out _);
        var image = ImageOf(h);
        var commandLine = CommandLineOf(h);
        var pbi = stackalloc byte[48];
        var parentPid = 0;
        if (Native.NtQueryInformationProcess(h, 0, pbi, 48, out _) >= 0)
        {
            parentPid = (int)*(nint*)(pbi + 40);
        }

        string? parentImage = null, parentVersion = null;
        using (var ph = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)parentPid))
        {
            if (!ph.IsInvalid && Native.GetProcessTimes(ph, out var pc, out _, out _, out _) && pc <= creation)
            {
                parentImage = ImageOf(ph);
                if (parentImage is not null)
                {
                    var v = FileVersionInfo.GetVersionInfo(parentImage);
                    parentVersion = (v.ProductName ?? "?") + " " + (v.ProductVersion ?? "?");
                }
            }
        }

        return new Derived(creation, image, commandLine, parentPid, parentImage, parentVersion, CwdOf(pid));
    }

    private static string? ImageOf(SafeProcessHandle h)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024;
        return Native.QueryFullProcessImageName(h, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    private static string? CommandLineOf(SafeProcessHandle h)
    {
        Native.NtQueryInformationProcess(h, 60, null, 0, out var needed);
        if (needed <= 0)
        {
            return null;
        }

        var buffer = new byte[needed];
        fixed (byte* p = buffer)
        {
            if (Native.NtQueryInformationProcess(h, 60, p, needed, out _) < 0)
            {
                return null;
            }

            var length = *(ushort*)p;
            var text = *(char**)(p + 8);
            return new string(text, 0, length / 2);
        }
    }

    /// <summary>The working directory, from the PEB. Undocumented layout (x64): PEB+0x20 ProcessParameters, +0x38 CurrentDirectory.DosPath.</summary>
    public static string? CwdOf(int pid)
    {
        using var h = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION | Native.PROCESS_VM_READ, false, (uint)pid);
        if (h.IsInvalid)
        {
            return null;
        }

        var pbi = stackalloc byte[48];
        if (Native.NtQueryInformationProcess(h, 0, pbi, 48, out _) < 0)
        {
            return null;
        }

        var peb = *(nint*)(pbi + 8);
        nint parameters;
        if (!Native.ReadProcessMemory(h, peb + 0x20, &parameters, (nuint)sizeof(nint), out _))
        {
            return null;
        }

        var us = stackalloc byte[16];
        if (!Native.ReadProcessMemory(h, parameters + 0x38, us, 16, out _))
        {
            return null;
        }

        var length = *(ushort*)us;
        var buffer = *(nint*)(us + 8);
        var text = new char[length / 2];
        fixed (char* t = text)
        {
            if (!Native.ReadProcessMemory(h, buffer, t, length, out _))
            {
                return null;
            }
        }

        return new string(text);
    }

    // derive <holders>
    public static int DeriveBench(string[] a)
    {
        var count = int.Parse(a[1], CultureInfo.InvariantCulture);
        var dir = Path.Combine(RunRoot, "derive-" + Guid.NewGuid().ToString("N")[..8]);
        var holders = new List<(Holder H, string Cwd)>();
        for (var i = 0; i < count; i++)
        {
            var cwd = Path.Combine(dir, "project-" + i.ToString("D3", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(cwd);
            holders.Add((StartHolder(dir, "idle", 0, 3, "none", "normal", cwd), cwd));
        }

        var times = new List<long>();
        var right = 0;
        Derived? sample = null;
        for (var round = 0; round < 5; round++)
        {
            foreach (var (h, cwd) in holders)
            {
                var t0 = Stopwatch.GetTimestamp();
                var d = Derive(h.Process.Id);
                times.Add(Stopwatch.GetTimestamp() - t0);
                sample ??= d;
                if (round == 0 && string.Equals(d.Cwd?.TrimEnd('\\'), cwd, StringComparison.OrdinalIgnoreCase) && d.ParentPid == Environment.ProcessId)
                {
                    right++;
                }
            }
        }

        times.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"DERIVE servers={count} correctCwdAndParent={right}/{count} perServerUs p50={Us(times, 0.5):F1} p99={Us(times, 0.99):F1}"));
        Console.WriteLine($"  sample: image={sample!.Image} parentImage={sample.ParentImage} parentVersion={sample.ParentVersion} cwd={sample.Cwd}");
        Console.WriteLine($"  sample commandLine={sample.CommandLine}");
        foreach (var (h, _) in holders)
        {
            h.Dispose();
        }

        TryDeleteTree(dir);
        return 0;
    }

    // ---------------------------------------------------------------- security descriptors
    public static int Sddl(string[] a)
    {
        var name = "IpcReview-sddl-" + Guid.NewGuid().ToString("N")[..8];
        using (var s = new NamedPipeServerStream(name + "-default", PipeDirection.InOut, 1))
        {
            Console.WriteLine("SDDL NamedPipeServerStream default       : " + Native.SddlOf(s.SafePipeHandle));
        }

        using (var s = new NamedPipeServerStream(name + "-cuo", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly))
        {
            Console.WriteLine("SDDL NamedPipeServerStream CurrentUserOnly: " + Native.SddlOf(s.SafePipeHandle));
        }

        using (var raw = Native.CreateNamedPipe(@"\\.\pipe\" + name + "-rawnull", Native.PIPE_ACCESS_DUPLEX | Native.FILE_FLAG_FIRST_PIPE_INSTANCE, Native.PIPE_REJECT_REMOTE_CLIENTS, 1, 4096, 4096, 0, 0))
        {
            Console.WriteLine("SDDL CreateNamedPipeW NULL attributes    : " + Native.SddlOf(raw));
        }

        // Can PIPE_REJECT_REMOTE_CLIENTS be read back, so that a test could hold it?
        foreach (var reject in new[] { false, true })
        {
            var pipeName = name + "-flags-" + (reject ? "reject" : "accept");
            using var srv = Native.CreateNamedPipe(@"\\.\pipe\" + pipeName,Native.PIPE_ACCESS_DUPLEX | Native.FILE_FLAG_FIRST_PIPE_INSTANCE, reject ? Native.PIPE_REJECT_REMOTE_CLIENTS : 0, 1, 4096, 4096, 0, 0);
            Native.GetNamedPipeInfo(srv, out var sf, out _, out _, out var smax);
            using var cli = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            var connect = Task.Run(() => cli.Connect(2000));
            Native.ConnectNamedPipe(srv, 0);
            connect.Wait();
            Native.GetNamedPipeInfo(cli.SafePipeHandle, out var cf, out _, out _, out _);
            Console.WriteLine($"GetNamedPipeInfo created {(reject ? "WITH" : "without")} PIPE_REJECT_REMOTE_CLIENTS: server flags=0x{sf:X} client flags=0x{cf:X} maxInstances={smax}");
        }

        using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\" + name + "-event"))
        {
            Console.WriteLine("SDDL named event Global\\ default         : " + Native.SddlOf(ev.SafeWaitHandle));
        }

        // FILE_FLAG_FIRST_PIPE_INSTANCE: a second server on the same name is refused.
        using (var first = new NamedPipeServerStream(name + "-first", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly))
        {
            try
            {
                using var second = new NamedPipeServerStream(name + "-first", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                Console.WriteLine("FIRST_PIPE_INSTANCE: second server CREATED (unexpected)");
            }
            catch (Exception e)
            {
                Console.WriteLine($"FIRST_PIPE_INSTANCE: second server refused: {e.GetType().Name} hr=0x{e.HResult:X8} {e.Message.Split('\n')[0]}");
            }
        }

        return 0;
    }

    // ---------------------------------------------------------------- helpers
    public static string RunRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "run"));

    public static double Us(List<long> sorted, double q)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var i = Math.Min(sorted.Count - 1, (int)Math.Ceiling(q * sorted.Count) - 1);
        return sorted[Math.Max(0, i)] * 1e6 / Stopwatch.Frequency;
    }

    public static void TryDeleteTree(string dir)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
