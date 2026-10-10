// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ExitRig;

/// <summary>
/// Option c's survival probe, 2026-10-03. Stand-ins for the coordinator-owned design:
///   coord     started by the Task Scheduler; makes a kill-on-close job J_c, starts the host stand-in in it,
///             serves its own control pipe (coord-quit closes J_c while the host still runs), and exits
///             when the host exits or after --max-minutes, closing J_c either way.
///   host      serves a pipe; "start <id> <logdir>" makes a kill-on-close job of its own for that id
///             (nested inside J_c) and starts a browser stand-in in it; "status <id>", "release <id>"
///             (closes that job), "list", "ping".
///   ctl       one request to a pipe, prints the answer.
///   watch     opens pid@creationFileTime pairs and reports which exit inside --ms.
/// </summary>
static class Coord
{
    static readonly ConcurrentDictionary<string, Browser> Browsers = new();

    sealed class Browser
    {
        public IntPtr Job;
        public SafeProcessHandle? Process;
        public int Pid;
        public long CreatedFt;
        public bool Released;
    }

    public static int RunCoordinator(string[] a)
    {
        var o = Args.Parse(a, 1);
        string dir = o["logdir"];
        string hostPipe = o["host-pipe"];
        string ctlPipe = o["ctl-pipe"];
        int maxMinutes = int.Parse(o.GetValueOrDefault("max-minutes", "60"), CultureInfo.InvariantCulture);
        var log = new Log(Path.Combine(dir, $"coord-{Environment.ProcessId}.log"));
        int ppid = Native.ParentPid();
        log.W("coord", "START", $"ppid={ppid} parentImage={Native.ImageOfPid(ppid)} cmdline={Environment.CommandLine}");
        log.W("coord", "JOB", Native.OwnJobInfo());
        log.W("coord", "CONSOLE", Native.ConsoleInfo());
        log.W("coord", "STDIO", $"stdin={Native.FileTypeOf(-10)} stdout={Native.FileTypeOf(-11)} stderr={Native.FileTypeOf(-12)}");
        using (var self = new SafeProcessHandle(Native.GetCurrentProcess(), false))
        {
            File.WriteAllText(Path.Combine(dir, "coord-identity.txt"), $"{Environment.ProcessId}@{Native.CreationTime(self)}");
        }

        IntPtr job = Native.CreateKillOnCloseJob(out uint flags);
        string exe = Environment.ProcessPath!;
        int hostPid = Native.StartInJob(job, exe, $"\"{exe}\" host --logdir \"{dir}\" --pipe {hostPipe}", out string err);
        log.W("coord", "HOST_STARTED", $"pid={hostPid} jobFlagsReadBack=0x{flags:X} {err}");

        using var host = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.SYNCHRONIZE, false, hostPid);
        var quit = new ManualResetEventSlim();
        var ctl = new Thread(() => ServeLines(ctlPipe, log, "coord", line =>
        {
            if (line == "coord-quit") { quit.Set(); return $"closing J_c pid={Environment.ProcessId}"; }
            if (line == "ping") return $"pong coord pid={Environment.ProcessId}";
            return "unknown";
        })) { IsBackground = true };
        ctl.Start();

        var hostGone = new Thread(() => { Native.WaitForSingleObject(host, Native.INFINITE); quit.Set(); }) { IsBackground = true };
        hostGone.Start();
        bool byQuit = quit.Wait(TimeSpan.FromMinutes(maxMinutes));
        log.W("coord", "ENDING", byQuit ? "quit or host gone" : $"max lifetime {maxMinutes} min reached");
        Native.CloseHandle(job);
        log.W("coord", "CLOSED_JOB", "J_c closed");
        Thread.Sleep(300);
        return 0;
    }

    public static int RunHost(string[] a)
    {
        var o = Args.Parse(a, 1);
        string dir = o["logdir"];
        string pipe = o["pipe"];
        var log = new Log(Path.Combine(dir, $"host-{Environment.ProcessId}.log"));
        int ppid = Native.ParentPid();
        log.W("host", "START", $"ppid={ppid} parentImage={Native.ImageOfPid(ppid)}");
        log.W("host", "JOB", Native.OwnJobInfo());
        string exe = Environment.ProcessPath!;
        ServeLines(pipe, log, "host", line =>
        {
            var parts = line.Split(' ', 3);
            switch (parts[0])
            {
                case "ping":
                    return $"pong host pid={Environment.ProcessId}";
                case "start":
                {
                    string id = parts[1];
                    string logdir = parts.Length > 2 ? parts[2] : dir;
                    IntPtr job = Native.CreateKillOnCloseJob(out uint flags);
                    int pid = Native.StartInJob(job, exe, $"\"{exe}\" standin --logdir \"{logdir}\"", out string err);
                    var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.SYNCHRONIZE, false, pid);
                    long ct = h.IsInvalid ? 0 : Native.CreationTime(h);
                    Browsers[id] = new Browser { Job = job, Process = h, Pid = pid, CreatedFt = ct };
                    log.W("host", "BROWSER_STARTED", $"id={id} pid={pid} created={ct} jobFlags=0x{flags:X} {err}");
                    return $"ok pid={pid} created={ct} {err}";
                }
                case "status":
                {
                    if (!Browsers.TryGetValue(parts[1], out var b) || b.Process is null) return "unknown";
                    bool exited = Native.WaitForSingleObject(b.Process, 0) == 0;
                    if (!exited) return $"alive pid={b.Pid}";
                    Native.GetExitCodeProcess(b.Process, out uint code);
                    return $"exited pid={b.Pid} code={code}";
                }
                case "release":
                {
                    if (!Browsers.TryGetValue(parts[1], out var b) || b.Process is null) return "unknown";
                    bool wasAlive = Native.WaitForSingleObject(b.Process, 0) != 0;
                    if (!b.Released) { Native.CloseHandle(b.Job); b.Released = true; }
                    bool died = Native.WaitForSingleObject(b.Process, 10000) == 0;
                    Native.GetExitCodeProcess(b.Process, out uint code);
                    log.W("host", "RELEASED", $"id={parts[1]} wasAlive={wasAlive} died={died} code={code}");
                    return $"released wasAlive={wasAlive} diedWithin10s={died} code={code}";
                }
                case "list":
                    return string.Join(";", Browsers.Select(kv => $"{kv.Key}={kv.Value.Pid}@{kv.Value.CreatedFt}"));
                default:
                    return "unknown";
            }
        });
        return 0;
    }

    /// <summary>Thread per connection, one line in, one line out.</summary>
    static void ServeLines(string name, Log log, string role, Func<string, string> answer)
    {
        while (true)
        {
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.None);
            server.WaitForConnection();
            var t = new Thread(() =>
            {
                try
                {
                    using (server)
                    {
                        var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                        var line = reader.ReadLine();
                        if (line is null) return;
                        string reply;
                        try { reply = answer(line); }
                        catch (Exception e) { reply = "error " + e.GetType().Name + ": " + e.Message; }
                        log.W(role, "REQUEST", $"{line} -> {reply}");
                        var bytes = Encoding.UTF8.GetBytes(reply + "\n");
                        server.Write(bytes, 0, bytes.Length);
                        server.Flush();
                        server.WaitForPipeDrain();
                    }
                }
                catch (Exception e) { log.W(role, "CONN_ERR", e.GetType().Name + ": " + e.Message); }
            }) { IsBackground = true };
            t.Start();
        }
    }

    public static string Ask(string pipe, string request, int timeoutMs = 10000)
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut);
        client.Connect(timeoutMs);
        var bytes = Encoding.UTF8.GetBytes(request + "\n");
        client.Write(bytes, 0, bytes.Length);
        client.Flush();
        using var reader = new StreamReader(client, new UTF8Encoding(false));
        return reader.ReadLine() ?? "<no answer>";
    }

    public static int Ctl(string[] a)
    {
        var o = Args.Parse(a, 1);
        try
        {
            Console.WriteLine(Ask(o["pipe"], o["request"], int.Parse(o.GetValueOrDefault("timeout-ms", "10000"), CultureInfo.InvariantCulture)));
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("ctl-error " + e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }

    /// <summary>--pair pid@ft : terminates exactly that process, after checking its creation time.</summary>
    public static int Kill(string[] a)
    {
        var o = Args.Parse(a, 1);
        var bits = o["pair"].Split('@');
        int pid = int.Parse(bits[0], CultureInfo.InvariantCulture);
        long ft = long.Parse(bits[1], CultureInfo.InvariantCulture);
        using var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_TERMINATE | Native.SYNCHRONIZE, false, pid);
        if (h.IsInvalid) { Console.WriteLine($"kill: open failed err={Marshal.GetLastWin32Error()}"); return 1; }
        long ct = Native.CreationTime(h);
        if (ct != ft) { Console.WriteLine($"kill: refused, pid {pid} was created {ct}, not {ft}"); return 2; }
        bool ok = Native.TerminateProcess(h, 0xC0FFEE);
        bool gone = Native.WaitForSingleObject(h, 10000) == 0;
        Console.WriteLine($"kill: terminated={ok} gone={gone} image={Native.ImageOf(h)}");
        return ok ? 0 : 3;
    }

    /// <summary>--pairs pid@ft,pid@ft --ms N : which of these exit inside N ms, by handle, creation time checked.</summary>
    public static int Watch(string[] a)
    {
        var o = Args.Parse(a, 1);
        int ms = int.Parse(o.GetValueOrDefault("ms", "15000"), CultureInfo.InvariantCulture);
        var results = new List<string>();
        var waits = new List<(string pair, SafeProcessHandle h)>();
        foreach (var pair in o["pairs"].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = pair.Split('@');
            int pid = int.Parse(bits[0], CultureInfo.InvariantCulture);
            long ft = long.Parse(bits[1], CultureInfo.InvariantCulture);
            var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.SYNCHRONIZE, false, pid);
            if (h.IsInvalid) { results.Add($"{pair} gone-before-watch(open err {Marshal.GetLastWin32Error()})"); continue; }
            long ct = Native.CreationTime(h);
            if (ct != ft) { results.Add($"{pair} gone-before-watch(pid reused, created {ct})"); h.Dispose(); continue; }
            waits.Add((pair, h));
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (pair, h) in waits)
        {
            long left = Math.Max(0, ms - sw.ElapsedMilliseconds);
            bool exited = Native.WaitForSingleObject(h, (uint)left) == 0;
            if (exited)
            {
                Native.GetExitCodeProcess(h, out uint code);
                results.Add($"{pair} exited code={code} byMs={sw.ElapsedMilliseconds}");
            }
            else results.Add($"{pair} STILL-ALIVE after {ms} ms");
            h.Dispose();
        }
        Console.WriteLine(string.Join("\n", results));
        return 0;
    }
}
