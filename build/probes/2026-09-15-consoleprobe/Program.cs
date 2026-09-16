// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch measurement, 2026-09-15: can a PipeReader parked on a CONSOLE stdin be
// woken by (a) cancelling its token, (b) disposing the stream it reads, or
// (c) both? Writes its answer to the file named by argv[0] so it needs no stdout.
using System.Diagnostics;
using System.IO.Pipelines;

var report = args.Length > 0 ? args[0] : "consoleprobe.txt";
var lines = new List<string>();

void Say(string s) { lines.Add(s); File.WriteAllLines(report, lines); }

var stdin = Console.OpenStandardInput();
Say($"stdin stream type = {stdin.GetType().FullName}");
Say($"stdin is a console (GetConsoleMode) = {IsConsole()}");

var reader = PipeReader.Create(stdin, new StreamPipeReaderOptions(leaveOpen: true));
using var shutdown = new CancellationTokenSource();

var started = new TaskCompletionSource();
var loop = Task.Run(async () =>
{
    started.SetResult();
    var result = await reader.ReadAsync(shutdown.Token).ConfigureAwait(false);
    return result.Buffer.Length;
});

await started.Task;
await Task.Delay(1500);            // the read is now parked in ReadFile on the console
Say($"after 1.5 s the read task is {loop.Status}");

var watch = Stopwatch.StartNew();
shutdown.Cancel();
Say($"cancel: completed within 3 s = {await Settles(loop)} (status {loop.Status}, {watch.ElapsedMilliseconds} ms)");

watch.Restart();
stdin.Dispose();
Say($"dispose: completed within 3 s = {await Settles(loop)} (status {loop.Status}, {watch.ElapsedMilliseconds} ms)");

// And the thing that matters for the product: is the parked read a background
// thread, i.e. does the process exit anyway once nothing awaits it?
Say("exiting Main without awaiting the read task");
return 0;

async Task<bool> Settles(Task t)
{
    try { await t.WaitAsync(TimeSpan.FromSeconds(3)); return true; }
    catch (TimeoutException) { return false; }
    catch (OperationCanceledException) { return true; }
    catch (Exception e) { lines.Add("   threw " + e.GetType().Name); File.WriteAllLines(report, lines); return true; }
}

static bool IsConsole()
{
    var h = GetStdHandle(-10);
    return h != IntPtr.Zero && h != new IntPtr(-1) && GetConsoleMode(h, out _);
}

[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr GetStdHandle(int n);

[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
static extern bool GetConsoleMode(IntPtr h, out uint mode);
