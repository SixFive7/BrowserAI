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
internal sealed partial class PageListener : IAsyncDisposable, IDisposable
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
    /// alone. No cookie is ever set and no CORS header is ever sent.
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

    /// <summary>Starts a listener on a fresh port with a fresh token, waiting for it on the calling thread.</summary>
    /// <param name="routes">What answers an admitted request.</param>
    /// <param name="logger">Where refusals and failures are recorded.</param>
    /// <returns>The listening listener.</returns>
    /// <remarks>
    /// <b>Synchronous on purpose</b>: it is called from the coordinator's own thread
    /// and from the pipe's, and neither has a synchronization context to deadlock
    /// on. The 2026-09-25 probes were listening 46 to 82 ms after their process
    /// started; how much of that is Kestrel's own start was not measured.
    /// </remarks>
    public static PageListener Start(IPageRoutes routes, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(logger);

        var port = 0;

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());

        _ = builder.WebHost.UseKestrelCore();
        _ = builder.WebHost.UseSockets(options => options.CreateBoundListenSocket = endpoint =>
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
        });
        _ = builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = MaximumConnections;
            options.Limits.MaxRequestBodySize = PageGate.MaximumBody;
            options.Limits.MaxRequestHeadersTotalSize = MaximumHeaderBytes;
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http1);
        });

        PageListener? listener = new(builder.Build(), routes, logger);

        try
        {
            listener._application.Run(listener.ServeAsync);
            listener._application.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            listener._gate = new PageGate(port, PageGate.NewToken());

            PageListenerLog.Listening(logger, port);

            var started = listener;

            // Handed to the caller; nothing here disposes it now.
            listener = null;

            return started;
        }
        finally
        {
            listener?.Dispose();
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

    /// <summary>Stops listening, waiting for the stop on the calling thread.</summary>
    /// <remarks>
    /// For the coordinator's own thread and the pipe's, neither of which has a
    /// synchronization context to deadlock on.
    /// </remarks>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

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
            request.Headers.TransferEncoding.Count > 0);
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

        bool served;

        try
        {
            served = await _routes.ServeAsync(context, gate.RouteOf(facts.RawTarget), PageGate.QueryOf(facts.RawTarget)).ConfigureAwait(false);
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
    }
}
