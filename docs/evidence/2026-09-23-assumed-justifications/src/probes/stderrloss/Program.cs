// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probe G: "Nothing a child writes to stderr can be lost."
// ChildProcessSession pumps the child's stderr on a Task and, at teardown, waits
// StandardErrorDrainTimeout = TimeSpan.FromSeconds(2) on that Task and swallows
// the timeout.  This rig reproduces exactly that shape and counts lines written
// against lines received.
//   arm "fast"      POSITIVE CONTROL: child writes 5 lines and exits at once.
//   arm "grandchild" the child spawns a grandchild that INHERITS the stderr write
//                    end, then exits; the grandchild writes after the drain window.
using System.Diagnostics;

const int DrainSeconds = 2;   // ChildProcessSession.StandardErrorDrainTimeout

if (args.Length >= 1 && args[0] == "--child-fast") { for (var i = 1; i <= 5; i++) Console.Error.WriteLine($"child-line-{i}"); return 0; }
if (args.Length >= 1 && args[0] == "--child-grandchild")
{
    Console.Error.WriteLine("child-line-1 (before the grandchild)");
    var g = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    g.ArgumentList.Add("--grandchild");
    Process.Start(g);                 // inherits this process's stderr write end
    Console.Error.WriteLine("child-line-2 (grandchild started; child now exits)");
    return 0;                          // the CHILD is gone; the PIPE is not
}
if (args.Length >= 1 && args[0] == "--grandchild")
{
    for (var i = 1; i <= 4; i++) { Thread.Sleep(1500); Console.Error.WriteLine($"grandchild-line-{i} at t+{1.5 * i:0.0}s"); }
    return 0;
}

Console.WriteLine("############ PROBE G -- can a line a child writes to stderr be lost?");
Console.WriteLine("rig mirrors ChildProcessSession: a pump Task, drained with WaitAsync(2 s), timeout swallowed");
Console.WriteLine(".NET 10.0.401 | Windows 11 Pro 10.0.26200 | 2026-09-23");
Console.WriteLine();

await Run("POSITIVE CONTROL: child writes 5 lines and exits", "--child-fast", 5);
await Run("CLAIM: a grandchild inherits the stderr write end and outlives the child", "--child-grandchild", 6);
return 0;

static async Task Run(string label, string childArg, int linesWritten)
{
    Console.WriteLine($"--- {label}");
    var psi = new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true,
    };
    psi.ArgumentList.Add(childArg);

    var received = new List<string>();
    using var child = Process.Start(psi)!;

    // Exactly ChildProcessSession.PumpStandardErrorAsync.
    var pump = Task.Run(async () =>
    {
        using var reader = child.StandardError;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) lock (received) received.Add(line);
    });

    await child.WaitForExitAsync();
    var sw = Stopwatch.StartNew();

    // Exactly ChildProcessSession.DrainStandardErrorAsync.
    var abandoned = false;
    try { await pump.WaitAsync(TimeSpan.FromSeconds(DrainSeconds)); }
    catch (TimeoutException) { abandoned = true; }
    catch (Exception) { abandoned = true; }

    Console.WriteLine($"    child exited, then drained for {sw.Elapsed.TotalSeconds:0.00} s; pump abandoned = {abandoned}");
    lock (received)
    {
        Console.WriteLine($"    LINES WRITTEN BY THE TREE: {linesWritten}   LINES RECEIVED: {received.Count}   LOST: {linesWritten - received.Count}");
        foreach (var l in received) Console.WriteLine($"      got| {l}");
    }
    Console.WriteLine();
}
