// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Protocol;
using BrowserAI.Proxy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The in-process rig: test client → BrowserAI → fake child, over pipes, with
/// no process anywhere and nothing on disk.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two hops, which is why the SDK's own fixtures are not vendored.</b>
/// <c>ClientServerTestBase</c> is 1,082 lines, unpublished to NuGet,
/// Apache-2.0, and wires a single client↔server pipe pair. A proxy is a server
/// on one side and a client on the other, so the rig needs two — and copying
/// theirs to add the second would buy a permanent three-way merge against an
/// upstream that edits <c>tests/</c> weekly.
/// </para>
/// <para>
/// <b>The teardown order is load-bearing, and what each step actually buys was
/// measured rather than assumed.</b> Cancel the token, complete <i>both</i>
/// writers on the caller hop, await the server task, then dispose downwards.
/// Removing one step at a time on ModelContextProtocol 2.2.0, 2026-08-16, over
/// the whole suite:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Cancel alone</b> ends the server task but leaves both pipes open for
/// writing, and the rig's own liveness check turns ten tests red.
/// </item>
/// <item>
/// <b>Completing both writers alone</b> ends the server task <i>and</i> closes
/// the hop. The suite is green with the cancellation removed entirely: EOF is
/// what <c>RunAsync</c> returns on.
/// </item>
/// <item>
/// <b>Neither</b> leaves the server task running — it was still running when
/// dispose began in every rig, against a bounded wait — as well as leaving the
/// pipes open.
/// </item>
/// </list>
/// <para>
/// So the plan's <i>"any other order hangs or throws"</i> is right about the
/// consequence and wrong about the mechanism: the two steps are not a sequence
/// in which the first enables the second, they are two independent ways to end
/// the server task of which only one also closes the pipes. Keeping both is
/// still correct — cancellation is what a caller-supplied token has to do, and
/// completion is what <see cref="StdioChannel.Over(Stream, Stream)"/> requires,
/// since it deliberately does not own the streams it is handed.
/// </para>
/// <para>
/// <b>Disposal asserts, and can therefore mask.</b> A teardown defect throws
/// from <see cref="DisposeAsync"/>, which is what makes "no test leaves a live
/// pipe behind" a mechanism rather than a habit. The cost is the ordinary one:
/// if a test body has already failed and teardown then fails too, the teardown
/// exception is the one that propagates. Its message names the pipe, which is
/// usually the more interesting half anyway.
/// </para>
/// </remarks>
internal sealed class McpTestHarness : IAsyncDisposable
{
    private readonly List<PipeDuplex> _hops = [];
    private readonly CancellationTokenSource _stopping;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PipeDuplex? _callerHop;
    private readonly PipeDuplex _childHop;
    private readonly BrowserProxy? _proxy;
    private readonly McpServer? _server;
    private readonly DirectStdioServerTransport? _serverTransport;
    private readonly Task _serverTask;

    private int _disposed;
    private bool _serverTaskDoneBeforeDispose;

    private McpTestHarness(Parts parts)
    {
        _stopping = parts.Stopping;
        _loggerFactory = parts.LoggerFactory;
        _childHop = parts.ChildHop;
        _callerHop = parts.CallerHop;
        _proxy = parts.Proxy;
        _server = parts.Server;
        _serverTransport = parts.ServerTransport;
        _serverTask = parts.ServerTask;

        Logs = parts.Logs;
        Child = parts.Child;
        Client = parts.Client;

        if (parts.CallerHop is not null)
        {
            _hops.Add(parts.CallerHop);
        }

        _hops.Add(parts.ChildHop);
    }

    /// <summary>The hand-written client on the caller's end.</summary>
    public RawPipeClient Client { get; }

    /// <summary>The double standing in for <c>@playwright/mcp</c>.</summary>
    public FakePlaywrightChild Child { get; }

    /// <summary>Everything the product logged during this test.</summary>
    public CapturingLoggerProvider Logs { get; }

    /// <summary>
    /// The product object under test, for the assertions that are about the
    /// options it hands the server rather than about a round trip.
    /// </summary>
    /// <exception cref="InvalidOperationException">This rig has no proxy in it.</exception>
    public BrowserProxy Proxy =>
        _proxy ?? throw new InvalidOperationException("This rig speaks straight to the double; there is no proxy in it.");

    /// <summary>
    /// The full topology: test client → BrowserAI → fake child, handshaken on
    /// both hops.
    /// </summary>
    /// <param name="configure">Programs the double before it is started.</param>
    /// <returns>The rig, ready for a request.</returns>
    public static async Task<McpTestHarness> ThroughTheProxyAsync(Action<FakePlaywrightChild>? configure = null)
    {
        var logs = new CapturingLoggerProvider();
        var loggerFactory = NewLoggerFactory(logs);
        var stopping = new CancellationTokenSource();

        var childHop = new PipeDuplex("child hop (BrowserAI ↔ fake child)");
        var child = new FakePlaywrightChild(childHop);
        configure?.Invoke(child);
        child.Start();

        BrowserProxy? proxy = null;
        DirectStdioServerTransport? serverTransport = null;
        McpServer? server = null;

        try
        {
            proxy = await BrowserProxy.ConnectAsync(
                new PipeClientTransport(childHop, loggerFactory),
                loggerFactory,
                RigSessionEnvironment.Create());

            var callerHop = new PipeDuplex("caller hop (test client ↔ BrowserAI)");

            serverTransport = new DirectStdioServerTransport(
                StdioChannel.Over(callerHop.ServerReads, callerHop.ServerWrites),
                loggerFactory);

            server = McpServer.Create(serverTransport, proxy.ServerOptions(), loggerFactory);

            var serverTask = server.RunAsync(stopping.Token);
            var client = new RawPipeClient(callerHop);

            _ = await client.InitializeAsync(TestDefaults.CallerProtocolVersion);

            return new McpTestHarness(new Parts
            {
                Stopping = stopping,
                LoggerFactory = loggerFactory,
                Logs = logs,
                ChildHop = childHop,
                Child = child,
                CallerHop = callerHop,
                Client = client,
                Proxy = proxy,
                Server = server,
                ServerTransport = serverTransport,
                ServerTask = serverTask,
            });
        }
        catch
        {
            if (server is not null)
            {
                await server.DisposeAsync();
            }

            if (serverTransport is not null)
            {
                await serverTransport.DisposeAsync();
            }

            if (proxy is not null)
            {
                await proxy.DisposeAsync();
            }

            await child.DisposeAsync();
            stopping.Dispose();
            loggerFactory.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The child hop alone: test client → fake child, with no proxy in
    /// between.
    /// </summary>
    /// <remarks>
    /// This is how a capability of the double is demonstrated to be the
    /// double's. Through the proxy, an unknown content type produces an error
    /// today because the SDK's typed layer throws on one; direct, the same
    /// bytes arrive untouched. Asserting both is what tells step 9 which half
    /// it has to change.
    /// </remarks>
    /// <param name="configure">Programs the double before it is started.</param>
    /// <returns>The rig, ready for a request.</returns>
    public static async Task<McpTestHarness> DirectToTheChildAsync(Action<FakePlaywrightChild>? configure = null)
    {
        var logs = new CapturingLoggerProvider();
        var loggerFactory = NewLoggerFactory(logs);
        var stopping = new CancellationTokenSource();

        var childHop = new PipeDuplex("child hop (test client ↔ fake child)");
        var child = new FakePlaywrightChild(childHop);
        configure?.Invoke(child);
        child.Start();

        var client = new RawPipeClient(childHop);

        try
        {
            _ = await client.InitializeAsync(TestDefaults.CallerProtocolVersion);
        }
        catch
        {
            await child.DisposeAsync();
            await client.DisposeAsync();
            stopping.Dispose();
            loggerFactory.Dispose();
            throw;
        }

        return new McpTestHarness(new Parts
        {
            Stopping = stopping,
            LoggerFactory = loggerFactory,
            Logs = logs,
            ChildHop = childHop,
            Child = child,
            Client = client,
            ServerTask = Task.CompletedTask,
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // 1. Cancel the token the server was started with. Measured: this ends
        //    the server task on its own, and closes nothing.
        await _stopping.CancelAsync();

        // 2. Complete BOTH writers on the caller hop. StdioChannel.Over does
        //    not own the streams it was handed, so this is the only thing that
        //    closes them -- and measured, it also ends the server task on its
        //    own, because EOF is what RunAsync returns on.
        if (_callerHop is not null)
        {
            await _callerHop.CompleteWritersAsync();
        }

        // 3. Await the server task. With neither of the two steps above it is
        //    still running when this returns, which is why the wait is bounded
        //    rather than open-ended.
        try
        {
            await _serverTask.WaitAsync(TestDefaults.Patience);
        }
        catch (OperationCanceledException)
        {
            // Expected: step 1 asked for it.
        }
#pragma warning disable CA1031 // A faulted server task is reported by the liveness check below rather than thrown from here.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        // Read here, before anything is disposed. Disposing the server ends the
        // task too, so a check made after step 4 can never see it still
        // running -- which is what the first version of this rig did, and the
        // arm would have been permanently dead.
        _serverTaskDoneBeforeDispose = _serverTask.IsCompleted;

        // 4. Dispose downwards: server, its transport, then the proxy -- which
        //    disposes the SDK client, which disposes the child leg and closes
        //    the child's stdin.
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        if (_serverTransport is not null)
        {
            await _serverTransport.DisposeAsync();
        }

        if (_proxy is not null)
        {
            await _proxy.DisposeAsync();
        }

        await _childHop.CompleteWritersAsync();
        await Child.DisposeAsync();
        await Client.DisposeAsync();

        // 5. The providers last, so anything logged on the way down is still
        //    captured.
        _loggerFactory.Dispose();
        Logs.Dispose();
        _stopping.Dispose();

        var faults = WhatIsStillLive();

        if (faults.Count is not 0)
        {
            throw new InvalidOperationException(
                $"The rig left something live after teardown:{Environment.NewLine}{string.Join(Environment.NewLine, faults)}");
        }
    }

    /// <summary>
    /// Everything still alive after teardown, which must be nothing.
    /// </summary>
    /// <returns>One line per defect; empty when the rig shut down cleanly.</returns>
    private List<string> WhatIsStillLive()
    {
        var faults = new List<string>();

        faults.AddRange(_hops.Select(hop => hop.WhatIsStillLive()).OfType<string>());

        if (!_serverTaskDoneBeforeDispose)
        {
            faults.Add("the MCP server task was still running when dispose began");
        }

        if (!Child.HasStopped)
        {
            faults.Add("the fake child's read loop is still running");
        }

        return faults;
    }

    private static ILoggerFactory NewLoggerFactory(CapturingLoggerProvider logs) =>
        LoggerFactory.Create(builder =>
        {
            _ = builder.SetMinimumLevel(LogLevel.Trace);
            _ = builder.AddProvider(new TUnitLoggerProvider());
            _ = builder.AddProvider(logs);
        });

    /// <summary>What one rig is made of, so the constructor is not eleven positional arguments.</summary>
    private sealed class Parts
    {
        public required CancellationTokenSource Stopping { get; init; }

        public required ILoggerFactory LoggerFactory { get; init; }

        public required CapturingLoggerProvider Logs { get; init; }

        public required PipeDuplex ChildHop { get; init; }

        public required FakePlaywrightChild Child { get; init; }

        public required RawPipeClient Client { get; init; }

        public required Task ServerTask { get; init; }

        public PipeDuplex? CallerHop { get; init; }

        public BrowserProxy? Proxy { get; init; }

        public McpServer? Server { get; init; }

        public DirectStdioServerTransport? ServerTransport { get; init; }
    }
}
