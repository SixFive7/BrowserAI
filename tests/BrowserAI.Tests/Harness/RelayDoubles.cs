// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Relay;

namespace BrowserAI.Tests.Harness;

/// <summary>One frame as it arrived: its bytes, and the object they parse to.</summary>
/// <param name="bytes">The frame, without its newline.</param>
internal sealed class WireFrame(byte[] bytes)
{
    /// <summary>The frame exactly as it arrived.</summary>
    public byte[] Bytes { get; } = bytes;

    /// <summary>The frame parsed.</summary>
    public JsonObject Json { get; } = JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidOperationException("The frame is JSON null.");

    /// <summary>The frame's text, for messages and for comparing with what was sent.</summary>
    public string Text => FrameChannel.TextOf(Bytes);

    /// <summary>Its method, when it has one.</summary>
    public string? Method => Json["method"] is JsonValue method && method.TryGetValue<string>(out var text) ? text : null;

    /// <summary>Its id when the id is a string; its JSON text when it is a number.</summary>
    public string? IdText => Json["id"] switch
    {
        JsonValue id when id.TryGetValue<string>(out var text) => text,
        JsonValue id => id.ToJsonString(),
        _ => null,
    };

    /// <summary>Its <c>params</c>.</summary>
    public JsonObject? Params => Json["params"] as JsonObject;

    /// <summary>Its <c>result</c>.</summary>
    public JsonObject? Result => Json["result"] as JsonObject;

    /// <summary>Its <c>error</c>.</summary>
    public JsonObject? Error => Json["error"] as JsonObject;

    /// <summary>Whether it is a tool result with <c>isError</c> set.</summary>
    public bool IsToolError => Result?["isError"] is JsonValue flag && flag.TryGetValue<bool>(out var set) && set;

    /// <summary>The text of a tool result's blocks, joined.</summary>
    public string ToolText => string.Concat(
        (Result?["content"] as JsonArray ?? [])
            .Select(block => block?["text"] is JsonValue text && text.TryGetValue<string>(out var value) ? value : string.Empty));

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// The finder the engine looks through: it hands over a background the arm offered,
/// and answers <see cref="IBackgroundFinder.Explain"/> with whatever the arm set.
/// </summary>
internal sealed class FakeBackgroundFinder : IBackgroundFinder, IDisposable
{
    /// <summary>The task name every arm's absences carry.</summary>
    public const string TaskName = "BrowserAI sign-in relay-tests";

    private readonly Lock _gate = new();
    private readonly Queue<PipeDuplex> _offered = new();
    private readonly List<FakeBackground> _backgrounds = [];
    private readonly List<int?> _explained = [];

    private BackgroundAbsence _absence = new BackgroundAbsence.NotRunning(TaskState.Ready, TaskName, null);
    private int _looks;
    private bool _failsToLook;
    private bool _failsToExplain;

    /// <summary>What <see cref="Explain"/> answers.</summary>
    public BackgroundAbsence Absence
    {
        get
        {
            lock (_gate)
            {
                return _absence;
            }
        }

        set
        {
            lock (_gate)
            {
                _absence = value;
            }
        }
    }

    /// <summary>Whether a look throws, as a finder whose pipe call failed in a way nobody foresaw does.</summary>
    public bool FailsToLook
    {
        get
        {
            lock (_gate)
            {
                return _failsToLook;
            }
        }

        set
        {
            lock (_gate)
            {
                _failsToLook = value;
            }
        }
    }

    /// <summary>Whether <see cref="Explain"/> throws, as a finder that could not read the Task Scheduler does.</summary>
    public bool FailsToExplain
    {
        get
        {
            lock (_gate)
            {
                return _failsToExplain;
            }
        }

        set
        {
            lock (_gate)
            {
                _failsToExplain = value;
            }
        }
    }

    /// <summary>How many times the engine has looked for the pipe.</summary>
    public int Looks
    {
        get
        {
            lock (_gate)
            {
                return _looks;
            }
        }
    }

    /// <summary>The last background pid the engine gave each time it asked why there is no background.</summary>
    public IReadOnlyList<int?> Explained
    {
        get
        {
            lock (_gate)
            {
                return [.. _explained];
            }
        }
    }

    /// <summary>Offers a background, which the engine's next look connects to.</summary>
    /// <returns>The background's end.</returns>
    public FakeBackground Offer()
    {
        var hop = new PipeDuplex("background hop (relay and background)");
        var background = new FakeBackground(hop);

        lock (_gate)
        {
            _offered.Enqueue(hop);
            _backgrounds.Add(background);
        }

        return background;
    }

    /// <inheritdoc />
    public Task<Stream?> TryConnectAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _looks++;

            if (_failsToLook)
            {
                throw new IOException("The suite's finder failed to open the pipe.");
            }

            return Task.FromResult<Stream?>(_offered.TryDequeue(out var hop) ? DuplexStream.Over(hop) : null);
        }
    }

    /// <inheritdoc />
    public BackgroundAbsence Explain(int? lastBackgroundPid)
    {
        lock (_gate)
        {
            _explained.Add(lastBackgroundPid);

            return _failsToExplain
                ? throw new InvalidOperationException("The suite's finder could not read the Task Scheduler.")
                : _absence;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var background in _backgrounds)
            {
                background.Dispose();
            }

            _backgrounds.Clear();
        }
    }
}

/// <summary>The background's end of one connection, driven by the arm.</summary>
/// <param name="hop">The connection.</param>
internal sealed class FakeBackground(PipeDuplex hop) : IDisposable
{
    /// <summary>The pid a background answers the greeting with unless an arm says otherwise.</summary>
    public const int Pid = 777;

    /// <summary>The frames the relay wrote, and the background's own writes.</summary>
    public FrameChannel Channel { get; } = new(hop.ServerReads, hop.ServerWrites);

    /// <summary>Reads the next frame the relay wrote.</summary>
    /// <returns>The frame.</returns>
    public async Task<WireFrame> NextAsync()
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        var frame = await Channel.ReadFrameAsync(hang.Token);

        return frame is null
            ? throw new InvalidOperationException("The relay closed this background's pipe with no further frame.")
            : new WireFrame(frame);
    }

    /// <summary>Whether the relay has closed the pipe with nothing more written.</summary>
    /// <returns><see langword="true"/> when the next read is the end of the pipe.</returns>
    public async Task<bool> ClosedAsync()
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        return await Channel.ReadFrameAsync(hang.Token) is null;
    }

    /// <summary>Writes one frame as the background.</summary>
    /// <param name="json">The frame.</param>
    /// <returns>The write.</returns>
    public async Task SendAsync(string json)
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        await Channel.WriteFrameAsync(json, hang.Token);
    }

    /// <summary>
    /// Answers the relay's greeting and its replayed <c>initialize</c>, and reads its
    /// <c>notifications/initialized</c>.
    /// </summary>
    /// <param name="pid">The pid to answer with.</param>
    /// <returns>The greeting.</returns>
    public async Task<WireFrame> GreetAsync(int pid = Pid)
    {
        var hello = await NextAsync();
        await AnswerGreetingAsync(hello, pid);
        return hello;
    }

    /// <summary>
    /// Answers a greeting already read, then the replayed <c>initialize</c>, and reads
    /// <c>notifications/initialized</c>.
    /// </summary>
    /// <param name="hello">The greeting.</param>
    /// <param name="pid">The pid to answer with.</param>
    /// <returns>The task.</returns>
    public async Task AnswerGreetingAsync(WireFrame hello, int pid = Pid)
    {
        ArgumentNullException.ThrowIfNull(hello);

        await SendAsync(Frames.HelloAnswer(hello.IdText!, pid));

        var replay = await NextAsync();
        await SendAsync(Frames.ReplayAnswer(replay.IdText!));

        var initialized = await NextAsync();

        if (initialized.Method is not "notifications/initialized")
        {
            throw new InvalidOperationException($"The relay sent {initialized.Text} where notifications/initialized was due.");
        }
    }

    /// <summary>Closes the background's end, as a background that exits does.</summary>
    public void GoAway() => Channel.CloseOutput();

    /// <inheritdoc />
    public void Dispose()
    {
        Channel.CloseOutput();
        Channel.Dispose();
    }
}

/// <summary>
/// The relay's end of a background hop, as the one duplex stream a named pipe is,
/// whose disposal ends a read in progress as closing a pipe handle does.
/// </summary>
internal sealed class DuplexStream : Stream
{
    private readonly Stream _reads;
    private readonly Stream _writes;
    private readonly CancellationTokenSource _closing = new();
    private readonly CancellationToken _closed;
    private int _disposed;

    private DuplexStream(Stream reads, Stream writes)
    {
        _reads = reads;
        _writes = writes;
        _closed = _closing.Token;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>The relay's end of a hop.</summary>
    /// <param name="hop">The hop.</param>
    /// <returns>The stream.</returns>
    public static DuplexStream Over(PipeDuplex hop) => new(hop.ClientReads, hop.ClientWrites);

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) is not 0)
        {
            return 0;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed);

        try
        {
            return await _reads.ReadAsync(buffer, linked.Token);
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
            // Closed under the read: the end of the stream, as a closed handle is.
            return 0;
        }
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("The relay reads asynchronously.");

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _writes.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _writes.WriteAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => _writes.Write(buffer, offset, count);

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _writes.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override void Flush() => _writes.Flush();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) is 0)
        {
            _closing.Cancel();
            _writes.Dispose();
            _reads.Dispose();
            _closing.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A stream whose writes wait at a gate the arm closes and opens, so that an arm can
/// hold the relay's loop inside one write and queue frames behind it.
/// </summary>
/// <param name="inner">Where writes go once the gate is open.</param>
internal sealed class GatedStream(Stream inner) : Stream
{
    private readonly Lock _gate = new();
    private TaskCompletionSource? _closed;
    private TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _broken;

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Completes when a write is waiting at the closed gate.</summary>
    public Task Waiting
    {
        get
        {
            lock (_gate)
            {
                return _waiting.Task;
            }
        }
    }

    /// <summary>Closes the gate: the next write waits until <see cref="Open"/>.</summary>
    public void Hold()
    {
        lock (_gate)
        {
            _closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Breaks the stream: every write from here on fails, as one into a pipe whose reader has gone does.</summary>
    public void Break()
    {
        lock (_gate)
        {
            _broken = true;
        }
    }

    /// <summary>Opens the gate.</summary>
    public void Open()
    {
        TaskCompletionSource? closed;

        lock (_gate)
        {
            closed = _closed;
            _closed = null;
        }

        _ = closed?.TrySetResult();
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Task? wait = null;

        lock (_gate)
        {
            if (_broken)
            {
                throw new IOException("The pipe is being closed.");
            }

            if (_closed is { } closed)
            {
                wait = closed.Task;
                _ = _waiting.TrySetResult();
            }
        }

        if (wait is not null)
        {
            await wait.WaitAsync(cancellationToken);
        }

        await inner.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("The relay writes asynchronously.");

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override void Flush() => inner.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>The frames a background answers the relay with, built as JSON and not typed by hand.</summary>
internal static class Frames
{
    /// <summary>A result.</summary>
    /// <param name="id">The request's id, a string.</param>
    /// <param name="result">The result.</param>
    /// <returns>The frame.</returns>
    public static string Result(string id, JsonNode result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    /// <summary>An empty result, as a probe is answered.</summary>
    /// <param name="id">The request's id, a string.</param>
    /// <returns>The frame.</returns>
    public static string Empty(string id) => Result(id, new JsonObject());

    /// <summary>A background's acceptance of the relay's greeting.</summary>
    /// <param name="id">The greeting's id.</param>
    /// <param name="pid">The pid the background gives.</param>
    /// <returns>The frame.</returns>
    public static string HelloAnswer(string id, int pid) =>
        Result(id, new JsonObject { ["build"] = RelayRig.Facts.Build, ["pid"] = pid });

    /// <summary>A background's answer to the client's replayed <c>initialize</c>.</summary>
    /// <param name="id">The replay's id.</param>
    /// <returns>The frame.</returns>
    public static string ReplayAnswer(string id) => Result(id, new JsonObject
    {
        ["protocolVersion"] = TestDefaults.CallerProtocolVersion,
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
        ["serverInfo"] = new JsonObject { ["name"] = "BrowserAI", ["version"] = RelayRig.Facts.Build },
    });

    /// <summary>A background's refusal of the relay's greeting.</summary>
    /// <param name="id">The greeting's id.</param>
    /// <param name="sentence">The refusal's sentence.</param>
    /// <param name="refusal">Its kind.</param>
    /// <returns>The frame.</returns>
    public static string Refusal(string id, string sentence, string refusal) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject
        {
            ["code"] = -32000,
            ["message"] = sentence,
            ["data"] = new JsonObject { ["refusal"] = refusal },
        },
    }.ToJsonString();

    /// <summary>The background's question to the relay.</summary>
    /// <param name="id">The question's id.</param>
    /// <returns>The frame.</returns>
    public static string ReadyToEnd(string id) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = "browserai/ready-to-end" }.ToJsonString();
}

/// <summary>Assertions on JSON the suite compares by meaning.</summary>
internal static class Json
{
    /// <summary>Whether two nodes are the same JSON.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    public static bool Same(JsonNode? left, JsonNode? right) => JsonNode.DeepEquals(left, right);

    /// <summary>A string member, or <see langword="null"/>.</summary>
    /// <param name="node">The object.</param>
    /// <param name="name">The member.</param>
    /// <returns>Its value when it is a string.</returns>
    public static string? Text(JsonNode? node, string name) =>
        node is JsonObject owner && owner[name] is JsonValue value && value.GetValueKind() is JsonValueKind.String
            ? value.GetValue<string>()
            : null;
}
