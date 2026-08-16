// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BrowserAI.Proxy;

/// <summary>
/// One <c>@playwright/mcp</c> child behind one MCP server, forwarding
/// <c>tools/list</c> and <c>tools/call</c> without changing a byte of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately thin.</b> No sessions, no locking, no artifact routing and no
/// injected <c>session</c> parameter — the two tool methods are forwarded as they
/// arrive.
/// </para>
/// <para>
/// <b>Nothing on the forwarding path touches an SDK contract type, and that is
/// the point of the design rather than a stylistic preference.</b> Every loss
/// this step exists to close is silent, and each one is produced by a type that
/// is doing its job: <c>ContentBlock</c>'s converter drops unknown properties
/// and throws on an unknown content <i>type</i>, which is correct
/// forward-compatibility for a client and data loss for a proxy;
/// <c>Tool</c> carries no <c>[JsonExtensionData]</c>, so a typed
/// <c>ListToolsResult</c> round trip discards tool-level extensions;
/// <c>ListToolsAsync(RequestOptions?, ct)</c> drops tools whose annotations fail
/// SEP-2243 validation without raising anything. So the request goes out as a
/// raw <see cref="JsonRpcRequest"/> and the answer comes back as the exact bytes
/// the child wrote.
/// </para>
/// <para>
/// <b>There is deliberately no typed fallback.</b> <c>Handlers</c> carries
/// neither a <c>ListToolsHandler</c> nor a <c>CallToolHandler</c>, so if the
/// filter below ever failed to short-circuit, the caller would get
/// <c>-32601</c> rather than a quietly lossy answer. That asymmetry is chosen:
/// a loud wrong answer can be found, and a lossy right-looking one cannot.
/// </para>
/// </remarks>
internal sealed class BrowserProxy : IAsyncDisposable
{
    /// <summary>
    /// The protocol revision BrowserAI speaks <b>to the child</b>, pinned to the
    /// child's measured ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pinning is not a tidiness choice.</b> Left null, the SDK client
    /// prefers <c>2026-07-28</c> and probes the child with
    /// <c>server/discover</c> first, bounded by <c>DiscoverProbeTimeout</c> —
    /// five seconds by default. A child that drops the unknown method instead of
    /// answering costs that on <i>every</i> spawn, against a ~300 ms baseline,
    /// and it presents as "browser automation got slow" with no error anywhere.
    /// Pinning an initialize-capable revision skips the probe entirely.
    /// </para>
    /// <para>
    /// It is a provenance stamp, not a target: <c>@playwright/mcp</c> 0.0.79
    /// caps here, verified 2026-08-16 from both directions — offering
    /// <c>2999-01-01</c> returned <c>2025-11-25</c> and offering
    /// <c>2025-06-18</c> returned <c>2025-06-18</c>. The child never
    /// <i>rejects</i> a version, so a mis-negotiation produces nothing to catch
    /// and
    /// <see cref="ConnectAsync(IClientTransport, ILoggerFactory, CancellationToken)"/>
    /// asserts on the negotiated value instead.
    /// </para>
    /// </remarks>
    public const string ChildProtocolVersion = "2025-11-25";

    /// <summary>
    /// What <c>CreateRemoteProtocolExceptionFromError</c> puts in front of every
    /// message it lifts out of a child's JSON-RPC error.
    /// </summary>
    /// <remarks>
    /// Only reached on the path where the raw error frame was not captured. The
    /// ordinary path never meets the prefix at all, because it never reads the
    /// message off the exception — see
    /// <see cref="AnswerChildErrorAsync(McpServer, RequestId, RequestId, McpProtocolException, CancellationToken)"/>.
    /// </remarks>
    private const string RemoteErrorPrefix = "Request failed (remote): ";

    private readonly McpClient _client;
    private readonly ChildLink _link;
    private readonly ILogger _logger;
    private readonly IAsyncDisposable _progressRelay;

    private McpServer? _caller;
    private long _childRequests;
    private int _disposed;

    private BrowserProxy(McpClient client, ChildLink link, ILogger logger)
    {
        _client = client;
        _link = link;
        _logger = logger;
        NegotiatedChildProtocolVersion = client.NegotiatedProtocolVersion;

        // The child→caller direction, and the only one the SDK gives no
        // server-side seam for: McpClientOptions has no Filters. A *named*
        // notification needs no decorator either way -- RegisterNotificationHandler
        // is public on McpSession, which McpClient inherits.
        _progressRelay = client.RegisterNotificationHandler(
            NotificationMethods.ProgressNotification,
            RelayToCallerAsync);
    }

    /// <summary>
    /// The revision actually negotiated with the child, as opposed to the one
    /// that was asked for.
    /// </summary>
    public string? NegotiatedChildProtocolVersion { get; }

    /// <summary>Starts the child and completes the handshake with it.</summary>
    /// <param name="options">What to start, from <see cref="Runtime.ChildLaunch"/>.</param>
    /// <param name="loggerFactory">Where the proxy, the transport and the session log.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The connected proxy.</returns>
    /// <exception cref="InvalidOperationException">The child negotiated a revision other than the pinned one.</exception>
    public static async Task<BrowserProxy> ConnectAsync(
        ChildProcessOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return await ConnectAsync(
            new DirectStdioClientTransport(options, loggerFactory),
            loggerFactory,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Completes the handshake over a transport the caller supplies, rather
    /// than over one this class starts a process for.
    /// </summary>
    /// <remarks>
    /// <b>The seam exists for the in-process test layer and nothing else
    /// uses it.</b> A proxy has two hops, so a harness that stands a fake child
    /// on the far end has to reach the client leg without a process — otherwise
    /// every passthrough assertion costs a `node` spawn and the layer that is
    /// supposed to run in milliseconds runs in seconds. Everything that decides
    /// behaviour — the pinned revision, the negotiation check, the raw
    /// forwarding path — is below this line rather than above it, so the harness
    /// exercises the same code the product runs.
    /// </remarks>
    /// <param name="transport">The client transport to connect over. This object does not own it; the client does.</param>
    /// <param name="loggerFactory">Where the proxy and the session log.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The connected proxy.</returns>
    /// <exception cref="InvalidOperationException">The child negotiated a revision other than the pinned one.</exception>
    public static async Task<BrowserProxy> ConnectAsync(
        IClientTransport transport,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger<BrowserProxy>();
        var link = new ChildLink(transport);

        var client = await McpClient.CreateAsync(
            link,
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "BrowserAI", Version = Version },
                ProtocolVersion = ChildProtocolVersion,
            },
            loggerFactory,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var negotiated = client.NegotiatedProtocolVersion;

            ProxyLog.ChildProtocolNegotiated(logger, ChildProtocolVersion, negotiated ?? "<none>");

            // The SDK refuses to negotiate BELOW a pinned version and throws, so
            // this fires only on a disagreement it does not police -- and it is
            // cheap enough to keep, because the failure it guards against is one
            // that produces no error at all on the wire.
            if (!string.Equals(negotiated, ChildProtocolVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The child negotiated protocol '{negotiated ?? "<none>"}' rather than the requested '{ChildProtocolVersion}'. The child caps or echoes silently and never rejects, so this is the only place a mis-negotiation is visible.");
            }

            return new BrowserProxy(client, link, logger);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The options the caller-facing MCP server is built from.</summary>
    /// <returns>Server options whose tool methods are short-circuited to the child.</returns>
    public McpServerOptions ServerOptions()
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "BrowserAI", Version = Version },

            // Upward: null means every revision the SDK implements, 2024-11-05
            // through 2026-07-28. The caller is a client this project does not
            // control and does not get to hold back; the child's ceiling is the
            // child's business and stops at the pin above. That split is the
            // whole point, and it is why these two properties disagree on
            // purpose.
            ProtocolVersion = null,

            Capabilities = new ServerCapabilities
            {
                // Declared so `initialize` advertises tools. It is what makes
                // the advertisement happen, independently of handlers -- and
                // there are deliberately no tool handlers at all.
                Tools = new ToolsCapability(),
            },
        };

        // Not WithMessageFilters: that is a DI extension in the hosting package,
        // and this is a Core/AOT server. An incoming message filter sees
        // JsonRpcRequest.Params as a raw JsonNode and never constructs a
        // ContentBlock, which is what makes it the only hook a lossless proxy
        // can use.
        options.Filters.Message.IncomingFilters.Add(next => (context, cancellationToken) =>
            OnIncomingAsync(next, context, cancellationToken));

        return options;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        await _progressRelay.DisposeAsync().ConfigureAwait(false);

        // Disposing the client closes the transport, which closes the child's
        // stdin -- upstream's own graceful teardown path -- and then closes the
        // job handle, which is what guarantees no browser is left behind.
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private static string Version { get; } =
        typeof(BrowserProxy).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>
    /// Rebuilds a typed error detail from the child's own error bytes.
    /// </summary>
    /// <remarks>
    /// This is the <i>fallback</i> shape only. The frame that actually reaches
    /// the caller is written from <paramref name="payload"/> verbatim, so this
    /// object exists so that a message which somehow escaped the verbatim path
    /// would still be semantically right rather than empty.
    /// </remarks>
    private static JsonRpcErrorDetail DetailFrom(VerbatimPayload payload)
    {
        var error = JsonNode.Parse(payload.Json)?.AsObject();

        return new JsonRpcErrorDetail
        {
            Code = error?["code"]?.GetValue<int>() ?? (int)McpErrorCode.InternalError,
            Message = error?["message"]?.GetValue<string>() ?? "The browser child reported an error carrying no message.",
            Data = error?["data"],
        };
    }

    private async Task OnIncomingAsync(McpMessageHandler next, MessageContext context, CancellationToken cancellationToken)
    {
        // Recorded for every message, not only the two that are forwarded: the
        // progress relay needs somewhere to send to, and a child may report
        // progress on the very first call.
        Volatile.Write(ref _caller, context.Server);

        if (context.JsonRpcMessage is JsonRpcRequest request &&
            request.Method is RequestMethods.ToolsCall or RequestMethods.ToolsList)
        {
            await ForwardAsync(context.Server, request, cancellationToken).ConfigureAwait(false);
            return;
        }

        await next(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends one caller request to the child and answers the caller with what
    /// came back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The id is ours, and that is what makes cancellation possible.</b> The
    /// SDK never emits <c>notifications/cancelled</c> downstream on either the
    /// raw or the typed path — <c>McpSessionHandler</c> has the machinery, but
    /// its registration is disposed as <c>tcs.Task.WaitAsync(ct)</c> unwinds and
    /// CTS callbacks run LIFO, so the notification callback is cancelled before
    /// it can run. An id we chose is an id we can name in a notification we send
    /// ourselves.
    /// </para>
    /// <para>
    /// The ids are strings in a namespace of ours, so they cannot collide with
    /// the numeric ones the SDK's own session allocates for <c>initialize</c>
    /// and <c>ping</c>.
    /// </para>
    /// </remarks>
    private async Task ForwardAsync(McpServer caller, JsonRpcRequest request, CancellationToken cancellationToken)
    {
        // Before anything is registered or sent. A token that is already
        // cancelled would otherwise fire the registration below synchronously
        // and announce the cancellation of a request that was never sent.
        cancellationToken.ThrowIfCancellationRequested();

        var name = "browserai-" + Interlocked.Increment(ref _childRequests).ToString(CultureInfo.InvariantCulture);
        var childId = new RequestId(name);

        var childRequest = new JsonRpcRequest
        {
            Id = childId,
            Method = request.Method,
            Params = request.Params,
        };

        _link.Session.Watch(childId);

        ProxyLog.Forwarding(_logger, request.Method, name, cancellationToken.CanBeCanceled);

        try
        {
            var response = await _client.SendRequestAsync(childRequest, cancellationToken).ConfigureAwait(false);

            await AnswerChildResultAsync(caller, request.Id, childId, response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await AnnounceCancellationAsync(name).ConfigureAwait(false);

            // The caller asked. The SDK's own message loop deliberately sends no
            // response for a user-initiated cancellation, and neither should we:
            // a caller that cancelled is not waiting for an answer.
            throw;
        }
        catch (McpProtocolException ex)
        {
            await AnswerChildErrorAsync(caller, request.Id, childId, ex, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Anything else is the child failing to answer at all, and the caller must be told rather than left waiting.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await AnswerTransportFailureAsync(caller, request.Id, request.Method, ex, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _link.Session.Forget(childId);
        }
    }

    private async Task AnswerChildResultAsync(
        McpServer caller,
        RequestId callerId,
        RequestId childId,
        JsonRpcResponse response,
        CancellationToken cancellationToken)
    {
        // A fresh envelope rather than the child's own: JsonRpcMessage.Context
        // carries RelatedTransport, and the SDK's send path routes to it in
        // preference to the session's transport -- so a forwarded object would
        // be sent back to the child it came from.
        var answer = new JsonRpcResponse { Id = callerId, Result = response.Result };

        if (_link.Session.TryTakePayload(childId, out var payload) && !payload.IsError)
        {
            Verbatim.Attach(answer, payload.Json);
        }
        else
        {
            // Reached only if the transport failed to capture the frame. The
            // answer is still semantically right -- Result is the child's own
            // JsonNode -- but its escaping is now ours, so the one claim this
            // step exists to make is no longer true of it. Said out loud rather
            // than absorbed.
            ProxyLog.VerbatimPayloadMissing(_logger, childId.ToString(), callerId.ToString());
        }

        await caller.SendMessageAsync(answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers a caller with the child's own JSON-RPC error.</summary>
    /// <remarks>
    /// <para>
    /// <b>The <c>"Request failed (remote): "</c> prefix is never met on this
    /// path, rather than met and stripped.</b> The SDK does add it — it is real,
    /// and <c>SdkErrorShapeTests</c> is what keeps that checked — but the bytes
    /// written here come from the child's frame, so the message that reaches the
    /// caller is the message the child sent.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-16 (previously
    /// <see href="../../../plan/stack.md#nine-places-where-the-sdk-must-be-deviated-from">deviation
    /// 8</see>: "reconstruct from <c>McpProtocolException</c> and strip the
    /// prefix").</b> Measured at build-order step 8 and again here: <c>code</c>
    /// and <c>data</c> survive the SDK's round trip intact and unflattened, so
    /// the reconstruction that deviation asks for was solving a problem that did
    /// not exist. What did need solving was the message, and splicing the raw
    /// frame answers it without a reconstruction at all.
    /// </para>
    /// </remarks>
    private async Task AnswerChildErrorAsync(
        McpServer caller,
        RequestId callerId,
        RequestId childId,
        McpProtocolException exception,
        CancellationToken cancellationToken)
    {
        JsonRpcError answer;

        if (_link.Session.TryTakePayload(childId, out var payload) && payload.IsError)
        {
            answer = new JsonRpcError { Id = callerId, Error = DetailFrom(payload) };
            Verbatim.Attach(answer, payload.Json);
        }
        else
        {
            ProxyLog.VerbatimPayloadMissing(_logger, childId.ToString(), callerId.ToString());

            answer = new JsonRpcError
            {
                Id = callerId,
                Error = new JsonRpcErrorDetail
                {
                    Code = (int)exception.ErrorCode,
                    Message = exception.Message.StartsWith(RemoteErrorPrefix, StringComparison.Ordinal)
                        ? exception.Message[RemoteErrorPrefix.Length..]
                        : exception.Message,
                },
            };
        }

        await caller.SendMessageAsync(answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a caller whose call the child never completed — it died, its
    /// stdout ended, the transport went away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the failure shape build-order step 8 measured and this step
    /// removes.</b> Through the SDK's typed <c>CallToolHandler</c>, an exception
    /// of any cause becomes a JSON-RPC <i>success</i> carrying
    /// <c>isError: true</c> and the text <c>"An error occurred invoking 'x'."</c>
    /// — identical for a child that died and for an unknown content type, naming
    /// neither. A success envelope with the only bad news inside the body is
    /// exactly what this project exists to eliminate, and it was arriving from
    /// our own dependency.
    /// </para>
    /// <para>
    /// It is answered as a JSON-RPC <b>error</b> because it is a transport
    /// failure rather than a tool outcome, and the cause is named. <b>The
    /// model-facing wording of every error is</b>
    /// <see href="../../../plan/H-model-surface.md">§H.4</see><b>'s catalogue and
    /// arrives at step 13</b>; what is fixed here is the shape and the fact that
    /// a cause reaches the caller at all.
    /// </para>
    /// </remarks>
    private async Task AnswerTransportFailureAsync(
        McpServer caller,
        RequestId callerId,
        string method,
        Exception cause,
        CancellationToken cancellationToken)
    {
        ProxyLog.ChildDidNotAnswer(_logger, method, callerId.ToString(), cause);

        var answer = new JsonRpcError
        {
            Id = callerId,
            Error = new JsonRpcErrorDetail
            {
                Code = (int)McpErrorCode.InternalError,
                Message = $"The browser child did not answer '{method}': {cause.GetType().Name}: {cause.Message}",
            },
        };

        await caller.SendMessageAsync(answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tells the child to stop, by the id BrowserAI put on the request it is
    /// working on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Measured 2026-08-16, and it contradicts
    /// <see href="../../../plan/stack.md#nine-places-where-the-sdk-must-be-deviated-from">deviation
    /// 6</see>'s prescribed remedy.</b> That remedy is <i>"assign
    /// <c>JsonRpcRequest.Id</c> yourself and send <c>notifications/cancelled</c>
    /// from your own <c>ct.Register</c>"</i>. The first half is necessary and is
    /// done. <b>The second half does not work</b>, and it fails for the exact
    /// reason the same document gives for the SDK's own relay failing.
    /// </para>
    /// <para>
    /// A registration scoped to the call it is protecting is disposed as that
    /// call unwinds. <c>SendRequestAsync</c> waits on
    /// <c>tcs.Task.WaitAsync(ct)</c>, which registers its own callback
    /// <i>after</i> ours; CTS callbacks run <b>LIFO</b>, so <c>WaitAsync</c>'s
    /// runs first, the await throws, the <c>using</c> disposes our registration,
    /// and ours is unregistered before LIFO ever reaches it. Observed directly:
    /// the token reports <c>CanBeCanceled</c>, the call throws
    /// <see cref="OperationCanceledException"/> with
    /// <c>IsCancellationRequested</c> true, the child records the
    /// <c>tools/call</c> — and the registration callback logs nothing, because
    /// it never ran.
    /// </para>
    /// <para>
    /// Announcing from the <c>catch</c> instead is strictly better on every
    /// axis: it is awaited rather than fire-and-forget, it cannot run before the
    /// request it names has been sent, and it is reached by the one path that
    /// definitely executes.
    /// </para>
    /// </remarks>
    /// <param name="childId">The request id, as the string it went out as.</param>
    /// <returns>A task that completes once the notification is on the wire, or has failed to be.</returns>
    private async Task AnnounceCancellationAsync(string childId)
    {
        try
        {
            // Not the caller's token: it is the thing that just fired, and a
            // notification sent under it would be cancelled before it left.
            await _client.SendMessageAsync(
                new JsonRpcNotification
                {
                    Method = NotificationMethods.CancelledNotification,
                    Params = new JsonObject
                    {
                        ["requestId"] = childId,
                        ["reason"] = "The caller cancelled the request.",
                    },
                },
                CancellationToken.None).ConfigureAwait(false);

            ProxyLog.CancellationForwarded(_logger, childId);
        }
#pragma warning disable CA1031 // A child that has already gone cannot be told to stop, and that is not a failure of this call.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ProxyLog.CancellationNotForwarded(_logger, childId, ex);
        }
    }

    private async ValueTask RelayToCallerAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _caller) is not { } caller)
        {
            return;
        }

        // A fresh envelope, for the same reason results get one: the child's own
        // message may carry a RelatedTransport that would send this straight
        // back where it came from. The params -- progress token included -- pass
        // through untouched, which is what puts it under the caller's token.
        await caller.SendMessageAsync(
            new JsonRpcNotification { Method = notification.Method, Params = notification.Params },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Source-generated log messages for the proxy.</summary>
internal static partial class ProxyLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Child protocol negotiated. requested={Requested} negotiated={Negotiated}")]
    public static partial void ChildProtocolNegotiated(ILogger logger, string requested, string negotiated);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "The raw frame for child request {ChildRequestId} was not captured, so caller request {CallerRequestId} is being answered from a re-serialised result. Passthrough is no longer byte-identical.")]
    public static partial void VerbatimPayloadMissing(ILogger logger, string childRequestId, string callerRequestId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "The child did not answer '{Method}' for caller request {CallerRequestId}.")]
    public static partial void ChildDidNotAnswer(ILogger logger, string method, string callerRequestId, Exception exception);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Forwarded notifications/cancelled for child request {ChildRequestId}.")]
    public static partial void CancellationForwarded(ILogger logger, string childRequestId);

    /// <summary>
    /// <paramref name="cancellable"/> is not decoration: a filter handed an
    /// uncancellable token would leave every cancellation a local abort with
    /// nothing downstream, and that is invisible in every other signal.
    /// </summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="method">The method being forwarded.</param>
    /// <param name="childRequestId">The id BrowserAI put on the outgoing request.</param>
    /// <param name="cancellable">Whether the caller's token can be cancelled at all.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Debug,
        Message = "Forwarding '{Method}' to the child as {ChildRequestId}. cancellable={Cancellable}")]
    public static partial void Forwarding(ILogger logger, string method, string childRequestId, bool cancellable);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Could not forward notifications/cancelled for child request {ChildRequestId}; the child may still be working.")]
    public static partial void CancellationNotForwarded(ILogger logger, string childRequestId, Exception exception);
}
