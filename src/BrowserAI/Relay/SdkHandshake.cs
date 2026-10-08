// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Collections.Concurrent;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BrowserAI.Relay;

/// <summary>
/// The relay's handshake, answered by the MCP SDK's own server in memory, built
/// from the options every caller-facing server of this product is built from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The SDK answers, so nothing about the protocol is written twice.</b> The
/// revision negotiated at <c>initialize</c>, the answer to <c>server/discover</c>,
/// <c>ping</c>, the <c>-32601</c> for a method nobody serves and the removal of the
/// logging capability the SDK would otherwise advertise all come from
/// <see cref="BrowserProxy.CallerFacingOptions"/> and the SDK behind it, so a pin
/// added there applies to the relay by construction and the relay can never
/// introduce itself differently from the server it replaced.
/// </para>
/// <para>
/// <b>It has no tool handlers, deliberately.</b> <c>tools/list</c> is answered by the
/// engine from the list built into the binary and <c>tools/call</c> goes to the
/// background; handed either, this refuses with an exception, so a routing mistake
/// cannot turn into a quiet <c>-32601</c>.
/// </para>
/// <para>
/// <b>The SDK is given no logger, on purpose.</b> At its finer levels it writes whole
/// messages, <c>params</c> and all, and the relay's rule is that no record carries a
/// frame's contents. What this server answers is the protocol's own bookkeeping, and
/// a frame it cannot read is logged by the engine, by its length.
/// </para>
/// </remarks>
internal sealed class SdkHandshake : IHandshake, IAsyncDisposable
{
    private readonly HandshakeTransport _transport;
    private readonly McpServer _server;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _running;

    private int _disposed;

    private SdkHandshake()
    {
        _transport = new HandshakeTransport();
        _server = McpServer.Create(_transport, BrowserProxy.CallerFacingOptions(), NullLoggerFactory.Instance);
        _running = _server.RunAsync(_stopping.Token);
    }

    /// <summary>Starts the in-memory server.</summary>
    /// <returns>The handshake, serving.</returns>
    public static SdkHandshake Start() => new();

    /// <inheritdoc />
    public async Task<byte[]?> AnswerAsync(byte[] frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var message = JsonLines.Parse(new ReadOnlySequence<byte>(frame))
            ?? throw new ArgumentException("The frame is JSON null, not a message.", nameof(frame));

        if (message is JsonRpcRequest { Method: RequestMethods.ToolsList or RequestMethods.ToolsCall } routed)
        {
            throw new InvalidOperationException(
                $"'{routed.Method}' reached the handshake. The relay answers the tool list from the binary and passes calls to the background; neither may reach the SDK's server, which has no tool handlers.");
        }

        if (message is not JsonRpcRequest request)
        {
            await _transport.DeliverAsync(message, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var answer = _transport.Expect(request.Id);

        try
        {
            await _transport.DeliverAsync(message, cancellationToken).ConfigureAwait(false);
            return RelayWire.Encode(await answer.WaitAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _transport.Forget(request.Id);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);

        // Completing the channel is what ends the server's read loop; the token
        // alone ends a read only between messages.
        await _transport.DisposeAsync().ConfigureAwait(false);

        try
        {
            await _running.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The token above is what ended it.
        }

        await _server.DisposeAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    /// <summary>
    /// A transport with no wire: the engine's frames go in through its channel, and the
    /// server's answers come out to whoever is waiting for that id.
    /// </summary>
    private sealed class HandshakeTransport : TransportBase
    {
        private readonly ConcurrentDictionary<RequestId, TaskCompletionSource<JsonRpcMessage>> _waiting = new();

        public HandshakeTransport()
            : base("BrowserAI relay handshake", NullLoggerFactory.Instance)
        {
            SetConnected();
        }

        /// <summary>Registers interest in the answer to one request, before it is delivered.</summary>
        /// <param name="id">The request's id.</param>
        /// <returns>The answer, when the server sends it.</returns>
        public Task<JsonRpcMessage> Expect(RequestId id)
        {
            var answer = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            return _waiting.TryAdd(id, answer)
                ? answer.Task
                : throw new InvalidOperationException($"A request with the id {id} is already waiting for the handshake's answer.");
        }

        /// <summary>Stops waiting for one request's answer.</summary>
        /// <param name="id">The request's id.</param>
        public void Forget(RequestId id) => _waiting.TryRemove(id, out _);

        /// <summary>Hands one client message to the server.</summary>
        /// <param name="message">The message.</param>
        /// <param name="cancellationToken">Ends the hand-over.</param>
        /// <returns>The hand-over.</returns>
        public async Task DeliverAsync(JsonRpcMessage message, CancellationToken cancellationToken) =>
            await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);

        /// <inheritdoc />
        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (message is JsonRpcMessageWithId { Id: var id } and (JsonRpcResponse or JsonRpcError)
                && _waiting.TryRemove(id, out var waiting))
            {
                _ = waiting.TrySetResult(message);
            }

            // Anything else the server sends of its own accord has nobody to go to:
            // the relay's client hears only answers to what it asked.
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override ValueTask DisposeAsync()
        {
            SetDisconnected();

            foreach (var waiting in _waiting.Values)
            {
                _ = waiting.TrySetCanceled();
            }

            _waiting.Clear();
            return ValueTask.CompletedTask;
        }
    }
}
