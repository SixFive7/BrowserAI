// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserAI.Tests.Harness;

/// <summary>One response, both as bytes and as a parsed envelope.</summary>
/// <param name="Frame">The response frame exactly as it arrived, terminator removed.</param>
/// <param name="Envelope">The same frame parsed, for the assertions that are about meaning.</param>
internal readonly record struct RawResponse(byte[] Frame, JsonObject Envelope)
{
    /// <summary>The response's <c>result</c>, or null when it carried an error.</summary>
    public JsonObject? Result => Envelope["result"]?.AsObject();

    /// <summary>The response's <c>error</c>, or null when it succeeded.</summary>
    public JsonObject? Error => Envelope["error"]?.AsObject();
}

/// <summary>
/// A hand-written JSON-RPC client over a pipe hop: the caller-side oracle for
/// the in-process layer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside <see cref="RawStdioClient"/> rather than instead
/// of it.</b> Both are hand-written and neither touches a product or SDK
/// protocol type, which is the property that makes either an oracle at all —
/// with both of BrowserAI's transports replaced, a test driven through an
/// <c>McpClient</c> is testing the code under test using the code under test.
/// They differ in the two things this layer needs and that one cannot give:
/// this one speaks over a <b>stream pair</b> rather than starting a process, so
/// no test here needs Node or a published binary; and it keeps every response's
/// <b>raw bytes</b>, because
/// <see href="../../../plan/build-order.md">step 9</see> asserts byte-identity
/// on the exact span of <c>result</c> and a client that hands back only a
/// parsed object has already thrown the evidence away.
/// </para>
/// <para>
/// It correlates by <c>id</c> and skips anything else on the stream. That is
/// not defensive coding: the naive "the next frame is the answer" version works
/// on a quiet machine and fails the first time a notification or a log message
/// arrives between the request and its reply.
/// </para>
/// </remarks>
internal sealed class RawPipeClient : IAsyncDisposable
{
    private readonly FrameChannel _channel;
    private readonly CancellationTokenSource _deadline;

    private int _nextId;
    private int _disposed;

    /// <summary>Speaks over the client end of a hop.</summary>
    /// <param name="link">The hop whose client end this occupies.</param>
    public RawPipeClient(PipeDuplex link)
    {
        ArgumentNullException.ThrowIfNull(link);

        _channel = new FrameChannel(link.ClientReads, link.ClientWrites);
        _deadline = new CancellationTokenSource(TestDefaults.Patience);
    }

    /// <summary>Every frame this client received, exactly as it arrived.</summary>
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

    /// <summary>Performs the <c>initialize</c> handshake.</summary>
    /// <param name="protocolVersion">The revision to offer.</param>
    /// <returns>The <c>initialize</c> result.</returns>
    public async Task<JsonObject> InitializeAsync(string protocolVersion)
    {
        var result = await RoundTripAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "BrowserAI.RawPipeClient", ["version"] = "1" },
        });

        await NotifyAsync("notifications/initialized");
        return result;
    }

    /// <summary>Sends a request and returns its <c>result</c>.</summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">Its parameters, or null for none.</param>
    /// <returns>The response's <c>result</c>.</returns>
    /// <exception cref="InvalidOperationException">The peer answered with an error.</exception>
    public async Task<JsonObject> RoundTripAsync(string method, JsonNode? parameters = null)
    {
        var response = await SendAsync(method, parameters);

        if (response.Error is { } error)
        {
            throw new InvalidOperationException($"'{method}' returned a JSON-RPC error: {error.ToJsonString()}");
        }

        return response.Result
            ?? throw new InvalidOperationException($"'{method}' returned neither a result nor an error: {response.Envelope.ToJsonString()}");
    }

    /// <summary>
    /// Sends a request and returns the whole response, so a test can assert on
    /// a JSON-RPC error without it being thrown.
    /// </summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">Its parameters, or null for none.</param>
    /// <returns>The response, as bytes and as an envelope.</returns>
    public async Task<RawResponse> SendAsync(string method, JsonNode? parameters = null) =>
        await AwaitAsync(await BeginAsync(method, parameters), method);

    /// <summary>
    /// Sends a request and returns its id <b>without</b> waiting for the
    /// answer.
    /// </summary>
    /// <remarks>
    /// The two-phase shape exists for cancellation: a test has to get a call
    /// in flight, cancel it, and then assert on what the far end saw — and a
    /// cancelled call is one nothing is ever going to answer, so a version that
    /// waits first can only be written as a timeout.
    /// </remarks>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">Its parameters, or null for none.</param>
    /// <returns>The id the request went out with.</returns>
    public async Task<int> BeginAsync(string method, JsonNode? parameters = null)
    {
        var id = Interlocked.Increment(ref _nextId);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };

        if (parameters is not null)
        {
            request["params"] = parameters;
        }

        await _channel.WriteFrameAsync(request.ToJsonString(), _deadline.Token);
        return id;
    }

    /// <summary>Waits for the answer to a request <see cref="BeginAsync"/> sent.</summary>
    /// <param name="id">The id that call returned.</param>
    /// <param name="method">The method, used only in failure messages.</param>
    /// <returns>The response, as bytes and as an envelope.</returns>
    public async Task<RawResponse> AwaitAsync(int id, string method = "<unnamed>")
    {
        while (true)
        {
            var frame = await _channel.ReadFrameAsync(_deadline.Token)
                ?? throw new InvalidOperationException(
                    $"The peer closed its end before answering '{method}' (id {id}).");

            if (frame.Length is 0)
            {
                continue;
            }

            JsonObject envelope;

            try
            {
                envelope = JsonNode.Parse(FrameChannel.TextOf(frame))?.AsObject()
                    ?? throw new JsonException("the frame parsed as JSON null");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"The peer sent a frame that is not a JSON object: {ex.Message}. Frame: {Preview(frame)}");
            }

            if (envelope["id"] is not { } received || (int?)received != id)
            {
                continue;
            }

            return new RawResponse(frame, envelope);
        }
    }

    /// <summary>
    /// Keeps reading frames until a condition holds, recording each one.
    /// </summary>
    /// <remarks>
    /// A client only sees what it reads. Notifications that arrive after the
    /// response to the request that provoked them sit in the pipe until
    /// something drains it, so a test asserting on
    /// <see cref="FramesReceived"/> without this is asserting on whatever the
    /// last round trip happened to consume — which is a race, and it passes on
    /// a quiet machine.
    /// </remarks>
    /// <param name="condition">Evaluated after every frame.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    public async Task ReadUntilAsync(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        while (!condition())
        {
            _ = await _channel.ReadFrameAsync(_deadline.Token)
                ?? throw new InvalidOperationException("The peer closed its end before the expected frames arrived.");
        }
    }

    /// <summary>
    /// Writes a literal frame, which is how a test sends something no encoder
    /// would produce.
    /// </summary>
    /// <param name="frame">The exact bytes of the frame, terminator excluded.</param>
    /// <returns>A task that completes once the frame is on the wire.</returns>
    public Task SendRawAsync(string frame) => _channel.WriteFrameAsync(frame, _deadline.Token);

    /// <summary>Sends a notification, which has no reply.</summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">Its parameters, or null for none.</param>
    /// <returns>A task that completes once the frame is on the wire.</returns>
    public async Task NotifyAsync(string method, JsonNode? parameters = null)
    {
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
        };

        if (parameters is not null)
        {
            notification["params"] = parameters;
        }

        await _channel.WriteFrameAsync(notification.ToJsonString(), _deadline.Token);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return ValueTask.CompletedTask;
        }

        _channel.Dispose();
        _deadline.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string Preview(byte[] frame)
    {
        var text = FrameChannel.TextOf(frame);
        return text.Length <= 400 ? text : text[..400] + "…";
    }
}
