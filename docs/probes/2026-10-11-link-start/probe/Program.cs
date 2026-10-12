// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The 55 a probe: what a browser starts for a link on a loopback page, and whether
// the window that process opens comes to the front. A scratch rig and a record of
// method; it is not the product and is never shipped.
//
// Modes, by the first argument:
//   register      writes HKCU\Software\Classes\browserai-probe, pointing at this file
//   unregister    removes that key, and only when its command names this file
//   status        writes what the key holds now
//   prepare       creates the folders the links open
//   observe       records the foreground window, shell hook events and input, until stop.flag
//   serve N P     serves the probe page as server N on 127.0.0.1:P (0: Windows picks), until stop.flag
//   standin       stands in for the task-started background: a pipe that opens Explorer or the picker
//   handle URL    what a browser starts for a browserai-probe: link
//   selftest DIR  checks the parser and the page text; listens on nothing, opens nothing, writes only DIR
//   analyze DIR   reads every log in DIR and writes DIR\report.txt
//
// The run folder is the one bin\current-run.txt names, which start.ps1 writes. Every
// long-running mode ends when stop.flag appears there, or after 45 minutes, and a
// handler after 10, so nothing this rig starts outlives the measurement.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LinkProbe;

internal static partial class Program
{
    public const string Scheme = "browserai-probe";
    public const string PipeName = "BrowserAI-LinkProbe-55a";
    private static readonly TimeSpan LongestRun = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan LongestHandler = TimeSpan.FromMinutes(10);

    private static string BinDirectory => AppContext.BaseDirectory;

    private static string RunDirectory
    {
        get
        {
            var pointer = Path.Combine(BinDirectory, "current-run.txt");
            if (File.Exists(pointer) && File.ReadAllText(pointer).Trim() is { Length: > 0 } named)
            {
                return named;
            }

            return Path.GetFullPath(Path.Combine(BinDirectory, "..", "run-unnamed"));
        }
    }

    private static string StopFlag => Path.Combine(RunDirectory, "stop.flag");

    private static string FoldersDirectory => Path.Combine(RunDirectory, "folders");

    private static bool Stopping(Stopwatch alive, TimeSpan longest) => File.Exists(StopFlag) || alive.Elapsed > longest;

    /// <summary>The two browsers installed on this machine, by full image path: the observer's cue to start the stand-in.</summary>
    private static readonly string[] Browsers =
    [
        @"C:\Program Files\Mozilla Firefox\firefox.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : string.Empty;

        try
        {
            switch (mode)
            {
                case "selftest" when args.Length == 2:
                    return SelfTest(args[1]);
                case "analyze" when args.Length == 2:
                    return Analyze(args[1]);
            }

            Directory.CreateDirectory(RunDirectory);
            Log.Open(RunDirectory, mode is { Length: > 0 } ? mode : "none");

            return mode switch
            {
                "register" => Register(),
                "unregister" => Unregister(),
                "status" => Status(),
                "prepare" => Prepare(),
                "observe" => Observe(),
                "serve" when args.Length == 3 => Serve(args[1], int.Parse(args[2], CultureInfo.InvariantCulture)),
                "standin" => StandIn(),
                "handle" => Handle(args),
                _ => Refuse(args),
            };
        }
        catch (Exception failure)
        {
            Log.Write("fatal", ("error", failure.ToString()));
            return 9;
        }
    }

    private static int Refuse(string[] args)
    {
        Log.Write("refused", ("why", "unknown mode"), ("args", string.Join(" | ", args)));
        return 2;
    }

    // ---------------------------------------------------------------- registry

    private static string CommandFor(string executable) => $"\"{executable}\" handle \"%1\"";

    private static int Register()
    {
        var executable = Environment.ProcessPath!;
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true);

        using (var existing = classes.OpenSubKey(Scheme))
        {
            if (existing is not null)
            {
                // Never written over: a key this rig did not write is somebody else's.
                Log.Write("register-refused", ("why", "the key already exists"));
                return 4;
            }
        }

        using (var key = classes.CreateSubKey(Scheme, writable: true))
        {
            key.SetValue(string.Empty, "URL:BrowserAI link probe", RegistryValueKind.String);
            key.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);
        }

        using (var command = classes.CreateSubKey($@"{Scheme}\shell\open\command", writable: true))
        {
            command.SetValue(string.Empty, CommandFor(executable), RegistryValueKind.String);
        }

        Log.Write("registered", ("key", $@"HKCU\Software\Classes\{Scheme}"), ("command", CommandFor(executable)));
        return Status();
    }

    private static int Unregister()
    {
        var executable = Environment.ProcessPath!;
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true)!;

        using (var command = classes.OpenSubKey($@"{Scheme}\shell\open\command"))
        {
            if (command is null)
            {
                using var bare = classes.OpenSubKey(Scheme);
                Log.Write("unregister", ("found", bare is null ? "nothing" : "a key with no command, left alone"));
                return bare is null ? 0 : 5;
            }

            if (command.GetValue(string.Empty) is not string value
                || !string.Equals(value, CommandFor(executable), StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("unregister-refused", ("why", "the command names another program"), ("command", command.GetValue(string.Empty)?.ToString()));
                return 5;
            }
        }

        classes.DeleteSubKeyTree(Scheme, throwOnMissingSubKey: false);

        using var after = classes.OpenSubKey(Scheme);
        Log.Write("unregistered", ("absentAfter", after is null));
        return after is null ? 0 : 6;
    }

    private static int Status()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Scheme}");
        using var command = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Scheme}\shell\open\command");
        using var merged = Registry.ClassesRoot.OpenSubKey(Scheme);

        Log.Write(
            "status",
            ("present", key is not null),
            ("default", key?.GetValue(string.Empty)?.ToString()),
            ("urlProtocol", key?.GetValueNames().Contains("URL Protocol")),
            ("command", command?.GetValue(string.Empty)?.ToString()),
            ("seenThroughHkcr", merged is not null));
        return 0;
    }

    // ---------------------------------------------------------------- folders and links

    /// <summary>The links the pages carry: a letter for what the handler does, and the page it was on.</summary>
    private static readonly Regex LinkShape = MakeLinkShape();

    [GeneratedRegex(@"^browserai-probe:/{0,2}(?<variant>[ABCDEP])/?\?at=(?<at>p1b|p1|p2)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MakeLinkShape();

    internal static bool TryParse(string link, out char variant, out string at)
    {
        variant = '\0';
        at = string.Empty;

        if (link.Length > 200)
        {
            return false;
        }

        var match = LinkShape.Match(link);

        if (!match.Success)
        {
            return false;
        }

        variant = char.ToUpperInvariant(match.Groups["variant"].Value[0]);
        at = match.Groups["at"].Value.ToLowerInvariant();
        return true;
    }

    private static string FolderFor(char variant, string at) => Path.Combine(FoldersDirectory, $"Probe-{variant}-{at}");

    private static readonly (char Variant, string At)[] Folders =
    [
        ('A', "p1"), ('A', "p2"), ('A', "p1b"), ('B', "p1"), ('E', "p1"),
    ];

    private static int Prepare()
    {
        foreach (var (variant, at) in Folders)
        {
            var folder = FolderFor(variant, at);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "this folder was opened by the link probe.txt"), $"Link {variant} on page {at}.\r\n");
        }

        Log.Write("prepared", ("folders", FoldersDirectory));
        return 0;
    }

    // ---------------------------------------------------------------- the handler

    private static int Handle(string[] args)
    {
        var parent = ProcessFacts.ParentOfThisProcess();
        var foreground = Native.GetForegroundWindow();

        Log.Write(
            "start",
            ("argc", args.Length),
            ("args", string.Join(" | ", args)),
            ("commandLine", Environment.CommandLine),
            ("parentPid", parent.Pid),
            ("parentImage", parent.Image),
            ("parentCreated", parent.Created),
            ("parentIsOlder", parent.IsOlderThanThis),
            ("inputTick", Native.LastInputTick()),
            ("foreground", WindowWatch.Describe(foreground)));

        if (args.Length != 2)
        {
            Log.Write("refused", ("why", "a link start carries exactly one argument after the mode"));
            return 2;
        }

        if (!TryParse(args[1], out var variant, out var at))
        {
            Log.Write("refused", ("why", "the link is not one of the probe's"), ("link", args[1]));
            return 2;
        }

        var watchdog = new Thread(() =>
        {
            var alive = Stopwatch.StartNew();
            while (!Stopping(alive, LongestHandler))
            {
                Thread.Sleep(500);
            }

            Log.Write("watchdog-exit");
            Environment.Exit(3);
        })
        {
            IsBackground = true,
            Name = "watchdog",
        };
        watchdog.Start();

        Log.Write("variant", ("variant", variant.ToString()), ("at", at));

        return variant switch
        {
            'A' => OpenItself(variant, at),
            'C' => PickItself(at),
            'B' => ThroughTheStandIn(variant, at, "open", grant: true),
            'D' => ThroughTheStandIn(variant, at, "pick", grant: true),
            'E' => ThroughTheStandIn(variant, at, "open", grant: false),

            // P: a dry run of the pipe that opens nothing, for checking the rig before the measurement.
            'P' => ThroughTheStandIn(variant, at, "ping", grant: false),
            _ => 2,
        };
    }

    /// <summary>
    /// The round trip the product's link start makes before it opens anything: it asks the
    /// background to check the token and name the folder. The stand-in answers a "noop"
    /// without touching the foreground, so the trip costs what the product's would and
    /// takes nothing from anybody.
    /// </summary>
    private static void RoundTrip(char variant, string at)
    {
        var reply = Ask(new StandInRequest("noop", variant.ToString(), at, null), out var server);
        Log.Write("round-trip", ("serverPid", server), ("reply", reply));
    }

    /// <summary>A: this process opens Explorer itself, with the product's own call.</summary>
    private static int OpenItself(char variant, string at)
    {
        RoundTrip(variant, at);

        var folder = FolderFor(variant, at);

        Log.Write("action-start", ("call", "ShellExecuteW explore"), ("folder", folder));
        var answer = Native.ShellExecuteW(0, "explore", folder, null, null, Native.SwShowNormal);
        var error = Marshal.GetLastPInvokeError();
        Log.Write("action-done", ("shell", (long)answer), ("lastError", error), ("foreground", WindowWatch.Describe(Native.GetForegroundWindow())));

        Native.Pump(TimeSpan.FromSeconds(3));

        // Only after the outcome has settled: a call naming this process would take
        // away a right that Explorer may still be holding.
        var right = Native.AllowSetForegroundWindow((uint)Environment.ProcessId);
        var rightError = Marshal.GetLastPInvokeError();
        Log.Write("right-after", ("allowSetForegroundWindowOnSelf", right), ("lastError", rightError), ("foreground", WindowWatch.Describe(Native.GetForegroundWindow())));
        return 0;
    }

    /// <summary>C: this process shows the folder picker itself, with the product's own flags and no owner.</summary>
    private static int PickItself(string at)
    {
        RoundTrip('C', at);

        Log.Write("action-start", ("call", "SHBrowseForFolderW"), ("apartment", Thread.CurrentThread.GetApartmentState().ToString()));
        var picked = Native.PickFolder($"Link probe C on page {at}: press Cancel.");
        Log.Write("action-done", ("outcome", picked), ("foreground", WindowWatch.Describe(Native.GetForegroundWindow())));
        return 0;
    }

    /// <summary>
    /// B, D, E: the stand-in background does it. It says first whether it may raise a
    /// window on its own, which a background the Task Scheduler started may not; a
    /// stand-in that may makes the run say nothing about a grant, and the analysis
    /// says so. Then this process grants it the right (B, D) or does not (E), and asks.
    /// </summary>
    private static int ThroughTheStandIn(char variant, string at, string operation, bool grant)
    {
        var before = Ask(new StandInRequest("ping", variant.ToString(), at, null), out var server);
        Log.Write("before-grant", ("serverPid", server), ("reply", before));

        if (operation == "ping")
        {
            return 0;
        }

        bool? granted = null;
        var grantError = 0;

        if (grant && server > 0)
        {
            granted = Native.AllowSetForegroundWindow((uint)server);
            grantError = Marshal.GetLastPInvokeError();
        }

        Log.Write("grant", ("serverPid", server), ("asked", grant), ("granted", granted), ("lastError", grantError));

        var reply = Ask(new StandInRequest(operation, variant.ToString(), at, granted), out _);
        Log.Write("reply", ("reply", reply), ("foreground", WindowWatch.Describe(Native.GetForegroundWindow())));
        return 0;
    }

    /// <summary>One request to the stand-in, one line back, one connection, as the product's client does it.</summary>
    private static string? Ask(StandInRequest request, out int server)
    {
        server = 0;
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);

        try
        {
            pipe.Connect(3000);
        }
        catch (TimeoutException)
        {
            Log.Write("stand-in-absent", ("op", request.Op));
            return null;
        }

        server = Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) ? (int)serverPid : 0;

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        var line = JsonSerializer.Serialize(request, ProbeJson.Default.StandInRequest);

        writer.WriteLine(line);
        return reader.ReadLine();
    }

    // ---------------------------------------------------------------- the stand-in background

    private static int StandIn()
    {
        var parent = ProcessFacts.ParentOfThisProcess();
        Log.Write("start", ("parentPid", parent.Pid), ("parentImage", parent.Image), ("apartment", Thread.CurrentThread.GetApartmentState().ToString()));

        var queue = new ConcurrentQueue<(StandInRequest Request, TaskCompletionSource<string> Done)>();
        // Never disposed: the pipe's thread may still set one as the process ends.
        var arrived = new AutoResetEvent(false);
        var stop = new ManualResetEvent(false);
        var alive = Stopwatch.StartNew();

        var pipes = new Thread(() => ServePipe(queue, arrived, stop))
        {
            IsBackground = true,
            Name = "stand-in pipe",
        };
        pipes.Start();

        // As the background's own loop: its single-threaded apartment waits on two
        // handles, and the shell calls run here.
        WaitHandle[] waits = [stop, arrived];

        while (true)
        {
            var woke = WaitHandle.WaitAny(waits, 250);

            if (Stopping(alive, LongestRun))
            {
                _ = stop.Set();
                break;
            }

            if (woke is 0)
            {
                break;
            }

            while (queue.TryDequeue(out var item))
            {
                item.Done.SetResult(Execute(item.Request));
            }
        }

        Log.Write("stop");
        return 0;
    }

    private static string Execute(StandInRequest request)
    {
        if (string.Equals(request.Op, "noop", StringComparison.Ordinal))
        {
            Log.Write("execute", ("op", request.Op), ("variant", request.Variant), ("at", request.At));
            return JsonSerializer.Serialize(new StandInReply(true, "noop", false, 0, 0), ProbeJson.Default.StandInReply);
        }

        // Whether this process may set the foreground now. Naming itself takes nothing
        // from anybody: a grant to this process stays when the same process is named.
        var right = Native.AllowSetForegroundWindow((uint)Environment.ProcessId);
        var rightError = Marshal.GetLastPInvokeError();
        Log.Write("execute", ("op", request.Op), ("variant", request.Variant), ("at", request.At), ("grantedByHandler", request.Granted), ("mayRaise", right), ("lastError", rightError));

        if (string.Equals(request.Op, "ping", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new StandInReply(true, "pong", right, rightError, 0), ProbeJson.Default.StandInReply);
        }

        if (string.Equals(request.Op, "pick", StringComparison.Ordinal))
        {
            var picked = Native.PickFolder($"Link probe {request.Variant} on page {request.At}: press Cancel.");
            Log.Write("executed", ("outcome", picked));
            return JsonSerializer.Serialize(new StandInReply(true, picked, right, rightError, 0), ProbeJson.Default.StandInReply);
        }

        if (request.Variant is not { Length: 1 } || !TryParse($"browserai-probe:{request.Variant}?at={request.At}", out var variant, out var at))
        {
            return JsonSerializer.Serialize(new StandInReply(false, "refused", right, rightError, 0), ProbeJson.Default.StandInReply);
        }

        var folder = FolderFor(variant, at);
        var answer = Native.ShellExecuteW(0, "explore", folder, null, null, Native.SwShowNormal);
        var error = Marshal.GetLastPInvokeError();
        Log.Write("executed", ("shell", (long)answer), ("lastError", error), ("folder", folder));
        return JsonSerializer.Serialize(new StandInReply(answer > 32, "opened", right, rightError, (long)answer), ProbeJson.Default.StandInReply);
    }

    private static void ServePipe(ConcurrentQueue<(StandInRequest, TaskCompletionSource<string>)> queue, AutoResetEvent arrived, ManualResetEvent stop)
    {
        while (!stop.WaitOne(0))
        {
            using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var waiting = server.WaitForConnectionAsync();

            while (!waiting.Wait(250))
            {
                if (stop.WaitOne(0))
                {
                    return;
                }
            }

            try
            {
                var client = Native.GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientPid) ? (int)clientPid : 0;
                using var reader = new StreamReader(server, new UTF8Encoding(false), leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
                var line = reader.ReadLine();
                Log.Write("request", ("clientPid", client), ("line", line));

                if (line is null || JsonSerializer.Deserialize(line, ProbeJson.Default.StandInRequest) is not { } request)
                {
                    continue;
                }

                var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                queue.Enqueue((request, done));
                _ = arrived.Set();
                writer.WriteLine(done.Task.GetAwaiter().GetResult());
            }
            catch (Exception failure) when (failure is IOException or JsonException)
            {
                Log.Write("pipe-failed", ("error", failure.Message));
            }
        }
    }

    // ---------------------------------------------------------------- the observer

    private static int Observe()
    {
        var parent = ProcessFacts.ParentOfThisProcess();
        Log.Write(
            "start",
            ("parentPid", parent.Pid),
            ("parentImage", parent.Image),
            ("foregroundLockTimeoutMs", Native.ForegroundLockTimeout()),
            ("probeImage", Environment.ProcessPath));

        var hook = new ShellHook();
        hook.Start();

        var alive = Stopwatch.StartNew();
        nint last = 0;
        uint lastInput = 0;
        long lastInputLogged = 0;
        var seen = new Dictionary<nint, string>();
        var round = 0;
        var standInStarted = false;

        while (!Stopping(alive, LongestRun))
        {
            var foreground = Native.GetForegroundWindow();

            if (foreground != last)
            {
                last = foreground;
                var facts = WindowWatch.Describe(foreground);
                Log.Write("foreground", ("window", facts), ("z", WindowWatch.ZIndex(foreground)));

                // The stand-in starts once a browser holds the foreground, so that it is
                // made by a process the person is no longer using: started at once from
                // this rig's shell, a descendant of the editor in front, it could raise a
                // window on its own, which a background the Task Scheduler started cannot.
                if (!standInStarted && facts.Image is { } image && Browsers.Contains(image, StringComparer.OrdinalIgnoreCase))
                {
                    standInStarted = true;

                    try
                    {
                        using var standIn = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "standin") { UseShellExecute = false, CreateNoWindow = true })!;
                        Log.Write("standin-started", ("standInPid", standIn.Id), ("standInStarted", standIn.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)), ("foreground", facts));
                    }
                    catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        Log.Write("standin-failed", ("error", failure.Message));
                    }
                }
            }

            var input = Native.LastInputTick();

            if (input != lastInput)
            {
                lastInput = input;

                // Input arrives in bursts, a mouse move every few milliseconds; one line
                // per quiet gap is what the analysis needs.
                if (Environment.TickCount64 - lastInputLogged > 150)
                {
                    lastInputLogged = Environment.TickCount64;
                    Log.Write("input", ("inputTick", input));
                }
            }

            if (round++ % 5 is 0)
            {
                WindowWatch.ScanProbeWindows(seen, foreground);
            }

            Thread.Sleep(20);
        }

        hook.Stop();
        Log.Write("stop");

        // The net under stop.ps1: the scheme goes when the measurement ends, also when
        // nobody ran the cleanup and the 45 minutes did. Only a key naming this file.
        var unregistered = Unregister();
        Log.Write("unregistered-at-end", ("exit", unregistered));
        return 0;
    }

    // ---------------------------------------------------------------- the page servers

    private static int Serve(string name, int port)
    {
        var alive = Stopwatch.StartNew();
        var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };

        try
        {
            listener.Start();
        }
        catch (SocketException failure) when (port != 0)
        {
            Log.Write("port-taken", ("port", port), ("error", failure.Message));
            listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
            listener.Start();
        }

        var actual = ((IPEndPoint)listener.LocalEndpoint).Port;
        File.WriteAllText(Path.Combine(RunDirectory, $"serve-{name}.port"), actual.ToString(CultureInfo.InvariantCulture));
        Log.Write("listening", ("name", name), ("port", actual), ("asked", port));

        while (!Stopping(alive, LongestRun))
        {
            var accepting = listener.AcceptTcpClientAsync();

            while (!accepting.Wait(250))
            {
                if (Stopping(alive, LongestRun))
                {
                    listener.Stop();
                    Log.Write("stop");
                    return 0;
                }
            }

            using var client = accepting.Result;

            try
            {
                Answer(client, name, actual);
            }
            catch (Exception failure) when (failure is IOException or SocketException)
            {
                Log.Write("request-failed", ("error", failure.Message));
            }
        }

        listener.Stop();
        Log.Write("stop");
        return 0;
    }

    private static void Answer(TcpClient client, string name, int port)
    {
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;
        AnswerOn(client.GetStream(), name, port);
    }

    /// <summary>One request and its answer, on any stream: the socket's, or the self-test's.</summary>
    internal static void AnswerOn(Stream stream, string name, int port)
    {
        var head = ReadHead(stream);

        if (head is null)
        {
            return;
        }

        var lines = head.Split("\r\n");
        var parts = lines[0].Split(' ');

        if (parts.Length != 3)
        {
            Write(stream, 400, "text/plain; charset=utf-8", "bad request");
            return;
        }

        var method = parts[0];
        var target = parts[1];
        var path = target.Split('?')[0];
        var query = target.Contains('?', StringComparison.Ordinal) ? target[(target.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        var host = lines.Skip(1).FirstOrDefault(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))?[5..].Trim();
        var at = PageFor(name, path);

        Log.Write("request", ("method", method), ("target", target), ("host", host), ("page", at));

        if (at is null)
        {
            Write(stream, 404, "text/plain; charset=utf-8", "not found");
            return;
        }

        var rest = path[(path.LastIndexOf('/') + 1)..];

        switch (method, rest)
        {
            case ("GET", ""):
                Write(stream, 200, "text/html; charset=utf-8", PageText(at, port));
                return;
            case ("GET", "probe.js"):
                Write(stream, 200, "text/javascript; charset=utf-8", Script);
                return;
            case ("POST", "beacon"):
                Log.Write("beacon", ("query", query), ("page", at));
                Write(stream, 204, null, null);
                return;
            default:
                Write(stream, 404, "text/plain; charset=utf-8", "not found");
                return;
        }
    }

    /// <summary>Which page a path is on a server: p1 and p1b on the first, p2 on the second.</summary>
    internal static string? PageFor(string server, string path) => (server, path.StartsWith("/t1/", StringComparison.Ordinal), path.StartsWith("/t2/", StringComparison.Ordinal)) switch
    {
        ("p1", true, _) => "p1",
        ("p1", _, true) => "p1b",
        ("p2", true, _) => "p2",
        _ => null,
    };

    private static string? ReadHead(Stream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];

        while (bytes.Count < 32 * 1024)
        {
            if (stream.Read(one, 0, 1) is 0)
            {
                return null;
            }

            bytes.Add(one[0]);

            var count = bytes.Count;

            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
            {
                return Encoding.ASCII.GetString([.. bytes]);
            }
        }

        return null;
    }

    /// <summary>The block every answer of the product's listener carries, copied from PageListener.SecurityHeaders.</summary>
    internal static readonly (string Name, string Value)[] SecurityHeaders =
    [
        ("Cache-Control", "no-store"),
        ("X-Content-Type-Options", "nosniff"),
        ("Referrer-Policy", "no-referrer"),
        ("Cross-Origin-Opener-Policy", "same-origin"),
        ("Cross-Origin-Resource-Policy", "same-origin"),
        ("X-Frame-Options", "DENY"),
        ("Content-Security-Policy", "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; form-action 'none'; frame-ancestors 'none'; base-uri 'none'"),
    ];

    private static void Write(Stream stream, int status, string? contentType, string? body)
    {
        var payload = body is null ? [] : Encoding.UTF8.GetBytes(body);
        var reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", _ => "Not Found" };
        var head = new StringBuilder().Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {status} {reason}\r\n");

        if (contentType is not null)
        {
            head.Append(CultureInfo.InvariantCulture, $"Content-Type: {contentType}\r\n");
        }

        head.Append(CultureInfo.InvariantCulture, $"Content-Length: {payload.Length}\r\nConnection: close\r\n");

        foreach (var (name, value) in SecurityHeaders)
        {
            head.Append(CultureInfo.InvariantCulture, $"{name}: {value}\r\n");
        }

        head.Append("\r\n");

        var bytes = Encoding.ASCII.GetBytes(head.ToString());
        stream.Write(bytes, 0, bytes.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    private const string Script = """
        'use strict';
        document.addEventListener('click', (event) => {
          const link = event.target instanceof Element ? event.target.closest('a[data-probe]') : null;
          if (link) {
            navigator.sendBeacon('beacon?l=' + encodeURIComponent(link.dataset.probe) + '&at=' + encodeURIComponent(document.body.dataset.at));
          }
        });
        """;

    internal static string PageText(string at, int port)
    {
        var links = at switch
        {
            "p1" => new[]
            {
                ('A', "BrowserAI's link process opens a folder itself"),
                ('C', "BrowserAI's link process shows the folder picker itself"),
                ('E', "The stand-in background opens a folder, with no help (today's product)"),
                ('B', "The link process lets the stand-in background bring a window forward, and it opens a folder"),
                ('D', "The link process lets the stand-in background bring a window forward, and it shows the folder picker"),
            },
            _ => [('A', "BrowserAI's link process opens a folder itself")],
        };

        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append(CultureInfo.InvariantCulture, $"<title>Link probe page {at} on port {port}</title>\n");
        html.Append("<script src=\"probe.js\" defer></script>\n</head>\n");
        html.Append(CultureInfo.InvariantCulture, $"<body data-at=\"{at}\">\n<h1>Link probe, page {at}, port {port}</h1>\n");
        html.Append("<p>Click one link at a time, as the steps say. Close each folder window, and cancel each picker, before the next.</p>\n<ol>\n");

        foreach (var (variant, words) in links)
        {
            html.Append(CultureInfo.InvariantCulture, $"<li><a id=\"{variant}\" data-probe=\"{variant}\" href=\"{Scheme}:{variant}?at={at}\">{variant}. {words}</a></li>\n");
        }

        html.Append("</ol>\n</body>\n</html>\n");
        return html.ToString();
    }

    // ---------------------------------------------------------------- the self-test

    private static int SelfTest(string directory)
    {
        Directory.CreateDirectory(directory);
        Log.Open(directory, "selftest");
        var report = new StringBuilder();
        var failures = 0;

        void Expect(string link, bool valid, char variant = '\0', string at = "")
        {
            var parsed = TryParse(link, out var gotVariant, out var gotAt);
            var ok = parsed == valid && (!valid || (gotVariant == variant && gotAt == at));
            failures += ok ? 0 : 1;
            report.Append(CultureInfo.InvariantCulture, $"{(ok ? "ok  " : "FAIL")} parse '{link}' -> {parsed} {(gotVariant == '\0' ? '-' : gotVariant)} {gotAt}\n");
        }

        Expect("browserai-probe:A?at=p1", true, 'A', "p1");
        Expect("browserai-probe:C?at=p1", true, 'C', "p1");
        Expect("browserai-probe:A?at=p2", true, 'A', "p2");
        Expect("browserai-probe:A?at=p1b", true, 'A', "p1b");
        Expect("BROWSERAI-PROBE:e?at=P1", true, 'E', "p1");
        Expect("browserai-probe://B/?at=p1", true, 'B', "p1");
        Expect("browserai-probe:A?at=p1/", true, 'A', "p1");
        Expect("browserai-probe:P?at=p1", true, 'P', "p1");
        Expect("browserai-probe:F?at=p1", false);
        Expect("browserai-probe:A?at=p3", false);
        Expect("browserai-probe:A?at=p1&x=1", false);
        Expect("browserai-probe:A?at=p1\" --background", false);
        Expect("vscode:A?at=p1", false);
        Expect(new string('A', 300), false);

        foreach (var (server, path, expected) in new[] { ("p1", "/t1/", "p1"), ("p1", "/t2/", "p1b"), ("p2", "/t1/", "p2"), ("p2", "/t2/", null), ("p1", "/x/", null) })
        {
            var got = PageFor(server, path);
            var ok = got == expected;
            failures += ok ? 0 : 1;
            report.Append(CultureInfo.InvariantCulture, $"{(ok ? "ok  " : "FAIL")} page {server} {path} -> {got ?? "none"}\n");
        }

        foreach (var at in new[] { "p1", "p1b", "p2" })
        {
            var text = PageText(at, 12345);
            File.WriteAllText(Path.Combine(directory, $"page-{at}.html"), text);
            var links = Regex.Matches(text, "href=\"(?<link>[^\"]+)\"").Select(match => match.Groups["link"].Value).ToList();
            var allParse = links.Count > 0 && links.All(link => TryParse(link, out _, out _));
            failures += allParse ? 0 : 1;
            report.Append(CultureInfo.InvariantCulture, $"{(allParse ? "ok  " : "FAIL")} page {at}: {links.Count} links, each parses: {string.Join(", ", links)}\n");
        }

        foreach (var (server, request, expectStatus, expectText) in new[]
        {
            ("p1", "GET /t1/ HTTP/1.1\r\nHost: 127.0.0.1:12345\r\n\r\n", "HTTP/1.1 200 OK", "browserai-probe:A?at=p1"),
            ("p1", "GET /t2/ HTTP/1.1\r\nHost: 127.0.0.1:12345\r\n\r\n", "HTTP/1.1 200 OK", "browserai-probe:A?at=p1b"),
            ("p2", "GET /t1/ HTTP/1.1\r\nHost: 127.0.0.1:12346\r\n\r\n", "HTTP/1.1 200 OK", "browserai-probe:A?at=p2"),
            ("p1", "GET /t1/probe.js HTTP/1.1\r\nHost: 127.0.0.1:12345\r\n\r\n", "HTTP/1.1 200 OK", "sendBeacon"),
            ("p1", "POST /t1/beacon?l=A&at=p1 HTTP/1.1\r\nHost: 127.0.0.1:12345\r\nContent-Length: 0\r\n\r\n", "HTTP/1.1 204 No Content", "Content-Security-Policy: default-src 'none'"),
            ("p1", "GET /favicon.ico HTTP/1.1\r\nHost: 127.0.0.1:12345\r\n\r\n", "HTTP/1.1 404 Not Found", "Connection: close"),
            ("p2", "GET /t2/ HTTP/1.1\r\nHost: 127.0.0.1:12346\r\n\r\n", "HTTP/1.1 404 Not Found", "not found"),
        })
        {
            var duplex = new Duplex(Encoding.ASCII.GetBytes(request));
            AnswerOn(duplex, server, 12345);
            var answer = Encoding.UTF8.GetString(duplex.Written.ToArray());
            var ok = answer.StartsWith(expectStatus, StringComparison.Ordinal) && answer.Contains(expectText, StringComparison.Ordinal);
            failures += ok ? 0 : 1;
            report.Append(CultureInfo.InvariantCulture, $"{(ok ? "ok  " : "FAIL")} http {server} {request.Split("\r\n")[0]} -> {answer.Split("\r\n")[0]}\n");
        }

        File.WriteAllText(Path.Combine(directory, "probe.js"), Script);
        report.Append(CultureInfo.InvariantCulture, $"command line registered: {CommandFor(Environment.ProcessPath ?? "LinkProbe.exe")}\n");
        report.Append(CultureInfo.InvariantCulture, $"failures: {failures}\n");
        File.WriteAllText(Path.Combine(directory, "selftest.txt"), report.ToString());
        return failures is 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- the analysis

    private static int Analyze(string directory)
    {
        var events = new List<JsonElement>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var source = Path.GetFileNameWithoutExtension(file);

            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement.Clone();
                events.Add(root);
                _ = source;
            }
        }

        static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) ? value.ValueKind is JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString() : string.Empty;

        static DateTime When(JsonElement element) => DateTime.Parse(Text(element, "t"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        events.Sort((left, right) => When(left).CompareTo(When(right)));

        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"Link probe report, {events.Count} events from {directory}\n\n");

        foreach (var start in events.Where(e => Text(e, "ev") == "start" && Text(e, "mode") is "observe" or "standin"))
        {
            report.Append(CultureInfo.InvariantCulture, $"{Text(start, "mode")} pid {Text(start, "pid")}: parent {Text(start, "parentImage")}, lock timeout {Text(start, "foregroundLockTimeoutMs")}\n");
        }

        report.Append('\n');

        var beacons = events.Where(e => Text(e, "ev") == "beacon").ToList();
        var handlers = events.Where(e => Text(e, "mode") == "handle" && Text(e, "ev") == "start").ToList();

        foreach (var handler in handlers)
        {
            var pid = Text(handler, "pid");
            var mine = events.Where(e => Text(e, "pid") == pid && Text(e, "mode") == "handle").ToList();
            var variant = mine.FirstOrDefault(e => Text(e, "ev") == "variant");
            var label = variant.ValueKind is JsonValueKind.Undefined ? "?" : Text(variant, "variant");
            var at = variant.ValueKind is JsonValueKind.Undefined ? "?" : Text(variant, "at");
            var started = When(handler);
            var beacon = beacons.LastOrDefault(b => When(b) <= started && Text(b, "query").Contains("l=" + label, StringComparison.Ordinal) && Text(b, "page") == at);
            var gap = beacon.ValueKind is JsonValueKind.Undefined ? (double?)null : (started - When(beacon)).TotalMilliseconds;

            report.Append(CultureInfo.InvariantCulture, $"Link {label} on page {at}, handler pid {pid}, started {started:HH:mm:ss.fff}Z by {Text(handler, "parentImage")}\n");
            report.Append(CultureInfo.InvariantCulture, $"  click to handler start: {(gap is { } ms ? ms.ToString("0", CultureInfo.InvariantCulture) + " ms" : "no click recorded")}{(gap > 800 ? " (a prompt was answered in between)" : gap is not null ? " (no prompt)" : string.Empty)}\n");

            foreach (var line in mine.Where(e => Text(e, "ev") is "round-trip" or "before-grant" or "grant" or "action-done" or "right-after" or "reply" or "refused" or "stand-in-absent"))
            {
                report.Append(CultureInfo.InvariantCulture, $"  {Text(line, "ev")}: {line.GetRawText()}\n");
            }

            foreach (var line in events.Where(e => Text(e, "mode") == "standin" && When(e) >= started && When(e) <= started.AddSeconds(8) && Text(e, "ev") is "execute" or "executed"))
            {
                report.Append(CultureInfo.InvariantCulture, $"  stand-in {Text(line, "ev")}: {line.GetRawText()}\n");
            }

            var window = events.Where(e => Text(e, "mode") == "observe" && When(e) >= started && When(e) <= started.AddSeconds(8)
                && Text(e, "ev") is "probe-window" or "foreground" or "shell")
                .Where(e => e.GetRawText().Contains($"Probe-{label}-{at}", StringComparison.Ordinal)
                    || (label is "C" or "D" && e.GetRawText().Contains("#32770", StringComparison.Ordinal)))
                .ToList();

            foreach (var line in window)
            {
                report.Append(CultureInfo.InvariantCulture, $"  {When(line):HH:mm:ss.fff} +{(When(line) - started).TotalMilliseconds:0} ms {Text(line, "ev")}: {line.GetRawText()}\n");
            }

            report.Append('\n');
        }

        File.WriteAllText(Path.Combine(directory, "report.txt"), report.ToString());
        return 0;
    }
}

internal sealed record StandInRequest(string Op, string Variant, string At, bool? Granted);

/// <summary>A stream that reads a request it was given and keeps what is written: the self-test's socket.</summary>
internal sealed class Duplex(byte[] request) : Stream
{
    private readonly MemoryStream _input = new(request);

    public MemoryStream Written { get; } = new();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
}

internal sealed record StandInReply(bool Ok, string Outcome, bool MayRaise, int MayRaiseError, long Shell);

[System.Text.Json.Serialization.JsonSerializable(typeof(StandInRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(StandInReply))]
internal sealed partial class ProbeJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>One JSON line per event, into the run folder, with the time, the tick and the pid.</summary>
internal static class Log
{
    private static readonly Lock Gate = new();
    private static string? _path;
    private static string _mode = "none";

    public static void Open(string directory, string mode)
    {
        _mode = mode;
        _path = Path.Combine(directory, $"{mode}-{Environment.ProcessId}.jsonl");
    }

    public static void Write(string name, params (string Key, object? Value)[] fields)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("t", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            writer.WriteNumber("tick", Environment.TickCount64);
            writer.WriteNumber("pid", Environment.ProcessId);
            writer.WriteString("mode", _mode);
            writer.WriteString("ev", name);

            foreach (var (key, value) in fields)
            {
                switch (value)
                {
                    case null:
                        writer.WriteNull(key);
                        break;
                    case bool flag:
                        writer.WriteBoolean(key, flag);
                        break;
                    case int number:
                        writer.WriteNumber(key, number);
                        break;
                    case long number:
                        writer.WriteNumber(key, number);
                        break;
                    case uint number:
                        writer.WriteNumber(key, number);
                        break;
                    case WindowFacts facts:
                        writer.WritePropertyName(key);
                        facts.WriteTo(writer);
                        break;
                    default:
                        writer.WriteString(key, Convert.ToString(value, CultureInfo.InvariantCulture));
                        break;
                }
            }

            writer.WriteEndObject();
        }

        var line = Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";

        lock (Gate)
        {
            File.AppendAllText(_path ?? Path.Combine(AppContext.BaseDirectory, $"orphan-{Environment.ProcessId}.jsonl"), line);
        }
    }
}

/// <summary>What one window is, with its title recorded only where it is the probe's.</summary>
internal sealed record WindowFacts(nint Handle, string Class, string Title, int Pid, string? Image, bool Visible, bool Minimized)
{
    public void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("hwnd", "0x" + Handle.ToString("X", CultureInfo.InvariantCulture));
        writer.WriteString("class", Class);
        writer.WriteString("title", Title);
        writer.WriteNumber("pid", Pid);
        writer.WriteString("image", Image);
        writer.WriteBoolean("visible", Visible);
        writer.WriteBoolean("minimized", Minimized);
        writer.WriteEndObject();
    }
}

internal static class WindowWatch
{
    private static readonly string ProbeImage = Environment.ProcessPath ?? string.Empty;

    public static WindowFacts Describe(nint window)
    {
        if (window == 0)
        {
            return new WindowFacts(0, string.Empty, string.Empty, 0, null, false, false);
        }

        _ = Native.GetWindowThreadProcessId(window, out var pid);
        var type = Native.ClassOf(window);
        var title = Native.TitleOf(window);
        var image = ProcessFacts.ImageOf((int)pid);

        // Titles are his: only the probe's own windows, and a page of the probe in his
        // browser, are recorded by name.
        var ours = title.Contains("probe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(image, ProbeImage, StringComparison.OrdinalIgnoreCase);

        return new WindowFacts(window, type, ours ? title : "(not recorded)", (int)pid, image, Native.IsWindowVisible(window), Native.IsIconic(window));
    }

    public static int ZIndex(nint window)
    {
        var index = 0;

        for (var above = Native.GetWindow(window, Native.GwHwndPrev); above != 0 && index < 5000; above = Native.GetWindow(above, Native.GwHwndPrev))
        {
            index++;
        }

        return index;
    }

    /// <summary>Logs every probe window that appears, goes or changes: an Explorer window on a Probe folder, or a dialog of the probe's.</summary>
    public static void ScanProbeWindows(Dictionary<nint, string> seen, nint foreground)
    {
        var now = new Dictionary<nint, string>();

        Native.EnumWindows((window, unused) =>
        {
            var type = Native.ClassOf(window);

            if (type is "CabinetWClass" or "#32770")
            {
                var title = Native.TitleOf(window);
                _ = Native.GetWindowThreadProcessId(window, out var pid);
                var ours = title.Contains("Probe-", StringComparison.Ordinal)
                    || (type == "#32770" && string.Equals(ProcessFacts.ImageOf((int)pid), ProbeImage, StringComparison.OrdinalIgnoreCase));

                if (ours)
                {
                    now[window] = $"{type}|{title}|{Native.IsWindowVisible(window)}";
                }
            }

            return true;
        }, 0);

        foreach (var (window, state) in now)
        {
            if (!seen.TryGetValue(window, out var before) || before != state)
            {
                Log.Write("probe-window", ("change", before is null ? "appeared" : "changed"), ("window", Describe(window)), ("z", ZIndex(window)), ("foreground", Describe(foreground)), ("foregroundZ", ZIndex(foreground)), ("isForeground", window == foreground));
            }
        }

        foreach (var window in seen.Keys.Where(window => !now.ContainsKey(window)).ToList())
        {
            Log.Write("probe-window", ("change", "gone"), ("hwnd", "0x" + window.ToString("X", CultureInfo.InvariantCulture)), ("was", seen[window]));
        }

        seen.Clear();

        foreach (var (window, state) in now)
        {
            seen[window] = state;
        }
    }
}

internal sealed record ParentFacts(int Pid, string? Image, string? Created, bool IsOlderThanThis);

internal static class ProcessFacts
{
    public static string? ImageOf(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)pid);

        if (handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            _ = Native.CloseHandle(handle);
        }
    }

    public static long? CreatedOf(int pid)
    {
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)pid);

        if (handle == 0)
        {
            return null;
        }

        try
        {
            return Native.GetProcessTimes(handle, out var created, out _, out _, out _) ? created : null;
        }
        finally
        {
            _ = Native.CloseHandle(handle);
        }
    }

    public static ParentFacts ParentOfThisProcess()
    {
        var information = default(Native.ProcessBasicInformation);
        var status = Native.NtQueryInformationProcess(Native.GetCurrentProcess(), 0, ref information, Marshal.SizeOf<Native.ProcessBasicInformation>(), out _);

        if (status != 0)
        {
            return new ParentFacts(0, null, null, false);
        }

        var parent = (int)information.InheritedFromUniqueProcessId;
        var parentCreated = CreatedOf(parent);
        var mine = CreatedOf(Environment.ProcessId);

        // A pid is reused: the parent is the one it names only if it began before this process did.
        var older = parentCreated is { } then && mine is { } now && then <= now;

        return new ParentFacts(
            parent,
            ImageOf(parent),
            parentCreated is { } stamp ? DateTime.FromFileTimeUtc(stamp).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) : null,
            older);
    }
}

/// <summary>A window that is never shown, registered for the shell's hook messages: created, activated and flashing windows.</summary>
internal sealed class ShellHook
{
    private Thread? _thread;
    private nint _window;
    private uint _shellMessage;
    private Native.WindowProcedure? _procedure;
    private readonly ManualResetEventSlim _ready = new();

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "shell hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ = _ready.Wait(5000);
    }

    public void Stop()
    {
        if (_window != 0)
        {
            _ = Native.PostMessageW(_window, Native.WmClose, 0, 0);
        }

        _ = _thread?.Join(3000);
    }

    private void Run()
    {
        _procedure = Procedure;
        var module = Native.GetModuleHandleW(null);
        var type = new Native.WindowClass
        {
            Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = module,
            ClassName = "LinkProbeShellHook",
        };

        var atom = Native.RegisterClassExW(ref type);
        _window = Native.CreateWindowExW(0, "LinkProbeShellHook", "Link probe shell hook", 0, 0, 0, 0, 0, 0, 0, module, 0);
        _shellMessage = Native.RegisterWindowMessageW("SHELLHOOK");
        var registered = _window != 0 && Native.RegisterShellHookWindow(_window);

        Log.Write("shell-hook", ("classAtom", (int)atom), ("window", "0x" + _window.ToString("X", CultureInfo.InvariantCulture)), ("registered", registered), ("message", _shellMessage));
        _ready.Set();

        while (Native.GetMessageW(out var message, 0, 0, 0) > 0)
        {
            _ = Native.TranslateMessage(ref message);
            _ = Native.DispatchMessageW(ref message);
        }
    }

    private nint Procedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == _shellMessage && _shellMessage != 0)
        {
            var code = (int)wParam;
            var name = code switch
            {
                1 => "created",
                2 => "destroyed",
                4 => "activated",
                6 => "redraw",
                0x8004 => "rude-activated",
                0x8006 => "flash",
                13 => "replaced",
                14 => "replacing",
                16 => "monitor-changed",
                _ => "code-" + code.ToString(CultureInfo.InvariantCulture),
            };

            var facts = WindowWatch.Describe(lParam);

            // A title changing anywhere is a redraw, many a second in a terminal: only the
            // probe's own windows and Explorer's are worth a line.
            var worth = code switch
            {
                2 => false,
                6 or 16 => facts.Class is "CabinetWClass" || facts.Title != "(not recorded)",
                _ => true,
            };

            if (worth)
            {
                Log.Write("shell", ("what", name), ("window", facts), ("foreground", WindowWatch.Describe(Native.GetForegroundWindow())));
            }

            return 0;
        }

        switch (message)
        {
            case Native.WmClose:
                _ = Native.DeregisterShellHookWindow(window);
                _ = Native.DestroyWindow(window);
                return 0;
            case Native.WmDestroy:
                Native.PostQuitMessage(0);
                return 0;
            default:
                return Native.DefWindowProcW(window, message, wParam, lParam);
        }
    }
}

internal static partial class Native
{
    public const int SwShowNormal = 1;
    public const uint GwHwndPrev = 3;
    public const uint WmClose = 0x0010;
    public const uint WmDestroy = 0x0002;
    public const uint ProcessQueryLimitedInformation = 0x1000;

    public delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    public delegate bool EnumWindowsProcedure(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Message
    {
        public nint Window;
        public uint Value;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public nint Owner;
        public nint Root;
        public nint DisplayName;
        public nint Title;
        public uint Flags;
        public nint Callback;
        public nint Parameter;
        public int Image;
    }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint window, out uint pid);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(uint pid);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProcedure callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int InternalGetWindowText(nint window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref uint value, uint flags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WindowClass type);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterShellHookWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeregisterShellHookWindow(nint window);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out Message message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessageW(out Message message, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessageW(ref Message message);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int code);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjects(uint count, nint[]? handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds, uint wakeMask);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? name);

    [DllImport("kernel32.dll")]
    public static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeServerProcessId(SafeHandle pipe, out uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out uint pid);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(nint process, int informationClass, ref ProcessBasicInformation information, int length, out int returned);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint ShellExecuteW(nint owner, string? verb, string file, string? parameters, string? directory, int show);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHBrowseForFolderW(ref BrowseInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListEx(nint list, nint path, int characters, uint options);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(nint block);

    public static string ClassOf(nint window)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassNameW(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static string TitleOf(nint window)
    {
        var buffer = new StringBuilder(512);
        _ = InternalGetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static uint LastInputTick()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.Time : 0;
    }

    public static uint ForegroundLockTimeout()
    {
        uint value = 0;
        return SystemParametersInfoW(0x2000, 0, ref value, 0) ? value : uint.MaxValue;
    }

    /// <summary>Lets the thread's messages run for a while, as a thread that lives on would.</summary>
    public static void Pump(TimeSpan duration)
    {
        var until = Environment.TickCount64 + (long)duration.TotalMilliseconds;

        while (Environment.TickCount64 < until)
        {
            _ = MsgWaitForMultipleObjects(0, null, false, (uint)Math.Max(1, until - Environment.TickCount64), 0x04FF);

            while (PeekMessageW(out var message, 0, 0, 0, 1))
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
            }
        }
    }

    /// <summary>The product's picker: SHBrowseForFolderW, only file-system folders, the new style, no owner.</summary>
    public static string PickFolder(string prompt)
    {
        var title = Marshal.StringToHGlobalUni(prompt);
        var display = Marshal.AllocHGlobal(sizeof(char) * 260);
        var path = Marshal.AllocHGlobal(sizeof(char) * 32768);
        var list = nint.Zero;

        try
        {
            var info = new BrowseInfo { Owner = 0, DisplayName = display, Title = title, Flags = 0x0001 | 0x0040 };
            list = SHBrowseForFolderW(ref info);

            if (list == 0)
            {
                return "cancelled";
            }

            return SHGetPathFromIDListEx(list, path, 32768, 0) ? "picked " + Marshal.PtrToStringUni(path) : "picked, no path";
        }
        finally
        {
            if (list != 0)
            {
                CoTaskMemFree(list);
            }

            Marshal.FreeHGlobal(path);
            Marshal.FreeHGlobal(display);
            Marshal.FreeHGlobal(title);
        }
    }
}
