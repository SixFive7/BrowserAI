// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Net;
using System.Net.Sockets;
using System.Text;
using BrowserAI.App.Page;
using BrowserAI.Tests.Harness;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The listener behind the browser tab and its one gate: what is admitted, what
/// gets the bare 404, and what Kestrel refuses before the gate is ever asked.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q334 a and Q335 a, the maintainer's words verbatim: <i>"Q334 a"</i> and
/// <i>"Q335 a"</i>.</b> The page proves itself by its address, and the token in it
/// is the only secret. The request shapes are the 2026-10-01 prototype's 24
/// ([kb](../../kb/windows/loopback-page.md#a-listener-with-one-gate-against-two-browsers----measured-2026-10-01)),
/// sent here from a raw socket at the product's own listener, Kestrel from the
/// empty builder (Q340 b).
/// </para>
/// <para>
/// <b>Nothing here opens a window or a browser.</b> The listener is in this
/// process on <c>127.0.0.1</c>, and the routes behind it are a stand-in that only
/// says which route was reached, so an arm fails on the gate and not on a page.
/// </para>
/// </remarks>
internal sealed class PageListenerTests
{
    /// <summary>
    /// The gate decides from the request's facts alone: a table of every rule, each
    /// broken on its own against a request that passes.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheGateAdmitsOnlyTheTokenOurHostAndOurOriginAndRefusesEachRuleOnItsOwn()
    {
        var token = PageGate.NewToken();
        var gate = new PageGate(54321, token);
        var page = $"/{token}/";

        await Assert.That(token.Length).IsEqualTo(PageGate.TokenCharacters);
        await Assert.That(PageGate.NewToken()).IsNotEqualTo(token);

        var read = new PageRequest("GET", page, ["127.0.0.1:54321"], [], [], [], null, false);
        var write = new PageRequest("POST", page + "action", ["127.0.0.1:54321"], ["http://127.0.0.1:54321"], ["same-origin"], ["application/json"], 2, false);

        (string Shape, PageRequest Request, PageRefusal Expected)[] table =
        [
            ("the page", read, PageRefusal.None),
            ("the page opened from outside the browser", read with { FetchSite = ["none"] }, PageRefusal.None),
            ("the page with our own Origin", read with { Origin = ["http://127.0.0.1:54321"] }, PageRefusal.None),
            ("a write", write, PageRefusal.None),
            ("a write with no Sec-Fetch-Site", write with { FetchSite = [] }, PageRefusal.None),
            ("PUT", read with { Method = "PUT" }, PageRefusal.Method),
            ("HEAD", read with { Method = "HEAD" }, PageRefusal.Method),
            ("OPTIONS, a preflight", read with { Method = "OPTIONS" }, PageRefusal.Method),
            ("no Host", read with { Host = [] }, PageRefusal.Host),
            ("two Hosts", read with { Host = ["127.0.0.1:54321", "127.0.0.1:54321"] }, PageRefusal.Host),
            ("localhost", read with { Host = ["localhost:54321"] }, PageRefusal.Host),
            ("a rebound name", read with { Host = ["attacker.example:54321"] }, PageRefusal.Host),
            ("another port", read with { Host = ["127.0.0.1:54322"] }, PageRefusal.Host),
            ("no token", read with { RawTarget = "/" }, PageRefusal.Token),
            ("a wrong token of the right length", read with { RawTarget = $"/{new string('A', token.Length)}/" }, PageRefusal.Token),
            ("the token with no closing slash", read with { RawTarget = $"/{token}" }, PageRefusal.Token),
            ("the token behind a dot segment", read with { RawTarget = $"/x/../{token}/" }, PageRefusal.Token),
            ("the token percent-encoded", read with { RawTarget = "/%" + Convert.ToHexString(Encoding.ASCII.GetBytes(token[..1])) + token[1..] + "/" }, PageRefusal.Token),
            ("an absolute target", read with { RawTarget = $"http://127.0.0.1:54321/{token}/" }, PageRefusal.Token),
            ("a cross-site request", read with { FetchSite = ["cross-site"] }, PageRefusal.FetchSite),
            ("a same-site request", read with { FetchSite = ["same-site"] }, PageRefusal.FetchSite),
            ("two Sec-Fetch-Site values", read with { FetchSite = ["same-origin", "none"] }, PageRefusal.FetchSite),
            ("a read with another Origin", read with { Origin = ["https://attacker.example"] }, PageRefusal.Origin),
            ("a read with two Origins", read with { Origin = ["http://127.0.0.1:54321", "http://127.0.0.1:54321"] }, PageRefusal.Origin),
            ("a write with no Origin", write with { Origin = [] }, PageRefusal.Origin),
            ("a write with another Origin", write with { Origin = ["https://attacker.example"] }, PageRefusal.Origin),
            ("a write with the null Origin", write with { Origin = ["null"] }, PageRefusal.Origin),
            ("a write from outside the browser", write with { FetchSite = ["none"] }, PageRefusal.Origin),
            ("a form", write with { ContentType = ["application/x-www-form-urlencoded"] }, PageRefusal.ContentType),
            ("plain text", write with { ContentType = ["text/plain"] }, PageRefusal.ContentType),
            ("JSON with a charset", write with { ContentType = ["application/json; charset=utf-8"] }, PageRefusal.ContentType),
            ("a write with no length", write with { ContentLength = null }, PageRefusal.TooLarge),
            ("a chunked write", write with { Chunked = true }, PageRefusal.TooLarge),
            ("a body one byte over", write with { ContentLength = PageGate.MaximumBody + 1 }, PageRefusal.TooLarge),
            ("a body exactly at the limit", write with { ContentLength = PageGate.MaximumBody }, PageRefusal.None),
        ];

        var wrong = table
            .Select(row => (row.Shape, row.Expected, Actual: gate.Admit(row.Request)))
            .Where(row => row.Actual != row.Expected)
            .Select(row => $"{row.Shape}: expected {row.Expected}, got {row.Actual}")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, wrong)).IsEmpty();

        // The route and the query an admitted target names.
        await Assert.That(gate.RouteOf(page)).IsEqualTo(string.Empty);
        await Assert.That(gate.RouteOf(page + "sessions?tab=3")).IsEqualTo("sessions");
        await Assert.That(PageGate.QueryOf(page + "events?tab=3&page=sessions")).IsEqualTo("tab=3&page=sessions");
        await Assert.That(PageGate.QueryValue("tab=3&page=sessions", "page")).IsEqualTo("sessions");
        await Assert.That(PageGate.QueryValue("tab=3", "page")).IsNull();
        await Assert.That(gate.Root).IsEqualTo($"http://127.0.0.1:54321/{token}/");
    }

    /// <summary>
    /// Over a real socket: the page and a write are admitted and carry the security
    /// headers; every request the gate refuses gets a 404 with nothing in it, and no
    /// route runs for any of them.
    /// </summary>
    /// <remarks>
    /// <b>The count of routes reached is the decisive half</b>: a 404 could come from
    /// a route that ran and then answered 404, and the stand-in counts every request
    /// that got past the gate.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OverARealSocketEveryRefusalIsABare404AndNoRouteRuns()
    {
        var routes = new CountingRoutes();
        using var listener = PageListener.Start(routes, NullLogger.Instance);

        var port = listener.Gate.Port;
        var token = listener.Gate.Token;
        var host = $"Host: 127.0.0.1:{port}";
        var origin = $"Origin: http://127.0.0.1:{port}";

        // Admitted.
        var page = await RawHttp.SendAsync(port, RawHttp.Get(port, $"/{token}/", host));

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).IsEqualTo("the page");

        foreach (var (name, value) in PageListener.SecurityHeaders)
        {
            await Assert.That(page.Header(name)).IsEqualTo(value).Because(name);
        }

        await Assert.That(page.Values("Server")).IsEmpty();
        await Assert.That(page.Values("Set-Cookie")).IsEmpty();
        await Assert.That(page.Values("Access-Control-Allow-Origin")).IsEmpty();

        var written = await RawHttp.SendAsync(port, RawHttp.Post($"/{token}/action", "{}", host, origin, "Sec-Fetch-Site: same-origin", "Content-Type: application/json"));

        await Assert.That(written.Status).IsEqualTo(204).Because(written.Raw);
        await Assert.That(routes.Reached).IsEqualTo(2);

        // Refused by the gate: each a 404, empty, with none of the headers an admitted answer carries.
        string[] refused =
        [
            RawHttp.Get(port, "/", host),
            RawHttp.Get(port, $"/{new string('A', token.Length)}/", host),
            RawHttp.Get(port, $"/{token}", host),
            RawHttp.Get(port, $"/x/../{token}/", host),
            RawHttp.Get(port, $"/{token}/", $"Host: localhost:{port}"),
            RawHttp.Get(port, $"/{token}/", $"Host: attacker.example:{port}"),
            $"GET /{token}/ HTTP/1.0\r\n\r\n",
            RawHttp.Get(port, $"/{token}/", host, "Origin: https://attacker.example"),
            RawHttp.Get(port, $"/{token}/", host, "Sec-Fetch-Site: cross-site"),
            RawHttp.Post($"/{token}/action", "{}", host, "Origin: https://attacker.example", "Content-Type: application/json"),
            RawHttp.Post($"/{token}/action", "{}", host, "Content-Type: application/json"),
            RawHttp.Post($"/{token}/action", "a=1", host, origin, "Content-Type: application/x-www-form-urlencoded"),
            RawHttp.Post($"/{token}/action", new string('x', PageGate.MaximumBody + 1), host, origin, "Content-Type: application/json"),
            $"POST /{token}/action HTTP/1.1\r\n{host}\r\n{origin}\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n2\r\n{{}}\r\n0\r\n\r\n",
            $"OPTIONS /{token}/action HTTP/1.1\r\n{host}\r\n{origin}\r\nAccess-Control-Request-Method: POST\r\nConnection: close\r\n\r\n",
            $"PUT /{token}/ HTTP/1.1\r\n{host}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            $"GET http://127.0.0.1:{port}/{token}/ HTTP/1.1\r\n{host}\r\nConnection: close\r\n\r\n",
            RawHttp.Get(port, $"/{token}/no-such-route", host),
        ];

        var wrong = new List<string>();

        foreach (var request in refused)
        {
            var answer = await RawHttp.SendAsync(port, request);
            var shape = request.Replace(token, "<token>", StringComparison.Ordinal).Split("\r\n")[0];

            if (answer.Status is not 404 || answer.Body.Length > 0 || answer.Values("Content-Security-Policy").Count > 0 || answer.Values("Server").Count > 0)
            {
                wrong.Add($"{shape}: {answer.Raw.Replace(token, "<token>", StringComparison.Ordinal)}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, wrong)).IsEmpty();
        await Assert.That(routes.Reached).IsEqualTo(3);
    }

    /// <summary>
    /// What Kestrel refuses before the gate is asked -- two <c>Host</c> headers, a
    /// header block over the limit, bytes that are not HTTP -- gets an empty
    /// answer, and no route runs.
    /// </summary>
    /// <remarks>
    /// <b>This is the one place the product's listener differs from the
    /// prototype's</b>, which answered these with its own 404: Kestrel parses before
    /// any handler runs and answers what it cannot parse itself. The property that
    /// matters is the one asserted: nothing in the answer, and nothing reached.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhatKestrelRefusesItselfIsAnsweredEmptyAndReachesNoRoute()
    {
        var routes = new CountingRoutes();
        using var listener = PageListener.Start(routes, NullLogger.Instance);

        var port = listener.Gate.Port;
        var token = listener.Gate.Token;
        var host = $"Host: 127.0.0.1:{port}";

        (string Shape, string Request, int Status)[] shapes =
        [
            ("two Host headers", RawHttp.Get(port, $"/{token}/", host, host), 400),
            ("a header block over the limit", RawHttp.Get(port, $"/{token}/", host, "X-Padding: " + new string('x', PageListener.MaximumHeaderBytes + 1024)), 431),
            ("HTTP/1.1 with no Host", $"GET /{token}/ HTTP/1.1\r\nConnection: close\r\n\r\n", 400),
            ("bytes that are not HTTP", "\u0016\u0003\u0001\u0002\u0000\u0001\u0000\u0001\u00fc\u0003\u0003 not http at all\r\n\r\n", 400),
        ];

        var wrong = new List<string>();

        foreach (var (shape, request, status) in shapes)
        {
            var answer = await RawHttp.SendAsync(IPAddress.Loopback, port, Encoding.Latin1.GetBytes(request));

            if (answer.Status != status || answer.Body.Length > 0)
            {
                wrong.Add($"{shape}: expected an empty {status}, got {answer.Status}: {answer.Raw.Replace(token, "<token>", StringComparison.Ordinal)}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, wrong)).IsEmpty();
        await Assert.That(routes.Reached).IsEqualTo(0);
    }

    /// <summary>
    /// The listener is on <c>127.0.0.1</c> alone and nothing else of this account
    /// can take its address: <c>[::1]</c> on the same port is refused, a second bind
    /// of <c>127.0.0.1</c> and the port is refused with and without a request to
    /// share, and a wildcard bound beside it receives none of its connections.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The listening socket asks for exclusive use, and this arm does not claim
    /// to see that flag.</b> Measured 2026-10-03 on Windows 11 build 26300: a second
    /// socket of the same user binding the same address and port was refused with
    /// <c>WSAEACCES</c> when it asked to share and <c>WSAEADDRINUSE</c> when it did
    /// not, whether or not the first had asked for exclusive use, and a wildcard bind
    /// of the same port succeeded beside both and received no connection made to
    /// <c>127.0.0.1</c>
    /// ([kb](../../kb/windows/loopback-page.md#the-products-listener-under-attack----measured-2026-10-03)).
    /// What is asserted is the property a person relies on: while the listener holds
    /// the port, a request to its address reaches the listener.
    /// </para>
    /// <para>
    /// <b>Another Windows user is the case this cannot reach</b>, on a machine with
    /// one account; the hazard index carries it open.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NothingElseOfThisAccountCanTakeTheListenersAddress()
    {
        var routes = new CountingRoutes();
        using var listener = PageListener.Start(routes, NullLogger.Instance);

        var port = listener.Gate.Port;

        await Assert.That(port).IsGreaterThan(0);

        using (var six = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp))
        {
            var refused = Assert.Throws<SocketException>(() => six.Connect(IPAddress.IPv6Loopback, port));

            await Assert.That(refused.SocketErrorCode).IsEqualTo(SocketError.ConnectionRefused);
        }

        foreach (var share in BothWays)
        {
            using var second = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            second.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, share);

            var taken = Assert.Throws<SocketException>(() => second.Bind(new IPEndPoint(IPAddress.Loopback, port)));

            await Assert.That(taken.SocketErrorCode).IsEqualTo(share ? SocketError.AccessDenied : SocketError.AddressAlreadyInUse);
        }

        // A wildcard bound beside it, listening, and a request to the listener's
        // address: the listener answers it and the wildcard receives nothing.
        using var wildcard = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        wildcard.Bind(new IPEndPoint(IPAddress.Any, port));
        wildcard.Listen();

        var answer = await RawHttp.SendAsync(port, RawHttp.Get(port, $"/{listener.Gate.Token}/", $"Host: 127.0.0.1:{port}"));

        await Assert.That(answer.Status).IsEqualTo(200).Because(answer.Raw);
        await Assert.That(routes.Reached).IsEqualTo(1);
        await Assert.That(wildcard.Poll(0, SelectMode.SelectRead)).IsFalse();

        // ⚠️ AND WHY NOTHING HERE CLAIMS TO SEE THE EXCLUSIVE FLAG: two listening
        // sockets of our own, one asking for exclusive use and one not, give every
        // second bind above the same answer on this build of Windows. A build where
        // they differ makes this red, and then the flag is observable and an arm
        // should hold it.
        var answers = BothWays.Select(exclusive =>
        {
            using var held = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = exclusive };

            held.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            held.Listen();

            var heldPort = ((IPEndPoint)held.LocalEndPoint!).Port;

            return string.Join(", ", BothWays.Select(share =>
            {
                using var second = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                second.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, share);

                try
                {
                    second.Bind(new IPEndPoint(IPAddress.Loopback, heldPort));
                    return "bound";
                }
                catch (SocketException refused)
                {
                    return refused.SocketErrorCode.ToString();
                }
            }));
        }).ToList();

        await Assert.That(answers[0]).IsEqualTo(answers[1]);
        await Assert.That(answers[0]).IsEqualTo("AccessDenied, AddressAlreadyInUse");
    }

    /// <summary>Both answers to a yes-or-no question, for the arms that ask it each way.</summary>
    private static readonly bool[] BothWays = [true, false];

    /// <summary>Routes that say which one was reached and count every request that got past the gate.</summary>
    private sealed class CountingRoutes : IPageRoutes
    {
        private int _reached;

        public int Reached => Volatile.Read(ref _reached);

        public async Task<bool> ServeAsync(HttpContext context, string route, string query)
        {
            _ = Interlocked.Increment(ref _reached);

            switch (route)
            {
                case "" when context.Request.Method is "GET":
                    var body = Encoding.UTF8.GetBytes("the page");

                    context.Response.ContentType = "text/plain";
                    context.Response.ContentLength = body.Length;
                    await context.Response.Body.WriteAsync(body);
                    return true;

                case "action" when context.Request.Method is "POST":
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return true;

                default:
                    return false;
            }
        }
    }
}
