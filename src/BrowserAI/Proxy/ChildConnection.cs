// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Proxy;

/// <summary>
/// One <c>@playwright/mcp</c> child: the SDK client, the transport its raw bytes
/// can be read from, and the request ids BrowserAI puts on what it sends.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is one of these per child, and there are now several children per
/// process.</b> Each session owns one, plus one for the run itself — the child
/// that answers <c>tools/list</c> before any session exists. Splitting it out of
/// <see cref="BrowserProxy"/> is what made that possible: the proxy is the
/// caller-facing server and decides <i>which</i> child a call goes to, and this
/// type is everything about speaking to one.
/// </para>
/// <para>
/// <b>The id on the outgoing request is ours, and that is what makes
/// cancellation possible.</b> The SDK never emits
/// <c>notifications/cancelled</c> downstream on either the raw or the typed
/// path: <c>McpSessionHandler</c> has the machinery, but its registration is
/// disposed as <c>tcs.Task.WaitAsync(ct)</c> unwinds and CTS callbacks run LIFO,
/// so the callback is unregistered before it can run. An id we chose is an id we
/// can name in a notification we send ourselves, from the <c>catch</c> — where it
/// is awaited and cannot fire before the request it names was sent.
/// </para>
/// </remarks>
internal sealed class ChildConnection : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly ChildLink _link;
    private readonly ILogger _logger;
    private readonly IAsyncDisposable _progressRelay;
    private readonly string _idPrefix;

    private long _requests;
    private int _disposed;

    private ChildConnection(
        McpClient client,
        ChildLink link,
        ILogger logger,
        string idPrefix,
        Func<JsonRpcNotification, CancellationToken, ValueTask> relay)
    {
        _client = client;
        _link = link;
        _logger = logger;
        _idPrefix = idPrefix;
        NegotiatedProtocolVersion = client.NegotiatedProtocolVersion;

        // The child→caller direction, and the only one the SDK gives no
        // server-side seam for: McpClientOptions has no Filters. A *named*
        // notification needs no decorator either way --
        // RegisterNotificationHandler is public on McpSession, which McpClient
        // inherits.
        _progressRelay = client.RegisterNotificationHandler(NotificationMethods.ProgressNotification, relay);
    }

    /// <summary>
    /// The protocol revision BrowserAI speaks <b>to a child</b>, pinned to the
    /// child's measured ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pinning is not a tidiness choice.</b> Left null, the SDK client
    /// prefers <c>2026-07-28</c> and probes the child with <c>server/discover</c>
    /// first, bounded by <c>DiscoverProbeTimeout</c> — five seconds by default.
    /// A child that drops the unknown method instead of answering costs that on
    /// <i>every</i> spawn, against a ~300 ms baseline, and it presents as
    /// "browser automation got slow" with no error anywhere.
    /// </para>
    /// <para>
    /// It is a provenance stamp, not a target: <c>@playwright/mcp</c> 0.0.79 caps
    /// here, verified 2026-08-16 from both directions. The child never
    /// <i>rejects</i> a version, so a mis-negotiation produces nothing to catch
    /// and <see cref="ConnectAsync"/> asserts on the negotiated value instead.
    /// </para>
    /// </remarks>
    public const string ChildProtocolVersion = "2025-11-25";

    /// <summary>
    /// How long a child gets to answer <c>initialize</c> before BrowserAI calls
    /// it hung.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Set here because the SDK's default is sixty seconds and inheriting it
    /// silently is the worst version of this decision.</b> Measured by reflection
    /// on <c>ModelContextProtocol.Core</c> 2.2.0, 2026-08-18:
    /// <c>McpClientOptions.InitializationTimeout</c> defaults to <c>00:01:00</c>.
    /// Nothing in this repository chose that number, nothing documented it, and
    /// the failure it produces — <c>Initialization timed out</c> — carries no
    /// elapsed time, no child identity and none of the child's stderr, so it
    /// reads as a protocol fault rather than as a slow start.
    /// </para>
    /// <para>
    /// <b>What is on the far side of it is a node process starting.</b> The child
    /// is <c>node.exe</c> loading <c>cli.js</c> out of a bundled payload; a warm
    /// spawn hands shakes in roughly 300 ms, and a cold one on a contended machine
    /// takes seconds. Sixty seconds is therefore not a hang detector at all on a
    /// loaded box — it is a promptness assertion on somebody else's process
    /// start. Ten minutes is more than two orders of magnitude above the warm
    /// cost and <b>must never be reached by a slow machine</b>; a child that has
    /// not spoken in ten minutes is not starting slowly, it is not starting.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-17 at unbounded suite parallelism, before this
    /// existed:</b> 46 <c>Initialization timed out</c> failures in one twenty-run
    /// session, none of them a logic fault, all of them this bound expiring while
    /// 419 tests contended for one machine. The suite pins the same value for the
    /// same reason in <c>TestDefaults.InitializationHang</c>.
    /// </para>
    /// </remarks>
    public static TimeSpan ChildInitializationHang { get; } = TimeSpan.FromMinutes(10);

    /// <summary>The revision actually negotiated, as opposed to the one asked for.</summary>
    public string? NegotiatedProtocolVersion { get; }

    /// <summary>
    /// The child's own process id, or <see langword="null"/> when the child is
    /// not a process at all — which is the in-process test layer and nothing
    /// else.
    /// </summary>
    public int? ProcessId => (_link.Session as ChildProcessSession)?.ProcessId;

    /// <summary>
    /// Every process the kernel currently reports in this child's job: the node
    /// child, the browser it launched and every helper under it.
    /// </summary>
    /// <remarks>
    /// <b>The kernel's own membership list rather than a tally anybody keeps</b>,
    /// which is what lets the idle close report <c>11 → 1</c> as evidence instead
    /// of asserting that a browser went. It is also the only question about
    /// browser processes this product can ask <i>per session</i>: an image-path
    /// scan of the machine cannot tell one session's Chromium from another's.
    /// </remarks>
    /// <returns>The pids, or empty when the child is not a process.</returns>
    public IReadOnlyList<int> JobProcessIds() =>
        (_link.Session as ChildProcessSession)?.Job.ProcessIds() ?? [];

    /// <summary>Connects to a child over a transport and completes the handshake.</summary>
    /// <param name="transport">The transport. The SDK client owns it once this returns.</param>
    /// <param name="loggerFactory">Where this connection and its transport log.</param>
    /// <param name="idPrefix">The namespace for the request ids this connection allocates.</param>
    /// <param name="relay">Where a child's progress notifications go.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The connected child.</returns>
    /// <exception cref="InvalidOperationException">The child negotiated a revision other than the pinned one.</exception>
    public static async Task<ChildConnection> ConnectAsync(
        IClientTransport transport,
        ILoggerFactory loggerFactory,
        string idPrefix,
        Func<JsonRpcNotification, CancellationToken, ValueTask> relay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger<ChildConnection>();
        var link = new ChildLink(transport);

        var client = await McpClient.CreateAsync(
            link,
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "BrowserAI", Version = BuildVersion.Current },
                ProtocolVersion = ChildProtocolVersion,

                // Never left at the SDK's 60 s default. See ChildInitializationHang.
                InitializationTimeout = ChildInitializationHang,
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

            return new ChildConnection(client, link, logger, idPrefix, relay);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends one request to the child and reports whatever came back.</summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">Its parameters, forwarded as they arrived.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The answer, which may be a result, the child's own error, or a failure to answer at all.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled. The child has been told.</exception>
    public async Task<ChildAnswer> AskAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        // Before anything is registered or sent. A token that is already
        // cancelled would otherwise announce the cancellation of a request that
        // was never sent.
        cancellationToken.ThrowIfCancellationRequested();

        var name = _idPrefix + Interlocked.Increment(ref _requests).ToString(CultureInfo.InvariantCulture);
        var childId = new RequestId(name);

        var request = new JsonRpcRequest
        {
            Id = childId,
            Method = method,
            Params = parameters,
        };

        _link.Session.Watch(childId);

        ProxyLog.Forwarding(_logger, method, name, cancellationToken.CanBeCanceled);

        try
        {
            var response = await _client.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            return new ChildAnswer
            {
                Response = response,
                Payload = TakePayload(childId, wantError: false),
            };
        }
        catch (OperationCanceledException)
        {
            await AnnounceCancellationAsync(name).ConfigureAwait(false);
            throw;
        }
        catch (McpProtocolException failure)
        {
            return new ChildAnswer
            {
                ProtocolFailure = failure,
                Payload = TakePayload(childId, wantError: true),
            };
        }
#pragma warning disable CA1031 // Anything else is the child failing to answer at all, and the caller must be told rather than left waiting.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            return new ChildAnswer { TransportFailure = failure };
        }
        finally
        {
            _link.Session.Forget(childId);
        }
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

    private VerbatimPayload? TakePayload(RequestId childId, bool wantError) =>
        _link.Session.TryTakePayload(childId, out var payload) && payload.IsError == wantError
            ? payload
            : null;

    /// <summary>Tells the child to stop, by the id BrowserAI put on the request.</summary>
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
}

/// <summary>What one request to a child produced.</summary>
/// <remarks>
/// Three outcomes rather than two, because they reach the caller as three
/// different frames: the child's result, the child's own JSON-RPC error, and the
/// child not answering at all. Collapsing the last two would produce the failure
/// shape this project exists to eliminate — an error naming no cause.
/// </remarks>
internal sealed record ChildAnswer
{
    /// <summary>The child's response, when it answered.</summary>
    public JsonRpcResponse? Response { get; init; }

    /// <summary>The child's own JSON-RPC error, when it answered with one.</summary>
    public McpProtocolException? ProtocolFailure { get; init; }

    /// <summary>Why the child did not answer at all, when it did not.</summary>
    public Exception? TransportFailure { get; init; }

    /// <summary>
    /// The exact bytes of the child's <c>result</c> or <c>error</c> member, when
    /// the transport captured them.
    /// </summary>
    public VerbatimPayload? Payload { get; init; }
}
