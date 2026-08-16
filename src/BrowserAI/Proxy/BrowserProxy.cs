// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BrowserAI.Proxy;

/// <summary>
/// The caller-facing MCP server: BrowserAI's own five tools, every
/// <c>@playwright/mcp</c> tool behind them, and the routing that decides which
/// child a call goes to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing on the forwarding path touches an SDK contract type, and that is
/// the point of the design rather than a stylistic preference.</b> Every loss
/// this design exists to close is silent, and each one is produced by a type that
/// is doing its job: <c>ContentBlock</c>'s converter drops unknown properties and
/// throws on an unknown content <i>type</i>, which is correct
/// forward-compatibility for a client and data loss for a proxy; <c>Tool</c>
/// carries no <c>[JsonExtensionData]</c>, so a typed <c>ListToolsResult</c> round
/// trip discards tool-level extensions;
/// <c>ListToolsAsync(RequestOptions?, ct)</c> drops tools whose annotations fail
/// SEP-2243 validation without raising anything. So requests go out as raw
/// <see cref="JsonRpcRequest"/>s and a <c>tools/call</c> answer comes back as the
/// exact bytes the child wrote.
/// </para>
/// <para>
/// <b><c>tools/list</c> is the one answer that is deliberately not
/// byte-identical</b>, because rewriting it is the job: the five authored tools
/// go in front and a required <c>session</c> parameter is injected into every
/// upstream <c>inputSchema</c>. The rewrite is done on the
/// <see cref="JsonNode"/> the child sent, never on a typed schema, so a
/// tool-level member no contract knows about still survives —
/// <c>LosslessPassthroughTests</c> asserts exactly that. Renaming remains
/// forbidden: upstream names pass through byte for byte.
/// </para>
/// <para>
/// <b>There is deliberately no typed fallback.</b> <c>Handlers</c> carries
/// neither a <c>ListToolsHandler</c> nor a <c>CallToolHandler</c>, so if the
/// filter below ever failed to short-circuit, the caller would get <c>-32601</c>
/// rather than a quietly lossy answer. A loud wrong answer can be found; a lossy
/// right-looking one cannot.
/// </para>
/// </remarks>
internal sealed class BrowserProxy : IAsyncDisposable
{
    /// <summary>
    /// The protocol revision BrowserAI speaks to a child, re-exported here
    /// because it is what the suite pins against.
    /// </summary>
    public const string ChildProtocolVersion = ChildConnection.ChildProtocolVersion;

    /// <summary>
    /// What <c>CreateRemoteProtocolExceptionFromError</c> puts in front of every
    /// message it lifts out of a child's JSON-RPC error.
    /// </summary>
    /// <remarks>
    /// Only reached on the path where the raw error frame was not captured. The
    /// ordinary path never meets the prefix at all, because it never reads the
    /// message off the exception.
    /// </remarks>
    private const string RemoteErrorPrefix = "Request failed (remote): ";

    private readonly ChildConnection _surface;
    private readonly SessionManager _sessions;
    private readonly ILogger _logger;

    private McpServer? _caller;
    private int _disposed;

    private BrowserProxy(ChildConnection surface, SessionManager sessions, ILogger logger)
    {
        _surface = surface;
        _sessions = sessions;
        _logger = logger;
    }

    /// <summary>The revision negotiated with the run's own child.</summary>
    public string? NegotiatedChildProtocolVersion => _surface.NegotiatedProtocolVersion;

    /// <summary>Starts the run's own child and completes the handshake with it.</summary>
    /// <param name="options">What to start, from <see cref="Runtime.ChildLaunch"/>.</param>
    /// <param name="loggerFactory">Where the proxy, the transport and the session log.</param>
    /// <param name="environment">Where sessions keep their index, payload and configs.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The connected proxy.</returns>
    public static async Task<BrowserProxy> ConnectAsync(
        ChildProcessOptions options,
        ILoggerFactory loggerFactory,
        SessionEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return await ConnectAsync(
            new DirectStdioClientTransport(options, loggerFactory),
            loggerFactory,
            environment,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Completes the handshake over a transport the caller supplies, rather than
    /// over one this class starts a process for.
    /// </summary>
    /// <remarks>
    /// <b>The seam exists for the in-process test layer and nothing else uses
    /// it.</b> A proxy has two hops, so a harness that stands a fake child on the
    /// far end has to reach the client leg without a process. Everything that
    /// decides behaviour — the pinned revision, the negotiation check, the raw
    /// forwarding path, the <c>tools/list</c> rewrite — is below this line rather
    /// than above it, so the harness exercises the same code the product runs.
    /// </remarks>
    /// <param name="transport">The client transport to connect over. The SDK client owns it.</param>
    /// <param name="loggerFactory">Where the proxy and the session log.</param>
    /// <param name="environment">Where sessions keep their index, payload and configs.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The connected proxy.</returns>
    public static async Task<BrowserProxy> ConnectAsync(
        IClientTransport transport,
        ILoggerFactory loggerFactory,
        SessionEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(environment);

        var logger = loggerFactory.CreateLogger<BrowserProxy>();

        // The manager is built before the connection because a session child's
        // progress notifications are relayed through the proxy, and the proxy
        // does not exist until the surface child has handshaken. The closure
        // reads `proxy` late, which is what unties the knot.
        BrowserProxy? proxy = null;
        SessionManager? sessions = null;
        ChildConnection? surface = null;

        ValueTask relay(JsonRpcNotification notification, CancellationToken token) =>
            proxy is null ? ValueTask.CompletedTask : proxy.RelayToCallerAsync(notification, token);

        try
        {
            // CA2000 is disabled for these two statements and nothing else. The
            // pattern the rule asks for is exactly what is here -- locals
            // declared before the try, nulled the instant ownership moves, and an
            // unconditional disposal in the finally -- but both types are
            // IAsyncDisposable rather than IDisposable, and the rule's dataflow
            // does not follow an `await x.DisposeAsync()` in a finally.
#pragma warning disable CA2000
            sessions = new SessionManager(environment, loggerFactory, relay);
            surface = await ChildConnection.ConnectAsync(transport, loggerFactory, "browserai-", relay, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2000

            proxy = new BrowserProxy(surface, sessions, logger);

            // Ownership of both has moved into the proxy, which the caller now
            // owns and disposes.
            sessions = null;
            surface = null;

            return proxy;
        }
        finally
        {
            if (surface is not null)
            {
                await surface.DisposeAsync().ConfigureAwait(false);
            }

            if (sessions is not null)
            {
                await sessions.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>The options the caller-facing MCP server is built from.</summary>
    /// <returns>Server options whose tool methods are short-circuited by a message filter.</returns>
    public McpServerOptions ServerOptions()
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "BrowserAI", Version = ChildConnection.Version },

            // Upward: null means every revision the SDK implements. The caller
            // is a client this project does not control and does not get to hold
            // back; the child's ceiling is the child's business and stops at the
            // pin in ChildConnection. That split is the whole point, and it is
            // why these two disagree on purpose.
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

        // Sessions first: each owns a child whose job holds a browser, and each
        // holds a directory lock that should be released while the process is
        // still able to log why.
        await _sessions.DisposeAsync().ConfigureAwait(false);
        await _surface.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Rebuilds a typed error detail from the child's own error bytes.</summary>
    /// <remarks>
    /// This is the <i>fallback</i> shape only. The frame that actually reaches
    /// the caller is written from the payload verbatim, so this object exists so
    /// that a message which somehow escaped the verbatim path would still be
    /// semantically right rather than empty.
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

    private static JsonObject TextResult(string text, bool isError) =>
        new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        };

    private async Task OnIncomingAsync(McpMessageHandler next, MessageContext context, CancellationToken cancellationToken)
    {
        // Recorded for every message, not only the two that are forwarded: the
        // progress relay needs somewhere to send to, and a child may report
        // progress on the very first call.
        Volatile.Write(ref _caller, context.Server);

        if (context.JsonRpcMessage is JsonRpcRequest request)
        {
            switch (request.Method)
            {
                case RequestMethods.ToolsList:
                    await AnswerToolsListAsync(context.Server, request, cancellationToken).ConfigureAwait(false);
                    return;

                case RequestMethods.ToolsCall:
                    await AnswerToolsCallAsync(context.Server, request, cancellationToken).ConfigureAwait(false);
                    return;

                default:
                    break;
            }
        }

        await next(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers <c>tools/list</c> from the run's own child, rewritten.
    /// </summary>
    /// <remarks>
    /// <b>One static list, and it has to be the union.</b> The MCP spec forbids
    /// the tool set varying per connection and SEP-2567 removed protocol-level
    /// sessions outright, so <c>init</c> cannot shrink it. The run's own child is
    /// started with every capability any mode can have, and a call its session's
    /// mode does not permit is refused at call time instead.
    /// </remarks>
    private async Task AnswerToolsListAsync(McpServer caller, JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var answer = await _surface.AskAsync(request.Method, request.Params, cancellationToken).ConfigureAwait(false);

        if (answer.Response is not { } response)
        {
            await AnswerFailureAsync(caller, request, answer, cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = response.Result as JsonObject ?? [];

        await caller.SendMessageAsync(
            new JsonRpcResponse { Id = request.Id, Result = SessionToolSurface.Rewrite(result) },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AnswerToolsCallAsync(McpServer caller, JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var parameters = request.Params as JsonObject;
        var name = (parameters?["name"] as JsonValue)?.GetValue<string>();
        var arguments = parameters?["arguments"] as JsonObject;

        if (SessionToolSurface.IsAuthored(name))
        {
            var outcome = await _sessions.InvokeAsync(name!, arguments, cancellationToken).ConfigureAwait(false);

            await caller.SendMessageAsync(
                new JsonRpcResponse { Id = request.Id, Result = TextResult(outcome.Text, outcome.IsError) },
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var session = (arguments?[SessionToolSurface.SessionParameter] as JsonValue)?.GetValue<string>();
        var target = _surface;
        var forwarded = request.Params;

        if (session is not null)
        {
            if (_sessions.Find(session) is not { } live)
            {
                ProxyLog.UnknownSession(_logger, name ?? "<none>", session);

                await caller.SendMessageAsync(
                    new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = TextResult(
                            $"'{session}' is not a session this BrowserAI is driving, so '{name}' was not run and nothing was changed. "
                            + $"Call {SessionToolSurface.Resume} with directory='{session}' to open it — a session is resumable forever, so one that exists can always be reopened — or {SessionToolSurface.Init} to create it, or {SessionToolSurface.List} to see what is under a path.",
                            isError: true),
                    },
                    cancellationToken).ConfigureAwait(false);

                return;
            }

            target = live.Child;

            // The child has never heard of `session`; BrowserAI added it. Removed
            // from a CLONE rather than from the caller's own node, because the
            // request object is the SDK's and may still be read after this.
            var clone = request.Params?.DeepClone() as JsonObject;

            if (clone?["arguments"] is JsonObject cloned)
            {
                _ = cloned.Remove(SessionToolSurface.SessionParameter);
            }

            forwarded = clone;
        }

        var answer = await target.AskAsync(request.Method, forwarded, cancellationToken).ConfigureAwait(false);

        if (answer.Response is { } response)
        {
            await AnswerChildResultAsync(caller, request.Id, response, answer.Payload, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AnswerFailureAsync(caller, request, answer, cancellationToken).ConfigureAwait(false);
    }

    private async Task AnswerFailureAsync(
        McpServer caller,
        JsonRpcRequest request,
        ChildAnswer answer,
        CancellationToken cancellationToken)
    {
        if (answer.ProtocolFailure is { } protocolFailure)
        {
            await AnswerChildErrorAsync(caller, request.Id, protocolFailure, answer.Payload, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AnswerTransportFailureAsync(
            caller,
            request.Id,
            request.Method,
            answer.TransportFailure ?? new InvalidOperationException("The child answered with neither a result nor an error."),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AnswerChildResultAsync(
        McpServer caller,
        RequestId callerId,
        JsonRpcResponse response,
        VerbatimPayload? payload,
        CancellationToken cancellationToken)
    {
        // A fresh envelope rather than the child's own: JsonRpcMessage.Context
        // carries RelatedTransport, and the SDK's send path routes to it in
        // preference to the session's transport -- so a forwarded object would
        // be sent back to the child it came from.
        var answer = new JsonRpcResponse { Id = callerId, Result = response.Result };

        if (payload is { } captured)
        {
            Verbatim.Attach(answer, captured.Json);
        }
        else
        {
            // Reached only if the transport failed to capture the frame. The
            // answer is still semantically right -- Result is the child's own
            // JsonNode -- but its escaping is now ours, so the one claim the
            // passthrough exists to make is no longer true of it. Said out loud
            // rather than absorbed.
            ProxyLog.VerbatimPayloadMissing(_logger, callerId.ToString());
        }

        await caller.SendMessageAsync(answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers a caller with the child's own JSON-RPC error.</summary>
    /// <remarks>
    /// <b>The <c>"Request failed (remote): "</c> prefix is never met on this
    /// path, rather than met and stripped.</b> The SDK does add it — it is real,
    /// and <c>SdkErrorShapeTests</c> is what keeps that checked — but the bytes
    /// written here come from the child's frame, so the message that reaches the
    /// caller is the message the child sent.
    /// </remarks>
    private async Task AnswerChildErrorAsync(
        McpServer caller,
        RequestId callerId,
        McpProtocolException exception,
        VerbatimPayload? payload,
        CancellationToken cancellationToken)
    {
        JsonRpcError answer;

        if (payload is { } captured)
        {
            answer = new JsonRpcError { Id = callerId, Error = DetailFrom(captured) };
            Verbatim.Attach(answer, captured.Json);
        }
        else
        {
            ProxyLog.VerbatimPayloadMissing(_logger, callerId.ToString());

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
    /// Through the SDK's typed <c>CallToolHandler</c>, an exception of any cause
    /// becomes a JSON-RPC <i>success</i> carrying <c>isError: true</c> and the
    /// text <c>"An error occurred invoking 'x'."</c> — identical for a child that
    /// died and for an unknown content type, naming neither. It is answered as a
    /// JSON-RPC <b>error</b> here because it is a transport failure rather than a
    /// tool outcome, and the cause is named.
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

    private async ValueTask RelayToCallerAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _caller) is not { } caller)
        {
            return;
        }

        // A fresh envelope, for the same reason results get one: the child's own
        // message may carry a RelatedTransport that would send this straight back
        // where it came from. The params -- progress token included -- pass
        // through untouched, which is what puts it under the caller's token.
        await caller.SendMessageAsync(
            new JsonRpcNotification { Method = notification.Method, Params = notification.Params },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Source-generated log messages for the proxy.</summary>
internal static partial class ProxyLog
{
    /// <summary>The child agreed a protocol revision.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="requested">What BrowserAI asked for.</param>
    /// <param name="negotiated">What came back.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Child protocol negotiated. requested={Requested} negotiated={Negotiated}")]
    public static partial void ChildProtocolNegotiated(ILogger logger, string requested, string negotiated);

    /// <summary>A result had to be re-serialised because its raw frame was not captured.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="callerRequestId">The caller request being answered.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "The raw frame for caller request {CallerRequestId} was not captured, so it is being answered from a re-serialised result. Passthrough is no longer byte-identical.")]
    public static partial void VerbatimPayloadMissing(ILogger logger, string callerRequestId);

    /// <summary>The child never answered.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="method">The method being forwarded.</param>
    /// <param name="callerRequestId">The caller request being answered.</param>
    /// <param name="exception">Why.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "The child did not answer '{Method}' for caller request {CallerRequestId}.")]
    public static partial void ChildDidNotAnswer(ILogger logger, string method, string callerRequestId, Exception exception);

    /// <summary>A cancellation was forwarded to a child.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="childRequestId">The id BrowserAI put on the outgoing request.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Forwarded notifications/cancelled for child request {ChildRequestId}.")]
    public static partial void CancellationForwarded(ILogger logger, string childRequestId);

    /// <summary>A cancellation could not be forwarded.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="childRequestId">The id BrowserAI put on the outgoing request.</param>
    /// <param name="exception">Why.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Could not forward notifications/cancelled for child request {ChildRequestId}; the child may still be working.")]
    public static partial void CancellationNotForwarded(ILogger logger, string childRequestId, Exception exception);

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

    /// <summary>A tool call named a session this process is not driving.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="session">The session it named.</param>
    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "'{Tool}' named session '{Session}', which is not open in this process; the caller was told to resume it.")]
    public static partial void UnknownSession(ILogger logger, string tool, string session);
}
