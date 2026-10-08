// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Relay;
using BrowserAI.Updates;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One relay engine in this process, between a hand-written client and any number
/// of hand-written backgrounds, on a clock only the test moves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is a process, a named pipe or a file.</b> The client is a
/// <see cref="FrameChannel"/> on one <see cref="PipeDuplex"/>; each background is a
/// <see cref="FrameChannel"/> on another, handed to the engine by
/// <see cref="FakeBackgroundFinder"/> when the engine next looks. The handshake is the
/// product's own <see cref="SdkHandshake"/>, so the answers to <c>initialize</c> and
/// <c>ping</c> are the SDK's as production gives them.
/// </para>
/// <para>
/// <b>Two ways to know the engine has acted, and an arm uses the right one.</b> After
/// moving the clock, <see cref="StepAsync"/> waits for the engine to settle: every
/// event the move caused is handled and every finder call is back. After writing a
/// frame, an arm waits for an answer to it, or for <see cref="BarrierAsync"/>, a
/// <c>ping</c> whose answer can only come after everything the client sent before it.
/// Neither is a wait on the wall clock.
/// </para>
/// </remarks>
internal sealed class RelayRig : IAsyncDisposable
{
    private readonly PipeDuplex _clientHop = new("client hop (client and relay)");
    private readonly CancellationTokenSource _stop = new();
    private readonly SdkHandshake _handshake = SdkHandshake.Start();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly List<string?> _classified = [];
    private readonly Lock _gate = new();

    private int _barriers;
    private int _lists;

    private RelayRig(Func<string?, RelayReconnect>? reconnectOf, bool gated)
    {
        Client = new FrameChannel(_clientHop.ClientReads, _clientHop.ClientWrites);
        Gate = gated ? new GatedStream(_clientHop.ServerWrites) : null;

        Engine = new RelayEngine(
            _clientHop.ServerReads,
            Gate ?? _clientHop.ServerWrites,
            Finder,
            _handshake,
            ToolList,
            reconnectOf ?? Classify,
            Facts,
            Clock,
            _logs.CreateLogger(nameof(RelayEngine)));

        Running = Engine.RunAsync(_stop.Token);

        // The client's end of stdout closes when the relay is over, as it does when the
        // process exits, so an arm waiting for a frame that is never coming fails at
        // once with the relay's own outcome.
        _ = Running.ContinueWith(
            static (_, state) => ((Stream)state!).Dispose(),
            _clientHop.ServerWrites,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>The facts every rig's relay is built with.</summary>
    public static RelayFacts Facts { get; } = new(
        Build: "9.9.9-relay-tests",
        RelayPid: 4242,
        ClientPid: 2424,
        Folder: @"C:\Projects\RelayTests",
        DataRoot: @"C:\Data\BrowserAI-relay-tests",
        LogPath: @"C:\Data\BrowserAI-relay-tests\logs");

    /// <summary>What the default classifier answers for any client: a terminal Claude Code.</summary>
    public static RelayReconnect Classification => RelayReconnect.McpReconnect;

    /// <summary>The clock every countdown in the engine runs on.</summary>
    public ManualClock Clock { get; } = new();

    /// <summary>The finder the engine looks through.</summary>
    public FakeBackgroundFinder Finder { get; } = new();

    /// <summary>The engine under test.</summary>
    public RelayEngine Engine { get; }

    /// <summary>The client's end: it writes the relay's input and reads its output.</summary>
    public FrameChannel Client { get; }

    /// <summary>The relay's output, held at a gate the arm controls, when the rig was started with one.</summary>
    public GatedStream? Gate { get; }

    /// <summary>The engine's run.</summary>
    public Task<RelayEnd> Running { get; }

    /// <summary>Every record the engine logged, in order.</summary>
    public IReadOnlyList<LogRecord> Logs => _logs.Records;

    /// <summary>Every client name the default classifier was asked about, in order.</summary>
    public IReadOnlyList<string?> Classified
    {
        get
        {
            lock (_gate)
            {
                return [.. _classified];
            }
        }
    }

    /// <summary>The tool list every rig's relay answers with: two tools, one of ours and one forwarded.</summary>
    /// <returns>A fresh copy.</returns>
    public static JsonObject ToolList() => new()
    {
        ["tools"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "browserai_init",
                ["description"] = "Opens a session.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["directory"] = new JsonObject { ["type"] = "string" } },
                },
            },
            new JsonObject
            {
                ["name"] = "browser_navigate",
                ["description"] = "Navigate to a URL.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["url"] = new JsonObject { ["type"] = "string" },
                        ["session"] = new JsonObject { ["type"] = "string" },
                        ["why"] = new JsonObject { ["type"] = "string" },
                    },
                },
            }),
    };

    /// <summary>The instant <paramref name="since"/> after the clock's start, as the relay writes instants.</summary>
    /// <param name="since">How long after the start.</param>
    /// <returns>Its text.</returns>
    public static string At(TimeSpan since) => RelayWire.Instant(DateTimeOffset.UnixEpoch + since);

    /// <summary>A client's <c>initialize</c>.</summary>
    /// <param name="clientName">Its <c>clientInfo.name</c>.</param>
    /// <param name="clientVersion">Its <c>clientInfo.version</c>.</param>
    /// <returns>The frame.</returns>
    public static string InitializeFrame(string clientName, string clientVersion) =>
        "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"" + TestDefaults.CallerProtocolVersion
        + "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"" + clientName + "\",\"version\":\"" + clientVersion + "\"}}}";

    /// <summary>A client's <c>tools/call</c>.</summary>
    /// <param name="id">Its id, as JSON: a number, or a string in quotes.</param>
    /// <param name="tool">The tool it names.</param>
    /// <returns>The frame.</returns>
    public static string CallFrame(string id, string tool = "browser_navigate") =>
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool
        + "\",\"arguments\":{\"url\":\"https://example.invalid/\",\"session\":\"C:\\\\s\",\"why\":\"the relay suite\"}}}";

    /// <summary>A client's cancellation of one request.</summary>
    /// <param name="id">The request's id, as JSON.</param>
    /// <returns>The frame.</returns>
    public static string CancelFrame(string id) =>
        "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":" + id + ",\"reason\":\"the suite\"}}";

    /// <summary>Starts a rig.</summary>
    /// <param name="reconnectOf">The classifier, when an arm needs its own; otherwise one that records and answers <see cref="Classification"/>.</param>
    /// <param name="gated">Whether the relay's output passes through <see cref="Gate"/>.</param>
    /// <returns>The rig, running.</returns>
    public static RelayRig Start(Func<string?, RelayReconnect>? reconnectOf = null, bool gated = false) => new(reconnectOf, gated);

    /// <summary>Writes one frame as the client.</summary>
    /// <param name="json">The frame.</param>
    /// <returns>The write.</returns>
    public async Task SendAsync(string json)
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        await Client.WriteFrameAsync(json, hang.Token);
    }

    /// <summary>Reads the next frame the relay wrote to the client.</summary>
    /// <returns>The frame.</returns>
    public async Task<WireFrame> NextAsync()
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        var frame = await Client.ReadFrameAsync(hang.Token);

        return frame is null
            ? throw new InvalidOperationException($"The relay closed the client's output with no further frame; its run is {Describe(Running)}.")
            : new WireFrame(frame);
    }

    /// <summary>
    /// The client's handshake: <c>initialize</c>, its answer, and
    /// <c>notifications/initialized</c>.
    /// </summary>
    /// <param name="clientName">The client's name.</param>
    /// <param name="clientVersion">The client's version.</param>
    /// <returns>The answer to <c>initialize</c>.</returns>
    public async Task<WireFrame> InitializeAsync(string clientName = "BrowserAI.RelayTests", string clientVersion = "1.0")
    {
        await SendAsync(InitializeFrame(clientName, clientVersion));
        var answer = await NextAsync();
        await SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        return answer;
    }

    /// <summary>Asks for the tool list as the client.</summary>
    /// <returns>The answer.</returns>
    public async Task<WireFrame> ListAsync()
    {
        await SendAsync($$"""{"jsonrpc":"2.0","id":"list-{{++_lists}}","method":"tools/list"}""");
        return await NextAsync();
    }

    /// <summary>
    /// Sends a <c>ping</c> and reads up to its answer, which the relay can only write
    /// after it has handled everything the client sent before it.
    /// </summary>
    /// <returns>Every frame that arrived before the answer.</returns>
    public async Task<List<WireFrame>> BarrierAsync()
    {
        var id = $"barrier-{++_barriers}";
        await SendAsync($$"""{"jsonrpc":"2.0","id":"{{id}}","method":"ping"}""");

        var before = new List<WireFrame>();

        while (true)
        {
            var frame = await NextAsync();

            if (frame.IdText == id && frame.Result is not null)
            {
                return before;
            }

            before.Add(frame);
        }
    }

    /// <summary>Moves the clock and waits for the engine to have handled everything the move caused.</summary>
    /// <param name="by">How far.</param>
    /// <returns>A task that completes once the engine has settled.</returns>
    public async Task StepAsync(TimeSpan by)
    {
        Clock.Advance(by);
        await SettledAsync();
    }

    /// <summary>Waits for the engine to settle.</summary>
    /// <returns>A task that completes once it has.</returns>
    public async Task SettledAsync() => await Engine.SettledAsync().WaitAsync(TestDefaults.InProcessHang);

    /// <summary>Waits until at least this many events wait in the engine's queue.</summary>
    /// <remarks>
    /// <b>Only for an arm that holds the engine at <see cref="Gate"/></b>, to know that
    /// frames it wrote are queued behind the write it holds. It polls a count; nothing
    /// is asserted on how long that takes.
    /// </remarks>
    /// <param name="count">How many.</param>
    /// <returns>A task that completes once they are queued.</returns>
    public async Task QueuedAsync(int count)
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

        while (Engine.EventsWaiting < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), hang.Token);
        }
    }

    /// <summary>Waits for the engine's run to end.</summary>
    /// <returns>How it ended.</returns>
    public async Task<RelayEnd> EndedAsync() => await Running.WaitAsync(TestDefaults.InProcessHang);

    /// <summary>Ends the client's input, as a client that exits does.</summary>
    public void EndTheClientsInput() => Client.CloseOutput();

    /// <summary>Fires the token the engine was started with.</summary>
    /// <returns>The cancellation.</returns>
    public async Task CancelAsync() => await _stop.CancelAsync();

    /// <summary>
    /// Offers a background and greets it: the engine finds it at its first look after
    /// <c>initialize</c>, which this sends.
    /// </summary>
    /// <param name="clientName">The client's name.</param>
    /// <param name="pid">The pid the background answers with.</param>
    /// <returns>The background, greeted, and the greeting it received.</returns>
    public async Task<(FakeBackground Background, WireFrame Hello)> ConnectedAsync(string clientName = "BrowserAI.RelayTests", int pid = FakeBackground.Pid)
    {
        var background = Finder.Offer();
        await InitializeAsync(clientName);
        var hello = await background.GreetAsync(pid);
        return (background, hello);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // A gate an arm left shut would hold the loop inside a write, where the stop
        // below never reaches it, and the wait for it would bury the arm's own failure.
        Gate?.Open();
        await _stop.CancelAsync();

        try
        {
            _ = await Running.WaitAsync(TestDefaults.InProcessHang);
        }
        finally
        {
            Finder.Dispose();
            await _clientHop.CompleteWritersAsync();
            Client.Dispose();
            await _handshake.DisposeAsync();
            _logs.Dispose();
            _stop.Dispose();
        }
    }

    private static string Describe(Task<RelayEnd> running) => running.Status switch
    {
        TaskStatus.RanToCompletion => $"ended: {running.Result}",
        TaskStatus.Faulted => $"faulted: {running.Exception}",
        TaskStatus.Canceled => "cancelled",
        _ => "still running",
    };

    private RelayReconnect Classify(string? clientName)
    {
        lock (_gate)
        {
            _classified.Add(clientName);
        }

        return Classification;
    }
}
