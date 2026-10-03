// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// sprobe: option S's listener with a page, an event stream and one write, plus
// a second, unguarded listener that plays "some other site" in the browser tests.
//
//   sprobe <portFile> <logFile>
//
// portFile gets: port <TAB> token <TAB> attackerPort
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SProbe;

var clock = Stopwatch.StartNew();
var portFile = args[0];
var logGate = new Lock();
using var logFile = new StreamWriter(args[1], append: false, new UTF8Encoding(false)) { AutoFlush = true };

void Log(string line)
{
    lock (logGate)
    {
        logFile.WriteLine($"{clock.Elapsed.TotalMilliseconds:F1}\t{line}");
    }
}

var pages = new ConcurrentDictionary<Socket, byte>();
var counter = 0;
var quit = new ManualResetEventSlim(false);

const string Html = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>BrowserAI</title><link rel=\"stylesheet\" href=\"app.css\"><script src=\"app.js\" defer></script></head><body><h1>BrowserAI</h1><p id=\"link\">starting</p><p id=\"state\">-</p></body></html>";
const string Css = "html{color-scheme:light dark;font:14px system-ui}body{margin:24px}";
const string Js = """
const es = new EventSource('events');
es.onopen = () => { document.getElementById('link').textContent = 'connected'; };
es.onerror = () => { document.getElementById('link').textContent = 'reconnecting'; };
es.onmessage = (e) => { document.getElementById('state').textContent = e.data; };
window.act = () => fetch('act', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }).then((r) => r.status);
""";

byte[] State() => Encoding.UTF8.GetBytes($"data: {{\"counter\":{Volatile.Read(ref counter)},\"pages\":{pages.Count}}}\n\n");

async Task Broadcast()
{
    var bytes = State();
    foreach (var page in pages.Keys)
    {
        try
        {
            _ = await page.SendAsync(bytes, SocketFlags.None).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is SocketException or ObjectDisposedException)
        {
        }
    }
}

async Task<bool> Route(Request request, string path, Socket client)
{
    switch (request.Method, path)
    {
        case ("GET", ""):
            await Loopback.SendAsync(client, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Html)).ConfigureAwait(false);
            return true;
        case ("GET", "app.css"):
            await Loopback.SendAsync(client, "200 OK", "text/css; charset=utf-8", Encoding.UTF8.GetBytes(Css)).ConfigureAwait(false);
            return true;
        case ("GET", "app.js"):
            await Loopback.SendAsync(client, "200 OK", "text/javascript; charset=utf-8", Encoding.UTF8.GetBytes(Js)).ConfigureAwait(false);
            return true;
        case ("GET", "state"):
            await Loopback.SendAsync(client, "200 OK", "application/json", Encoding.UTF8.GetBytes($"{{\"counter\":{Volatile.Read(ref counter)}}}")).ConfigureAwait(false);
            return true;
        case ("GET", "events"):
            {
                await Loopback.OpenStreamAsync(client).ConfigureAwait(false);
                pages[client] = 0;
                Log($"+page\tn={pages.Count}");
                _ = await client.SendAsync(State(), SocketFlags.None).ConfigureAwait(false);

                // No timer and no ping: the kernel completes this read when the tab, the
                // browser or the connection goes. That completion is the liveness signal.
                var one = new byte[1];
                try
                {
                    while (await client.ReceiveAsync(one, SocketFlags.None).ConfigureAwait(false) > 0)
                    {
                    }
                }
                catch (SocketException)
                {
                }

                _ = pages.TryRemove(client, out _);
                Log($"-page\tn={pages.Count}");
                return true;
            }

        case ("POST", "act"):
            _ = Interlocked.Increment(ref counter);
            await Loopback.SendAsync(client, "204 No Content", "text/plain", []).ConfigureAwait(false);
            await Broadcast().ConfigureAwait(false);
            return true;
        case ("POST", "quit"):
            await Loopback.SendAsync(client, "204 No Content", "text/plain", []).ConfigureAwait(false);
            quit.Set();
            return true;
        default:
            return false;
    }
}

// sprobe <portFile> <logFile> [<port> <token>]: the last two take a port and a token again.
Loopback loopback;
try
{
    loopback = args.Length >= 4 ? new Loopback(Route, Log, int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3]) : new Loopback(Route, Log);
}
catch (SocketException failure)
{
    File.WriteAllText(portFile + ".error", $"{failure.SocketErrorCode}	{clock.Elapsed.TotalMilliseconds:F2}");
    return 3;
}

using var disposeLoopback = loopback;

// "Some other site": no gate at all, one blank page for any request.
using var other = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
other.Bind(new IPEndPoint(IPAddress.Loopback, 0));
other.Listen(16);
var otherPort = ((IPEndPoint)other.LocalEndPoint!).Port;
_ = Task.Run(async () =>
{
    var page = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: 46\r\nConnection: close\r\n\r\n<!doctype html><title>other site</title><p>x</p>");
    while (true)
    {
        Socket client;
        try
        {
            client = await other.AcceptAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            var buffer = new byte[4096];
            _ = await client.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);
            _ = await client.SendAsync(page, SocketFlags.None).ConfigureAwait(false);
        }
        catch (SocketException)
        {
        }
        finally
        {
            client.Dispose();
        }
    }
});

File.WriteAllText(portFile + ".tmp", $"{loopback.Port}\t{loopback.Token}\t{otherPort}\t{clock.Elapsed.TotalMilliseconds:F2}");
File.Move(portFile + ".tmp", portFile, overwrite: true);
Log($"listening\t{loopback.Port}\tother={otherPort}");

// The test ends it through POST quit, or by closing its stdin.
_ = Task.Run(() =>
{
    try
    {
        while (Console.In.Read() >= 0)
        {
        }
    }
    catch (IOException)
    {
    }

    quit.Set();
});

quit.Wait();
Log("stopping");
return 0;
