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
/// the point of the design and not a stylistic preference.</b> Every loss
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
/// <see cref="JsonNode"/> parsed from the child's own answer, never on a typed
/// schema, so a tool-level member no contract knows about still survives --
/// <c>LosslessPassthroughTests</c> asserts exactly that. Renaming remains
/// forbidden: upstream names pass through byte for byte. ⚠️ <i>Corrected
/// 2026-10-08 (previously "the <see cref="JsonNode"/> the child sent"): the answer
/// is the one the payload's child gave the build, compiled into the binary, and
/// each session's child is held to it byte for byte (<see cref="UpstreamToolList"/>).</i>
/// </para>
/// <para>
/// <b>There is deliberately no typed fallback.</b> <c>Handlers</c> carries
/// neither a <c>ListToolsHandler</c> nor a <c>CallToolHandler</c>, so if the
/// filter below ever failed to short-circuit, the caller would get <c>-32601</c>
/// and not a quietly lossy answer. A loud wrong answer can be found; a lossy
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
    /// The one protocol revision BrowserAI offers <b>to its caller</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pinned 2026-10-08, and the same value as the child's pin for a different
    /// reason.</b> The child's is the child's measured ceiling; this one is the
    /// newest revision whose answers BrowserAI writes correctly. Revision
    /// <c>2026-07-28</c> requires a <c>resultType</c> on every result, and BrowserAI
    /// answers <c>tools/list</c> itself, so a caller that chose it got a list it
    /// had to reject. The SDK offers every revision it implements unless one is
    /// set, and 2.2.0 implements <c>2026-07-28</c>.
    /// </para>
    /// <para>
    /// <b>What the pin does on the wire</b>, read in <c>McpServerImpl</c> at
    /// <c>v2.2.0</c> and held against the published binary by
    /// <c>ProtocolSplitTests</c>: a request whose per-request <c>_meta</c> names
    /// any other revision is refused with <c>-32022</c> and a
    /// <c>data.supported</c> of this revision alone, which is the refusal Claude
    /// Code falls back to <c>initialize</c> on; and <c>initialize</c> answers this
    /// revision whatever the caller offered. Implementing <c>2026-07-28</c> is in
    /// <c>TODO.md</c>
    /// ([kb](../../../kb/mcp/protocol.md#the-new-opening-request-and-the-one-revision-browserai-offers----measured-2026-10-08)).
    /// </para>
    /// </remarks>
    public const string CallerProtocolVersion = "2025-11-25";

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

    private readonly SessionHost _host;
    private readonly bool _ownsHost;
    private readonly SessionManager _sessions;
    private readonly ToolVerdicts _verdicts;
    private readonly UpstreamToolList _upstream;
    private readonly ILogger _logger;

    private int _disposed;

    /// <summary>
    /// What every tool in the surface takes, read off the list this server
    /// answers <c>tools/list</c> with, or <see langword="null"/> before the first
    /// reading.
    /// </summary>
    /// <remarks>
    /// One list for the whole run: the surface is static, and every reading of it
    /// is the same list. See <see cref="Signatures"/>.
    /// </remarks>
    private ToolSignatures? _signatures;

    /// <summary>
    /// Whether this connection has asked for a tool list since its handshake.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A field and not a dictionary, because a stdio server has exactly one
    /// connection.</b> One process, one pipe pair, one client -- which is the same
    /// premise <see cref="Connection"/> already rests on: since 2026-10-03 the
    /// session host makes one proxy per connection, so the premise holds there too.
    /// It is reset on
    /// <c>initialize</c> and not only at construction, so *since the
    /// handshake* is literally what it means and a second handshake on the same
    /// transport starts the question again.
    /// </para>
    /// <para>
    /// <b>Set on ARRIVAL and not on a successful answer.</b> The question is
    /// whether the client asked, not whether the answer reached it: a
    /// <c>tools/list</c> whose answer could not be written still tells us the
    /// client is not working from a list it inherited from a dead server.
    /// <i>Corrected 2026-10-08 (previously "not whether the child managed to reply:
    /// a <c>tools/list</c> the run's own child failed to answer"): the list is
    /// answered from the binary and no child is asked for it.</i>
    /// </para>
    /// </remarks>
    private int _toolsListed;

    /// <summary>
    /// Whether a relay answers this connection's client's <c>tools/list</c>, so the
    /// stale-list refusal is the relay's and never this proxy's
    /// (<see cref="ListsThroughTheRelay"/>).
    /// </summary>
    private int _listedByTheRelay;

    /// <summary>Whether the one stale-list refusal has already been made.</summary>
    /// <remarks>
    /// <b>Once per connection, and the second call is forwarded.</b> Refusing
    /// every call until a list arrives is the direction Q261 explicitly did not
    /// take -- it turns a working session into a failing one for as long as the
    /// model does not happen to list.
    /// </remarks>
    private int _staleListRefused;

    // ⚠️ DELETED 2026-10-10, by the maintainer's decision "9 a": the table of
    // tool calls in flight, kept so that a stop for an update could refuse each of
    // them (Q286 b), and the two flags that refused every call at the door, one
    // once a stop for an update had begun and one while this install's updater ran
    // (Q296 c). The stop through a server's own pipe and the updater watch that set
    // them went with S a on 2026-10-08, and since that day the relay answers a call
    // an update meets (`RelayErrors.UpdateInstalling`,
    // `RelayErrors.UpdateInstallingDuringTheCall`).

    // ⚠️ THE ACTIVITY RECORD WAS DELETED 2026-10-10, by the maintainer's decision
    // "9 a": ServerActivity, kept "for the server's pipe to describe", with this
    // proxy's Activity and HeldSessions. The per-server pipe went with S a on
    // 2026-10-08, so the proxy went on writing every call and every introduction into
    // a record that nothing read. The dashboard reads the background's sessions and
    // relays from SessionManager.Held and the roster.

    private BrowserProxy(SessionHost host, CallerConnection connection, bool ownsHost, ILogger logger)
    {
        _host = host;
        Connection = connection;
        _ownsHost = ownsHost;
        _sessions = host.Sessions;
        _verdicts = host.Verdicts;
        _upstream = host.UpstreamTools;
        _logger = logger;
    }

    /// <summary>The connection this proxy answers.</summary>
    public CallerConnection Connection { get; }

    /// <summary>A proxy for one connection over a host's sessions.</summary>
    /// <remarks>
    /// <b>Two owners, and the difference is what disposal does.</b> A server a client
    /// started owns its host through its one proxy, so disposing the proxy ends every
    /// session as it always did; the session host's proxies own nothing, so disposing
    /// one detaches what its connection drove (Q366 b).
    /// </remarks>
    /// <param name="host">The host.</param>
    /// <param name="connection">The connection.</param>
    /// <param name="ownsHost">Whether disposing the proxy disposes the host.</param>
    /// <returns>The proxy.</returns>
    internal static BrowserProxy For(SessionHost host, CallerConnection connection, bool ownsHost)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(connection);

        return new BrowserProxy(host, connection, ownsHost, host.LoggerFactory.CreateLogger<BrowserProxy>());
    }

    /// <summary>Every open session's idle countdown, for the update and the dashboard.</summary>
    /// <remarks>
    /// <b>Added 2026-10-08</b>, the seam lane SESS exposes for lanes ARCH and UI; see
    /// <see cref="SessionManager.Countdowns"/>.
    /// </remarks>
    /// <returns>One countdown per open session.</returns>
    public IReadOnlyList<SessionCountdown> SessionCountdowns() => _sessions.Countdowns();

    /// <summary>A server's one proxy, owning a host of its own.</summary>
    /// <remarks>
    /// <para>
    /// <b>One connection, and it owns its host</b> -- the shape of every server a
    /// client starts, and of the in-process rig. Since 2026-10-03 the sessions and
    /// the verdicts live in a <see cref="SessionHost"/> (Q366 b), and a server with
    /// one connection is a host with one proxy that owns it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Nothing is started and nothing is awaited, since 2026-10-08.</b>
    /// <i>Corrected (previously <c>ConnectAsync</c>, in two overloads: "Starts the
    /// run's own child and completes the handshake with it", and one over a
    /// transport the caller supplied, "The seam exists for the in-process test layer
    /// and nothing else uses it")</i>. The tool list is compiled into the binary,
    /// so there is no child to start before a server can answer, and the rig no
    /// longer needs a seam to stand a double where that child was.
    /// </para>
    /// </remarks>
    /// <param name="loggerFactory">Where the proxy and the sessions log.</param>
    /// <param name="environment">Where sessions keep their index, payload and configs, and the list and verdicts.</param>
    /// <returns>The proxy.</returns>
    public static BrowserProxy Create(ILoggerFactory loggerFactory, SessionEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(environment);

#pragma warning disable CA2000 // Ownership moves into the proxy, which disposes the host.
        return For(SessionHost.Create(loggerFactory, environment), new CallerConnection(), ownsHost: true);
#pragma warning restore CA2000
    }

    /// <summary>
    /// Marks this connection as a relay's: its client lists BrowserAI's tools from the
    /// relay, which never passes <c>tools/list</c> on, so this proxy never refuses a
    /// first call for a list it was not asked for.
    /// </summary>
    /// <remarks>
    /// <b>The design's "What moves", 2026-10-08: Q261 b's stale-list refusal moves into
    /// the relay</b>, which is what knows whether its client has listed. A relay
    /// replays its client's <c>initialize</c> to every background it reaches and
    /// answers the list itself, so without this mark the background would refuse the
    /// first call of every relay.
    /// </remarks>
    public void ListsThroughTheRelay() => Volatile.Write(ref _listedByTheRelay, 1);

    /// <summary>The options the caller-facing MCP server is built from.</summary>
    /// <returns>Server options whose tool methods are short-circuited by a message filter.</returns>
    public McpServerOptions ServerOptions()
    {
        var options = CallerFacingOptions();

        // Not WithMessageFilters: that is a DI extension in the hosting package,
        // and this is a Core/AOT server. An incoming message filter sees
        // JsonRpcRequest.Params as a raw JsonNode and never constructs a
        // ContentBlock, which is what makes it the only hook a lossless proxy
        // can use.
        options.Filters.Message.IncomingFilters.Add(next => (context, cancellationToken) =>
            OnIncomingAsync(next, context, cancellationToken));

        // ⚠️ DELETED 2026-10-10, by the maintainer's decision "9 a": the outgoing
        // filter that held ONE ANSWER PER CALL (Q286 b). A stop refused every call
        // still in flight, and the call's own answer could still arrive afterwards,
        // so the first answer for an id claimed it and a second was dropped. Only
        // that refusal ever made a second answer, it went with the stop through a
        // server's own pipe on 2026-10-08 (S a), and each path here that answers a
        // call sends one answer and returns.

        return options;
    }

    /// <summary>
    /// What every caller-facing server this product runs says at
    /// <c>initialize</c>: its name and version, its instructions, the revisions
    /// it speaks and the one capability it declares.
    /// </summary>
    /// <remarks>
    /// <b>One copy, so the instructions have one place to live.</b> ⚠️ <i>Corrected
    /// 2026-10-03 with Q296 c (previously "Shared with <c>UpdateInProgressServer</c>,
    /// which answers a client while its install's updater runs and has to introduce
    /// itself exactly as the full server would -- a second copy of these lines would
    /// be a second place for the instructions to drift")</i>: that server is gone,
    /// and a server that starts during an update is this one, refusing its calls
    /// through <c>RefuseCallsWhileAnUpdateInstalls</c>. The reason it was
    /// shared is why it stays one method. <i>Corrected 2026-10-10 by addition: that
    /// refusal went with the server on 2026-10-08 (S a) and was deleted on
    /// 2026-10-10; a call an update meets is the relay's to answer.</i>
    /// </remarks>
    /// <returns>Options with no incoming filter yet.</returns>
    internal static McpServerOptions CallerFacingOptions()
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

            // ⚠️ UPWARD: EXACTLY THE ONE REVISION BROWSERAI IMPLEMENTS. Corrected
            // 2026-10-08 @ ModelContextProtocol 2.2.0 (previously null, "Upward:
            // null means every revision the SDK implements. The caller is a client this project
            // does not control and does not get to hold back; the child's ceiling
            // is the child's business and stops at the pin in ChildConnection.
            // That split is the whole point, and it is why these two disagree on
            // purpose."). Null offered 2026-07-28 too, which ModelContextProtocol
            // 2.2.0 implements and BrowserAI's own answers do not: its tools/list
            // carries no resultType. From 2026-09-30 Claude Code opened with
            // server/discover at 2026-07-28, and 145 of 153 connections to the
            // installed 1.1.0 then listed no tool. Pinned, the SDK refuses that
            // opening with -32022 naming this revision alone, and Claude Code
            // falls back to initialize, read in its own code. See CallerProtocolVersion.
            ProtocolVersion = CallerProtocolVersion,

            Capabilities = new ServerCapabilities
            {
                // Declared so `initialize` advertises tools. It is what makes
                // the advertisement happen, independently of handlers -- and
                // there are deliberately no tool handlers at all.
                Tools = new ToolsCapability(),
            },
        };

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
    /// -- it has never emitted a <c>notifications/message</c> and never will -- and
    /// a client that called <c>logging/setLevel</c> got <c>{}</c> and then silence
    /// for ever. That is this project's founding failure shape: something reports
    /// a capability it does not have, and nothing anywhere goes red. It was also a
    /// live divergence from the child, whose golden snapshot records
    /// <c>{"tools":{}}</c> and no logging at all, so a reader comparing the two
    /// ends would have concluded BrowserAI adds logging.
    /// </para>
    /// <para>
    /// <b>Why an outgoing filter and not the options object.</b> Setting
    /// <c>Capabilities.Logging = null</c> does nothing -- the constructor overwrites
    /// it -- and the property is <c>[Obsolete(DiagnosticId = "MCP9005")]</c> at
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
        // Shape and not id, and deliberately: `initialize` is the only result
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

        // ⚠️ A PROXY THAT OWNS ITS HOST ENDS EVERY SESSION; ONE THAT DOES NOT
        // DETACHES WHAT ITS CONNECTION DROVE -- Q366 b, 2026-10-03. The first is
        // every server a client starts, unchanged; the second is a connection to
        // the session host ending, after which its sessions outlive it.
        if (_ownsHost)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            return;
        }

        await _host.EndAsync(Connection).ConfigureAwait(false);
    }

    /// <summary>Rebuilds a typed error detail from the child's own error bytes.</summary>
    /// <remarks>
    /// This is the <i>fallback</i> shape only. The frame that actually reaches
    /// the caller is written from the payload verbatim, so this object exists so
    /// that a message which somehow escaped the verbatim path would still be
    /// semantically right and not empty.
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

    /// <summary>A tool result carrying one text block.</summary>
    /// <param name="text">The text.</param>
    /// <param name="isError">Whether the result is an error.</param>
    /// <returns>The result object.</returns>
    internal static JsonObject TextResult(string text, bool isError) =>
        new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        };

    private async Task OnIncomingAsync(McpMessageHandler next, MessageContext context, CancellationToken cancellationToken)
    {
        // Recorded for every message, not only the two that are forwarded: the
        // progress relay needs somewhere to send to, and a child may report
        // progress on the very first call. Since 2026-10-03 it is the
        // connection's, and a session relays to whichever connection drives it.
        Connection.AnsweredThrough(context.Server);

        if (context.JsonRpcMessage is JsonRpcRequest request)
        {
            switch (request.Method)
            {
                case RequestMethods.Initialize:
                    // ⚠️ NOT a `return`: the SDK owns the handshake and this only
                    // resets what "since the handshake" is counted from, so the
                    // frame has to go on to `next`.
                    Volatile.Write(ref _toolsListed, 0);
                    Volatile.Write(ref _staleListRefused, 0);
                    break;

                case RequestMethods.ToolsList:
                    Volatile.Write(ref _toolsListed, 1);
                    await AnswerToolsListAsync(context.Server, request, cancellationToken).ConfigureAwait(false);
                    return;

                case RequestMethods.ToolsCall:
                    // ⚠️ THE TWO UPDATE DOORS WERE DELETED 2026-10-10, by the
                    // maintainer's decision "9 a": a stop for an update that had
                    // begun (Q286 b) and this install's updater still running (Q296
                    // c) each refused the call here. Nothing set either after S a on
                    // 2026-10-08. And so was the activity scope around this call the
                    // same day, which counted every call for the server's pipe to
                    // describe; nothing read it after that pipe went.
                    if (await RefuseAToolListThatPredatesThisServerAsync(context.Server, request, cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }

                    await AnswerToolsCallAsync(context.Server, request, cancellationToken).ConfigureAwait(false);
                    return;

                default:
                    break;
            }
        }

        await next(context, cancellationToken).ConfigureAwait(false);

        // After the SDK has handled the handshake, so what is recorded is what it
        // accepted: the client's own name, which every refusal that names a client
        // says. (Its title and version went to the activity record as well, until
        // that record was deleted on 2026-10-10.)
        if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.Initialize }
            && context.Server.ClientInfo is { } introduced)
        {
            Connection.Introduced(introduced.Name);
        }
    }

    /// <summary>
    /// Refuses the first <c>tools/call</c> of a connection that has never asked
    /// for a tool list, and sends the list-changed notification with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q261, settled 2026-09-24 in the maintainer's words: <i>"Q261 b"</i>.</b>
    /// The condition this answers is one nothing on the wire otherwise reports.
    /// Measured 2026-09-24 @ Claude Code 2.1.281, 3/3: a client whose stdio server
    /// has exited re-launches it transparently on the next tool call and sends
    /// <c>initialize</c> and <c>tools/call</c> and <b>no</b> <c>tools/list</c> --
    /// so after an update the model goes on calling the surface of a server that
    /// no longer exists, and a tool that was renamed or removed answers it with an
    /// error it reads as its own mistake.
    /// </para>
    /// <para>
    /// <b>The notification goes out BEFORE the refusal, so that a client which
    /// does act on one has it first.</b> Both are frames on one pipe, so the order
    /// is the order the client reads them in, and it costs nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>AND ON THE PATH THIS EXISTS FOR, THE NOTIFICATION DOES NOTHING --
    /// measured 2026-09-24 @ Claude Code 2.1.281, 3/3.</b> Against the published
    /// slice, with the first server made to exit after answering one call and 5.1 s
    /// of idle connection deliberately left between the refusal and the retry,
    /// <b>no <c>tools/list</c> ever arrived on the re-dialled connection</b>. The
    /// client's debug log carried <i>"Cleared connection cache for reconnection"</i>
    /// and no refresh line. The 3/3 refetch measured on 2026-09-23 was on a
    /// connection the client had established and listed from, which is a different
    /// connection. So the notification is kept -- it is free, it is correct, and a
    /// client that honours it is helped -- and the <b>refusal is the thing that
    /// actually recovers the turn</b>, which is why the wording no longer promises
    /// a refresh. The retry itself was forwarded and answered 3/3.
    /// </para>
    /// <para>
    /// <b>Three directions were dropped and the reasons are in
    /// <see cref="SessionErrors.ToolListPredatesThisServer"/>.</b> The
    /// notification alone moves Claude Code and is ignored by Codex; refusing
    /// every call until a list arrives is a wall; accepting the mismatch rests on
    /// a model re-tooling after an error, which it does in practice and which is
    /// not a mechanism.
    /// </para>
    /// <para>
    /// ⚠️ <b>The tool name is read leniently here and kind-checked later.</b> This
    /// runs in front of <see cref="AnswerToolsCallAsync"/>'s
    /// <see cref="Text(JsonObject?, string)"/>, so a <c>name</c> that arrived as a
    /// number must not throw out of this method -- it has its own named refusal
    /// one step further on, and pre-empting it with an exception would replace a
    /// sentence the caller can act on with a bare <c>-32603</c>.
    /// </para>
    /// </remarks>
    /// <param name="caller">The connection to answer.</param>
    /// <param name="request">The <c>tools/call</c> that arrived.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns><see langword="true"/> when the call was refused and must not be forwarded.</returns>
    private async Task<bool> RefuseAToolListThatPredatesThisServerAsync(
        McpServer caller,
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _toolsListed) is not 0 || Volatile.Read(ref _listedByTheRelay) is not 0)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _staleListRefused, 1) is not 0)
        {
            return false;
        }

        var name = ToolNameOf(request);

        var client = caller.ClientInfo?.Name;

        // Q366 b: a proxy that does not own its host is one of the session host's
        // connections, and the host outlives the front a client re-dials through.
        var throughTheHost = !_ownsHost;

        if (throughTheHost)
        {
            ProxyLog.ToolListMayPredateTheSessionHost(_logger, name, client ?? "<unnamed>", BuildVersion.Current);
        }
        else
        {
            ProxyLog.ToolListPredatesThisServer(_logger, name, client ?? "<unnamed>", BuildVersion.Current);
        }

        var signatures = Signatures();

        await caller.SendMessageAsync(
            new JsonRpcNotification { Method = NotificationMethods.ToolListChangedNotification },
            cancellationToken).ConfigureAwait(false);

        await RefuseAsync(
            caller,
            request.Id,
            SessionErrors.ToolListPredatesThisServer(name, BuildVersion.Current, client, signatures),
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    // ⚠️ DELETED 2026-10-10, by the maintainer's decision "9 a":
    // RefuseCallsInFlightForAnUpdateAsync, which answered every call still in
    // flight with SessionErrors.UpdateIsBeingInstalled when a server was stopped
    // through its pipe for an update (Q286 b); RefuseCallsWhileAnUpdateInstalls and
    // TheUpdateHasGone, which refused every call with
    // SessionErrors.UpdateIsStillInstalling while this install's updater ran, and
    // served again once it had gone (Q296 c); and the two door refusals they
    // turned on. Program.Main's stop through the pipe and its watch on the updater
    // called them, both went with S a on 2026-10-08, and after that day only
    // ErrorCatalogueTests did. The two catalogue rows went with them, and a call an
    // update meets is answered by the relay: RelayErrors.UpdateInstalling while the
    // updater runs or the background refuses a relay for an update, and
    // RelayErrors.UpdateInstallingDuringTheCall for a call an update ends.

    /// <summary>The tool a <c>tools/call</c> names, read leniently: a name that is not a string is <c>&lt;none&gt;</c>.</summary>
    /// <param name="request">The call.</param>
    /// <returns>The name, or <c>&lt;none&gt;</c>.</returns>
    internal static string ToolNameOf(JsonRpcRequest request) =>
        (request.Params as JsonObject)?["name"] is JsonValue value
            && value.GetValueKind() is JsonValueKind.String
                ? value.GetValue<string>()
                : "<none>";

    /// <summary>
    /// Answers <c>tools/list</c> from the list compiled into this binary, rewritten.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>From the binary, and no child is asked, since 2026-10-08</b> -- step 1
    /// of the one-binary plan, the maintainer's words of 2026-10-04 verbatim: <i>"I'd
    /// argue that the relay always answers the tool list from the binary. I see no
    /// reason why it would ever defer to Playwright, as the Playwright version is
    /// bound to that binary version is it not?"</i> <i>Corrected (previously
    /// "Answers <c>tools/list</c> from the run's own child, rewritten", with the
    /// child started before the handshake was answered and asked on every
    /// <c>tools/list</c>)</i>. The list is what that child answered when the build
    /// asked it, and each session's own child is held to it byte for byte when it
    /// starts (<see cref="UpstreamToolList"/>).
    /// </para>
    /// <para>
    /// <b>One static list, and it has to be the union.</b> The MCP spec forbids
    /// the tool set varying per connection and SEP-2567 removed protocol-level
    /// sessions outright, so <c>init</c> cannot shrink it. The list was taken from
    /// a child started with every capability, and every session's child is started
    /// with the same capabilities.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously "The one refusal this proxy still
    /// makes by name is <c>browser_annotate</c>, and that is liveness , not
    /// permission").</b> The refusals this proxy makes by name are now whatever
    /// <c>tool-verdicts.json</c> says they are, plus every name that file does not
    /// carry a row for. <c>browser_annotate</c> is still the only tool this build
    /// ships a <c>deny</c> for, and it is still liveness and not permission --
    /// what changed is that the sentence is a fact about the file and not
    /// about the code.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-19 (previously ", and a call its session's mode
    /// does not permit is refused at call time instead").</b> The
    /// <c>(tool, mode)</c> matrix went on 2026-08-18, and what a session's own
    /// capability set decides is still upstream's: a session without
    /// <c>storage</c> has no cookie tools in its process, so an allowed call is
    /// forwarded and upstream answers that the tool does not exist.
    /// </para>
    /// </remarks>
    private async Task AnswerToolsListAsync(McpServer caller, JsonRpcRequest request, CancellationToken cancellationToken)
    {
        // A fresh copy each time: the rewrite changes the object it is handed.
        var rewritten = SessionToolSurface.Rewrite(_upstream.Result(), _verdicts);

        // The list a call is checked against is the list a caller was given,
        // read here before it goes out. See ToolSignatures.
        Volatile.Write(ref _signatures, ToolSignatures.From(rewritten));

        await caller.SendMessageAsync(
            new JsonRpcResponse { Id = request.Id, Result = rewritten },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What every tool in the surface takes, read off the list this server
    /// answers <c>tools/list</c> with.
    /// </summary>
    /// <remarks>
    /// <b>The list a caller was given, and read here only when this connection
    /// never asked.</b> <see cref="AnswerToolsListAsync"/> keeps what it sent; a
    /// call that arrives first reads the same compiled list and rewrites it the
    /// same way, because the surface is one static list and either reading is that
    /// list. ⚠️ <i>Corrected 2026-10-08 (previously "a call that arrives first asks
    /// the run's own child for the same list", with "A child that cannot answer
    /// leaves BrowserAI's own tools still checked")</i>: there is no child to ask and
    /// none that can fail to answer, so every reading carries upstream's tools.
    /// </remarks>
    /// <returns>The signatures, never <see langword="null"/>.</returns>
    private ToolSignatures Signatures()
    {
        if (Volatile.Read(ref _signatures) is { } known)
        {
            return known;
        }

        var read = ToolSignatures.From(SessionToolSurface.Rewrite(_upstream.Result(), _verdicts));

        Volatile.Write(ref _signatures, read);
        return read;
    }

    /// <summary>
    /// Writes a refusal made before any session was resolved onto the session
    /// the call named, when this process holds it.
    /// </summary>
    /// <param name="session">The <c>session</c> the call carried, if any.</param>
    /// <param name="tool">The tool, as the caller spelled it.</param>
    /// <param name="why">What the caller said it was for.</param>
    /// <param name="refusal">What the caller is told.</param>
    /// <returns>The session's own logger, or the machine-wide one when there is none.</returns>
    private ILogger RecordOnTheNamedSession(string? session, string tool, string? why, string refusal)
    {
        if (string.IsNullOrWhiteSpace(session) || _sessions.Find(session) is not { } named)
        {
            return _logger;
        }

        Refused(named, tool, why, refusal);
        return named.Logger;
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

        // ⚠️ EVERY CALL THAT NAMES A LIVE SESSION RESTARTS ITS COUNTDOWN, WHATEVER
        // THE ANSWER, since 2026-10-08 -- F2, the maintainer's words verbatim: "Also,
        // any type of call, even if refused once because the settings are different
        // should reset the countdown timer on live sessions." Asked here, before
        // anything below can refuse the call, so a tool BrowserAI does not have, an
        // argument a schema does not list, a session another client drives, a closed
        // session and a held-back resume all restart it, and so does every one of
        // BrowserAI's own tools. A session is named by `session`, or by `directory` on
        // the three tools that take a session's own directory there; `browserai_list`
        // takes a tree and names no session. Until that day only a forwarded call
        // reset the countdown, below.
        _sessions.NoteActivity(session ?? SessionDirectoryNamedBy(name, arguments));

        var signatures = Signatures();
        var signature = signatures.Find(name);

        // ⚠️ A TOOL THIS BROWSERAI DOES NOT HAVE IS TOLD SO PLAINLY, since
        // 2026-10-04 -- the maintainer's decision of 2026-10-03, in his words:
        // "Calls to a tool BrowserAI doesn't have: a) Yes, in the same lane." A
        // name that is in neither the list this server advertises nor its
        // verdicts file is no tool of this build, and the answer says so and sends
        // the caller to its tool list. Until then such a name met the verdict
        // door's sentence for a gap a human must adjudicate, or, with no session,
        // "this needs a session". Corrected 2026-10-08 (previously "A name in
        // BrowserAI's own namespace is judged by the authored names alone, which
        // need no list; any other name needs the list, and with no list the
        // verdict door decides, as before."): the list is compiled into the
        // binary, so there is always one, and every name is judged against it.
        //
        // ⚠️ AND A DENIED TOOL IS ANSWERED THE SAME WAY, since the same day: a
        // tool BrowserAI does not offer should look to a model like any other it
        // does not have, and a deny row's `why` is the human record in the file.
        var verdict = _verdicts.Find(name);

        if (signature is null
            && !SessionToolSurface.IsAuthored(name)
            && verdict is not { Kind: ToolVerdictKind.Allow })
        {
            var named = name ?? "<none>";
            var absent = SessionErrors.ToolDoesNotExist(named, signatures);
            var recordedIn = RecordOnTheNamedSession(session, named, why, absent);

            ProxyLog.ToolDoesNotExist(recordedIn, named);
            await RefuseAsync(caller, request.Id, absent, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ⚠️ AN ARGUMENT THE TOOL'S SCHEMA DOES NOT HAVE IS REFUSED, and nothing
        // runs -- since 2026-10-04, the maintainer's words of 2026-10-03: "I'd
        // expect that any call carrying any parameter or argument that we do not
        // recognize would be refused actively with a syntax error." Checked against
        // the schema the caller was given -- for a forwarded tool, upstream's with
        // `session` and `why` added -- and BEFORE every other refusal, because a
        // misspelt `session` is then told both what it sent and what is required.
        // Measured the day before, through the published binary at d8a0101a: an
        // unknown argument on `browserai_list` and on `browser_navigate` was
        // dropped without a word, and upstream's own parse strips one too.
        if (signature?.Unrecognised(arguments) is { Count: > 0 } unrecognised)
        {
            var refusal = SessionErrors.UnrecognisedArguments(signature.Name, unrecognised, signature);

            // An authored tool's arguments name no `session` the proxy reads, and
            // its refusals are its own; a forwarded call's is recorded on the
            // session it named, as every refused forwarded call is.
            var logger = SessionToolSurface.IsAuthored(name) && !string.Equals(name, SessionToolSurface.PageTool, StringComparison.Ordinal)
                ? _logger
                : RecordOnTheNamedSession(session, signature.Name, why, refusal);

            ProxyLog.UnrecognisedArguments(logger, signature.Name, string.Join(", ", unrecognised));
            await RefuseAsync(caller, request.Id, refusal, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ⚠️ EVERY AUTHORED TOOL BUT ONE. `browserai_page_tool` is ours by name
        // and a forward by behaviour -- it resolves a name against the session
        // child's live tool list and hands the call to that child -- so it takes
        // the routing path below and not this one, and gets the session
        // resolution, the provisioning and child-liveness refusals, the `why`
        // requirement and the session log row that every forwarded call gets.
        // Answering it here would mean a second copy of all of that, reachable
        // only from an authored tool, which is how two of them end up disagreeing.
        if (SessionToolSurface.IsAuthored(name) && !string.Equals(name, SessionToolSurface.PageTool, StringComparison.Ordinal))
        {
            var authored = await _sessions.InvokeAsync(Connection, name!, arguments, cancellationToken).ConfigureAwait(false);

            await caller.SendMessageAsync(
                new JsonRpcResponse { Id = request.Id, Result = TextResult(authored.Text, authored.IsError) },
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var tool = name ?? "<none>";

        // Mandatory, with no fall-through. Before build step 13 a call naming no
        // session was answered by the run's own child, which was a session nobody
        // chose the mode of -- so every enforcement decision below could be
        // sidestepped by omitting an argument. That child is gone since
        // 2026-10-08, and the session is still mandatory: it is routing.
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

            // ⚠️ DELETED 2026-10-04: the branch that answered a `browserai_`
            // name that is not one of ours with `SessionToolSurface.NotOneOfOurs`
            // instead of "this needs a session". Such a name is told it does not
            // exist above, with or without a session, so what reaches here is a
            // tool this BrowserAI has.
            await RefuseAsync(caller, request.Id, SessionErrors.SessionMissing(tool), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_sessions.Find(session) is not { } live)
        {
            ProxyLog.UnknownSession(_logger, tool, session);
            await RefuseAsync(caller, request.Id, SessionManager.ExplainUnknownSession(tool, session), cancellationToken).ConfigureAwait(false);
            return;
        }

        // ⚠️ WHO DRIVES IT -- Q366 b, the maintainer's words of 2026-10-03,
        // verbatim: "This allows restarting vscode, the claude code plugin or
        // soemthing without losing the state." A relaunched client reconnects to
        // the session the host kept through its next call, which is this: a
        // session whose client went is taken over, its browser never having
        // closed. One another open connection drives is refused, and one the host
        // is letting go is, to this caller, a session nobody holds. A server a
        // client started has one connection, so all three are unreachable there.
        switch (live.Claim(Connection, out var holder))
        {
            case SessionClaim.HeldElsewhere:
            {
                var elsewhere = SessionErrors.SessionDrivenByAnotherClient(tool, live.Location.FullPath, holder!.Describe());

                ProxyLog.SessionDrivenElsewhere(live.Logger, tool, live.Location.FullPath);
                Refused(live, tool, why, elsewhere);
                await RefuseAsync(caller, request.Id, elsewhere, cancellationToken).ConfigureAwait(false);
                return;
            }

            case SessionClaim.Releasing:
                ProxyLog.UnknownSession(_logger, tool, session);
                await RefuseAsync(caller, request.Id, SessionManager.ExplainUnknownSession(tool, session), cancellationToken).ConfigureAwait(false);
                return;

            case SessionClaim.TakenOver:
                if (live.Logger.IsEnabled(LogLevel.Information))
                {
                    var driving = Connection.Describe();

                    SessionToolLog.TakenOver(live.Logger, live.Location.FullPath, driving);
                }

                break;

            default:
                break;
        }

        // ⚠️ THE VERDICT, AND SINCE 2026-08-26 IT IS READ FROM A FILE RATHER
        // THAN FROM A CONSTANT. `tool-verdicts.json` ships inside the payload it
        // describes and carries a row per tool: `allow` forwards, `deny` refuses
        // with the row's own reason, and a name with NO row is refused too --
        // deny by default. See `ToolVerdicts` for why the default is that way
        // round and what stops it silently costing a capability.
        //
        // It is still not a permission. A denial is liveness or fitness, and an
        // unjudged name is a gap; neither is a boundary against a caller who
        // owns the session directory and reads the profile inside it as the same
        // user. What it buys is that a name this build has never been told about
        // does not start a browser -- upstream creates the browser context
        // before it looks the name up -- and that the surface a model reads
        // carries nothing that cannot be called.
        //
        // Corrected 2026-08-26 (previously `SessionToolPolicy.Decide(tool)`,
        // a denylist of exactly one name with its reasoning in a doc comment).
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
        //
        // ⚠️ AND IT IS ASKED ABOUT UPSTREAM NAMES ONLY, since 2026-09-21. The one
        // authored tool that reaches this point -- `browserai_page_tool` -- is
        // judged by being in `SessionToolSurface.Names` at all, which is the same
        // thing that advertises it and the same thing `tool-verdicts.json`'s
        // `authored` rows are held identical to in both directions. Asking the
        // door about it would deny it: `Decide` reads the `upstream` half, an
        // `answer` row falls through to the no-verdict arm, and a tool of ours
        // would refuse itself.
        var decision = SessionToolSurface.IsAuthored(tool) ? ToolDecision.Allowed : _verdicts.Decide(tool);

        if (!decision.IsAllowed)
        {
            ProxyLog.ToolRefused(live.Logger, tool, live.Location.FullPath);
            Refused(live, tool, why, decision.Refusal!);
            await RefuseAsync(caller, request.Id, decision.Refusal!, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ⚠️ A CLOSED SESSION REFUSES EVERY FORWARDED CALL UNTIL
        // `browserai_resume`, P2 a and P3 b, the maintainer's words of
        // 2026-10-03, verbatim: "p2 a / p3 b". Added that day. Before it, the
        // call after an idle close relaunched a browser on `about:blank` and
        // answered as though nothing had happened, which a field report met as
        // a script failing on the wrong page with nothing saying why.
        //
        // BEFORE provisioning and the dead-child check, because a closed
        // session's child is gone on purpose and the refusal that says so is the
        // one that names the way back. And it is a refusal and not a relaunch
        // because the per-run settings are applied, and the tabs restored, by
        // the resume and by nothing else.
        //
        // ⚠️ AND A BROWSER THAT ENDED WITH NOBODY ASKING CLOSES THE SESSION TOO,
        // since 2026-10-04, 8 b, the maintainer's words verbatim: "8 b - log in our
        // catchup resume that it was the user who closed it." The wait on the
        // browser (`LiveSession.WatchTheBrowser`) usually marks the session closed
        // the moment the browser ends; this is the same question asked of the job,
        // for a browser that ended before the wait could be armed. Until then the
        // call after a person closed a headed window was forwarded, and
        // @playwright/mcp started a new browser on its own.
        if (live.Closed is null && live.BrowserWasSeenUp && !live.BrowserIsOpen && !live.Child.ChildHasGone)
        {
            live.TheBrowserEnded(exitCode: null);
        }

        if (live.Closed is { } closure)
        {
            var closed = SessionErrors.SessionWasClosed(tool, live.Location.FullPath, closure, Connection);

            ProxyLog.SessionWasClosed(live.Logger, tool, live.Location.FullPath);
            Refused(live, tool, why, closed);
            await RefuseAsync(caller, request.Id, closed, cancellationToken).ConfigureAwait(false);
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

        // ⚠️ THE CHILD IS ASKED WHETHER IT IS STILL THERE, AND THIS IS THE ONE
        // PLACE IT CAN BE ASKED CHEAPLY ENOUGH TO ASK EVERY TIME. Added
        // 2026-09-17. Forwarded to a child whose peer has gone, a call is
        // registered in the SDK's pending-request table and NEVER completed:
        // the table is faulted once, as the transport's channel completes, and a
        // request registered after that moment is faulted by nothing. Measured
        // the same day against the published slice -- one browser_navigate
        // outstanding at 900,000 ms with the server alive and no log line after
        // the transport's own end-of-stream. See HAZARDS.md and
        // docs/evidence/2026-09-17-resume-wedge.
        //
        // ⚠️ A call ALREADY IN FLIGHT when the child dies is not this branch and
        // never was: the SDK faults every pending request as the channel
        // completes, which is what LosslessPassthroughTests'
        // AChildThatDiesMidCallProducesANamedErrorRatherThanASuccess asserts.
        // What is left uncovered is the window between this check and that
        // registration, which is microseconds wide and which nothing in the
        // suite can plant red -- it is named here and not implied.
        //
        // BEFORE the `why` check below, for the reason provisioning is:
        // a caller whose session has no browser server behind it has a more
        // useful thing to be told than that it omitted an argument, and being
        // told the less useful one first costs it a turn.
        if (live.Child.ChildHasGone)
        {
            // 8 b: the next BrowserAI to open the session says why it was closed.
            live.RecordTheServerEnded();

            var gone = SessionErrors.BrowserServerHasGone(tool, live.Location.FullPath);

            ProxyLog.ChildHasGone(live.Logger, tool, live.Location.FullPath);
            Refused(live, tool, why, gone);
            await RefuseAsync(caller, request.Id, gone, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ⚠️ REQUIRED, and refused here and not left to the child. `why` is
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

        // ⚠️ THE SAME ORDERING, AND HERE IT IS A REFUSAL, NOT A LOG LINE.
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
        // `filename` ... and the artifact index beside it says where the file
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

        // ⚠️ DELETED 2026-10-08: the caller's own `browser_close`, which stood here
        // since 2026-10-03 (P3 b) -- the session was marked closed before the call
        // went out, a close with no browser up was answered without being forwarded,
        // and the child was ended once the close was over. F1 a, the maintainer's
        // words verbatim: "f1 a", and of the denied tool "do not make an exception".
        // `browser_close` is a deny row since that day, so a call naming it is
        // answered at the door like any tool BrowserAI does not have and never
        // reaches this line; the close itself is `browserai_close`, which
        // `LiveSession.CloseForTheAgentAsync` makes with the same ordering.
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
        // ⚠️ ONE SCAN CAME BACK, 2026-10-04, by addition (the paragraphs above are
        // unchanged). Q380, the maintainer's words verbatim: "9 d - and add a todo
        // to the repo to track the progress of the bug for when to remove our
        // checks." A successful `browser_take_screenshot` answer in a Chromium
        // session has its image's header read, and an image past 16,384 px on a
        // side is refused below, because Chromium repeats it past that line and
        // reports success. Nothing is rewritten: an answer either goes back as the
        // child wrote it or is refused whole. See `ScreenshotLimit`.
        //
        // The child has never heard of `session` or `why`; BrowserAI added both.
        // Removed from a CLONE and not from the caller's own node, because
        // the request object is the SDK's and may still be read after this.
        var isPageTool = string.Equals(tool, SessionToolSurface.PageTool, StringComparison.Ordinal);

        var forwarded = request.Params?.DeepClone() as JsonObject;

        if (forwarded?["arguments"] is JsonObject cloned)
        {
            _ = cloned.Remove(SessionToolSurface.SessionParameter);
            _ = cloned.Remove(SessionToolSurface.WhyParameter);
        }

        try
        {
            // The one timer, reset here and nowhere else. A call this session
            // forwards is what "being driven" means for a browser-idle timer -- a
            // call refused by the mode policy or by provisioning never reaches a
            // browser and never keeps one warm -- and the scope holds the call
            // outstanding across the await, so a navigation that outlives the
            // whole period cannot have the browser closed underneath it.
            //
            // ⚠️ NO TIMER AT ALL ON A HEADED SESSION since 2026-10-03, Q326 a, so
            // there is nothing to reset there.
            //
            // ⚠️ REVERSED 2026-10-08, both paragraphs above, by F2 and E2: every call
            // that names a live session restarts its countdown, refused or not, at the
            // top of this method, and a visible window has a countdown too, so this is
            // no longer the only reset. What stays here is the half only a forwarded
            // call has: the scope that holds the countdown while the call runs.
            using var driving = live.Driving();

            // 8 b: whether this call closes a tab decides how a browser that ends
            // right after it is described.
            live.NoteTheCall(tool, arguments);

            // ⚠️ THE ONE CALL WHOSE NAME IS NOT THE NAME THAT GOES OUT, and the
            // rewrite is upstream's own: a page tool's wire name is `webmcp_` and
            // a sanitised copy of the name the page gave it, which is the name
            // the snapshot block printed and the caller read. `PageTools` owns
            // the map and the resolution is re-done from the live tab on every
            // call, because the same wire name is a different page's code after a
            // navigation. Refusing here instead of forwarding is what keeps that
            // hazard from firing.
            PageToolResolution? resolved = null;

            if (isPageTool)
            {
                resolved = await SessionManager.ResolvePageToolAsync(live, arguments, cancellationToken).ConfigureAwait(false);

                if (resolved.Refusal is { } unresolved)
                {
                    outcome = SessionStore.Failed;
                    payload = Encoding.UTF8.GetBytes(unresolved);

                    await RefuseAsync(caller, request.Id, unresolved, cancellationToken).ConfigureAwait(false);
                    return;
                }

                forwarded = resolved.Call;
            }

            // ⚠️ BROWSERAI'S OWN CLOCK, AND ONLY ON THE ONE CALL NOTHING ELSE
            // BOUNDS. Upstream awaits a page-supplied handler with no timeout of
            // its own, so a page can hold a call open forever; every other tool
            // here is upstream's code with upstream's own limits and is left
            // alone. See `SessionToolSurface.PageToolBudget` for the number and
            // why it is that number.
            using var budget = isPageTool ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;

            budget?.CancelAfter(SessionToolSurface.PageToolBudget);

            ChildAnswer answer;

            try
            {
                // ⚠️ The caller's own close went from here on 2026-10-08 with F1 a; its
                // rule, that a close outlives the caller's wait, is
                // `LiveSession.CloseForTheAgentAsync`'s now.
                answer = await live.Child.AskAsync(request.Method, forwarded, budget?.Token ?? cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (budget is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested)
            {
                // ⚠️ OURS FIRED, NOT THE CALLER'S, and the two are answered
                // differently: a caller that cancelled is told nothing, because it
                // has gone, while this one gets a sentence saying what is still
                // running and what releases it. `ChildConnection.AskAsync` has
                // already told the child; the child cannot stop the page's code
                // and the refusal says so instead of implying a clean stop.
                var abandoned = SessionErrors.PageToolDidNotAnswer(
                    resolved!.Name,
                    resolved.WireName,
                    SessionToolSurface.PageToolBudget);

                ProxyLog.PageToolAbandoned(live.Logger, tool, live.Location.FullPath);

                outcome = SessionStore.Failed;
                payload = Encoding.UTF8.GetBytes(abandoned);

                await RefuseAsync(caller, request.Id, abandoned, cancellationToken).ConfigureAwait(false);
                return;
            }

            (outcome, payload) = Judge(answer);

            // ⚠️ 8 b: THE WATCH IS ARMED BEFORE THE CALLER HEARS THE ANSWER. Armed
            // only in the `finally`, it raced the caller: a browser that ended
            // between the answer and the `finally` was never watched, and the next
            // call met upstream starting a new browser. Found on 2026-10-04 by the
            // double's crash arm, which lost that race in its second iteration.
            live.WatchTheBrowser();

            // ⚠️ THE ONE SUCCESSFUL ANSWER BROWSERAI READS, Q380, decided
            // 2026-10-04 by the maintainer, in his words verbatim: "9 d - and add a
            // todo to the repo to track the progress of the bug for when to remove
            // our checks. Also, the refusal should mention the chromium bug link."
            // A Chromium screenshot larger than 16,384 px on a side repeats itself
            // past that line and still reports success, so its image's own header is
            // read, and an image past the line is refused and not handed over. The
            // row is settled with the refusal, as every refusal's is. Firefox
            // refuses past 32,767 px with an error of its own and is not read. See
            // `ScreenshotLimit`, and TODO.md for when this comes out again.
            if (outcome is SessionStore.Successful
                && string.Equals(tool, ScreenshotLimit.ScreenshotTool, StringComparison.Ordinal)
                && !BrowserConfiguration.IsFirefox(live.Config.Browser)
                && ScreenshotLimit.Refusal(answer.Response?.Result, Path.Combine(live.Location.FullPath, SessionLayout.OutputFolderName)) is { } repeated)
            {
                ProxyLog.ScreenshotPastChromiumsLimit(live.Logger, tool, live.Location.FullPath);

                outcome = SessionStore.Failed;
                payload = Encoding.UTF8.GetBytes(repeated);

                await RefuseAsync(caller, request.Id, repeated, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (answer.Response is { } response)
            {
                // The one answer BrowserAI rewrites instead of forwarding, and the
                // trade is deliberate: upstream's "not installed" message ends with
                // an npx command this product does not ship, which resolves a
                // different package at a different revision into a directory
                // BrowserAI never launches from -- and a model will run it. Byte
                // identity is given up for exactly this payload, only when the
                // child reported an error and the marker is present, and the fact
                // is logged and not absorbed.
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

            // 8 b: once a call has left a browser up, its end is watched, so a
            // person closing its window closes the session. Armed above for an
            // answered call; this covers every other way out of the block. The
            // caller's own close ended the child here until 2026-10-08, F1 a.
            live.WatchTheBrowser();
        }
    }

    /// <summary>
    /// The session directory a call names in its <c>directory</c> argument, for the
    /// three tools that take a session's own directory there, or
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <b>Read leniently and never refused here</b>: a <c>directory</c> of the wrong
    /// kind is refused by the tool itself, with its own sentence, and a call that
    /// names no live session restarts nothing.
    /// </remarks>
    /// <param name="tool">The tool name.</param>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The directory, or <see langword="null"/>.</returns>
    private static string? SessionDirectoryNamedBy(string? tool, JsonObject? arguments) =>
        tool is SessionToolSurface.Init or SessionToolSurface.Resume or SessionToolSurface.Destroy
            && arguments?["directory"] is JsonValue value
            && value.GetValueKind() is JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    /// <summary>
    /// A string argument, or a named refusal when it arrived as something else.
    /// </summary>
    /// <remarks>
    /// <b>F6. The whole of it is that a wrong JSON type is a caller mistake and
    /// not a server fault.</b> <c>-32603 Internal error</c> tells a model that
    /// BrowserAI broke; what actually happened is that it sent a number where
    /// the schema says string, which it can fix on the next turn if anybody
    /// tells it. Absent stays absent -- the callers below distinguish *missing*
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
                $"'{name}' must be a string, and it arrived as {ArgumentKind.Of(value)}. Nothing was forwarded and nothing was changed.");
    }

    /// <summary>
    /// Records a call this proxy refused, on the session it named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A REFUSED CALL IS A FACT ABOUT THE SESSION AND IT IS RECORDED
    /// (2026-08-26, previously it reached <c>browserai.log</c> and nothing
    /// else).</b> <i>The agent reached for a tool this build will not forward</i>
    /// is replay, not diagnostics -- and with the session log file gone this
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
    /// and a copy in the record would make the record the traffic and not
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
    /// beside every timeout in the record -- which is the confident-wrong-answer
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
    /// that merely rendered upstream's sentence -- in its title, in an issue, in
    /// release notes -- had BrowserAI's own instruction text spliced into it, and
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
    /// ⚠️ ***Corrected 2026-08-26 (previously "It does not close the bypass on
    /// its own, and the caller no longer relies on it to ... What makes that
    /// harmless is that the rewrite branch now runs `Complete` like every other
    /// answered call: a rewritten answer is still an answer that may have
    /// published a pointer").*** <b>There is no <c>Complete</c>.</b> It went with
    /// the artifact machinery in <c>feec42b</c>, and the rewrite branch
    /// <c>return</c>s immediately after sending -- so the acknowledged residual
    /// risk was defended by a mechanism that no longer existed.
    /// </para>
    /// <para>
    /// <b>The real bound is the scan's own narrowness, and it was nowhere
    /// written.</b> The <c>isError</c> gate does not close the bypass on its own:
    /// an error answer against a live tab carries the page's own title and the
    /// console and snapshot pointers in the same result, so page content can
    /// still reach this. What bounds the damage is what
    /// <c>ProvisioningRemediation.Rewrite</c> will actually do. It fires only on
    /// a block containing <c>install-browser</c>; it replaces only the anchored
    /// regex <c>Run `[^`]*install-browser[^`]*` to install\.?</c>; and it answers
    /// <see langword="null"/> when the replacement changed nothing, which sends
    /// the block back untouched. <b>So a page that merely mentions the marker is
    /// forwarded byte-identical</b>, and the worst a page can do is get
    /// BrowserAI's own two sentences substituted for upstream's <i>exact</i>
    /// install instruction, which is the substitution this method exists to make.
    /// <c>ProvisioningRemediationTests.APageQuotingUpstreamsAdviceInASuccessfulAnswerIsForwardedUntouched</c>
    /// holds the gate; the no-op return is what holds the rest.
    /// </para>
    /// </remarks>
    private JsonObject? Remediate(JsonRpcResponse response)
    {
        // `GetValueKind()` and not `GetValue<bool>()`: the latter throws on
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
    /// offset).</b> The splice existed to reconcile two requirements -- every byte
    /// the child wrote survives, and a file BrowserAI relocated is reported at
    /// the path it was relocated to -- and it was the right resolution while both
    /// held. BrowserAI relocates nothing now, so the second requirement has no
    /// subject and the reconciliation has nothing to reconcile.
    /// </para>
    /// <para>
    /// <b>The one thing that can still cost byte-identity is a frame the
    /// transport did not capture</b>, and it is said out loud and not
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
        // A fresh envelope and not the child's own: JsonRpcMessage.Context
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
    /// path; it is not met and stripped.</b> The SDK does add it -- it is real,
    /// and <c>SdkErrorShapeTests</c> is what keeps that checked -- but the bytes
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
    /// Answers a caller whose call the child never completed -- it died, its
    /// stdout ended, the transport went away.
    /// </summary>
    /// <remarks>
    /// Through the SDK's typed <c>CallToolHandler</c>, an exception of any cause
    /// becomes a JSON-RPC <i>success</i> carrying <c>isError: true</c> and the
    /// text <c>"An error occurred invoking 'x'."</c> -- identical for a child that
    /// died and for an unknown content type, naming neither. It is answered as a
    /// JSON-RPC <b>error</b> here because it is a transport failure and not a
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

    /// <summary>A call was refused because this session's child has gone.</summary>
    /// <param name="logger">The session's own logger.</param>
    /// <param name="method">The tool that was refused.</param>
    /// <param name="session">The session directory.</param>
    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Error,
        Message = "'{Method}' was not forwarded: the browser server for {Session} has gone. Nothing was sent to it.")]
    public static partial void ChildHasGone(ILogger logger, string method, string session);

    /// <summary>A call was refused because this session's browser was closed and nothing has resumed it.</summary>
    /// <param name="logger">The session's own logger.</param>
    /// <param name="method">The tool that was refused.</param>
    /// <param name="session">The session directory.</param>
    [LoggerMessage(
        EventId = 22,
        Level = LogLevel.Information,
        Message = "'{Method}' was not forwarded: the browser for {Session} was closed, and browser calls are refused until browserai_resume.")]
    public static partial void SessionWasClosed(ILogger logger, string method, string session);

    /// <summary>A tool call named a session this process is not driving.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="session">The session it named.</param>
    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "'{Tool}' named session '{Session}', which is not open in this process; the caller was told to resume it.")]
    public static partial void UnknownSession(ILogger logger, string tool, string session);

    /// <summary>
    /// A connection to the session host called a tool before it ever listed them, and
    /// the host cannot tell whether that list was its own.
    /// </summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="client">What the client called itself.</param>
    /// <param name="version">This build's version.</param>
    [LoggerMessage(
        EventId = 24,
        Level = LogLevel.Information,
        Message = "'{Tool}' arrived from client '{Client}' before any tools/list on this connection to the session host, BrowserAI {Version}, which cannot tell whether the client's list was its own. Refused once, and notifications/tools/list_changed was sent with the refusal.")]
    public static partial void ToolListMayPredateTheSessionHost(ILogger logger, string tool, string client, string version);

    /// <summary>A tool call named a session another connection of the session host drives.</summary>
    /// <param name="logger">The session's own logger.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="session">The session it named.</param>
    [LoggerMessage(
        EventId = 23,
        Level = LogLevel.Information,
        Message = "'{Tool}' named the session at {Session}, which another client drives right now; the call was refused and nothing was forwarded.")]
    public static partial void SessionDrivenElsewhere(ILogger logger, string tool, string session);

    /// <summary>A tool call arrived with no session at all.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was called.</param>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "'{Tool}' named no session; it was refused, and nothing was sent to a child.")]
    public static partial void SessionMissing(ILogger logger, string tool);

    /// <summary>
    /// A call was refused by the verdict for the tool it named, or by the
    /// absence of one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THE FULL NAME THE CALLER SENT GOES HERE AND NOWHERE ELSE THAT A
    /// MODEL READS.</b> An unjudged call is refused with a sentence that quotes
    /// nothing, so this record and the session's own log row are where <i>what
    /// did it try to call</i> survives -- on stderr and in a database, neither of
    /// which is in anybody's context window.
    /// </para>
    /// <para>
    /// <b>Corrected 2026-08-26 (previously "it is not in this build's tools/list
    /// and would have blocked until this run was killed").</b> That sentence
    /// described <c>browser_annotate</c> and only ever could: the refusal is now
    /// whatever <c>tool-verdicts.json</c> says, and the commonest one by far will
    /// be a name that file has no row for at all -- which has not blocked
    /// anything and was never in any list.
    /// </para>
    /// <para>
    /// <b>Corrected 2026-08-18 (previously "refused by the <c>(tool, mode)</c>
    /// decision ... the record that the security boundary the charter traded away
    /// for one process is actually being enforced").</b> There is no such
    /// boundary and there never was one here: the caller owns the session
    /// directory and reads the profile inside it as the same user. Information is
    /// still the right level -- a call that was declined is a call whose absence
    /// somebody will eventually have to explain.
    /// </para>
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool that was refused, verbatim as the caller spelled it.</param>
    /// <param name="session">The session directory named.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Information,
        Message = "'{Tool}' was refused on the session at {Session}: this build's tool-verdicts.json does not allow it to be forwarded. Nothing reached the browser.")]
    public static partial void ToolRefused(ILogger logger, string tool, string session);

    /// <summary>
    /// A session-scoped call arrived without the <c>why</c> its schema requires.
    /// </summary>
    /// <remarks>
    /// Warning, not Information: the refusal is correct, but a caller
    /// repeatedly omitting a required parameter is a client that is not reading
    /// the schema, and that has to be visible without turning anything on.
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
    /// Error, not Warning: nothing was forwarded and nothing was
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
    /// Warning, not Information: this is the one place the passthrough's
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
    /// A page tool was still silent when BrowserAI's own budget ran out.
    /// </summary>
    /// <remarks>
    /// Warning, not Information: the call is gone from BrowserAI's side
    /// and is not gone from the page's, which is the one state on this path a
    /// reader has to be able to find afterwards.
    /// </remarks>
    /// <param name="logger">The session's own logger.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="session">The session directory named.</param>
    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Warning,
        Message = "'{Tool}' on the session at {Session} abandoned a page tool that had not answered. The browser server bounds nothing here, so the page's own code may still be running; navigating the tab or closing it releases it.")]
    public static partial void PageToolAbandoned(ILogger logger, string tool, string session);

    /// <summary>
    /// A Chromium screenshot came back larger than Chromium captures faithfully, and
    /// was refused.
    /// </summary>
    /// <remarks>
    /// Warning, not Information: the browser server reported success, and the
    /// session's output directory holds a file whose image repeats itself.
    /// </remarks>
    /// <param name="logger">The session's own logger.</param>
    /// <param name="tool">The tool that was called.</param>
    /// <param name="session">The session directory named.</param>
    [LoggerMessage(
        EventId = 28,
        Level = LogLevel.Warning,
        Message = "'{Tool}' on the session at {Session} returned a screenshot larger than Chromium captures faithfully, so it was refused and not handed over. Chromium's issue: https://issues.chromium.org/issues/41347676")]
    public static partial void ScreenshotPastChromiumsLimit(ILogger logger, string tool, string session);

    /// <summary>
    /// A connection called a tool before it ever asked for a tool list, so the
    /// list it is calling from came from a different BrowserAI.
    /// </summary>
    /// <remarks>
    /// <b>Machine-wide, like the other two records about a client getting the
    /// surface wrong</b> -- no session has been resolved at this point, so there
    /// is nowhere else for it to go, and it is a fact about the client and not
    /// about any one session. <b>Information and not a warning:</b> it is the
    /// mechanism working, and on a machine that updates BrowserAI it is expected
    /// to appear once per relaunched session.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="tool">The tool the call named.</param>
    /// <param name="client">What the client called itself in its handshake.</param>
    /// <param name="version">The version actually serving the connection.</param>
    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Information,
        Message = "'{Tool}' arrived from client '{Client}' before any tools/list on this connection, so its tool list predates BrowserAI {Version}. Refused once, and notifications/tools/list_changed was sent with the refusal.")]
    public static partial void ToolListPredatesThisServer(ILogger logger, string tool, string client, string version);

    /// <summary>A call named a tool this BrowserAI does not have.</summary>
    /// <remarks>
    /// <b>Information, like <see cref="ToolRefused"/>:</b> the refusal is the
    /// mechanism working. The name goes here verbatim, as it does on the
    /// session's own row when the call named a session this process holds.
    /// </remarks>
    /// <param name="logger">The named session's own logger, or the machine-wide one.</param>
    /// <param name="tool">The name the caller sent.</param>
    [LoggerMessage(
        EventId = 26,
        Level = LogLevel.Information,
        Message = "'{Tool}' is not a tool this BrowserAI has; the call was refused and nothing ran.")]
    public static partial void ToolDoesNotExist(ILogger logger, string tool);

    /// <summary>A call carried arguments its tool's schema does not have.</summary>
    /// <remarks>
    /// <b>Warning, like <see cref="WhyMissing"/>:</b> the refusal is correct, and
    /// a caller that keeps sending names a schema does not carry is a client not
    /// reading the schema, which has to be visible without turning anything on.
    /// </remarks>
    /// <param name="logger">The named session's own logger, or the machine-wide one.</param>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">The names its schema does not have, as the caller sent them.</param>
    [LoggerMessage(
        EventId = 27,
        Level = LogLevel.Warning,
        Message = "'{Tool}' arrived with arguments its schema does not have ({Arguments}); the call was refused and nothing ran.")]
    public static partial void UnrecognisedArguments(ILogger logger, string tool, string arguments);

    // ⚠️ EVENT IDS 10, 11 AND 12 ARE RETIRED AND ARE NOT TO BE REUSED,
    // 2026-08-26. They were `InlineImageRestored`, `FilenameRefused` and
    // `NoteNotSpliced` -- three of the four records the artifact machinery
    // wrote, all deleted with it. An id is a key somebody's log query may still
    // be written against, and a retired one silently reassigned makes an old
    // query answer about a new event.
    //
    // ⚠️ AND 16 WAS THE FOURTH, AND 16 IS IN USE AGAIN. Corrected 2026-09-22
    // (previously "EVENT IDS 10, 11, 12 AND 16 ARE RETIRED AND ARE NOT TO BE
    // REUSED, 2026-08-26. They were `InlineImageRestored`, `FilenameRefused`,
    // `NoteNotSpliced` and `ReservationReleased` -- the four records the
    // artifact machinery wrote, all deleted with it"). THE RULE IS NOT WHAT
    // CHANGED; THE CODE BROKE IT, and this comment is corrected and not the
    // event renumbered.
    //
    // The history, read out of `git log -S` and not remembered:
    // `ReservationReleased` took 16 in `dbf1346` on 2026-08-24, was deleted
    // with the other three in `feec42b` on 2026-08-26 -- the commit that wrote
    // the sentence above -- and `ChildHasGone` was given 16 in `425a256` on
    // 2026-09-17, twenty-two days later. `PageToolAbandoned` then took 17,
    // which is the id 16 would have been had anybody read this.
    //
    // NOTHING CAUGHT IT AND NOTHING COULD: no test asserts that an id is
    // unused, or that the retired set stays retired. The suite asserts
    // particular ids on particular paths and nothing more -- searched, with a
    // positive control on the same corpus, 2026-09-22.
    //
    // ✅ THE SECOND HALF OF THAT SENTENCE IS NO LONGER TRUE, and it is
    // corrected here and not rewritten. Corrected 2026-09-22 by addition
    // (previously the paragraph above stood alone, and "nothing could" was its
    // last word). `ProxyLogTests.EveryLogEventIdIsUniqueInItsClassAndNoRetiredIdIsInUse`
    // reads every `[LoggerMessage]` in `src\` as text, refuses two events
    // sharing an id inside one class, and refuses any id the marker below
    // names. It was planted red twice against this tree -- once with a
    // synthetic duplicate and once with `ChildHasGone` moved back onto a
    // retired id -- before it was allowed to be green. What it still CANNOT see
    // is whether an id ever shipped under an older meaning; that is what the
    // marker is for, and keeping the marker honest is a person's job.
    //
    // ⚠️ THE LINE BELOW IS READ BY THAT TEST. It is the machine-readable half
    // of the prose above, beside it and not in place of it -- the same
    // arrangement `drift-check.json` prescribes for sqlite.org's `PRODUCT`
    // line. Taking an id off it is how a deliberate reuse is recorded, and the
    // prose above is where the reason goes. 16 is deliberately NOT on it: it is
    // in use, which is what the correction two paragraphs up is about.
    //
    // ⚠️ AND 19, 20 AND 21 ARE RETIRED TOO -- 2026-10-10, added by addition. They
    // were the update refusals' three records, deleted with the refusals by the
    // maintainer's decision "9 a": `CutOffForAnUpdate`, "'{Tool}' was still running
    // when this server was stopped for an update; it was answered with the update
    // refusal before the conversation ended."; `RefusedForAnUpdate`, "'{Tool}'
    // arrived while an update is being installed and was refused; nothing was
    // forwarded."; and `RefusalNotSent`, "The update refusal for '{Tool}' could not
    // be sent; the stop goes ahead and the client sees its connection close
    // instead." Builds up to 1.1.0 write them, so a saved query may still meet them
    // in an old log, and the marker below carries them.
    //
    // RETIRED-EVENT-IDS: 10, 11, 12, 19, 20, 21
    //
    // WHAT THE REUSE ACTUALLY COSTS, measured, not assumed, because the
    // sentence above is about somebody's old query. `ReservationReleased` held
    // 16 for TWO DAYS and is in no artifact anybody can fetch today: every
    // asset on the standing `v1.0.0` release object was built on 2026-09-17,
    // after the deletion, and that release's own binaries carry 16 as
    // `ChildHasGone`. WHAT CANNOT BE READ is whether an EARLIER release object
    // carried it -- that object was replaced, so `gh` no longer describes it --
    // and that gap is named and not closed.
    //
    // NOT RENUMBERED HERE. Moving `ChildHasGone` to 18 would be the tidy edit
    // and it is a DECISION and not a repair: 16 is what the shipped v1.0.0
    // binaries emit for it, so renumbering trades a stale meaning for a second
    // stale meaning, in the same key, for the sake of a rule about the first.
    // It belongs to whoever owns the log surface. The reuse is recorded here so
    // that a reader of an old log meets it.
}
