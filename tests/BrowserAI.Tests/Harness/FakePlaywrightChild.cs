// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What a <c>tools/call</c> against one tool does. Every field is a way for the
/// double to misbehave on purpose.
/// </summary>
internal sealed record FakeToolBehaviour
{
    /// <summary>
    /// The literal JSON spliced in as the response's <c>result</c>, byte for
    /// byte.
    /// </summary>
    /// <remarks>
    /// A string rather than a typed object, and that is the point: the double
    /// never serialises through a contract, so anything that arrives at the
    /// caller differently is attributable to the proxy rather than to the
    /// double's writer.
    /// </remarks>
    public string? RawResult { get; init; }

    /// <summary>A JSON-RPC error code to answer with instead of a result.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>The message that accompanies <see cref="ErrorCode"/>.</summary>
    public string ErrorMessage { get; init; } = "The fake child was told to fail this call.";

    /// <summary>Literal JSON for the error's <c>data</c> member, if any.</summary>
    public string? RawErrorData { get; init; }

    /// <summary>How long to wait before answering.</summary>
    /// <remarks>
    /// This one blocks the child's read loop, which is what several ordering
    /// assertions rely on. A call that has to stay open <i>while the child keeps
    /// listening</i> — the shape a cancellation test needs — uses
    /// <see cref="HoldUntil"/> instead.
    /// </remarks>
    public TimeSpan Delay { get; init; }

    /// <summary>
    /// Holds the call open without blocking the read loop, so the child can
    /// still hear what arrives while it is working.
    /// </summary>
    /// <remarks>
    /// <b>Without this a cancellation test cannot exist.</b> A child parked
    /// inside its own dispatch cannot read the
    /// <c>notifications/cancelled</c> that the test is trying to prove reaches
    /// it, so the assertion would be about the double rather than about the
    /// proxy.
    /// </remarks>
    public Task? HoldUntil { get; init; }

    /// <summary>
    /// How many <c>notifications/progress</c> to send before answering, echoing
    /// the caller's own <c>progressToken</c>.
    /// </summary>
    public int ProgressUpdates { get; init; }

    /// <summary>
    /// Closes stdout without answering, which is what a child that dies
    /// mid-call looks like from the outside.
    /// </summary>
    public bool DieWithoutAnswering { get; init; }

    /// <summary>
    /// Writes this many bytes to the absolute path the call's <c>filename</c>
    /// argument names, the way upstream writes an artifact.
    /// </summary>
    /// <remarks>
    /// <b>The file has to actually appear for the routing assertions to be worth
    /// anything.</b> "The result carries the absolute path" is satisfied by a
    /// proxy that invented one; "the file is at the absolute path the result
    /// carries" is not, and it is the claim §F needs.
    /// </remarks>
    public int? WritesArtifactBytes { get; init; }
}

/// <summary>
/// A scriptable in-process stand-in for <c>@playwright/mcp</c>: canned
/// <c>tools/list</c>, programmable <c>tools/call</c> results, injectable
/// errors, delays, oversized payloads, unknown content types and mid-call
/// death.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never uses the SDK, and that is the load-bearing property.</b> Every
/// response is a literal string this class writes onto the wire, so a
/// difference observed at the caller is a difference the proxy introduced —
/// not one a second serialiser happened to make. A double built on
/// <c>McpServer</c> would re-escape its own output through the same encoder the
/// product replaced, and the passthrough tests it exists to support would be
/// asserting agreement between two copies of the same bug.
/// </para>
/// <para>
/// <b>It answers <c>server/discover</c> with <c>-32601</c>.</b> Two things are
/// going on and only one of them is the code. What matters is that it
/// <i>answers at all</i>: a peer that drops the unknown method costs the client
/// its whole <see cref="TestDefaults.DiscoverProbeTimeout"/> on every connect,
/// and the 2026-08-15 spike burned 30 s per rig on exactly that with no error
/// anywhere. Which code to answer with is then a fidelity question, and
/// <c>-32601</c> is the one measured from the real child on 2026-08-16.
/// BrowserAI's own end answers <c>-32602</c> — per-request metadata missing —
/// because the SDK implements <c>2026-07-28</c> and the child does not. A
/// double of the child that answered <c>-32602</c> would be doubling the proxy.
/// </para>
/// <para>
/// <b>It caps, it does not reject.</b> Handed a revision above
/// <see cref="TestDefaults.ChildProtocolCeiling"/> it returns the ceiling;
/// handed one below, it echoes what it was given. That is what the real child
/// was measured doing from both directions, and it is why a mis-negotiation
/// produces nothing to catch on the wire.
/// </para>
/// </remarks>
internal sealed class FakePlaywrightChild : IAsyncDisposable
{
    /// <summary>
    /// The canned <c>tools/list</c> result. <b>This is the double's payload,
    /// not a schema the product declares</b> — the scope rule that forbids
    /// hand-written tool schemas is about what BrowserAI ships, and what
    /// BrowserAI ships comes from the child at runtime. The real surface lives
    /// in <c>upstream-snapshots/tools-list.json</c>, and a test that needs it
    /// can point <see cref="ToolsListResult"/> there.
    /// </summary>
    private const string DefaultToolsList =
        """{"tools":[{"name":"browser_navigate","description":"Navigate to a URL","inputSchema":{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}},{"name":"browser_snapshot","description":"Capture an accessibility snapshot","inputSchema":{"type":"object","properties":{}}}]}""";

    private readonly FrameChannel _channel;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<string> _methods = [];
    private readonly List<string> _tools = [];
    private readonly ConcurrentBag<Task> _held = [];

    private Task _loop = Task.CompletedTask;
    private int _disposed;

    /// <summary>Wires the double onto one hop's server end.</summary>
    /// <param name="link">The hop whose server end this child occupies.</param>
    public FakePlaywrightChild(PipeDuplex link)
    {
        ArgumentNullException.ThrowIfNull(link);

        _channel = new FrameChannel(link.ServerReads, link.ServerWrites);
    }

    /// <summary>What <c>tools/list</c> returns, as literal JSON.</summary>
    public string ToolsListResult { get; set; } = DefaultToolsList;

    /// <summary>The highest revision this child will negotiate.</summary>
    public string ProtocolCeiling { get; set; } = TestDefaults.ChildProtocolCeiling;

    /// <summary>
    /// Whether <c>server/discover</c> is answered at all. Setting it false is
    /// how a test reproduces the probe stall rather than merely describing it.
    /// </summary>
    public bool AnswersDiscover { get; set; } = true;

    /// <summary>What each named tool does when it is called.</summary>
    public ConcurrentDictionary<string, FakeToolBehaviour> Tools { get; } = new(StringComparer.Ordinal);

    /// <summary>Every method this child has been asked for, in order.</summary>
    public IReadOnlyList<string> MethodsReceived
    {
        get
        {
            lock (_methods)
            {
                return [.. _methods];
            }
        }
    }

    /// <summary>
    /// Every tool this child has been asked to call, in order, whether or not it
    /// knew what to do with it.
    /// </summary>
    /// <remarks>
    /// <b>Recorded separately from <see cref="MethodsReceived"/> because the
    /// browser-idle timer's whole observable behaviour is a <i>tool name</i>.</b>
    /// Every idle close and every caller navigation is the same JSON-RPC method,
    /// so a test counting methods cannot tell them apart.
    /// </remarks>
    public IReadOnlyList<string> ToolCallsReceived
    {
        get
        {
            lock (_tools)
            {
                return [.. _tools];
            }
        }
    }

    /// <summary>Every frame this child received, exactly as it arrived.</summary>
    public IReadOnlyList<byte[]> FramesReceived
    {
        get
        {
            lock (_channel.Received)
            {
                return [.. _channel.Received];
            }
        }
    }

    /// <summary>Every frame this child sent, exactly as it went out.</summary>
    public IReadOnlyList<byte[]> FramesSent
    {
        get
        {
            lock (_channel.Sent)
            {
                return [.. _channel.Sent];
            }
        }
    }

    /// <summary>Whether the read loop has ended.</summary>
    public bool HasStopped => _loop.IsCompleted;

    /// <summary>Starts serving.</summary>
    public void Start() => _loop = Task.Run(RunAsync, CancellationToken.None);

    /// <summary>Pushes a frame the caller did not ask for.</summary>
    /// <param name="frame">The literal JSON to send.</param>
    /// <returns>A task that completes once the frame is on the wire.</returns>
    public Task SendRawAsync(string frame) => _channel.WriteFrameAsync(frame, _stopping.Token);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        await _stopping.CancelAsync();
        _channel.CloseOutput();

        try
        {
            // Held calls first: each is a task this object started, and a task
            // nobody waits for is a task whose exception nobody sees.
            await Task.WhenAll(_held).WaitAsync(TestDefaults.InProcessHang);
        }
#pragma warning disable CA1031 // A held call ending because the child stopped is the ordinary path.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        try
        {
            await _loop.WaitAsync(TestDefaults.InProcessHang);
        }
#pragma warning disable CA1031 // The loop's stream closing under a read in flight is how it normally ends.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        _channel.Dispose();
        _stopping.Dispose();
    }

    private static string IdOf(JsonNode? request) =>
        request?["id"]?.ToJsonString() ?? "null";

    private static string? ToolNameOf(JsonNode? request) =>
        request?["params"]?["name"]?.GetValue<string>();

    private static string Error(string id, int code, string message, string? rawData)
    {
        // A raw interpolated literal allows one fewer consecutive literal
        // closing brace than it has leading '$' characters, and these frames end
        // in two. Hence three rather than two, and {{{ }}} for every hole.
        var data = rawData is null ? "" : $$$""","data":{{{rawData}}}""";

        return $$$"""{"jsonrpc":"2.0","id":{{{id}}},"error":{"code":{{{code.ToString(CultureInfo.InvariantCulture)}}},"message":{{{JsonSerializer.Serialize(message)}}}{{{data}}}}}""";
    }

    private static string Result(string id, string rawResult) =>
        $$"""{"jsonrpc":"2.0","id":{{id}},"result":{{rawResult}}}""";

    private async Task RunAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            byte[]? frame;

            try
            {
                frame = await _channel.ReadFrameAsync(_stopping.Token);
            }
#pragma warning disable CA1031 // Cancellation and a closed pipe are both ordinary ends to this loop.
            catch (Exception)
#pragma warning restore CA1031
            {
                return;
            }

            if (frame is null)
            {
                return;
            }

            if (!await DispatchAsync(frame))
            {
                return;
            }
        }
    }

    /// <summary>Answers one frame. Returns false once the child has stopped.</summary>
    private async Task<bool> DispatchAsync(byte[] frame)
    {
        JsonNode? request;

        try
        {
            request = JsonNode.Parse(FrameChannel.TextOf(frame));
        }
        catch (JsonException)
        {
            // A double that dies on a malformed frame turns a product defect
            // into a harness hang, which is the harder failure to read.
            return true;
        }

        var method = request?["method"]?.GetValue<string>();

        if (method is null)
        {
            return true;
        }

        lock (_methods)
        {
            _methods.Add(method);
        }

        // A notification has no id and gets no answer, which includes
        // notifications/initialized and notifications/cancelled. Both are
        // recorded above, which is what step 9's cancellation relay asserts on.
        if (request?["id"] is null)
        {
            return true;
        }

        var id = IdOf(request);

        switch (method)
        {
            case "initialize":
                await _channel.WriteFrameAsync(Result(id, Initialize(request)), _stopping.Token);
                return true;

            case "server/discover":
                if (!AnswersDiscover)
                {
                    return true;
                }

                await _channel.WriteFrameAsync(Error(id, -32601, "Method not found", rawData: null), _stopping.Token);
                return true;

            case "ping":
                await _channel.WriteFrameAsync(Result(id, "{}"), _stopping.Token);
                return true;

            case "tools/list":
                await _channel.WriteFrameAsync(Result(id, ToolsListResult), _stopping.Token);
                return true;

            case "tools/call":
                return await CallToolAsync(id, request);

            default:
                await _channel.WriteFrameAsync(Error(id, -32601, $"Method not found: {method}", rawData: null), _stopping.Token);
                return true;
        }
    }

    private static string? ProgressTokenOf(JsonNode? request) =>
        request?["params"]?["_meta"]?["progressToken"]?.ToJsonString();

    private async Task<bool> CallToolAsync(string id, JsonNode? request)
    {
        var toolName = ToolNameOf(request);

        lock (_tools)
        {
            _tools.Add(toolName ?? "<none>");
        }

        if (toolName is null || !Tools.TryGetValue(toolName, out var behaviour))
        {
            await _channel.WriteFrameAsync(
                Error(id, -32602, $"Unknown tool: {toolName ?? "<none>"}. The fake child answers only tools a test programmed.", rawData: null),
                _stopping.Token);

            return true;
        }

        if (behaviour.HoldUntil is { } held)
        {
            // Off the read loop deliberately: the child stays able to hear
            // whatever arrives while the call is open.
            _held.Add(Task.Run(async () =>
            {
                try
                {
                    await held.WaitAsync(_stopping.Token);
                    _ = await AnswerAsync(id, request, behaviour);
                }
#pragma warning disable CA1031 // A held call that is never released ends with the child, which is the ordinary path here.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }));

            return true;
        }

        if (behaviour.Delay > TimeSpan.Zero)
        {
            await Task.Delay(behaviour.Delay, _stopping.Token);
        }

        return await AnswerAsync(id, request, behaviour);
    }

    private async Task<bool> AnswerAsync(string id, JsonNode? request, FakeToolBehaviour behaviour)
    {
        if (behaviour.DieWithoutAnswering)
        {
            // Not an error frame and not a close-then-answer: stdout simply
            // ends mid-call, which is what a killed node child looks like from
            // the parent's side.
            _channel.CloseOutput();
            return false;
        }

        // Echoing the caller's own token rather than inventing one: a relay that
        // rewrote it would still look right in a test that only counted
        // notifications.
        if (behaviour.ProgressUpdates > 0 && ProgressTokenOf(request) is { } token)
        {
            for (var step = 1; step <= behaviour.ProgressUpdates; step++)
            {
                await _channel.WriteFrameAsync(
                    $$$"""{"jsonrpc":"2.0","method":"notifications/progress","params":{"progressToken":{{{token}}},"progress":{{{step.ToString(CultureInfo.InvariantCulture)}}},"total":{{{behaviour.ProgressUpdates.ToString(CultureInfo.InvariantCulture)}}}}}""",
                    _stopping.Token);
            }
        }

        if (behaviour.WritesArtifactBytes is { } size
            && request?["params"]?["arguments"]?["filename"]?.GetValue<string>() is { } artifact)
        {
            // The path arrives absolute because BrowserAI made it so. Written
            // before the answer, which is the order upstream writes in.
            await File.WriteAllBytesAsync(artifact, new byte[size], _stopping.Token);
        }

        var frame = behaviour.ErrorCode is { } code
            ? Error(id, code, behaviour.ErrorMessage, behaviour.RawErrorData)
            : Result(id, behaviour.RawResult ?? """{"content":[{"type":"text","text":"ok"}]}""");

        await _channel.WriteFrameAsync(frame, _stopping.Token);
        return true;
    }

    private string Initialize(JsonNode? request)
    {
        var requested = request?["params"]?["protocolVersion"]?.GetValue<string>() ?? ProtocolCeiling;

        // Caps or echoes, never rejects. The revisions are ISO dates, so an
        // ordinal comparison is a chronological one.
        var negotiated = string.CompareOrdinal(requested, ProtocolCeiling) > 0 ? ProtocolCeiling : requested;

        return $$$"""{"protocolVersion":{{{JsonSerializer.Serialize(negotiated)}}},"capabilities":{"tools":{"listChanged":true},"logging":{}},"serverInfo":{"name":"fake-playwright-mcp","version":"0.0.0-fake"}}""";
    }
}
