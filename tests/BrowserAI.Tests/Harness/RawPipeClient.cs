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
/// <b>raw bytes</b>, because <see cref="LosslessPassthroughTests"/> asserts
/// byte-identity on the exact span of <c>result</c> — found by
/// <c>Utf8JsonReader</c> token offset, never by re-serialising and comparing —
/// and a client that hands back only a parsed object has already thrown the
/// evidence away.
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

    private int _nextId;
    private int _disposed;

    /// <summary>Speaks over the client end of a hop.</summary>
    /// <param name="link">The hop whose client end this occupies.</param>
    public RawPipeClient(PipeDuplex link)
    {
        ArgumentNullException.ThrowIfNull(link);

        _channel = new FrameChannel(link.ClientReads, link.ClientWrites);
    }

    /// <summary>
    /// A fresh deadline for <b>one</b> frame, which is what
    /// <see cref="TestDefaults.Patience"/> has always claimed to be.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-08-17 (previously one <see cref="CancellationTokenSource"/>
    /// armed in the constructor and handed to every read and write for the life
    /// of the client).</b> That made it a whole-conversation budget wearing a
    /// per-exchange name: a test doing forty prompt round trips over
    /// thirty-one seconds died on the fortieth, and died as a bare
    /// <c>OperationCanceledException: The operation was canceled.</c> from
    /// somewhere inside <c>System.IO.Pipelines</c> — no method, no id, no
    /// elapsed time. Two real-browser tests hit it under full parallelism
    /// because most of their thirty seconds is spent legitimately waiting for a
    /// browser rather than for this pipe.
    /// <para>
    /// Per frame is also the stronger hang detector, not the weaker one: a peer
    /// that has genuinely stopped sends nothing at all, and thirty seconds of
    /// silence still fails — now with the operation named.
    /// <see cref="RawStdioClient"/> keeps the whole-conversation shape
    /// deliberately, because there the peer is a process that may never start;
    /// here both ends are in this process.
    /// </para>
    /// </remarks>
    /// <returns>A source the caller must dispose.</returns>
    private static CancellationTokenSource OneFrame() => new(TestDefaults.Patience);

    /// <summary>Reads one frame, or says which operation went unanswered.</summary>
    /// <param name="what">What the caller was waiting for, for the failure message.</param>
    /// <returns>The frame, or <see langword="null"/> if the peer closed its end.</returns>
    private async Task<byte[]?> ReadOneAsync(string what)
    {
        using var deadline = OneFrame();

        try
        {
            return await _channel.ReadFrameAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No frame arrived on this pipe within {TestDefaults.Patience.TotalSeconds:F0} s while waiting for {what}. The peer is in this process, so this is a deadlock or a dropped write rather than a slow machine.");
        }
    }

    /// <summary>Writes one frame, or says which operation could not be sent.</summary>
    /// <param name="frame">The frame's text.</param>
    /// <param name="what">What was being sent, for the failure message.</param>
    private async Task WriteOneAsync(string frame, string what)
    {
        using var deadline = OneFrame();

        try
        {
            await _channel.WriteFrameAsync(frame, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"This pipe would not accept the frame for {what} within {TestDefaults.Patience.TotalSeconds:F0} s. Nothing is draining the client's end.");
        }
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

        await WriteOneAsync(request.ToJsonString(), $"the '{method}' request (id {id})");
        return id;
    }

    /// <summary>
    /// Puts every request on the wire before reading any answer, and returns the
    /// answers <b>in the order they arrived</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the only way to get more than one request outstanding at the
    /// server from a single client.</b> <see cref="AwaitAsync"/> discards frames
    /// whose id it is not waiting for, so two overlapping
    /// <see cref="SendAsync"/> calls would each throw the other's answer away;
    /// the demultiplexing has to happen in one place, which is here.
    /// </para>
    /// <para>
    /// What it establishes is exactly one thing, stated plainly because a
    /// concurrency test that over-claims is worse than none: every request in the
    /// list was written before the first answer was read, so all of them were
    /// outstanding at the server together. Whether the server then fans them out
    /// is the server's business, and the <b>arrival order</b> this returns is
    /// what lets a caller say so rather than assume it.
    /// </para>
    /// </remarks>
    /// <param name="requests">The requests, in the order they go out.</param>
    /// <returns>The answers, in arrival order, each with the id it answers.</returns>
    public async Task<IReadOnlyList<(int Id, JsonObject Envelope)>> RoundTripManyAsync(
        IReadOnlyList<(string Method, JsonNode? Parameters)> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var outstanding = new HashSet<int>();

        foreach (var (method, parameters) in requests)
        {
            _ = outstanding.Add(await BeginAsync(method, parameters));
        }

        var answers = new List<(int, JsonObject)>(requests.Count);

        while (outstanding.Count is not 0)
        {
            var frame = await ReadOneAsync($"{outstanding.Count} of {requests.Count} outstanding answers")
                ?? throw new InvalidOperationException(
                    $"The peer closed its end with {outstanding.Count} of {requests.Count} answers still owed.");

            if (frame.Length is 0)
            {
                continue;
            }

            var envelope = JsonNode.Parse(FrameChannel.TextOf(frame))?.AsObject();

            if (envelope?["id"] is not { } received
                || (int?)received is not { } id
                || !outstanding.Remove(id))
            {
                // A notification, or an answer to something else. Skipped rather
                // than treated as a failure: the relay puts progress frames on
                // this pipe too.
                continue;
            }

            answers.Add((id, envelope));
        }

        return answers;
    }

    /// <summary>Waits for the answer to a request <see cref="BeginAsync"/> sent.</summary>
    /// <param name="id">The id that call returned.</param>
    /// <param name="method">The method, used only in failure messages.</param>
    /// <returns>The response, as bytes and as an envelope.</returns>
    public async Task<RawResponse> AwaitAsync(int id, string method = "<unnamed>")
    {
        while (true)
        {
            var frame = await ReadOneAsync($"the answer to '{method}' (id {id})")
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
            _ = await ReadOneAsync("the frames a caller is draining for")
                ?? throw new InvalidOperationException("The peer closed its end before the expected frames arrived.");
        }
    }

    /// <summary>
    /// Writes a literal frame, which is how a test sends something no encoder
    /// would produce.
    /// </summary>
    /// <param name="frame">The exact bytes of the frame, terminator excluded.</param>
    /// <returns>A task that completes once the frame is on the wire.</returns>
    public Task SendRawAsync(string frame) => WriteOneAsync(frame, "a literal frame");

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

        await WriteOneAsync(notification.ToJsonString(), $"the '{method}' notification");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return ValueTask.CompletedTask;
        }

        _channel.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string Preview(byte[] frame)
    {
        var text = FrameChannel.TextOf(frame);
        return text.Length <= 400 ? text : text[..400] + "…";
    }
}
