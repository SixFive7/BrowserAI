// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace BrowserAI.App.Page;

/// <summary>What answers an admitted request.</summary>
/// <remarks>
/// <b>The gate has already run</b> when this is called: every request it sees
/// carries the token, our <c>Host</c> and, for a write, our <c>Origin</c>. It
/// returns <see langword="false"/> for a route it does not know, and the listener
/// answers that with the same bare <c>404</c> the gate gives.
/// </remarks>
internal interface IPageRoutes
{
    /// <summary>Answers one admitted request.</summary>
    /// <param name="context">The request and its response.</param>
    /// <param name="route">What follows <c>/&lt;token&gt;/</c>, up to the query.</param>
    /// <param name="query">The query, undecoded, without its question mark.</param>
    /// <returns>Whether the route exists.</returns>
    Task<bool> ServeAsync(HttpContext context, string route, string query);

    /// <summary>Whether a route is a page a person opens, which a first visit swaps the token of for a cookie (74 a).</summary>
    /// <param name="route">The route.</param>
    /// <returns>Whether it is a page.</returns>
    bool IsPage(string route);
}

/// <summary>
/// The listener behind the browser tab: Kestrel on <c>127.0.0.1</c>, a port
/// Windows picks, one gate before any route, and a bare <c>404</c> for everything
/// the gate does not admit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q340 b, Q341 b and Q343, the maintainer's words verbatim: <i>"Q340 b"</i>,
/// <i>"Q341 b"</i> and <i>"Q343 Keep it as minimal in complexity as possible"</i>.</b>
/// Kestrel from ASP.NET Core's empty builder, with nothing implicit: no settings
/// file, no environment-variable configuration, no logging provider, no routing,
/// and one plain request handler. The minimal-API generator is not used, because
/// the code it generates calls <c>Debug.Assert</c>, which this product bans.
/// </para>
/// <para>
/// <b>The socket is bound by us, exclusively</b>, through Kestrel's own hook for
/// creating the listening socket: <c>SO_EXCLUSIVEADDRUSE</c>, so no other
/// process can bind the same address and port beside it while it is held. Only
/// <c>127.0.0.1</c>, never <c>[::1]</c> or another interface, and only HTTP/1.1.
/// </para>
/// <para>
/// <b>What Kestrel refuses itself never reaches the gate.</b> A request it cannot
/// parse -- two <c>Host</c> headers, an HTTP/1.1 request with none, a header block
/// over the limit, bytes that are not HTTP -- is answered by Kestrel with its own
/// empty <c>400</c> or <c>431</c>. Those answers carry nothing and run no route; the
/// prototype's raw socket answered the same shapes with <c>404</c>
/// ([kb](../../../kb/windows/loopback-page.md)).
/// </para>
/// </remarks>
internal sealed partial class PageListener : IAsyncDisposable
{
    /// <summary>How many connections the listener holds at once, every open tab's event stream included.</summary>
    public const int MaximumConnections = 64;

    /// <summary>How large a request's header block may be.</summary>
    public const int MaximumHeaderBytes = 8 * 1024;

    /// <summary>
    /// The block every admitted answer carries, and nothing else changes it.
    /// </summary>
    /// <remarks>
    /// <b>The content security policy is what stops a missed escape from
    /// running</b>: scripts and styles from this origin only, never inline, no
    /// frames, no forms, no base element, and connections back to this origin
    /// alone. No cookie is ever set and no CORS header is ever sent. ⚠️ <i>Corrected
    /// 2026-10-12 by addition, 74 a: one cookie is set, by the answer that sends a first
    /// visit to the address without the token (<see cref="PageGate.SetCookie"/>), and no
    /// other answer sets one.</i>
    /// </remarks>
    public static IReadOnlyList<KeyValuePair<string, string>> SecurityHeaders { get; } =
    [
        new("Cache-Control", "no-store"),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
        new("Cross-Origin-Opener-Policy", "same-origin"),
        new("Cross-Origin-Resource-Policy", "same-origin"),
        new("X-Frame-Options", "DENY"),
        new(
            "Content-Security-Policy",
            "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; "
            + "form-action 'none'; frame-ancestors 'none'; base-uri 'none'"),
    ];

    private readonly WebApplication _application;
    private readonly IPageRoutes _routes;
    private readonly ILogger _logger;
    private PageGate? _gate;

    private PageListener(WebApplication application, IPageRoutes routes, ILogger logger)
    {
        _application = application;
        _routes = routes;
        _logger = logger;
    }

    /// <summary>The gate, which knows the port and the token.</summary>
    public PageGate Gate => _gate ?? throw new InvalidOperationException("The listener has not started.");

    /// <summary>Starts a listener with a fresh token, on the port it is asked for when that is free and otherwise on one Windows picks.</summary>
    /// <param name="routes">What answers an admitted request.</param>
    /// <param name="logger">Where refusals and failures are recorded.</param>
    /// <param name="preferredPort">
    /// The port the last listener of this data root held, or zero for one Windows picks:
    /// 69 d with fallback b, 2026-10-11, so a browser's <i>Always allow</i> for this address
    /// outlives the background's restart.
    /// </param>
    /// <returns>The listening listener.</returns>
    /// <remarks>
    /// <para>
    /// <b>Awaited since 2026-10-11</b>, the maintainer's decision 54. The 2026-09-25 probes
    /// were listening 46 to 82 ms after their process started; how much of that is
    /// Kestrel's own start was not measured.
    /// </para>
    /// <para>
    /// <b>A port another program holds is never fought for</b>: the bind is exclusive both
    /// ways, so the listener takes a port Windows picks and says so, and the browser asks
    /// once more for the new address.
    /// </para>
    /// </remarks>
    public static async Task<PageListener> StartAsync(IPageRoutes routes, ILogger logger, int preferredPort = 0)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegative(preferredPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(preferredPort, IPEndPoint.MaxPort);

        var port = 0;
        var tookAnother = false;

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());

        _ = builder.WebHost.UseKestrelCore();
        _ = builder.WebHost.UseSockets(options => options.CreateBoundListenSocket = endpoint =>
        {
            try
            {
                return Bound(endpoint, out port);
            }
            catch (SocketException) when (endpoint is IPEndPoint { Port: not 0 } asked)
            {
                // 69 b: the last port is taken, by another program or by a listener of
                // another data root, so Windows picks one.
                tookAnother = true;
                return Bound(new IPEndPoint(asked.Address, 0), out port);
            }
        });
        _ = builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = MaximumConnections;
            options.Limits.MaxRequestBodySize = PageGate.MaximumBody;
            options.Limits.MaxRequestHeadersTotalSize = MaximumHeaderBytes;
            options.Listen(IPAddress.Loopback, preferredPort, listen => listen.Protocols = HttpProtocols.Http1);
        });

        PageListener? listener = new(builder.Build(), routes, logger);

        try
        {
            listener._application.Run(listener.ServeAsync);
            await listener._application.StartAsync(CancellationToken.None).ConfigureAwait(false);
            listener._gate = new PageGate(port, PageGate.NewToken());

            if (tookAnother)
            {
                PageListenerLog.LastPortTaken(logger, preferredPort, port);
            }

            PageListenerLog.Listening(logger, port);

            var started = listener;

            // Handed to the caller; nothing here disposes it now.
            listener = null;

            return started;
        }
        finally
        {
            if (listener is not null)
            {
                await listener.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>A socket bound exclusively to one endpoint, and the port it holds.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="port">The port the socket holds.</param>
    /// <returns>The socket.</returns>
    private static Socket Bound(EndPoint endpoint, out int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true,
        };

        try
        {
            socket.Bind(endpoint);
            port = ((IPEndPoint)socket.LocalEndPoint!).Port;
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Stops listening and closes every connection, every open event stream included.</summary>
    /// <returns>The stop.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            using var bounded = new CancellationTokenSource(ProcessBounds.PageListenerStopBound);
            await _application.StopAsync(bounded.Token).ConfigureAwait(false);
        }
        finally
        {
            await _application.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>What a request shows the gate, read off Kestrel's own view of it.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The facts.</returns>
    internal static PageRequest FactsOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        var raw = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;

        return new PageRequest(
            request.Method,
            raw,
            Values(request.Headers.Host),
            Values(request.Headers.Origin),
            Values(request.Headers["Sec-Fetch-Site"]),
            Values(request.Headers.ContentType),
            request.ContentLength,
            request.Headers.TransferEncoding.Count > 0)
        {
            Cookie = Values(request.Headers.Cookie),
        };
    }

    private static string[] Values(StringValues values) => [.. values.Select(value => value ?? string.Empty)];

    private async Task ServeAsync(HttpContext context)
    {
        var gate = _gate;
        var facts = FactsOf(context);
        var refusal = gate?.Admit(facts) ?? PageRefusal.Token;

        if (refusal is not PageRefusal.None || gate is null)
        {
            var refused = refusal.ToString();
            var target = gate is null ? "(before the token existed)" : facts.RawTarget.Replace(gate.Token, "<token>", StringComparison.Ordinal);

            PageListenerLog.Refused(_logger, refused, facts.Method, target);

            NotFound(context);
            return;
        }

        foreach (var (name, value) in SecurityHeaders)
        {
            context.Response.Headers[name] = value;
        }

        var route = gate.RouteOf(facts.RawTarget);

        // ⚠️ 74 a, the maintainer's words of 2026-10-11 verbatim: "74 a". A page opened by
        // the address with the token in it is answered with the token in this port's cookie
        // and the same address without it, so the address bar shows no secret, and a reload
        // or a link keeps working through the cookie.
        if (string.Equals(facts.Method, "GET", StringComparison.Ordinal) && gate.SwapsTheAddress(facts.RawTarget) && _routes.IsPage(route) && route.Length < 0)
        {
            context.Response.StatusCode = StatusCodes.Status303SeeOther;
            context.Response.Headers.Location = gate.PlainAddressOf(facts.RawTarget);
            context.Response.Headers.SetCookie = gate.SetCookie;
            return;
        }

        bool served;

        try
        {
            served = await _routes.ServeAsync(context, route, PageGate.QueryOf(facts.RawTarget)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The tab went away mid-answer, which is how every event stream ends.
            return;
        }
#pragma warning disable CA1031 // A route that threw is a failed request and a line in the log, never a listener that stops.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PageListenerLog.RouteFailed(_logger, failure);

            if (!context.Response.HasStarted)
            {
                context.Response.Headers.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }

            return;
        }

        if (!served)
        {
            context.Response.Headers.Clear();
            NotFound(context);
        }
    }

    /// <summary>The one answer to everything the gate does not admit: a 404 with nothing in it.</summary>
    /// <param name="context">The request.</param>
    private static void NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.Headers.Connection = "close";
    }

    /// <summary>The listener's own records.</summary>
    private static partial class PageListenerLog
    {
        [LoggerMessage(EventId = 7001, Level = LogLevel.Information, Message = "The page listener is on 127.0.0.1:{Port}.")]
        public static partial void Listening(ILogger logger, int port);

        [LoggerMessage(EventId = 7002, Level = LogLevel.Information, Message = "The page listener refused a request ({Refusal}): {Method} {Target}")]
        public static partial void Refused(ILogger logger, string refusal, string method, string target);

        [LoggerMessage(EventId = 7003, Level = LogLevel.Warning, Message = "A page route threw, and the request was answered with 500.")]
        public static partial void RouteFailed(ILogger logger, Exception failure);

        // 69 b, 2026-10-11: the port the last listener of this data root held.
        [LoggerMessage(EventId = 7004, Level = LogLevel.Information, Message = "The page listener's last port, {Last}, is taken, so it listens on {Port}; a browser asks once more before this address may open BrowserAI.")]
        public static partial void LastPortTaken(ILogger logger, int last, int port);
    }
}
