// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The session host in process: one <see cref="SessionHost"/> over a rig's session
/// doubles, and any number of client connections to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b, the maintainer's words of 2026-10-03, verbatim:</b> <i>"If the server
/// crashes and the coordinator loses the pipe, keep the browser around with the
/// already running activity timeout timer active."</i> The session host serves one
/// MCP conversation per pipe connection over one set of sessions; this rig gives each
/// connection its own pair of in-process pipes, its own server and its own proxy,
/// exactly as the host's own accept loop does, so what a test sees across two
/// connections is the product's and not the rig's.
/// </para>
/// <para>
/// <b>A connection ends the way a front that dies ends it</b>: its end of the pipe
/// closes, the server's read sees the end of input, and the proxy is disposed, which
/// detaches what it drove. Nothing in the host is told anything else.
/// </para>
/// <para>
/// ⚠️ <i>Corrected 2026-10-08 (previously "over a fake tool-list child and a rig's
/// session doubles", with that double started first): the host answers
/// <c>tools/list</c> from the list compiled into the binary, here the rig's own
/// list, and starts no child for it.</i>
/// </para>
/// </remarks>
internal sealed class SessionHostRig : IAsyncDisposable
{
    private readonly List<HostConnection> _connections = [];
    private readonly ILoggerFactory _loggerFactory;
    private int _clients;
    private int _disposed;

    private SessionHostRig(
        SessionHost host,
        RigSessionEnvironment sessions,
        ILoggerFactory loggerFactory,
        CapturingLoggerProvider logs)
    {
        Host = host;
        Sessions = sessions;
        _loggerFactory = loggerFactory;
        Logs = logs;
    }

    /// <summary>The host under test.</summary>
    public SessionHost Host { get; }

    /// <summary>The environment its sessions are opened in.</summary>
    public RigSessionEnvironment Sessions { get; }

    /// <summary>Everything the product logged.</summary>
    public CapturingLoggerProvider Logs { get; }

    /// <summary>Starts a host over a rig's sessions.</summary>
    /// <param name="sessions">The rig's sessions, which the caller owns and disposes after this.</param>
    /// <returns>The rig, with no connection yet.</returns>
    public static Task<SessionHostRig> StartAsync(RigSessionEnvironment sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var logs = new CapturingLoggerProvider();
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            _ = builder.SetMinimumLevel(LogLevel.Trace);
            _ = builder.AddProvider(new TUnitLoggerProvider());
            _ = builder.AddProvider(logs);
        });

        sessions.CaptureSessionRecordsInto(logs);

        return Task.FromResult(new SessionHostRig(SessionHost.Create(loggerFactory, sessions.Environment), sessions, loggerFactory, logs));
    }

    /// <summary>A new client connection, handshaken and, unless asked not to, listed.</summary>
    /// <param name="clientName">What the client calls itself.</param>
    /// <param name="listTools">Whether it asks for the tool list after the handshake, as a first connect does.</param>
    /// <returns>The connection.</returns>
    public async Task<HostConnection> ConnectAsync(string? clientName = null, bool listTools = true)
    {
        var number = Interlocked.Increment(ref _clients);
        var hop = new PipeDuplex($"caller hop {number} (test client ↔ session host)");

        // Ownership of all three moves into the HostConnection below, whose EndAsync
        // disposes them, and the rig ends every connection it made.
#pragma warning disable CA2000
        var transport = new DirectStdioServerTransport(StdioChannel.Over(hop.ServerReads, hop.ServerWrites), _loggerFactory);

        // A pid of its own per connection, so a refusal naming the client that
        // drives a session can be told apart from one naming another.
        var connection = new CallerConnection(clientProcessId: 70000 + number);
        var proxy = Host.Accept(connection);
        var server = McpServer.Create(transport, proxy.ServerOptions(), _loggerFactory);
        var serving = server.RunAsync(CancellationToken.None);
        var client = new RawPipeClient(hop);
#pragma warning restore CA2000

        var opened = new HostConnection(hop, transport, proxy, server, serving, client);

        lock (_connections)
        {
            _connections.Add(opened);
        }

        _ = await client.InitializeAsync(TestDefaults.CallerProtocolVersion, clientName ?? $"rig-client-{number}");

        // A client that re-dials lists nothing, measured of Claude Code (Q261); an arm
        // about that path says so here.
        if (listTools)
        {
            _ = await client.RoundTripAsync("tools/list", new JsonObject());
        }

        return opened;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        List<HostConnection> open;

        lock (_connections)
        {
            open = [.. _connections];
        }

        foreach (var connection in open)
        {
            await connection.EndAsync();
        }

        await Host.DisposeAsync();

        _loggerFactory.Dispose();
        Logs.Dispose();
    }
}

/// <summary>One client connection to a <see cref="SessionHostRig"/>.</summary>
/// <param name="hop">The connection's pipes.</param>
/// <param name="transport">The server's end.</param>
/// <param name="proxy">The proxy answering it.</param>
/// <param name="server">The MCP server over the transport.</param>
/// <param name="serving">The server's run.</param>
/// <param name="client">The test's own client.</param>
internal sealed class HostConnection(
    PipeDuplex hop,
    DirectStdioServerTransport transport,
    BrowserProxy proxy,
    McpServer server,
    Task serving,
    RawPipeClient client)
{
    private int _ended;

    /// <summary>The test's client on this connection.</summary>
    public RawPipeClient Client { get; } = client;

    /// <summary>The proxy answering this connection.</summary>
    public BrowserProxy Proxy { get; } = proxy;

    /// <summary>Calls one tool and returns its result.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The result object.</returns>
    public Task<JsonObject> CallAsync(string tool, JsonObject arguments) =>
        Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    /// <summary>
    /// Ends the connection the way a front that dies ends it: its pipe closes, the
    /// server reads the end, and the proxy is disposed.
    /// </summary>
    /// <returns>The end, once the proxy has detached what it drove.</returns>
    public async Task EndAsync()
    {
        if (Interlocked.Exchange(ref _ended, 1) is not 0)
        {
            return;
        }

        await hop.CompleteWritersAsync();

        try
        {
            await serving.WaitAsync(TestDefaults.InProcessHang);
        }
#pragma warning disable CA1031 // A server ending because its input closed is the path under test, however it ends.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        await server.DisposeAsync();
        await transport.DisposeAsync();
        await Proxy.DisposeAsync();
        await Client.DisposeAsync();
    }

    /// <summary>The text of a tool result.</summary>
    /// <param name="result">The result.</param>
    /// <returns>Its text blocks, joined.</returns>
    public static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
