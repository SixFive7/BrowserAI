// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probe A/B: what actually bounds a STALLED Velopack download, and a STALLED
// Velopack update CHECK?  Velopack 1.2.158 downloads through
// HttpClientFileDownloader, whose only bound is
// client.Timeout = TimeSpan.FromMinutes(SimpleWebSource.Timeout), default 30.
//   arm 1 CONTROL     server ends the body early; proves the rig sees a body read end.
//   arm 2 the DOWNLOAD shape: GetAsync(ResponseHeadersRead) then read the stream.
//   arm 3 the CHECK   shape: GetStringAsync (ResponseContentRead).
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
Console.WriteLine("############ PROBE A/B -- what bounds a stalled HTTP read?");
Console.WriteLine($".NET 10.0.401 | Windows 11 Pro 10.0.26200 | 2026-09-23 | loopback:{port}");
Console.WriteLine();

_ = Task.Run(async () =>
{
    while (true)
    {
        var c = await listener.AcceptTcpClientAsync();
        _ = Task.Run(async () =>
        {
            using (c)
            {
                var s = c.GetStream();
                var buf = new byte[4096];
                var read = await s.ReadAsync(buf);
                var trickleFor = Encoding.ASCII.GetString(buf, 0, read).Contains("/short") ? 2 : 60;
                var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1000000\r\nContent-Type: application/octet-stream\r\n\r\n");
                await s.WriteAsync(head); await s.FlushAsync();
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < trickleFor)
                { await s.WriteAsync(new byte[] { 0x41 }); await s.FlushAsync(); await Task.Delay(1000); }
            }
        });
    }
});

await Headers("CONTROL: server ends the body at 2 s, client Timeout = 5 s", $"http://127.0.0.1:{port}/short", 5);
await Headers("THE DOWNLOAD SHAPE: GetAsync(ResponseHeadersRead) + stream read, trickle 60 s, Timeout = 5 s", $"http://127.0.0.1:{port}/long", 5);
await Whole("THE CHECK SHAPE: GetStringAsync (ResponseContentRead), trickle 60 s, Timeout = 5 s", $"http://127.0.0.1:{port}/long", 5);
return 0;

static async Task Headers(string label, string url, int timeoutSeconds)
{
    Console.WriteLine("--- " + label);
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    var sw = Stopwatch.StartNew();
    try
    {
        // Exactly HttpClientFileDownloader.DownloadToStreamInternal's shape.
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        Console.WriteLine($"    headers at {sw.Elapsed.TotalSeconds:0.00} s, status {(int)response.StatusCode}, content-length {response.Content.Headers.ContentLength}");
        using var body = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[81920];
        long total = 0; int n;
        while ((n = await body.ReadAsync(buffer)) != 0) total += n;
        Console.WriteLine($"    RESULT: body read COMPLETED at {sw.Elapsed.TotalSeconds:0.00} s, {total} bytes");
    }
    catch (Exception e) { Report(e, sw); }
    Console.WriteLine();
}

static async Task Whole(string label, string url, int timeoutSeconds)
{
    Console.WriteLine("--- " + label);
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    var sw = Stopwatch.StartNew();
    try
    {
        // Exactly HttpClientFileDownloader.DownloadString's shape, which is what
        // SimpleWebSource.GetReleaseFeed -- and so CheckForUpdatesAsync -- uses.
        var s = await client.GetStringAsync(url);
        Console.WriteLine($"    RESULT: COMPLETED at {sw.Elapsed.TotalSeconds:0.00} s, {s.Length} chars");
    }
    catch (Exception e) { Report(e, sw); }
    Console.WriteLine();
}

static void Report(Exception e, Stopwatch sw)
{
    Console.WriteLine($"    RESULT: THREW at {sw.Elapsed.TotalSeconds:0.00} s -- {e.GetType().FullName}");
    Console.WriteLine($"            is OperationCanceledException: {e is OperationCanceledException}   <-- UpdateService.RunOnceAsync catches this and logs UpdateLog.TripwireFired");
    Console.WriteLine($"            message: {e.Message.ReplaceLineEndings(" ")}");
    if (e.InnerException is { } i) Console.WriteLine($"            inner: {i.GetType().FullName}: {i.Message.ReplaceLineEndings(" ")}");
}
