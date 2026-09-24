// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review scratch prototype, 2026-09-24. Not product code.
// Single-instance guard and "show your window" handover for the coordinator, two ways:
//   mutex - Global\ named mutex taken with a zero wait; a loser sets a Global\ auto-reset event
//   pipe  - a named pipe created with FILE_FLAG_FIRST_PIPE_INSTANCE; a loser connects and says "show"
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;

internal static class Single
{
    // contender <impl> <name> <startEvent> <resultDir> <holdMs>
    public static int Contender(string[] a)
    {
        var impl = a[1];
        var name = a[2];
        using var start = EventWaitHandle.OpenExisting(a[3]);
        var resultDir = a[4];
        var holdMs = int.Parse(a[5], CultureInfo.InvariantCulture);
        start.WaitOne();
        var t0 = Stopwatch.GetTimestamp();
        var line = impl == "mutex" ? ViaMutex(name, holdMs, t0) : ViaPipe(name, holdMs, t0);
        File.WriteAllText(Path.Combine(resultDir, Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".txt"), line);
        return 0;
    }

    private static string Ms(long from) => ((Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture);

    private static string ViaMutex(string name, int holdMs, long t0)
    {
        using var mutex = new Mutex(false, @"Global\IpcReview-coord-" + name);
        bool mine;
        var abandoned = false;
        try
        {
            mine = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            mine = true;
            abandoned = true;
        }

        if (mine)
        {
            var decided = Ms(t0);
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\IpcReview-show-" + name);
            var wakes = 0;
            var until = Stopwatch.GetTimestamp() + holdMs * Stopwatch.Frequency / 1000;
            while (Stopwatch.GetTimestamp() < until)
            {
                if (show.WaitOne(50))
                {
                    wakes++;
                }
            }

            mutex.ReleaseMutex();
            return $"WINNER impl=mutex decidedMs={decided} abandoned={abandoned} showWakes={wakes}";
        }

        // Loser: hand over. The event must exist; the winner may not have created it yet.
        var tries = 0;
        while (true)
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(@"Global\IpcReview-show-" + name);
                show.Set();
                return $"LOSER impl=mutex decidedAndSignalledMs={Ms(t0)} openRetries={tries} coordinatorPid=UNKNOWN (an event carries no pid)";
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                tries++;
                Thread.Sleep(1);
                if (tries > 2000)
                {
                    return "LOSER impl=mutex FAILED: show event never appeared";
                }
            }
        }
    }

    private static string ViaPipe(string name, int holdMs, long t0)
    {
        NamedPipeServerStream? server = null;
        try
        {
            // maxInstances 1 => .NET sets FILE_FLAG_FIRST_PIPE_INSTANCE (NamedPipeServerStream.Windows.cs, release/10.0).
            server = new NamedPipeServerStream("IpcReview-coord-" + name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            server = null;
        }

        if (server is not null)
        {
            var decided = Ms(t0);
            var served = 0;
            var until = Stopwatch.GetTimestamp() + holdMs * Stopwatch.Frequency / 1000;
            var buffer = new byte[64];
            while (Stopwatch.GetTimestamp() < until)
            {
                using var cts = new CancellationTokenSource(Math.Max(1, (int)((until - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency)));
                try
                {
                    server.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    var r = server.Read(buffer, 0, buffer.Length);
                    var reply = Encoding.ASCII.GetBytes("ok " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
                    server.Write(reply, 0, reply.Length);
                    while (server.Read(buffer, 0, buffer.Length) > 0)
                    {
                    }

                    served++;
                }
                catch (IOException)
                {
                }

                server.Disconnect();
            }

            server.Dispose();
            return $"WINNER impl=pipe decidedMs={decided} served={served}";
        }

        // Loser: connect, learn the coordinator's pid (for AllowSetForegroundWindow), say "show".
        try
        {
            using var client = new NamedPipeClientStream(".", "IpcReview-coord-" + name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(5000);
            Native.GetNamedPipeServerProcessId(client.SafePipeHandle, out var pid);
            client.Write("show\n"u8);
            var buffer = new byte[64];
            var n = client.Read(buffer, 0, buffer.Length);
            return $"LOSER impl=pipe handedOverMs={Ms(t0)} coordinatorPid={pid} reply={Encoding.ASCII.GetString(buffer, 0, n).Trim()}";
        }
        catch (Exception e)
        {
            return $"LOSER impl=pipe FAILED {e.GetType().Name}: {e.Message.Split('\n')[0]}";
        }
    }

    // single <impl> <contenders> <holdMs>
    public static int Bench(string[] a)
    {
        var impl = a[1];
        var count = int.Parse(a[2], CultureInfo.InvariantCulture);
        var holdMs = int.Parse(a[3], CultureInfo.InvariantCulture);
        var name = Guid.NewGuid().ToString("N")[..12];
        var resultDir = Path.Combine(global::Bench.RunRoot, "single-" + impl + "-" + name);
        Directory.CreateDirectory(resultDir);
        var startName = @"Local\IpcReview-start-" + name;
        using var start = new EventWaitHandle(false, EventResetMode.ManualReset, startName);
        var processes = new List<Process>();
        for (var i = 0; i < count; i++)
        {
            processes.Add(Launch(impl, name, startName, resultDir, holdMs));
        }

        Thread.Sleep(1500); // let every contender reach the barrier
        start.Set();
        foreach (var p in processes)
        {
            if (!p.WaitForExit(holdMs + 20000))
            {
                p.Kill();
            }
        }

        var lines = Directory.GetFiles(resultDir, "*.txt").Select(File.ReadAllText).ToList();
        var winners = lines.Count(l => l.StartsWith("WINNER", StringComparison.Ordinal));
        Console.WriteLine($"SINGLE impl={impl} contenders={count} results={lines.Count} winners={winners}");
        foreach (var l in lines.Where(l => l.StartsWith("WINNER", StringComparison.Ordinal)))
        {
            Console.WriteLine("  " + l);
        }

        var losers = lines.Where(l => l.StartsWith("LOSER", StringComparison.Ordinal)).ToList();
        Console.WriteLine($"  losers={losers.Count} failed={losers.Count(l => l.Contains("FAILED", StringComparison.Ordinal))}");
        foreach (var l in losers.Take(3))
        {
            Console.WriteLine("  " + l);
        }

        var ms = losers.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"Ms=([0-9.]+)")).Where(m => m.Success)
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).OrderBy(x => x).ToList();
        if (ms.Count > 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  loser handover ms: min={ms[0]:F2} p50={ms[ms.Count / 2]:F2} max={ms[^1]:F2}"));
        }

        // Crash recovery: the winner is killed with TerminateProcess; a new contender must win at once.
        using var start2 = new EventWaitHandle(false, EventResetMode.ManualReset, startName + "-2");
        var resultDir2 = resultDir + "-crash";
        Directory.CreateDirectory(resultDir2);
        var first = Launch(impl, name, startName + "-2", resultDir2, 60000);
        Thread.Sleep(800);
        start2.Set();
        Thread.Sleep(500);
        first.Kill(); // our own child: the stand-in coordinator dies without releasing anything
        first.WaitForExit();
        var killedAt = Stopwatch.GetTimestamp();
        using var start3 = new EventWaitHandle(false, EventResetMode.ManualReset, startName + "-3");
        var resultDir3 = resultDir + "-after";
        Directory.CreateDirectory(resultDir3);
        var next = Launch(impl, name, startName + "-3", resultDir3, 200);
        Thread.Sleep(800);
        start3.Set();
        next.WaitForExit(20000);
        var after = Directory.GetFiles(resultDir3, "*.txt").Select(File.ReadAllText).FirstOrDefault() ?? "<none>";
        Console.WriteLine($"  after the winner was killed, the next start said: {after}");
        return 0;
    }

    private static Process Launch(string impl, string name, string startName, string resultDir, int holdMs)
    {
        var psi = new ProcessStartInfo(global::Bench.Self) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "contender", impl, name, startName, resultDir, holdMs.ToString(CultureInfo.InvariantCulture) })
        {
            psi.ArgumentList.Add(arg);
        }

        return Process.Start(psi)!;
    }
}
