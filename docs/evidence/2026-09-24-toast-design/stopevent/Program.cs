// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Q254 scratch: a per-server named "stop" event, answered by a graceful-shutdown path.
// The "server" mimics BrowserAI.Server's shape: a CancellationTokenSource named `stopping`
// whose Cancel is the one shutdown path (src/BrowserAI/Program.cs:258, :454, :471).
using System.Diagnostics;

internal static class Program
{
    private static int Main(string[] args) => args[0] switch
    {
        "server" => Server(args[1], args[2]),
        "measure" => Measure(args[1]),
        _ => 2,
    };

    private static int Server(string prefix, string outDir)
    {
        var name = $@"{prefix}BrowserAI-Q254-Stop-{Environment.ProcessId}-{Guid.NewGuid():N}";
        using var stopping = new CancellationTokenSource();
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, name, out var createdNew);
        var signalledAt = 0L;

        // The wait is a thread-pool registration, never a thread of its own and never a poll.
        var registration = ThreadPool.RegisterWaitForSingleObject(
            stop,
            (_, _) =>
            {
                Interlocked.Exchange(ref signalledAt, Stopwatch.GetTimestamp());
                stopping.Cancel();   // exactly what the update lane's requestShutdown does
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        File.WriteAllText(Path.Combine(outDir, "name.txt"), $"{name}\n{createdNew}");

        stopping.Token.WaitHandle.WaitOne();

        // Stand-in for the disposals Main runs on the way out: transport, proxy, sessions, jobs, log.
        Thread.Sleep(50);
        registration.Unregister(null);
        File.WriteAllText(Path.Combine(outDir, "server.txt"), $"{Stopwatch.GetTimestamp()} {signalledAt}");
        return 0;
    }

    private static int Measure(string root)
    {
        foreach (var prefix in new[] { @"Local\", @"Global\" })
        {
            for (var run = 1; run <= 3; run++)
            {
                var dir = Path.Combine(root, $"{prefix.TrimEnd('\\')}-{run}");
                Directory.CreateDirectory(dir);
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    File.Delete(f);
                }

                var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("server");
                psi.ArgumentList.Add(prefix);
                psi.ArgumentList.Add(dir);
                using var server = Process.Start(psi)!;

                var nameFile = Path.Combine(dir, "name.txt");
                while (!File.Exists(nameFile) || File.ReadAllText(nameFile).Split('\n').Length < 2)
                {
                    Thread.Sleep(5);
                }

                var lines = File.ReadAllText(nameFile).Split('\n');
                var name = lines[0];

                var before = Stopwatch.GetTimestamp();
                using (var open = EventWaitHandle.OpenExisting(name))
                {
                    open.Set();
                }

                server.WaitForExit();
                var exited = Stopwatch.GetTimestamp();
                var parts = File.ReadAllText(Path.Combine(dir, "server.txt")).Split(' ');
                var signalled = long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);

                Console.WriteLine(
                    $"{prefix} run {run}: createdNew={lines[1]} set->callback {Stopwatch.GetElapsedTime(before, signalled).TotalMilliseconds:F2} ms, "
                    + $"set->process exit {Stopwatch.GetElapsedTime(before, exited).TotalMilliseconds:F1} ms, exit code {server.ExitCode}");

                // After the server is gone, the name is gone with it: a late signal has nothing to reach.
                try
                {
                    using var late = EventWaitHandle.OpenExisting(name);
                    Console.WriteLine($"{prefix} run {run}: the name STILL opened after exit");
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Console.WriteLine($"{prefix} run {run}: after exit, OpenExisting -> WaitHandleCannotBeOpenedException (the object died with its last handle)");
                }
            }
        }

        return 0;
    }
}
