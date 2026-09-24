// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review scratch prototype, 2026-09-24. Not product code.
// What the record costs to build, frame, check and parse, per call; and what a census probe says about a hung server.
using System.Diagnostics;
using System.Globalization;

internal static class Micro
{
    // micro
    public static int Run(string[] a)
    {
        foreach (var sessions in new[] { 0, 3, 20 })
        {
            var json = Record.Json(1, 12345, sessions, @"C:\Source\SixFive7\BrowserAI");
            const int N = 20000;
            var sw = Stopwatch.StartNew();
            var alloc0 = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < N; i++)
            {
                _ = Record.Json(i, 12345, sessions, @"C:\Source\SixFive7\BrowserAI");
            }

            var buildUs = sw.Elapsed.TotalMilliseconds * 1000 / N;
            var buildAlloc = (GC.GetAllocatedBytesForCurrentThread() - alloc0) / N;
            sw.Restart();
            uint sink = 0;
            for (var i = 0; i < N; i++)
            {
                sink ^= Record.Crc32(json);
            }

            var crcUs = sw.Elapsed.TotalMilliseconds * 1000 / N;
            var framed = Record.Framed(json);
            sw.Restart();
            for (var i = 0; i < N; i++)
            {
                _ = Record.TryUnframe(framed, out var j, out var ok);
                _ = Record.Check(j, out _);
            }

            var verifyParseUs = sw.Elapsed.TotalMilliseconds * 1000 / N;
            sw.Restart();
            for (var i = 0; i < N; i++)
            {
                _ = Record.Check(json, out _);
            }

            var parseUs = sw.Elapsed.TotalMilliseconds * 1000 / N;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"MICRO sessions={sessions} jsonBytes={json.Length} buildUs={buildUs:F2} buildAllocBytes={buildAlloc} crc32Us={crcUs:F2} ({json.Length / crcUs:F0} bytes/us) parseUs={parseUs:F2} unframe+crc+parseUs={verifyParseUs:F2} sink={sink & 1}"));
        }

        return 0;
    }

    // censushung: the census probe against a server whose pipe listener is hung, and against one that was killed
    public static int CensusHung(string[] a)
    {
        var dir = Path.Combine(Bench.RunRoot, "censushung-" + Guid.NewGuid().ToString("N")[..8]);
        using var hung = Bench.StartHolder(dir, "idle", 0, 3, "raw", "hang-after-accept");
        // Park its only listener inside a request, so it can never answer again.
        _ = ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Bench.PipeDescribe(hung.PipeName, 2000, 60000, out uint _);
            }
            catch (Exception)
            {
            }
        });
        Thread.Sleep(300);
        foreach (var (label, marker) in new[] { ("hung-listener", hung.Marker) })
        {
            var t0 = Stopwatch.GetTimestamp();
            var state = Probe(marker);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"CENSUSHUNG {label}: census probe says {state} in {(Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency:F0} us"));
            var t1 = Stopwatch.GetTimestamp();
            string pipe;
            try
            {
                pipe = Bench.PipeDescribe(hung.PipeName, 500, 500, out uint _) is null ? "null" : "answer";
            }
            catch (Exception e)
            {
                pipe = e.GetType().Name;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"CENSUSHUNG {label}: pipe describe (500 ms timeouts) says {pipe} in {(Stopwatch.GetTimestamp() - t1) * 1e3 / Stopwatch.Frequency:F0} ms"));
        }

        var dead = Bench.StartHolder(dir, "idle", 0, 3, "raw", "normal");
        var deadMarker = dead.Marker;
        dead.Process.Kill();
        dead.Process.WaitForExit();
        var t2 = Stopwatch.GetTimestamp();
        var deadState = Probe(deadMarker);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"CENSUSHUNG killed: census probe says {deadState} in {(Stopwatch.GetTimestamp() - t2) * 1e6 / Stopwatch.Frequency:F0} us (the pipe connect would have waited its whole timeout)"));
        hung.Process.Kill();
        hung.Process.WaitForExit();
        dead.Stop.Dispose();
        dead.Process.Dispose();
        Bench.TryDeleteTree(dir);
        return 0;
    }

    private static string Probe(string marker)
    {
        try
        {
            using var probe = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
            return "FREE (holder gone)";
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) is 32 or 33)
        {
            return "HELD (holder alive)";
        }
        catch (FileNotFoundException)
        {
            return "ABSENT";
        }
    }
}
