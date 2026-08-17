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
    private readonly RigSessionEnvironment? _sessions;

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
        _sessions = parts.Sessions;

        Logs = parts.Logs;
        SurfaceChild = parts.Child;
        Session = parts.Session;
        Client = parts.Client;

        if (parts.CallerHop is not null)
        {
            _hops.Add(parts.CallerHop);
        }

        _hops.Add(parts.ChildHop);
    }

    /// <summary>The hand-written client on the caller's end.</summary>
    public RawPipeClient Client { get; }

    /// <summary>
    /// The double a <c>tools/call</c> reaches: the default session's child where
    /// there is one, and otherwise the run's own.
    /// </summary>
    /// <remarks>
    /// <b>These stopped being the same object at step 13.</b> Once <c>session</c>
    /// became mandatory, a <c>tools/call</c> goes to the child of the session it
    /// names and never to the run's own — so a test asserting on what the child
    /// received has to look at the session's. <c>tools/list</c> still comes from
    /// <see cref="SurfaceChild"/>, because the tool set may not vary per
    /// connection and one static list has to be answerable before any session
    /// exists.
    /// </remarks>
    public FakePlaywrightChild Child =>
        _sessions?.SessionChildren is [var first, ..] ? first : SurfaceChild;

    /// <summary>The double that answers <c>tools/list</c> for the whole run.</summary>
    public FakePlaywrightChild SurfaceChild { get; }

    /// <summary>
    /// The default session this rig opened, or <see langword="null"/> if it could
    /// not open one.
    /// </summary>
    public string? Session { get; }

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
    /// <param name="configure">Programs the surface double before it is started.</param>
    /// <param name="sessions">
    /// The environment sessions are opened in. Supplied by a test that means to
    /// open one; the default can open none, which keeps every other test in this
    /// layer touching nothing on disk.
    /// </param>
    /// <returns>The rig, ready for a request.</returns>
    public static async Task<McpTestHarness> ThroughTheProxyAsync(
        Action<FakePlaywrightChild>? configure = null,
        RigSessionEnvironment? sessions = null)
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
        var sessionEnvironment = sessions ?? RigSessionEnvironment.Create(configure);

        try
        {
            proxy = await BrowserProxy.ConnectAsync(
                new PipeClientTransport(childHop, loggerFactory),
                loggerFactory,
                sessionEnvironment.Environment);

            var callerHop = new PipeDuplex("caller hop (test client ↔ BrowserAI)");

            serverTransport = new DirectStdioServerTransport(
                StdioChannel.Over(callerHop.ServerReads, callerHop.ServerWrites),
                loggerFactory);

            server = McpServer.Create(serverTransport, proxy.ServerOptions(), loggerFactory);

            var serverTask = server.RunAsync(stopping.Token);
            var client = new RawPipeClient(callerHop);

            _ = await client.InitializeAsync(TestDefaults.CallerProtocolVersion);

            // One session, opened up front, because `session` is mandatory and a
            // layer that could not open one could no longer exercise a single
            // tools/call. It costs a directory, a lock and a log under the
            // suite's scratch root -- so this layer is no longer "nothing on
            // disk", and saying so is cheaper than a reader discovering it.
            var session = sessionEnvironment.CanOpenSessions && sessionEnvironment.OpensDefaultSession
                ? await OpenDefaultSessionAsync(client, sessionEnvironment)
                : null;

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
                Sessions = sessionEnvironment,
                Session = session,
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
            await sessionEnvironment.DisposeAsync();
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

        // After the proxy, because disposing it is what tears every session
        // down -- lock, child, log -- and this closes the hops those children
        // sat on.
        if (_sessions is not null)
        {
            await _sessions.DisposeAsync();
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

        if (_sessions is not null)
        {
            faults.AddRange(_sessions.WhatIsStillLive());
        }

        // ⚠️ BOTH conditions, and the second one is what makes this a leak check
        // rather than a stopwatch.
        //
        // Corrected 2026-08-17 (previously: `_serverTaskDoneBeforeDispose`
        // alone). That flag is `IsCompleted` read after a bounded 30 s wait, so
        // on a loaded machine it says "the continuation has not been scheduled
        // yet", and the rig then reported a LEAK. Measured under a saturated
        // suite: eleven rigs in one run, none of them leaking anything -- every
        // one of those tasks completed moments later.
        //
        // The property the flag exists for is real and is kept: cancelling the
        // token and completing the writers end the server task ON THEIR OWN,
        // without anything being disposed. What is no longer asserted is that
        // they do it within thirty seconds, which is not a property of the
        // product. A task that is genuinely stuck is still caught, because it is
        // still incomplete after the whole disposal chain has run -- and that is
        // the only state in which "still live after teardown" is true.
        if (!_serverTaskDoneBeforeDispose && !_serverTask.IsCompleted)
        {
            faults.Add(
                "the MCP server task was still running when dispose began AND is still running after it: cancelling the token and completing the caller hop's writers did not end it, and neither did disposing everything below it");
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

        public RigSessionEnvironment? Sessions { get; init; }

        public string? Session { get; init; }
    }

    /// <summary>Opens the one session every tools/call in this layer runs against.</summary>
    private static async Task<string> OpenDefaultSessionAsync(RawPipeClient client, RigSessionEnvironment sessions)
    {
        var directory = Path.Combine(sessions.Root, "rig-session");

        var answer = await client.RoundTripAsync("tools/call", new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = "browserai_init",
            ["arguments"] = new System.Text.Json.Nodes.JsonObject
            {
                ["directory"] = directory,

                // `persistent` so the policy permits every tool this layer
                // calls: the passthrough assertions are about bytes, and a mode
                // refusal would replace the child's answer with ours.
                //
                // ⚠️ Read off the rig rather than written here, because the
                // reason above stops holding the moment the child is real.
                // `persistent` is Headed:true, and behind a real node child that
                // is a Chromium window on the developer's screen which takes
                // their foreground — measured 2026-08-17 as the ONLY thing in
                // the whole suite that did. RigSessionEnvironment decides, so a
                // second real-child arm inherits the answer instead of
                // rediscovering the defect.
                ["mode"] = sessions.DefaultSessionMode,
                ["purpose"] = "the in-process rig's own session",
            },
        });

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException(
                "The rig could not open its default session, so no tools/call in this layer can reach a child: "
                + string.Concat((answer["content"]?.AsArray() ?? [])
                    .Select(block => (string?)block?["text"] ?? string.Empty)));
        }

        return directory;
    }
}
