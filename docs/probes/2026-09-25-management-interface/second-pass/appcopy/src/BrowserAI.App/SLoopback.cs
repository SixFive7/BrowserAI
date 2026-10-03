// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#if S_LOOPBACK
#pragma warning disable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
// Option S's listener, as small as it can be made and still be safe.
//
// One raw socket on 127.0.0.1, exclusive, on a port the system picks. Every
// request passes ONE gate before any route is looked at, and anything the gate
// does not admit gets the same bare 404. There are no cookies, no CORS headers,
// and nothing that changes state on a GET.


/// <summary>One parsed request. Nothing here has been trusted yet.</summary>
internal sealed record Request(string Method, string Target, Dictionary<string, List<string>> Headers, byte[] Body)
{
    public string? Single(string name) => Headers.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;

    public bool Has(string name) => Headers.ContainsKey(name);
}

/// <summary>Why the gate turned a request away. Never sent to the caller; logged.</summary>
internal enum Refusal
{
    None,
    Malformed,
    Method,
    Host,
    Token,
    FetchSite,
    Origin,
    ContentType,
    TooLarge,
}

internal sealed class Loopback : IDisposable
{
    public const int MaxHead = 8 * 1024;
    public const int MaxBody = 64 * 1024;
    public const int MaxConnections = 32;

    private const string SecurityHeaders =
        "Cache-Control: no-store\r\n" +
        "X-Content-Type-Options: nosniff\r\n" +
        "Referrer-Policy: no-referrer\r\n" +
        "Cross-Origin-Opener-Policy: same-origin\r\n" +
        "Cross-Origin-Resource-Policy: same-origin\r\n" +
        "Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'\r\n";

    private readonly Socket _listener;
    private readonly byte[] _token;
    private readonly string _host;
    private readonly string _origin;
    private readonly Func<Request, string, Socket, Task<bool>> _route;
    private readonly Action<string> _log;
    private int _connections;

    /// <summary>When set, a connection whose owner is not this user gets the bare 404.</summary>
    public static bool EnforcePeer { get; } = Environment.GetEnvironmentVariable("SPROBE_ENFORCE_PEER") == "1";

    /// <param name="route">Called only for admitted requests, with the path after the token. Returns false for "no such route".</param>
    /// <param name="resumePort">A port to take again, with <paramref name="resumeToken"/>: the restart after an update. Zero for a new one.</param>
    public Loopback(Func<Request, string, Socket, Task<bool>> route, Action<string> log, int resumePort = 0, string? resumeToken = null)
    {
        _route = route;
        _log = log;
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        _listener.Bind(new IPEndPoint(IPAddress.Loopback, resumePort));
        _listener.Listen(MaxConnections);
        Port = ((IPEndPoint)_listener.LocalEndPoint!).Port;
        Token = resumeToken ?? Base64Url(RandomNumberGenerator.GetBytes(32));
        _token = Encoding.ASCII.GetBytes(Token);
        _host = $"127.0.0.1:{Port}";
        _origin = $"http://127.0.0.1:{Port}";
        _ = AcceptAsync();
    }

    public int Port { get; }

    /// <summary>256 random bits, in the path of every URL this listener answers.</summary>
    public string Token { get; }

    public string Url => $"{_origin}/{Token}/";

    private async Task AcceptAsync()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is SocketException or ObjectDisposedException)
            {
                return;
            }

            if (Interlocked.Increment(ref _connections) > MaxConnections)
            {
                _ = Interlocked.Decrement(ref _connections);
                client.Dispose();
                continue;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(Socket client)
    {
        try
        {
            client.NoDelay = true;

            // Optional: answer only a process of this user. Checked before a byte is read.
            var peer = Peer.Check(client);
            _log($"peer	pid={peer.ProcessId}	{peer.Why}	{peer.Milliseconds:F3} ms");
            if (EnforcePeer && !peer.SameUser)
            {
                await NotFoundAsync(client).ConfigureAwait(false);
                return;
            }

            var (request, malformed) = await ReadAsync(client).ConfigureAwait(false);
            var refusal = request is null ? malformed : Admit(request);

            if (refusal is not Refusal.None || request is null)
            {
                _log($"deny\t{refusal}\t{request?.Method} {Shorten(request?.Target)}\thost={request?.Single("host")}\torigin={request?.Single("origin")}\tsite={request?.Single("sec-fetch-site")}");
                await NotFoundAsync(client).ConfigureAwait(false);
                return;
            }

            var path = request.Target[(Token.Length + 2)..];
            _log($"admit\t{request.Method} {Shorten(path)}\torigin={request.Single("origin")}\tsite={request.Single("sec-fetch-site")}\tdest={request.Single("sec-fetch-dest")}");
            if (!await _route(request, path, client).ConfigureAwait(false))
            {
                await NotFoundAsync(client).ConfigureAwait(false);
            }
        }
        catch (Exception failure) when (failure is SocketException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            _ = Interlocked.Decrement(ref _connections);
            client.Dispose();
        }
    }

    /// <summary>
    /// The one gate. Order matters only for the log; the answer to the caller is
    /// the same bare 404 whichever check failed.
    /// </summary>
    public Refusal Admit(Request request)
    {
        if (request.Method is not ("GET" or "POST"))
        {
            return Refusal.Method;
        }

        // Exactly one Host header, exactly ours. A rebinding page carries its own name here.
        if (request.Single("host") != _host)
        {
            return Refusal.Host;
        }

        // The capability: "/<token>/..." and nothing else, compared in constant time.
        var target = request.Target;
        if (target.Length < _token.Length + 2
            || target[0] != '/'
            || target[_token.Length + 1] != '/'
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(target.Substring(1, _token.Length)), _token))
        {
            return Refusal.Token;
        }

        // A browser says where a request came from. "none" is a typed or opened URL.
        var site = request.Single("sec-fetch-site");
        if (request.Has("sec-fetch-site") && site is not ("same-origin" or "none"))
        {
            return Refusal.FetchSite;
        }

        if (request.Method is "POST")
        {
            if (request.Single("origin") != _origin || site is "none")
            {
                return Refusal.Origin;
            }

            if (request.Single("content-type") is not "application/json")
            {
                return Refusal.ContentType;
            }
        }
        else if (request.Has("origin") && request.Single("origin") != _origin)
        {
            return Refusal.Origin;
        }

        return Refusal.None;
    }

    private static async Task<(Request? Request, Refusal Why)> ReadAsync(Socket client)
    {
        var buffer = new byte[MaxHead];
        var filled = 0;
        var headEnd = -1;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (headEnd < 0)
        {
            if (filled == buffer.Length)
            {
                return (null, Refusal.TooLarge);
            }

            int read;
            try
            {
                read = await client.ReceiveAsync(buffer.AsMemory(filled), SocketFlags.None, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (null, Refusal.Malformed);
            }

            if (read == 0)
            {
                return (null, Refusal.Malformed);
            }

            filled += read;
            headEnd = buffer.AsSpan(0, filled).IndexOf("\r\n\r\n"u8);
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headEnd).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3 || first[2] is not ("HTTP/1.1" or "HTTP/1.0") || first[1].Length == 0 || first[1][0] != '/')
        {
            return (null, Refusal.Malformed);
        }

        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.AsSpan(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || line[0] is ' ' or '\t')
            {
                return (null, Refusal.Malformed);
            }

            var name = line[..colon];
            if (!headers.TryGetValue(name, out var values))
            {
                headers[name] = values = [];
            }

            values.Add(line[(colon + 1)..].Trim());
        }

        var body = Array.Empty<byte>();
        if (headers.ContainsKey("transfer-encoding"))
        {
            return (null, Refusal.Malformed);
        }

        if (headers.TryGetValue("content-length", out var lengths))
        {
            if (lengths.Count != 1 || !int.TryParse(lengths[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var length))
            {
                return (null, Refusal.Malformed);
            }

            if (length > MaxBody)
            {
                return (null, Refusal.TooLarge);
            }

            body = new byte[length];
            var have = Math.Min(length, filled - (headEnd + 4));
            buffer.AsSpan(headEnd + 4, have).CopyTo(body);
            while (have < length)
            {
                int read;
                try
                {
                    read = await client.ReceiveAsync(body.AsMemory(have), SocketFlags.None, deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return (null, Refusal.Malformed);
                }

                if (read == 0)
                {
                    return (null, Refusal.Malformed);
                }

                have += read;
            }
        }

        return (new Request(first[0], first[1], headers, body), Refusal.None);
    }

    public static Task SendAsync(Socket client, string status, string contentType, byte[] body) =>
        client.SendAsync(Compose(status, $"Content-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n", body), SocketFlags.None);

    /// <summary>The head of a response that never ends: the page's event stream.</summary>
    public static Task OpenStreamAsync(Socket client) =>
        client.SendAsync(Compose("200 OK", "Content-Type: text/event-stream\r\nConnection: close\r\n", []), SocketFlags.None);

    private static Task NotFoundAsync(Socket client) =>
        client.SendAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), SocketFlags.None);

    private static byte[] Compose(string status, string headers, byte[] body)
    {
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{SecurityHeaders}{headers}\r\n");
        var all = new byte[head.Length + body.Length];
        head.CopyTo(all, 0);
        body.CopyTo(all, head.Length);
        return all;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string Shorten(string? target) => target is null ? string.Empty : target.Replace(Token, "<token>", StringComparison.Ordinal);

    public void Dispose() => _listener.Dispose();
}

// Who is on the other end of a loopback connection: the owning pid from the
// system's TCP table, then that process's user, compared with ours. TCP has no
// per-user access list; this is the check that stands in for one.


internal static unsafe class Peer
{
    private static readonly byte[] Mine = UserOf(GetCurrentProcess());

    public readonly record struct Verdict(bool SameUser, int ProcessId, string Why, double Milliseconds);

    public static Verdict Check(Socket client)
    {
        var clock = Stopwatch.StartNew();
        var remote = (IPEndPoint)client.RemoteEndPoint!;
        var local = (IPEndPoint)client.LocalEndPoint!;
        var pid = OwnerOf(remote, local);
        if (pid == 0)
        {
            return new Verdict(false, 0, "no row in the TCP table", clock.Elapsed.TotalMilliseconds);
        }

        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid);
        if (process == 0)
        {
            return new Verdict(false, pid, $"OpenProcess failed ({Marshal.GetLastWin32Error()})", clock.Elapsed.TotalMilliseconds);
        }

        try
        {
            var theirs = UserOf(process);
            var same = theirs.Length != 0 && theirs.AsSpan().SequenceEqual(Mine);
            return new Verdict(same, pid, same ? "same user" : theirs.Length == 0 ? "token unreadable" : "another user", clock.Elapsed.TotalMilliseconds);
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    // The CLIENT's row: its local port is our remote port, and its remote port is our listening port.
    private static int OwnerOf(IPEndPoint remote, IPEndPoint local)
    {
        uint size = 0;
        _ = GetExtendedTcpTable(0, ref size, false, 2 /* AF_INET */, 4 /* TCP_TABLE_OWNER_PID_CONNECTIONS */, 0);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buffer = Marshal.AllocHGlobal((nint)size);
            try
            {
                var rc = GetExtendedTcpTable(buffer, ref size, false, 2, 4, 0);
                if (rc == 122 /* ERROR_INSUFFICIENT_BUFFER */)
                {
                    continue;
                }

                if (rc != 0)
                {
                    return 0;
                }

                var count = *(uint*)buffer;
                var rows = (Row*)((byte*)buffer + 4);
                var clientPort = (ushort)IPAddress.HostToNetworkOrder((short)remote.Port);
                var serverPort = (ushort)IPAddress.HostToNetworkOrder((short)local.Port);
#pragma warning disable CS0618
                var loopback = (uint)IPAddress.Loopback.Address;
#pragma warning restore CS0618
                for (var i = 0; i < count; i++)
                {
                    if (rows[i].LocalAddr == loopback && rows[i].RemoteAddr == loopback
                        && (ushort)rows[i].LocalPort == clientPort && (ushort)rows[i].RemotePort == serverPort)
                    {
                        return (int)rows[i].OwningPid;
                    }
                }

                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return 0;
    }

    private static byte[] UserOf(nint process)
    {
        if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token))
        {
            return [];
        }

        try
        {
            _ = GetTokenInformation(token, 1 /* TokenUser */, 0, 0, out var needed);
            var buffer = Marshal.AllocHGlobal((nint)needed);
            try
            {
                if (!GetTokenInformation(token, 1, buffer, needed, out _))
                {
                    return [];
                }

                var sid = *(nint*)buffer;
                var length = GetLengthSid(sid);
                var bytes = new byte[length];
                Marshal.Copy(sid, bytes, 0, (int)length);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Row
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(nint table, ref uint size, bool order, uint family, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int kind, nint info, uint length, out uint needed);

    [DllImport("advapi32.dll")]
    private static extern uint GetLengthSid(nint sid);
}

internal static class SProbeHost
{
    public static int Run(string[] args)
    {

var clock = Stopwatch.StartNew();
var portFile = args[1];
var logGate = new Lock();
using var logFile = new StreamWriter(args[2], append: false, new UTF8Encoding(false)) { AutoFlush = true };

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

Loopback loopback;
try
{
    loopback = args.Length >= 5 ? new Loopback(Route, Log, int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), args[4]) : new Loopback(Route, Log);
}
catch (SocketException failure)
{
    File.WriteAllText(portFile + ".error", $"{failure.SocketErrorCode}	{clock.Elapsed.TotalMilliseconds:F2}");
    return 3;
}

using var disposeLoopback = loopback;

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

    }
}
#endif
