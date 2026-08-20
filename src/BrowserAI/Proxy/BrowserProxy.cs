// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Artifacts;
using BrowserAI.Hosting;
using BrowserAI.Protocol;
using BrowserAI.Runtime;
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
            ServerInfo = new Implementation { Name = "BrowserAI", Version = BuildVersion.Current },

            // The only channel that reaches a model before it calls anything,
            // and the only one that can pre-empt the first mistake after a
            // restart: a browser tool called with no session. Rendered from the
            // one mode table, capped at 2,048 UTF-16 characters because the
            // client truncates it there in silence. Corrected 2026-08-18
            // (previously "capped at 2 KB"), which was the byte reading the
            // 2026-08-18 measurement @ Claude Code 2.1.234 retired.
            ServerInstructions = Proxy.ServerInstructions.Text,

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

        // ⚠️ The one thing this outgoing filter does, and it exists because the
        // SDK advertises a capability we never asked for. See UnadvertiseLogging.
        options.Filters.Message.OutgoingFilters.Add(next => (context, cancellationToken) =>
        {
            UnadvertiseLogging(context);
            return next(context, cancellationToken);
        });

        return options;
    }

    /// <summary>
    /// Removes <c>capabilities.logging</c> from the <c>initialize</c> result on
    /// its way out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>BrowserAI advertised MCP logging and never asked to.</b>
    /// <see cref="ServerOptions"/> declares <c>Tools</c> and nothing else, but
    /// <c>McpServerImpl</c>'s constructor builds a fresh
    /// <see cref="ServerCapabilities"/> and then calls nine <c>Configure*</c>
    /// methods over it. <c>ConfigureTools</c>, <c>ConfigurePrompts</c>,
    /// <c>ConfigureResources</c> and <c>ConfigureCompletion</c> each begin with an
    /// early return when nothing was supplied; <c>ConfigureLogging</c> has no such
    /// guard and reaches <c>ServerCapabilities.Logging = new()</c>
    /// unconditionally, registering a <c>logging/setLevel</c> handler with it.
    /// Read out of <c>ModelContextProtocol</c> 2.2.0's shipped source at
    /// <c>v2.2.0</c>.
    /// </para>
    /// <para>
    /// <b>So the handshake claimed a capability this server does not implement</b>
    /// — it has never emitted a <c>notifications/message</c> and never will — and
    /// a client that called <c>logging/setLevel</c> got <c>{}</c> and then silence
    /// for ever. That is this project's founding failure shape: something reports
    /// a capability it does not have, and nothing anywhere goes red. It was also a
    /// live divergence from the child, whose golden snapshot records
    /// <c>{"tools":{}}</c> and no logging at all, so a reader comparing the two
    /// ends would have concluded BrowserAI adds logging.
    /// </para>
    /// <para>
    /// <b>Why an outgoing filter rather than the options object.</b> Setting
    /// <c>Capabilities.Logging = null</c> does nothing — the constructor overwrites
    /// it — and the property is <c>[Obsolete(DiagnosticId = "MCP9005")]</c> at
    /// 2.2.0, so naming it at all needs a suppression, which the style rule
    /// forbids. Rewriting the frame is the only route that neither lies nor
    /// suppresses. It is <b>subtractive only</b>: nothing is added, nothing is
    /// reordered, and every other member of the result is the SDK's own node.
    /// </para>
    /// <para>
    /// <b>MCP deprecated logging in SEP-2577</b> and its stated migration path for
    /// a stdio server is <i>log to stderr</i>, which is what BrowserAI already
    /// does. So there is nothing here to adopt later that this removes.
    /// </para>
    /// </remarks>
    /// <param name="context">The outgoing message.</param>
    private static void UnadvertiseLogging(MessageContext context)
    {
        // Shape rather than id, and deliberately: `initialize` is the only result
        // carrying both of these, the SDK owns the id, and matching on shape needs
        // no state shared between the two filter directions. A `tools/list` result
        // has `tools`; a `tools/call` result has `content`; neither has
        // `protocolVersion`.
        if (context.JsonRpcMessage is JsonRpcResponse { Result: JsonObject result }
            && result.ContainsKey("protocolVersion")
            && result["capabilities"] is JsonObject capabilities)
        {
            _ = capabilities.Remove("logging");
        }
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
    /// <para>
    /// <b>One static list, and it has to be the union.</b> The MCP spec forbids
    /// the tool set varying per connection and SEP-2567 removed protocol-level
    /// sessions outright, so <c>init</c> cannot shrink it. The run's own child is
    /// started with every capability any mode can have.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-19 (previously ", and a call its session's mode
    /// does not permit is refused at call time instead").</b> Nothing refuses it:
    /// the <c>(tool, mode)</c> matrix went on 2026-08-18 and the enforcement that
    /// replaced it is the <i>child's own capability set</i> — a session without
    /// <c>storage</c> has no cookie tools in its process, so the call is forwarded
    /// and upstream answers that the tool does not exist. The one refusal this
    /// proxy still makes by name is <c>browser_annotate</c>, and that is liveness
    /// rather than permission.
    /// </para>
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

        var tool = name ?? "<none>";
        var session = (arguments?[SessionToolSurface.SessionParameter] as JsonValue)?.GetValue<string>();

        // Mandatory, with no fall-through to the run's own child. Before this
        // step a call naming no session was answered by the surface child, which
        // is a session nobody chose the mode of -- so every enforcement decision
        // below could be sidestepped by omitting an argument.
        if (string.IsNullOrWhiteSpace(session))
        {
            ProxyLog.SessionMissing(_logger, tool);
            await RefuseAsync(caller, request.Id, SessionErrors.SessionMissing(tool), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_sessions.Find(session) is not { } live)
        {
            ProxyLog.UnknownSession(_logger, tool, session);
            await RefuseAsync(caller, request.Id, SessionManager.ExplainUnknownSession(tool, session), cancellationToken).ConfigureAwait(false);
            return;
        }

        // The one refusal left, and it is a LIVENESS guard rather than a
        // permission: `browser_annotate` opens a dashboard window, has no
        // self-timeout, and blocks until a human draws in it -- so an unattended
        // run that called it would hang until it was killed. It is withheld from
        // `tools/list` as well, and this is the other half of that: a model that
        // knows the name from upstream rather than from this server can still
        // send the call, and forwarding it is what the withholding exists to
        // prevent.
        //
        // Corrected 2026-08-18 (previously `Decide(tool, live.Mode)`, refusing
        // only on a mode that opens no window). The daemon lands in %TEMP% and
        // outlives its parent whatever the window says, so there was no mode
        // this was safe on and no mode argument left to pass. Modes themselves
        // went on 2026-08-20.
        //
        // Corrected 2026-08-18 (previously "THE enforcement point", deciding a
        // (tool, mode) permission matrix): that matrix was never a boundary
        // against the caller, who chooses the session directory and reads the
        // profile inside it as the same user. Change control lives at the
        // release gate, in the four golden snapshots.
        var decision = SessionToolPolicy.Decide(tool);

        if (!decision.IsAllowed)
        {
            ProxyLog.ToolRefused(_logger, tool, live.Location.FullPath);
            await RefuseAsync(caller, request.Id, decision.Refusal!, cancellationToken).ConfigureAwait(false);
            return;
        }

        // First-run provisioning, and it happens before the child hears about
        // the call for the same reason the liveness decision does: a browser tool
        // forwarded now would block inside the child's own launch for the whole
        // download and answer with upstream's `npx` advice at the end of it.
        if (_sessions.ProvisioningRefusal(tool, live) is { } notYet)
        {
            await RefuseAsync(caller, request.Id, notYet, cancellationToken).ConfigureAwait(false);
            return;
        }

        // §F's first half, and it happens before the child hears about the call:
        // a `filename` is rewritten into the folder its generator prefix
        // implies, so the file is born in the right place rather than swept
        // there. A refusal here is a refusal -- never a path normalised into
        // something that happens to land somewhere.
        live.Artifacts.Observe(arguments);

        ArtifactPlan? plan;

        try
        {
            plan = live.Artifacts.Plan(tool, arguments);
        }
        catch (SessionToolException refusal)
        {
            ProxyLog.FilenameRefused(_logger, tool, live.Location.FullPath);
            await RefuseAsync(caller, request.Id, refusal.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The child has never heard of `session`; BrowserAI added it. Removed
        // from a CLONE rather than from the caller's own node, because the
        // request object is the SDK's and may still be read after this.
        var forwarded = request.Params?.DeepClone() as JsonObject;

        if (forwarded?["arguments"] is JsonObject cloned)
        {
            _ = cloned.Remove(SessionToolSurface.SessionParameter);
        }

        if (plan is not null)
        {
            ArtifactRouter.Apply(plan, forwarded);
        }

        // The one timer, reset here and nowhere else. A call this session
        // forwards is what "being driven" means for a browser-idle timer — a
        // call refused by the mode policy or by provisioning never reaches a
        // browser and never keeps one warm — and the scope holds the call
        // outstanding across the await, so a navigation that outlives the whole
        // period cannot have the browser closed underneath it.
        using var driving = live.Idle.Call();

        var answer = await live.Child.AskAsync(request.Method, forwarded, cancellationToken).ConfigureAwait(false);

        if (answer.Response is { } response)
        {
            // The one answer BrowserAI rewrites rather than forwards, and the
            // trade is deliberate: upstream's "not installed" message ends with
            // an npx command this product does not ship, which resolves a
            // different package at a different revision into a directory
            // BrowserAI never launches from -- and a model will run it. Byte
            // identity is given up for exactly this payload, only when the
            // marker is present, and the fact is logged rather than absorbed.
            if (Remediate(response) is { } corrected)
            {
                live.Artifacts.Release(plan);
                ProxyLog.RemediationRewritten(_logger, tool, live.Location.FullPath);

                await caller.SendMessageAsync(
                    new JsonRpcResponse { Id = request.Id, Result = corrected },
                    cancellationToken).ConfigureAwait(false);

                return;
            }

            // §F's second half, which ships with the first or not at all:
            // relocating a file while telling the model otherwise is a new
            // silent failure introduced by the fix for an old one. It carries
            // the image block back as well, on the calls whose name BrowserAI
            // supplied -- see ArtifactTools.
            var completion = live.Artifacts.Complete(plan);

            await AnswerChildResultAsync(caller, request.Id, response, answer.Payload, completion, cancellationToken).ConfigureAwait(false);
            return;
        }

        live.Artifacts.Release(plan);
        await AnswerFailureAsync(caller, request, answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces upstream's install advice in a child's answer, or answers
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// <b>Every ordinary answer returns <see langword="null"/> here and goes back
    /// as the child's own bytes.</b> The scan is one <c>Contains</c> per text
    /// block against a marker that appears in exactly one upstream sentence, and
    /// it is worth that cost because the sentence is an <i>instruction</i>: a
    /// model that reads it will run <c>npx</c>, and on the paths where it appears
    /// at all the answer is already a failure with no bytes worth preserving.
    /// </remarks>
    private JsonObject? Remediate(JsonRpcResponse response)
    {
        if (response.Result is not JsonObject result || result["content"] is not JsonArray)
        {
            return null;
        }

        var copy = (JsonObject)result.DeepClone();
        var rewritten = false;

        foreach (var block in (JsonArray)copy["content"]!)
        {
            if (block is not JsonObject text
                || (text["text"] as JsonValue)?.GetValue<string>() is not { } original
                || ProvisioningRemediation.Rewrite(original, _sessions.BrowsersDirectory) is not { } replacement)
            {
                continue;
            }

            text["text"] = replacement;
            rewritten = true;
        }

        return rewritten ? copy : null;
    }

    private static async Task RefuseAsync(
        McpServer caller,
        RequestId callerId,
        string text,
        CancellationToken cancellationToken) =>
        await caller.SendMessageAsync(
            new JsonRpcResponse { Id = callerId, Result = TextResult(text, isError: true) },
            cancellationToken).ConfigureAwait(false);

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

    /// <summary>Answers a caller with the child's result, and what BrowserAI did to the file it names.</summary>
    /// <remarks>
    /// <para>
    /// <b>Byte-identity is a property of forwarding, and this is where that is
    /// made precise.</b> With nothing to append — every call that names no file,
    /// which is nearly all of them — the child's own bytes are written unchanged,
    /// which is step 9's guarantee untouched. Otherwise the child's
    /// <c>content</c> array gains elements by a splice into those same bytes:
    /// nothing the child wrote is re-serialised, re-escaped or reordered, and
    /// what is added is appended at the end.
    /// </para>
    /// <para>
    /// <b>One element, or two.</b> The note is always one text block. A
    /// screenshot BrowserAI named gains a second, and it is the <c>image</c>
    /// block upstream itself would have sent had the caller's own arguments
    /// reached it — see <c>ArtifactTools</c> for why that is a restoration
    /// rather than an addition, and why it is that tool alone.
    /// </para>
    /// <para>
    /// The <see cref="JsonNode"/> arm is the fallback for a result carrying no
    /// top-level <c>content</c> array. It is logged rather than absorbed, because
    /// the escaping is then ours.
    /// </para>
    /// </remarks>
    private async Task AnswerChildResultAsync(
        McpServer caller,
        RequestId callerId,
        JsonRpcResponse response,
        VerbatimPayload? payload,
        ArtifactAnswer? completion,
        CancellationToken cancellationToken)
    {
        // A fresh envelope rather than the child's own: JsonRpcMessage.Context
        // carries RelatedTransport, and the SDK's send path routes to it in
        // preference to the session's transport -- so a forwarded object would
        // be sent back to the child it came from.
        var answer = new JsonRpcResponse { Id = callerId, Result = response.Result };

        if (completion is null)
        {
            if (payload is { } untouched)
            {
                Verbatim.Attach(answer, untouched.Json);
            }
            else
            {
                // Reached only if the transport failed to capture the frame. The
                // answer is still semantically right -- Result is the child's own
                // JsonNode -- but its escaping is now ours, so the one claim the
                // passthrough exists to make is no longer true of it. Said out
                // loud rather than absorbed.
                ProxyLog.VerbatimPayloadMissing(_logger, callerId.ToString());
            }

            await caller.SendMessageAsync(answer, cancellationToken).ConfigureAwait(false);
            return;
        }

        byte[][] blocks = completion.Image is { } image
            ? [ResultNote.Block(completion.Note), ResultNote.ImageBlock(image.Data, image.MediaType)]
            : [ResultNote.Block(completion.Note)];

        // The IsEnabled guard is the analyzer's (CA1873) and it is right: this
        // line is Debug, and formatting the request id to build a message
        // nobody has asked for is work on the hot path of every screenshot.
        if (completion.Image is { } appended)
        {
            ProxyLog.InlineImageRestored(_logger, callerId, appended.MediaType, appended.Data.Length);
        }

        var spliced = payload is { } captured ? ResultNote.Append(captured.Json, blocks) : null;

        if (spliced is not null)
        {
            Verbatim.Attach(answer, spliced);
        }
        else
        {
            ProxyLog.NoteNotSpliced(_logger, callerId.ToString());
        }

        // Kept in step with the bytes on both arms. On the spliced arm nothing
        // reads it, but a message whose object and whose bytes disagree is a
        // trap for whatever reads it next.
        answer.Result = WithNote(response.Result, completion);

        await caller.SendMessageAsync(answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The child's result with what BrowserAI appended, as nodes.</summary>
    private static JsonObject WithNote(JsonNode? result, ArtifactAnswer completion)
    {
        var copy = result?.DeepClone() as JsonObject ?? [];

        if (copy["content"] is not JsonArray content)
        {
            content = [];
            copy["content"] = content;
        }

        // The (JsonNode) cast is the one AOT trap the 2026-08-15 spike found
        // in our own code rather than the SDK's: the generic overload is
        // RequiresDynamicCode and turns the publish red.
        content.Add((JsonNode)ResultNote.Node(completion.Note));

        if (completion.Image is { } image)
        {
            content.Add((JsonNode)ResultNote.ImageNode(image.Data, image.MediaType));
        }

        return copy;
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

    /// <summary>A tool call arrived with no session at all.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was called.</param>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "'{Tool}' named no session; it was refused rather than sent to this run's own child.")]
    public static partial void SessionMissing(ILogger logger, string tool);

    /// <summary>
    /// A call was refused because it would not have returned.
    /// </summary>
    /// <remarks>
    /// <b>Corrected 2026-08-18 (previously "refused by the <c>(tool, mode)</c>
    /// decision … the record that the security boundary the charter traded away
    /// for one process is actually being enforced").</b> There is no such
    /// boundary and there never was one here: the caller owns the session
    /// directory and reads the profile inside it as the same user. What this
    /// records now is the single liveness refusal, and Information is still the
    /// right level — a call that was declined is a call whose absence somebody
    /// will eventually have to explain.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was refused.</param>
    /// <param name="session">The session directory named.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Information,
        Message = "'{Tool}' was refused on the session at {Session}: it is not in this build's tools/list and would have blocked until this run was killed.")]
    public static partial void ToolRefused(ILogger logger, string tool, string session);

    /// <summary>
    /// Upstream's install advice was replaced, and byte-identity was given up to
    /// do it.
    /// </summary>
    /// <remarks>
    /// Warning rather than Information: this is the one place the passthrough's
    /// central claim is deliberately not true of an answer, and a trade nobody
    /// can see in the log is one nobody can audit.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="session">The session directory named.</param>
    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Warning,
        Message = "'{Tool}' on the session at {Session} answered with upstream's 'npx @playwright/mcp install-browser' advice, which does not apply to a BrowserAI install. It was replaced, so that answer is not byte-identical.")]
    public static partial void RemediationRewritten(ILogger logger, string tool, string session);

    /// <summary>
    /// An answer regained the image block that renaming its file had cost it.
    /// </summary>
    /// <remarks>
    /// Debug rather than Information: it happens on every screenshot a caller
    /// did not name, which is most of them, and Information would make the log
    /// of a session that took a hundred screenshots a hundred lines longer for a
    /// fact nobody is looking for. It is logged at all because the block is the
    /// one thing in an answer that costs the caller tokens without appearing in
    /// any file, so "why is this conversation so expensive" has an answer with a
    /// size in it.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="callerRequestId">
    /// The caller request being answered, taken as the id itself rather than as
    /// <c>id.ToString()</c>. At <see cref="LogLevel.Debug"/> the analyzer counts
    /// a conversion at the call site as work done to build a message that is
    /// usually discarded, and this one would do it on every screenshot (CA1873);
    /// handed the struct, the generated method formats it only if the level is
    /// enabled.
    /// </param>
    /// <param name="mediaType">What the block says it is.</param>
    /// <param name="bytes">How big the file was, before base64 grew it by a third.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Debug,
        Message = "Appended the inline {MediaType} upstream would have sent for caller request {CallerRequestId}: {Bytes} bytes on disk.")]
    public static partial void InlineImageRestored(ILogger logger, RequestId callerRequestId, string mediaType, int bytes);

    /// <summary>A call named a file outside the session it named.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="session">The session directory named.</param>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "'{Tool}' named a 'filename' outside the session at {Session}; it was refused rather than normalised.")]
    public static partial void FilenameRefused(ILogger logger, string tool, string session);

    /// <summary>
    /// A routed artifact's note could not be spliced into the child's own bytes.
    /// </summary>
    /// <remarks>
    /// Error rather than Warning: the note still reaches the caller, so nothing
    /// is lost, but the answer was rebuilt from a node and its escaping is
    /// therefore ours. That is the one claim the passthrough exists to make, and
    /// losing it silently is what this line prevents.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="callerRequestId">The caller request being answered.</param>
    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Error,
        Message = "The result for caller request {CallerRequestId} carries no top-level 'content' array, so the artifact note was appended by rebuilding it. That answer is no longer byte-identical.")]
    public static partial void NoteNotSpliced(ILogger logger, string callerRequestId);
}
