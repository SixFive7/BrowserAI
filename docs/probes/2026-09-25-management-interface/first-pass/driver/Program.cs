// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Track B's measurement driver. JIT-compiled on purpose: it is the instrument,
// not the subject. Every probe it starts is its own child, started with no
// window (console subsystem + CreateNoWindow, streams redirected), bound to
// 127.0.0.1 only, and stopped through its own /quit or, failing that, through
// the Process object this driver holds for it. Nothing is ever selected by name.
//
//   driver measure <root> <runs> <probe>...      startup, latency, memory
//   driver security <root> <probe> [probe args]   what the server answers by default
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

var mode = args[0];
var root = Path.GetFullPath(args[1]);
var runDir = Path.Combine(root, "run");
Directory.CreateDirectory(runDir);

return mode switch
{
    "measure" => Measure(root, runDir, int.Parse(args[2], CultureInfo.InvariantCulture), args[3..]),
    "security" => Security(root, runDir, args[2], args[3..]),
    "webview2" => Wv2Launch.Run(root, args[2], Path.GetFullPath(args[3]), int.Parse(args[4], CultureInfo.InvariantCulture)),
    _ => 64,
};

static void DeleteWithRetry(string path)
{
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            File.Delete(path);
            return;
        }
        catch (IOException) when (attempt < 200)
        {
            Thread.Sleep(5);
        }
    }
}

static string ExeOf(string root, string probe) => Path.Combine(root, "out", probe, probe + ".exe");

static Process StartProbe(string exe, string portFile, IEnumerable<string> extra)
{
    var psi = new ProcessStartInfo(exe)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        WorkingDirectory = Path.GetDirectoryName(exe)!,
    };
    psi.ArgumentList.Add(portFile);
    foreach (var e in extra)
    {
        psi.ArgumentList.Add(e);
    }

    var p = Process.Start(psi)!;
    p.OutputDataReceived += (_, _) => { };
    p.ErrorDataReceived += (_, _) => { };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    return p;
}

// Waits for the probe's announcement; returns (port, probe's own ms since Main), or null if it exited first.
static (int Port, double ProbeMs)? AwaitAnnouncement(Process p, string portFile, TimeSpan budget)
{
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < budget)
    {
        if (File.Exists(portFile))
        {
            // A just-renamed file can be held briefly by something else on the
            // machine (a scanner): retry the read, do not treat it as absent.
            string text;
            try
            {
                using var stream = new FileStream(portFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
            catch (IOException)
            {
                Thread.SpinWait(200);
                continue;
            }

            var parts = text.Trim().Split('\t');
            return (int.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        if (p.HasExited)
        {
            return null;
        }

        Thread.SpinWait(200);
    }

    return null;
}

static (string Status, string Head, string Body, double Ms) Raw(IPAddress address, int port, string request)
{
    var clock = Stopwatch.StartNew();
    using var s = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ReceiveTimeout = 5000, SendTimeout = 5000, NoDelay = true };
    s.Connect(new IPEndPoint(address, port));
    s.Send(Encoding.ASCII.GetBytes(request));
    using var all = new MemoryStream();
    var buffer = new byte[8192];
    int n;
    while ((n = s.Receive(buffer)) > 0)
    {
        all.Write(buffer, 0, n);
    }

    var ms = clock.Elapsed.TotalMilliseconds;
    var text = Encoding.UTF8.GetString(all.ToArray());
    var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
    var head = split < 0 ? text : text[..split];
    var body = split < 0 ? string.Empty : text[(split + 4)..];
    var status = head.Split("\r\n")[0];
    return (status, head, body, ms);
}

static string Get(string path, string host, string extraHeaders = "") =>
    $"GET {path} HTTP/1.1\r\nHost: {host}\r\n{extraHeaders}Connection: close\r\n\r\n";

static double Percentile(List<double> values, double p)
{
    var sorted = values.OrderBy(v => v).ToList();
    if (sorted.Count == 0)
    {
        return double.NaN;
    }

    var rank = p * (sorted.Count - 1);
    var lo = (int)Math.Floor(rank);
    var hi = (int)Math.Ceiling(rank);
    return sorted[lo] + ((sorted[hi] - sorted[lo]) * (rank - lo));
}

static void StopOwnChild(Process p, int port)
{
    try
    {
        if (port > 0)
        {
            _ = Raw(IPAddress.Loopback, port, Get("/quit", $"127.0.0.1:{port}"));
            _ = Raw(IPAddress.Loopback, port, Get("/quit", $"localhost:{port}"));
        }
    }
    catch (SocketException)
    {
    }

    if (!p.WaitForExit(5000))
    {
        p.Kill(entireProcessTree: false);
        p.WaitForExit();
    }

    p.WaitForExit();
}

static int Measure(string root, string runDir, int runs, string[] probes)
{
    var report = new StringBuilder();
    report.AppendLine(CultureInfo.InvariantCulture, $"# measured {DateTimeOffset.UtcNow:O} on {Environment.OSVersion.VersionString}, {Environment.ProcessorCount} logical processors");
    report.AppendLine("probe\tbytes\tstart_to_listening_ms_p50\tp10\tp90\tprobe_main_to_listening_ms_p50\tfirst_request_ms_p50\tsteady_request_ms_p50\tworking_set_MB_p50\tprivate_MB_p50\texit_after_quit_ms_p50");

    foreach (var probe in probes)
    {
        var exe = ExeOf(root, probe);
        var bytes = new FileInfo(exe).Length;
        var announce = new List<double>();
        var probeMs = new List<double>();
        var first = new List<double>();
        var steady = new List<double>();
        var ws = new List<double>();
        var priv = new List<double>();
        var exitMs = new List<double>();

        for (var i = -2; i < runs; i++)
        {
            var portFile = Path.Combine(runDir, $"{probe}-{i + 2}.port");
            DeleteWithRetry(portFile);
            var clock = Stopwatch.StartNew();
            var p = StartProbe(exe, portFile, []);
            var seen = AwaitAnnouncement(p, portFile, TimeSpan.FromSeconds(30));
            var tAnnounce = clock.Elapsed.TotalMilliseconds;
            if (seen is null)
            {
                Console.Error.WriteLine($"{probe}: no announcement; exited={p.HasExited}");
                StopOwnChild(p, 0);
                return 2;
            }

            var (port, pms) = seen.Value;
            if (port == 0)
            {
                // The baseline announces and exits.
                p.WaitForExit();
                if (i >= 0)
                {
                    announce.Add(tAnnounce);
                    probeMs.Add(pms);
                }

                continue;
            }

            var r1 = Raw(IPAddress.Loopback, port, Get("/api/state", $"127.0.0.1:{port}"));
            var perRun = new List<double>();
            for (var k = 0; k < 30; k++)
            {
                perRun.Add(Raw(IPAddress.Loopback, port, Get("/api/state", $"127.0.0.1:{port}")).Ms);
            }

            p.Refresh();
            var wsMb = p.WorkingSet64 / 1048576.0;
            var privMb = p.PrivateMemorySize64 / 1048576.0;
            var quitClock = Stopwatch.StartNew();
            StopOwnChild(p, port);
            var tExit = quitClock.Elapsed.TotalMilliseconds;

            if (!r1.Status.Contains("200", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{probe}: first answer was '{r1.Status}'");
            }

            if (i >= 0)
            {
                announce.Add(tAnnounce);
                probeMs.Add(pms);
                first.Add(r1.Ms);
                steady.Add(Percentile(perRun, 0.5));
                ws.Add(wsMb);
                priv.Add(privMb);
                exitMs.Add(tExit);
            }
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"{probe}\t{bytes}\t{Percentile(announce, 0.5):F2}\t{Percentile(announce, 0.1):F2}\t{Percentile(announce, 0.9):F2}\t{Percentile(probeMs, 0.5):F2}\t{Percentile(first, 0.5):F3}\t{Percentile(steady, 0.5):F3}\t{Percentile(ws, 0.5):F1}\t{Percentile(priv, 0.5):F1}\t{Percentile(exitMs, 0.5):F1}");
    }

    Console.Write(report.ToString());
    return 0;
}

static int Security(string root, string runDir, string probe, string[] probeArgs)
{
    var exe = ExeOf(root, probe);
    var portFile = Path.Combine(runDir, $"{probe}-security.port");
    DeleteWithRetry(portFile);
    DeleteWithRetry(portFile + ".error");
    var p = StartProbe(exe, portFile, probeArgs);
    var seen = AwaitAnnouncement(p, portFile, TimeSpan.FromSeconds(30));
    if (seen is null)
    {
        p.WaitForExit();
        var error = File.Exists(portFile + ".error") ? File.ReadAllText(portFile + ".error").Trim() : "(no error file)";
        Console.WriteLine($"{probe} {string.Join(' ', probeArgs)}: DID NOT LISTEN, exit {p.ExitCode}: {error}");
        return 0;
    }

    var port = seen.Value.Port;
    var lines = new List<string> { $"== {probe} {string.Join(' ', probeArgs)} on 127.0.0.1:{port}" };

    void Try(string label, IPAddress address, string request)
    {
        try
        {
            var r = Raw(address, port, request);
            var bodyStart = r.Body.Length > 60 ? r.Body[..60] + "..." : r.Body;
            lines.Add($"{label,-58} -> {r.Status} | body: {bodyStart.Replace("\r", "\\r").Replace("\n", "\\n")}");
        }
        catch (SocketException failure)
        {
            lines.Add($"{label,-58} -> no connection: {failure.SocketErrorCode}");
        }
    }

    var lo = IPAddress.Loopback;
    Try("A GET /api/state, Host: 127.0.0.1:port", lo, Get("/api/state", $"127.0.0.1:{port}"));
    Try("B GET /api/state, Host: localhost:port", lo, Get("/api/state", $"localhost:{port}"));
    Try("C GET /api/state, Host: attacker.example:port (rebinding)", lo, Get("/api/state", $"attacker.example:{port}"));
    Try("D GET /api/state, Host: attacker.example (no port)", lo, Get("/api/state", "attacker.example"));
    Try("E GET /api/state, HTTP/1.0 with no Host header", lo, "GET /api/state HTTP/1.0\r\n\r\n");
    Try("F GET /api/state, Origin: http://attacker.example", lo, Get("/api/state", $"127.0.0.1:{port}", "Origin: http://attacker.example\r\nSec-Fetch-Site: cross-site\r\n"));
    Try("G POST /api/state text/plain, Origin: attacker (CSRF)", lo,
        $"POST /api/state HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nOrigin: http://attacker.example\r\nContent-Type: text/plain\r\nContent-Length: 2\r\nConnection: close\r\n\r\nhi");
    Try("H OPTIONS preflight from attacker", lo,
        $"OPTIONS /api/state HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nOrigin: http://attacker.example\r\nAccess-Control-Request-Method: POST\r\nConnection: close\r\n\r\n");
    Try("I same port on IPv6 loopback [::1]", IPAddress.IPv6Loopback, Get("/api/state", $"[::1]:{port}"));

    foreach (var address in NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(u => u.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
        .Take(2))
    {
        // The address itself is not recorded: only whether the port answered on it.
        Try("J same port on a non-loopback IPv4 address of this machine", address, Get("/api/state", $"x:{port}"));
        Try("K same, but claiming Host: localhost:port", address, Get("/api/state", $"localhost:{port}"));
    }

    StopOwnChild(p, port);
    lines.Add($"   (probe exited {p.ExitCode})");
    Console.WriteLine(string.Join(Environment.NewLine, lines));
    return 0;
}
