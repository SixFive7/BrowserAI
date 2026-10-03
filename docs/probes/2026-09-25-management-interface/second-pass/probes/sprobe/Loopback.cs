// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Option S's listener, as small as it can be made and still be safe.
//
// One raw socket on 127.0.0.1, exclusive, on a port the system picks. Every
// request passes ONE gate before any route is looked at, and anything the gate
// does not admit gets the same bare 404. There are no cookies, no CORS headers,
// and nothing that changes state on a GET.
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SProbe;

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
