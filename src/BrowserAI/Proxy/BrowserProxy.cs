// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Protocol;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Storage;
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
            // The run's own child, which no session owns -- so this one stays in
            // the machine-wide log, where a child that will not answer
            // `tools/list` is visible to whoever is looking at the machine.
            await AnswerFailureAsync(_logger, caller, request, answer, cancellationToken).ConfigureAwait(false);
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
        var arguments = parameters?["arguments"] as JsonObject;

        string? name;
        string? session;
        string? why;

        // ⚠️ F6. THREE STRINGS THE CALLER CHOOSES, KIND-CHECKED BEFORE ANYTHING
        // READS THEM AS STRINGS. `(node as JsonValue)?.GetValue<string>()` is
        // the shape that was here, and on `"name": 5` it does not answer null --
        // `JsonValue` accepts a number and `GetValue<string>` throws
        // `InvalidOperationException`, which escapes this method and reaches the
        // caller as a bare `-32603` with the SDK's own wording. A wrong type is
        // an ordinary caller mistake and it gets an ordinary named refusal
        // saying which argument, what it must be and what arrived.
        try
        {
            name = Text(parameters, "name");
            session = Text(arguments, SessionToolSurface.SessionParameter);
            why = Text(arguments, SessionToolSurface.WhyParameter);
        }
        catch (SessionToolException wrongKind)
        {
            ProxyLog.SessionMissing(_logger, "<unreadable>");
            await RefuseAsync(caller, request.Id, wrongKind.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (SessionToolSurface.IsAuthored(name))
        {
            var authored = await _sessions.InvokeAsync(name!, arguments, cancellationToken).ConfigureAwait(false);

            await caller.SendMessageAsync(
                new JsonRpcResponse { Id = request.Id, Result = TextResult(authored.Text, authored.IsError) },
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var tool = name ?? "<none>";

        // Mandatory, with no fall-through to the run's own child. Before this
        // step a call naming no session was answered by the surface child, which
        // is a session nobody chose the mode of -- so every enforcement decision
        // below could be sidestepped by omitting an argument.
        if (string.IsNullOrWhiteSpace(session))
        {
            // ⚠️ ONE OF THE TWO RECORDS IN THIS METHOD THAT STAY IN THE
            // MACHINE-WIDE LOG, and the reason is that there is nowhere else for
            // them to go. Since 2026-08-24 everything attributable to a session
            // is written to that session's own file and to nothing else -- but a
            // call that named no session, and the one below that named one
            // nobody opened, have no session directory to be written into. They
            // are also the two a reader goes to the shared log for: a client
            // getting the tool surface wrong is a fact about the client, not
            // about any one session.
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
            ProxyLog.ToolRefused(live.Logger, tool, live.Location.FullPath);
            Refused(live, tool, why, decision.Refusal!);
            await RefuseAsync(caller, request.Id, decision.Refusal!, cancellationToken).ConfigureAwait(false);
            return;
        }

        // First-run provisioning, and it happens before the child hears about
        // the call for the same reason the liveness decision does: a browser tool
        // forwarded now would block inside the child's own launch for the whole
        // download and answer with upstream's `npx` advice at the end of it.
        if (_sessions.ProvisioningRefusal(tool, live) is { } notYet)
        {
            Refused(live, tool, why, notYet);
            await RefuseAsync(caller, request.Id, notYet, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ⚠️ REQUIRED, and refused here rather than left to the child. `why` is
        // injected into every upstream schema beside `session`, so a caller that
        // omits it is not following a schema it was given -- and the child has
        // never heard of the parameter, so forwarding the call would succeed and
        // the session's log would silently lose the entry the whole feature
        // exists for. The refusal is deliberately AFTER routing and provisioning:
        // a call that names an unknown session or a browser that is still
        // downloading has a more useful thing to be told first, and both of
        // those refusals already say what to do next.
        if (string.IsNullOrWhiteSpace(why))
        {
            ProxyLog.WhyMissing(live.Logger, tool, live.Location.FullPath);
            Refused(live, tool, why, SessionErrors.WhyMissing(tool));
            await RefuseAsync(caller, request.Id, SessionErrors.WhyMissing(tool), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Written before the call is forwarded, so a call that never returns --
        // a navigation that hangs, a child that dies -- still left a record of
        // what it was for. A log line written on the way back would be missing
        // from exactly the calls anybody investigates.
        SessionToolLog.Why(live.Logger, tool, why);

        // ⚠️ THE SAME ORDERING, AND HERE IT IS A REFUSAL RATHER THAN A LOG LINE.
        // The row goes into browserai.data as `in-flight`, and a call BrowserAI
        // could not record is not forwarded: the whole point of one time-ordered
        // log is that reading it back tells you what the session did, and a gap
        // nobody is told about is worse than a refusal somebody can act on.
        //
        // The row's OUTCOME is settled below, on the way back. What is written
        // here is that the call was made and what it was for -- which is
        // everything a hung call, a dead child or a killed process ever leaves.
        //
        // The tool name and the caller's `why`, and nothing else -- no
        // arguments, no answer. ⚠️ *Corrected 2026-08-26 (previously "Recorded
        // from the CALLER's own arguments, before the artifact plan rewrites
        // `filename` … and the artifact index beside it says where the file
        // landed").* Nothing rewrites `filename` and there is no index: the
        // arguments reach the child as the caller spelled them, so there is no
        // second version of them for a record to have to choose between.
        long row;

        try
        {
            row = live.Lock.Append(tool, why);
        }
        catch (Exception failure) when (failure is SqliteException or ObjectDisposedException)
        {
            ProxyLog.LogEntryRefused(live.Logger, tool, live.Location.FullPath, failure);

            await RefuseAsync(
                caller,
                request.Id,
                SessionErrors.SessionLogCouldNotBeWritten(tool, live.Lock.Location.DataFile, failure.Message),
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var outcome = SessionStore.InFlight;
        byte[]? payload = null;

        // ⚠️ EVERYTHING BETWEEN THE DOOR AND THE CHILD IS GONE, 2026-08-26, AND
        // THE ABSENCE IS THE FEATURE. What stood here observed the caller's
        // `url`, judged its `filename` against a table of tools, refused the
        // shapes it did not like, reserved a name, created a typed folder and
        // rewrote the argument to an absolute path -- and on the way back it
        // read the child's own answer as text, pinned the names that answer
        // mentioned, swept the output root, moved what it found and spliced a
        // note into the child's bytes saying where everything went.
        //
        // Nothing between the two servers except the session system and the
        // reason system. The remaining arguments are forwarded byte-identical
        // and the child's answer is returned byte-identical: no note, no scan,
        // no path handling, no filename rewrite, no artifact routing. Upstream
        // resolves a relative `filename` against its own working directory,
        // which is this session's `output\`, and refuses anything that leaves it
        // -- `allowUnrestrictedFileAccess` is written `false` for exactly that,
        // and it is the only containment there is now (BrowserConfiguration).
        //
        // The child has never heard of `session` or `why`; BrowserAI added both.
        // Removed from a CLONE rather than from the caller's own node, because
        // the request object is the SDK's and may still be read after this.
        var forwarded = request.Params?.DeepClone() as JsonObject;

        if (forwarded?["arguments"] is JsonObject cloned)
        {
            _ = cloned.Remove(SessionToolSurface.SessionParameter);
            _ = cloned.Remove(SessionToolSurface.WhyParameter);
        }

        try
        {
            // The one timer, reset here and nowhere else. A call this session
            // forwards is what "being driven" means for a browser-idle timer — a
            // call refused by the mode policy or by provisioning never reaches a
            // browser and never keeps one warm — and the scope holds the call
            // outstanding across the await, so a navigation that outlives the
            // whole period cannot have the browser closed underneath it.
            using var driving = live.Idle.Call();

            var answer = await live.Child.AskAsync(request.Method, forwarded, cancellationToken).ConfigureAwait(false);

            (outcome, payload) = Judge(answer);

            if (answer.Response is { } response)
            {
                // The one answer BrowserAI rewrites rather than forwards, and the
                // trade is deliberate: upstream's "not installed" message ends with
                // an npx command this product does not ship, which resolves a
                // different package at a different revision into a directory
                // BrowserAI never launches from -- and a model will run it. Byte
                // identity is given up for exactly this payload, only when the
                // child reported an error and the marker is present, and the fact
                // is logged rather than absorbed.
                if (Remediate(response) is { } corrected)
                {
                    ProxyLog.RemediationRewritten(live.Logger, tool, live.Location.FullPath);

                    await caller.SendMessageAsync(
                        new JsonRpcResponse { Id = request.Id, Result = corrected },
                        cancellationToken).ConfigureAwait(false);

                    return;
                }

                await AnswerChildResultAsync(live.Logger, caller, request.Id, response, answer.Payload, cancellationToken).ConfigureAwait(false);
                return;
            }

            await AnswerFailureAsync(live.Logger, caller, request, answer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // ⚠️ IN A `finally`, SO EVERY WAY OUT OF THIS BLOCK SETTLES THE ROW
            // -- including the cancelled one, which is a way out the child never
            // hears about. A call that is still `in-flight` after this has
            // genuinely never come back: the process was killed, or it is still
            // hanging. `browserai_catch_up` renders that as "no answer was
            // recorded", which is the true statement and the one a reader can
            // act on.
            live.Lock.Settle(
                row,
                outcome is SessionStore.InFlight ? SessionStore.Failed : outcome,
                outcome is SessionStore.InFlight
                    ? Encoding.UTF8.GetBytes("The call did not reach the child, or the caller cancelled it before an answer arrived. BrowserAI never saw a result.")
                    : payload);
        }
    }

    /// <summary>
    /// A string argument, or a named refusal when it arrived as something else.
    /// </summary>
    /// <remarks>
    /// <b>F6. The whole of it is that a wrong JSON type is a caller mistake and
    /// not a server fault.</b> <c>-32603 Internal error</c> tells a model that
    /// BrowserAI broke; what actually happened is that it sent a number where
    /// the schema says string, which it can fix on the next turn if anybody
    /// tells it. Absent stays absent — the callers below distinguish *missing*
    /// from *wrong*, and they answer differently.
    /// </remarks>
    /// <param name="node">The object the argument lives in.</param>
    /// <param name="name">The argument.</param>
    /// <returns>The string, or <see langword="null"/> when there is none.</returns>
    /// <exception cref="SessionToolException">It is there and it is not a string.</exception>
    private static string? Text(JsonObject? node, string name)
    {
        if (node?[name] is not { } value || value.GetValueKind() is JsonValueKind.Null)
        {
            return null;
        }

        return value.GetValueKind() is JsonValueKind.String
            ? value.GetValue<string>()
            : throw new SessionToolException(
                $"'{name}' must be a string, and it arrived as {value.GetValueKind()}. Nothing was forwarded and nothing was changed.");
    }

    /// <summary>
    /// Records a call this proxy refused, on the session it named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A REFUSED CALL IS A FACT ABOUT THE SESSION AND IT IS RECORDED
    /// (2026-08-26, previously it reached <c>browserai.log</c> and nothing
    /// else).</b> <i>The agent reached for a tool this build will not forward</i>
    /// is replay, not diagnostics — and with the session log file gone this
    /// record is the only place it survives. The row is written and settled
    /// <c>failed</c> in one go, because there was never an in-flight window:
    /// nothing was forwarded.
    /// </para>
    /// <para>
    /// <b>A refusal that could not be recorded is still a refusal.</b> The call
    /// is not being forwarded either way, so converting a bookkeeping failure
    /// into a second, different refusal would replace a sentence the caller can
    /// act on with one it cannot.
    /// </para>
    /// </remarks>
    /// <param name="live">The session the call named.</param>
    /// <param name="tool">The tool name, verbatim, whatever the caller said.</param>
    /// <param name="why">What the caller said it was for, if it said anything.</param>
    /// <param name="refusal">What the caller is being told, which is the failure payload.</param>
    private static void Refused(LiveSession live, string tool, string? why, string refusal)
    {
        try
        {
            var row = live.Lock.Append(tool, why ?? string.Empty);

            live.Lock.Settle(row, SessionStore.Failed, Encoding.UTF8.GetBytes(refusal));
        }
        catch (Exception failure) when (failure is SqliteException or ObjectDisposedException)
        {
            ProxyLog.LogEntryRefused(live.Logger, tool, live.Location.FullPath, failure);
        }
    }

    /// <summary>
    /// How a forwarded call ended, and what to keep about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Failure payloads only.</b> A call that worked stores the fact and the
    /// two instants; its answer already went back to the caller byte-identical
    /// and a copy in the record would make the record the traffic rather than
    /// the reasons. A call that failed stores what failed, because that is the
    /// one thing nobody can reconstruct afterwards.
    /// </para>
    /// <para>
    /// <b>Three shapes of failure and all three are kept whole.</b> The child's
    /// own error frame goes in as the bytes it sent; a protocol failure with no
    /// frame goes in as the exception; a transport failure goes in with its
    /// stack trace, because <i>the pipe closed</i> without a stack is a fact
    /// nobody can act on.
    /// </para>
    /// <para>
    /// ⚠️ <b>An <c>isError</c> result is a FAILED call, not a successful one.</b>
    /// Upstream answers a tool error inside an ordinary JSON-RPC result, so a
    /// navigation that timed out and a navigation that worked are the same shape
    /// at the transport. Reading them the same way would put <i>successful</i>
    /// beside every timeout in the record — which is the confident-wrong-answer
    /// class this repository keeps closing.
    /// </para>
    /// </remarks>
    /// <param name="answer">What came back.</param>
    /// <returns>The outcome and the payload to store with it.</returns>
    private static (string Outcome, byte[]? Payload) Judge(ChildAnswer answer)
    {
        if (answer.Response is { } response)
        {
            if ((response.Result as JsonObject)?["isError"]?.GetValueKind() is not JsonValueKind.True)
            {
                return (SessionStore.Successful, null);
            }

            return (
                SessionStore.Failed,
                answer.Payload is { } captured
                    ? captured.Json
                    : Encoding.UTF8.GetBytes(response.Result?.ToJsonString() ?? "the child answered with an error and no content"));
        }

        if (answer.ProtocolFailure is { } protocolFailure)
        {
            return (
                SessionStore.Failed,
                answer.Payload is { } captured ? captured.Json : Encoding.UTF8.GetBytes(protocolFailure.ToString()));
        }

        return (
            SessionStore.Failed,
            Encoding.UTF8.GetBytes(
                answer.TransportFailure?.ToString() ?? "The child answered with neither a result nor an error."));
    }

    /// <summary>
    /// Replaces upstream's install advice in a child's answer, or answers
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every ordinary answer returns <see langword="null"/> here and goes back
    /// as the child's own bytes.</b> The scan is one <c>Contains</c> per text
    /// block against a marker that appears in exactly one upstream sentence, and
    /// it is worth that cost because the sentence is an <i>instruction</i>: a
    /// model that reads it will run <c>npx</c>.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24 (previously "and on the paths where it appears
    /// at all the answer is already a failure with no bytes worth
    /// preserving").*** That was a claim about provenance and there was nothing
    /// enforcing it: the scan read every text block of every answer, so a page
    /// that merely rendered upstream's sentence — in its title, in an issue, in
    /// release notes — had BrowserAI's own instruction text spliced into it, and
    /// byte-identity was lost on an ordinary successful call. The gate is
    /// <c>isError</c>, and it is the whole provenance check there is: upstream's
    /// <c>Response.serialize()</c> returns
    /// <c>...sections.some(s =&gt; s.isError) ? { isError: true } : {}</c> and
    /// <c>throwIfExecutableMissing</c>'s throw is what puts an <c>Error</c>
    /// section there, so the gate loses nothing on the path the rewrite exists
    /// for (measured twice 2026-08-16,
    /// <see href="../../../kb/playwright/configuration.md">kb</see>).
    /// </para>
    /// <para>
    /// <b>It does not close the bypass on its own, and the caller no longer
    /// relies on it to.</b> An <c>isError</c> answer against a live tab carries
    /// the page's own title and the console and snapshot pointers in the same
    /// result, so page content can still trip this. What makes that harmless is
    /// that the rewrite branch now runs <c>Complete</c> like every other
    /// answered call: a rewritten answer is still an answer that may have
    /// published a pointer.
    /// </para>
    /// </remarks>
    private JsonObject? Remediate(JsonRpcResponse response)
    {
        // `GetValueKind()` rather than `GetValue<bool>()`: the latter throws on
        // `"isError": 5`, which a misbehaving child can send.
        if (response.Result is not JsonObject result
            || result["content"] is not JsonArray
            || result["isError"]?.GetValueKind() is not JsonValueKind.True)
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

    private static async Task AnswerFailureAsync(
        ILogger log,
        McpServer caller,
        JsonRpcRequest request,
        ChildAnswer answer,
        CancellationToken cancellationToken)
    {
        if (answer.ProtocolFailure is { } protocolFailure)
        {
            await AnswerChildErrorAsync(log, caller, request.Id, protocolFailure, answer.Payload, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AnswerTransportFailureAsync(
            log,
            caller,
            request.Id,
            request.Method,
            answer.TransportFailure ?? new InvalidOperationException("The child answered with neither a result nor an error."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers a caller with the child's result, exactly as it arrived.</summary>
    /// <param name="log">The session's own logger: every record below names a call that session made.</param>
    /// <remarks>
    /// <para>
    /// <b>Byte-identity is a property of forwarding, and this is where it is
    /// made.</b> The child's own bytes are written unchanged, on every answer,
    /// with nothing appended and nothing rewritten.
    /// </para>
    /// <para>
    /// ⚠️ <b>Simplified 2026-08-26 (previously it took an
    /// <c>ArtifactAnswer? completion</c> and, when there was one, spliced one or
    /// two encoded blocks into the child's <c>content</c> array by token
    /// offset).</b> The splice existed to reconcile two requirements — every byte
    /// the child wrote survives, and a file BrowserAI relocated is reported at
    /// the path it was relocated to — and it was the right resolution while both
    /// held. BrowserAI relocates nothing now, so the second requirement has no
    /// subject and the reconciliation has nothing to reconcile.
    /// </para>
    /// <para>
    /// <b>The one thing that can still cost byte-identity is a frame the
    /// transport did not capture</b>, and it is said out loud rather than
    /// absorbed: the answer is semantically right, because <c>Result</c> is the
    /// child's own <see cref="JsonNode"/>, but its escaping is then ours.
    /// </para>
    /// </remarks>
    /// <param name="caller">The connection to answer.</param>
    /// <param name="callerId">The caller's own request id.</param>
    /// <param name="response">The child's response.</param>
    /// <param name="payload">The child's frame as captured bytes, when the transport kept it.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The send task.</returns>
    private static async Task AnswerChildResultAsync(
        ILogger log,
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

        if (payload is { } untouched)
        {
            Verbatim.Attach(answer, untouched.Json);
        }
        else
        {
            ProxyLog.VerbatimPayloadMissing(log, callerId.ToString());
        }

        await caller.SendMessageAsync(answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers a caller with the child's own JSON-RPC error.</summary>
    /// <param name="log">Where to record: the session's own logger, or the run's when no session owns the call.</param>
    /// <remarks>
    /// <b>The <c>"Request failed (remote): "</c> prefix is never met on this
    /// path, rather than met and stripped.</b> The SDK does add it — it is real,
    /// and <c>SdkErrorShapeTests</c> is what keeps that checked — but the bytes
    /// written here come from the child's frame, so the message that reaches the
    /// caller is the message the child sent.
    /// </remarks>
    /// <param name="caller">The connection to answer.</param>
    /// <param name="callerId">The caller's own request id.</param>
    /// <param name="exception">The child's protocol failure.</param>
    /// <param name="payload">The child's error frame as captured bytes, when the transport kept it.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The send task.</returns>
    private static async Task AnswerChildErrorAsync(
        ILogger log,
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
            ProxyLog.VerbatimPayloadMissing(log, callerId.ToString());

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
    private static async Task AnswerTransportFailureAsync(
        ILogger log,
        McpServer caller,
        RequestId callerId,
        string method,
        Exception cause,
        CancellationToken cancellationToken)
    {
        ProxyLog.ChildDidNotAnswer(log, method, callerId.ToString(), cause);

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
    /// A session-scoped call arrived without the <c>why</c> its schema requires.
    /// </summary>
    /// <remarks>
    /// Warning rather than Information: the refusal is correct, but a caller
    /// repeatedly omitting a required parameter is a client that is not reading
    /// the schema, and that is worth seeing without turning anything on.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was refused.</param>
    /// <param name="session">The session directory named.</param>
    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Warning,
        Message = "'{Tool}' on the session at {Session} arrived without 'why', which its schema requires. Nothing was forwarded.")]
    public static partial void WhyMissing(ILogger logger, string tool, string session);

    /// <summary>
    /// A call was refused because its log entry could not be written.
    /// </summary>
    /// <remarks>
    /// Error rather than Warning: nothing was forwarded and nothing was
    /// recorded, and a session whose record cannot be written is one whose
    /// ownership is in doubt.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was refused.</param>
    /// <param name="session">The session directory named.</param>
    /// <param name="failure">What went wrong.</param>
    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Error,
        Message = "'{Tool}' on the session at {Session} was not forwarded: its log entry could not be written.")]
    public static partial void LogEntryRefused(ILogger logger, string tool, string session, Exception failure);

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

    // ⚠️ EVENT IDS 10, 11, 12 AND 16 ARE RETIRED AND ARE NOT TO BE REUSED,
    // 2026-08-26. They were `InlineImageRestored`, `FilenameRefused`,
    // `NoteNotSpliced` and `ReservationReleased` -- the four records the
    // artifact machinery wrote, all deleted with it. An id is a key somebody's
    // log query may still be written against, and a retired one silently
    // reassigned makes an old query answer about a new event.
}
