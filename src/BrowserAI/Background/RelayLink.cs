// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Coordination;
using BrowserAI.Protocol;
using BrowserAI.Relay;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Background;

/// <summary>
/// The background's end of one relay's connection, as the MCP server sees it: every
/// frame of the client's passes through, and every message BrowserAI's own processes
/// exchange is taken out on the way.
/// </summary>
/// <remarks>
/// <para>
/// <b>The relay forwards its client's frames byte for byte</b> (S a), so what reaches
/// this transport is the client's own traffic, framed as MCP's stdio transport frames
/// it, plus what the relay says for itself under methods that begin
/// <see cref="BackgroundPipe.MethodPrefix"/>. Those never reach the MCP server: an
/// activity report and a withdrawn yes are read here, and an answer to a question the
/// background asked completes that question.
/// </para>
/// <para>
/// <b>It counts the calls in flight</b>, because a relay with a call in flight holds an
/// update (H1): a <c>tools/call</c> request is counted when it arrives and uncounted
/// when its answer goes out, or when the client cancels it, since MCP has a cancelled
/// request go unanswered.
/// </para>
/// </remarks>
internal sealed class RelayLink : TransportBase
{
    /// <summary>What every id of a request the background puts on the pipe begins with.</summary>
    public const string OwnIdPrefix = BackgroundPipe.IdPrefix + "background-";

    private readonly PipeServerTransport _inner;
    private readonly ConcurrentDictionary<RequestId, byte> _calls = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcMessage>> _asked = new(StringComparer.Ordinal);
    private readonly Action<RelayLink, RelayNotice> _notice;
    private long _nextId;
    private int _disposed;

    /// <summary>Starts passing the relay's frames on.</summary>
    /// <param name="inner">The pipe's transport. Owned from here.</param>
    /// <param name="name">The connection's name, for the log.</param>
    /// <param name="notice">What to do when the relay reports activity or withdraws a yes, or a call starts or ends; called on the pump's thread.</param>
    /// <param name="loggerFactory">Where the transport logs.</param>
    public RelayLink(PipeServerTransport inner, string name, Action<RelayLink, RelayNotice> notice, ILoggerFactory loggerFactory)
        : base(name, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(notice);

        _inner = inner;
        _notice = notice;
        SetConnected();
        Closed = Task.Run(PumpAsync, CancellationToken.None);
    }

    /// <summary>Whether a call of the client's is in flight.</summary>
    public bool CallInFlight => !_calls.IsEmpty;

    /// <summary>The pump's end: the relay's connection closed.</summary>
    public Task Closed { get; }

    /// <inheritdoc />
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message is JsonRpcMessageWithId { Id: var id } and (JsonRpcResponse or JsonRpcError)
            && _calls.TryRemove(id, out _))
        {
            _notice(this, RelayNotice.CallsChanged);
        }

        await _inner.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asks the relay something of BrowserAI's own and waits for its answer.</summary>
    /// <param name="method">The method, one of <see cref="BackgroundPipe"/>'s.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="cancellationToken">Ends the wait; nothing is sent when it has already fired.</param>
    /// <returns>The relay's answer: a response or an error.</returns>
    public async Task<JsonRpcMessage> AskAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = OwnIdPrefix + Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
        var answer = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        _asked[id] = answer;

        try
        {
            await _inner.SendMessageAsync(
                new JsonRpcRequest { Id = new RequestId(id), Method = method, Params = parameters },
                cancellationToken).ConfigureAwait(false);

            return await answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _asked.TryRemove(id, out _);
        }
    }

    /// <summary>Tells the relay something of BrowserAI's own, expecting no answer.</summary>
    /// <param name="method">The method, one of <see cref="BackgroundPipe"/>'s.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="cancellationToken">Ends the send.</param>
    /// <returns>A task that completes once the frame is written.</returns>
    public Task TellAsync(string method, JsonNode? parameters, CancellationToken cancellationToken) =>
        _inner.SendMessageAsync(new JsonRpcNotification { Method = method, Params = parameters }, cancellationToken);

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        await _inner.DisposeAsync().ConfigureAwait(false);

        try
        {
            await Closed.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The pump reports its own end; disposal only waits for it.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        foreach (var waiting in _asked.Values)
        {
            _ = waiting.TrySetCanceled();
        }

        SetDisconnected();
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var message in _inner.MessageReader.ReadAllAsync().ConfigureAwait(false))
            {
                if (Take(message))
                {
                    continue;
                }

                await WriteMessageAsync(message).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var waiting in _asked.Values)
            {
                _ = waiting.TrySetCanceled();
            }

            if (!_calls.IsEmpty)
            {
                _calls.Clear();
                _notice(this, RelayNotice.CallsChanged);
            }

            // Ends the MCP server's read loop: the relay has gone.
            SetDisconnected();
        }
    }

    /// <summary>Takes a message that is BrowserAI's own out of the stream, and counts a client's call.</summary>
    /// <param name="message">The message.</param>
    /// <returns>Whether it was taken, and goes no further.</returns>
    private bool Take(JsonRpcMessage message)
    {
        switch (message)
        {
            case JsonRpcResponse or JsonRpcError when message is JsonRpcMessageWithId { Id.Id: string id }
                && id.StartsWith(OwnIdPrefix, StringComparison.Ordinal):
                if (_asked.TryRemove(id, out var waiting))
                {
                    _ = waiting.TrySetResult(message);
                }

                return true;

            case JsonRpcNotification { Method: RelayProtocol.Activity } activity:
                Report(RelayNotice.Activity(IdleAtIn(activity.Params)));
                return true;

            case JsonRpcNotification { Method: RelayProtocol.Withdraw } withdraw:
                Report(RelayNotice.Withdrew(IdleAtIn(withdraw.Params)));
                return true;

            case JsonRpcNotification { Method: var method } when BackgroundPipe.IsOurs(method):
                // Nothing else of the relay's is a notification the background reads.
                return true;

            case JsonRpcRequest { Method: var method } request when BackgroundPipe.IsOurs(method):
                _ = _inner.SendMessageAsync(
                    new JsonRpcError
                    {
                        Id = request.Id,
                        Error = new JsonRpcErrorDetail { Code = (int)McpErrorCode.MethodNotFound, Message = $"The background does not serve '{method}' on a relay's connection." },
                    });
                return true;

            case JsonRpcRequest { Method: RequestMethods.ToolsCall } call:
                if (_calls.TryAdd(call.Id, 0))
                {
                    _notice(this, RelayNotice.CallsChanged);
                }

                return false;

            case JsonRpcNotification { Method: NotificationMethods.CancelledNotification } cancelled
                when RequestIdIn(cancelled.Params) is { } cancelledId && _calls.TryRemove(cancelledId, out _):
                _notice(this, RelayNotice.CallsChanged);
                return false;

            default:
                return false;
        }
    }

    private void Report(RelayNotice notice) => _notice(this, notice);

    private static DateTimeOffset? IdleAtIn(JsonNode? parameters) =>
        parameters?["idleAt"] is JsonValue value
        && value.TryGetValue<string>(out var text)
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;

    private static RequestId? RequestIdIn(JsonNode? parameters) =>
        parameters?["requestId"] is JsonValue value
            ? value.TryGetValue<string>(out var text) ? new RequestId(text)
            : value.TryGetValue<long>(out var number) ? new RequestId(number)
            : null
            : null;
}

/// <summary>What a relay's connection told the background, for its roster.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="IdleAt">The relay's countdown, when the notice carries one.</param>
internal readonly record struct RelayNotice(RelayNoticeKind Kind, DateTimeOffset? IdleAt)
{
    /// <summary>A call started or ended.</summary>
    public static RelayNotice CallsChanged { get; } = new(RelayNoticeKind.CallsChanged, null);

    /// <summary>The relay's countdown moved.</summary>
    /// <param name="idleAt">When it runs out now.</param>
    /// <returns>The notice.</returns>
    public static RelayNotice Activity(DateTimeOffset? idleAt) => new(RelayNoticeKind.Activity, idleAt);

    /// <summary>The relay heard from its client after it said yes.</summary>
    /// <param name="idleAt">When its countdown runs out now.</param>
    /// <returns>The notice.</returns>
    public static RelayNotice Withdrew(DateTimeOffset? idleAt) => new(RelayNoticeKind.Withdrew, idleAt);
}

/// <summary>The kinds of <see cref="RelayNotice"/>.</summary>
internal enum RelayNoticeKind
{
    /// <summary>A call started or ended.</summary>
    CallsChanged,

    /// <summary>The relay's countdown moved.</summary>
    Activity,

    /// <summary>The relay withdrew its yes.</summary>
    Withdrew,
}
