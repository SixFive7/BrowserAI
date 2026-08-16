// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Reflection;
using BrowserAI.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BrowserAI.Proxy;

/// <summary>
/// One <c>@playwright/mcp</c> child behind one MCP server: the vertical slice.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately thin.</b> No sessions, no locking, no artifact routing and no
/// injected <c>session</c> parameter — <c>tools/list</c> and <c>tools/call</c>
/// are forwarded as they arrive. What this exists to make real is the set of
/// decisions that were settled on paper: NativeAOT with the SDK and our
/// P/Invoke in one binary, the protocol-version split, a mandatory
/// <c>browserName</c> and chromium-alias channel, and <c>--sandbox</c> as a CLI
/// flag.
/// </para>
/// <para>
/// <b>What is knowingly not lossless yet.</b> These handlers travel the SDK's
/// typed path, which drops unknown tool-level members and throws on an unknown
/// content <i>type</i>. Byte-exact passthrough is
/// [build-order step 9](../../plan/build-order.md#9-lossless-passthrough) and
/// arrives as a <c>JsonNode</c> rewrite plus an
/// <c>McpServerOptions.Filters.Message.IncomingFilters</c> short circuit. The
/// one loss that is <b>not</b> deferred is the convenience
/// <c>ListToolsAsync</c> overload, which silently drops tools whose annotations
/// fail SEP-2243 validation: the raw overload costs one argument, so there is no
/// reason to ship the lossy one for a single step.
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

    private readonly McpClient _client;
    private readonly ILogger _logger;
    private int _disposed;

    private BrowserProxy(McpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        NegotiatedChildProtocolVersion = client.NegotiatedProtocolVersion;
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
    /// <c>tools/list</c> overload — is below this line rather than above it, so
    /// the harness exercises the same code the product runs.
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

        var client = await McpClient.CreateAsync(
            transport,
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

            return new BrowserProxy(client, logger);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The options the caller-facing MCP server is built from.</summary>
    /// <returns>Server options whose tool handlers forward to the child.</returns>
    public McpServerOptions ServerOptions() => new()
    {
        ServerInfo = new Implementation { Name = "BrowserAI", Version = Version },

        // Upward: null means every revision the SDK implements, 2024-11-05
        // through 2026-07-28. The caller is a client this project does not
        // control and does not get to hold back; the child's ceiling is the
        // child's business and stops at the pin above. That split is the whole
        // point, and it is why these two properties disagree on purpose.
        ProtocolVersion = null,

        Capabilities = new ServerCapabilities
        {
            // Declared so `initialize` advertises tools; the list itself is the
            // child's, fetched per call.
            Tools = new ToolsCapability(),
        },

        Handlers = new McpServerHandlers
        {
            ListToolsHandler = ListToolsAsync,
            CallToolHandler = CallToolAsync,
        },
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // Disposing the client closes the transport, which closes the child's
        // stdin -- upstream's own graceful teardown path -- and then closes the
        // job handle, which is what guarantees no browser is left behind.
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private static string Version { get; } =
        typeof(BrowserProxy).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    private async ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> context,
        CancellationToken cancellationToken)
    {
        // The raw overload, taking the params object. The convenience overload
        // -- ListToolsAsync(RequestOptions?, ct) -- silently drops any tool
        // whose x-mcp-header annotations fail SEP-2243 validation, which shrinks
        // the exposed surface with no error anywhere. Measured on the same child
        // in the same run: 5 tools against 4.
        var result = await _client.ListToolsAsync(context.Params ?? new ListToolsRequestParams(), cancellationToken)
            .ConfigureAwait(false);

        ProxyLog.ToolsListed(_logger, result.Tools.Count);
        return result;
    }

    private async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var parameters = context.Params
            ?? throw new McpException("tools/call arrived with no parameters.");

        return await _client.CallToolAsync(parameters, cancellationToken).ConfigureAwait(false);
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
        Level = LogLevel.Information,
        Message = "Forwarded tools/list. tools={ToolCount}")]
    public static partial void ToolsListed(ILogger logger, int toolCount);
}
