// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Protocol;

/// <summary>
/// The half of a newline-delimited JSON-RPC transport that is identical on both
/// sides of the proxy: frame in, frame out, and a read loop that ends on EOF.
/// </summary>
/// <remarks>
/// <para>
/// BrowserAI replaces <b>both</b> of the SDK's stdio transports, for two
/// unrelated reasons — <c>StdioClientTransport</c> interposes <c>cmd.exe</c>,
/// and <c>StreamServerTransport</c> re-escapes every result. What is left over
/// once those two are removed is the same code twice, so it is written once
/// here and the two subclasses supply only what genuinely differs: where the
/// bytes go, and what has to be shut down.
/// </para>
/// <para>
/// The read loop runs on <see cref="PipeReader"/> rather than
/// <see cref="StreamReader"/>. That is not a preference: a
/// <see cref="StreamReader"/> would decode every frame to a string and the JSON
/// reader would immediately re-encode it, which is a UTF-16 round trip in the
/// middle of the path whose entire purpose is that bytes survive it.
/// </para>
/// </remarks>
internal abstract class JsonLinesTransport : TransportBase
{
    private readonly ArrayBufferWriter<byte> _outgoing = new();
    private readonly Utf8JsonWriter _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    private Task _readLoop = Task.CompletedTask;
    private int _disposed;

    /// <summary>Initialises the shared half of a transport.</summary>
    /// <param name="name">The transport's name, used in diagnostics.</param>
    /// <param name="loggerFactory">Where the transport logs, or <see langword="null"/> to log nowhere.</param>
    protected JsonLinesTransport(string name, ILoggerFactory? loggerFactory)
        : base(name, loggerFactory)
    {
        // TransportBase exposes a Logger, and it is `private protected`:
        // measured 2026-08-16 against 2.2.0, an assembly outside the SDK cannot
        // reach it, and neither can it reach LogTransportSendingMessageSensitive.
        // A transport written against the public TransportBase therefore has to
        // carry its own, which is why the factory is taken twice.
        Log = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;
        _writer = JsonLines.CreateWriter(_outgoing);
    }

    /// <summary>Cancelled once <see cref="DisposeAsync"/> has begun.</summary>
    protected CancellationToken ShutdownToken => _shutdown.Token;

    /// <summary>Where this transport logs.</summary>
    protected ILogger Log { get; }

    /// <inheritdoc />
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsConnected)
        {
            // Dropped rather than thrown, because this is reached during
            // teardown when the peer has already gone and an exception storm
            // there hides the reason the session ended. Logged, because a
            // dropped protocol message that nothing records is precisely the
            // failure class this project exists to eliminate.
            TransportLog.SendOnDisconnectedTransport(Log, Name);
            return;
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // One buffer and one writer for the life of the session, reset per
            // frame under this lock. The buffer keeps the high-water mark of
            // the largest message seen, which for a proxy carrying screenshots
            // is the point.
            _outgoing.ResetWrittenCount();
            _writer.Reset();
            JsonLines.Write(_writer, message);

            await WriteFrameAsync(_outgoing.WrittenMemory, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Every send failure is reported the same way, and the inner exception carries what it was.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            throw new IOException($"'{Name}' could not send a JSON-RPC frame.", ex);
        }
        finally
        {
            _ = _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        try
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);

            // Closing the peer's end is what actually wakes a read blocked in a
            // syscall; cancellation alone does not, which is the SDK's own
            // finding and the reason this is a separate step rather than a
            // token.
            await ShutdownPeerAsync().ConfigureAwait(false);

            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the token above is what ended the loop.
            }
#pragma warning disable CA1031 // Teardown reports and continues; nothing above this can act on the difference.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                TransportLog.ReadLoopFailedDuringShutdown(Log, Name, ex);
            }
        }
        finally
        {
            _shutdown.Dispose();
            _writer.Dispose();
            _sendLock.Dispose();
            SetDisconnected();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Writes one already-encoded frame, terminator included, and flushes it.
    /// </summary>
    /// <param name="utf8Payload">The frame body. Never contains a newline.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the frame is on the wire.</returns>
    protected abstract ValueTask WriteFrameAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken);

    /// <summary>
    /// Closes whatever the read loop is blocked on, so that it observes EOF.
    /// </summary>
    /// <returns>A task that completes once the peer has been closed.</returns>
    protected abstract ValueTask ShutdownPeerAsync();

    /// <summary>
    /// Marks the transport connected and starts reading frames from
    /// <paramref name="source"/>. Called by a subclass once its own state is
    /// complete, never from this constructor: the loop can deliver a message
    /// before a derived constructor has finished running.
    /// </summary>
    /// <param name="source">The stream carrying inbound frames.</param>
    protected void StartReading(Stream source)
    {
        SetConnected();
        _readLoop = Task.Run(() => ReadLoopAsync(source), CancellationToken.None);
    }

    /// <summary>
    /// Removes a frame's trailing carriage return, if it has one.
    /// </summary>
    /// <remarks>
    /// Strict on the way out, tolerant on the way in. BrowserAI never emits
    /// CRLF — <see cref="StdioChannel"/> exists to guarantee that — but a peer
    /// that does is answerable rather than mysterious, and the alternative is a
    /// session that dies on a stray byte nobody can see in a log.
    /// </remarks>
    private static ReadOnlySequence<byte> TrimTerminator(in ReadOnlySequence<byte> frame) =>
        frame.Length > 0 && frame.Slice(frame.Length - 1).FirstSpan[0] is (byte)'\r'
            ? frame.Slice(0, frame.Length - 1)
            : frame;

    private static bool TrySliceFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frame)
    {
        var newline = buffer.PositionOf((byte)'\n');

        if (newline is null)
        {
            frame = default;
            return false;
        }

        frame = buffer.Slice(0, newline.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, newline.Value));
        return true;
    }

    private async Task ReadLoopAsync(Stream source)
    {
        // leaveOpen: the subclass owns the stream and closes it in
        // ShutdownPeerAsync, which is also what ends this loop.
        var reader = PipeReader.Create(source, new StreamPipeReaderOptions(leaveOpen: true));
        Exception? error = null;

        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(_shutdown.Token).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (TrySliceFrame(ref buffer, out var frame))
                {
                    await DispatchAsync(frame).ConfigureAwait(false);
                }

                if (result.IsCompleted)
                {
                    // A last frame with no terminator. A peer that closes its
                    // end mid-write would otherwise have its final message
                    // dropped without a word.
                    await DispatchAsync(buffer).ConfigureAwait(false);
                    reader.AdvanceTo(buffer.End);
                    TransportLog.EndOfStream(Log, Name);
                    break;
                }

                // Every ReadAsync must be answered by exactly one AdvanceTo,
                // including the one that ends the loop above.
                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal. Not an error, and not a disconnect reason worth
            // reporting: the caller asked for it.
        }
#pragma warning disable CA1031 // Whatever ended the loop becomes the transport's disconnect reason rather than an unobserved task exception.
        catch (Exception ex) when (!_shutdown.IsCancellationRequested)
#pragma warning restore CA1031
        {
            TransportLog.ReadLoopFailed(Log, Name, ex);
            error = ex;
        }
#pragma warning disable CA1031 // Same, for the shutdown race below.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Disposal closed the stream out from under a read that was already
            // in flight. Reporting that as the session's failure reason would
            // bury whatever actually ended it.
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
            SetDisconnected(error);
        }
    }

    private async ValueTask DispatchAsync(ReadOnlySequence<byte> frame)
    {
        frame = TrimTerminator(frame);

        if (frame.IsEmpty)
        {
            return;
        }

        JsonRpcMessage? message;

        try
        {
            message = JsonLines.Parse(frame);
        }
        catch (JsonException ex)
        {
            // The loop survives a malformed frame: one bad message from a peer
            // must not end a session that is otherwise healthy. It is reported
            // at Error because a caller whose request was dropped will now wait
            // for a reply that is never coming, and the log is the only place
            // that says why.
            //
            // The SDK additionally recovers a top-level `id` and answers -32700
            // so the caller fails instead of hanging. That is better, it is
            // error shaping rather than transport, and it is owed at step 9
            // where the error catalogue lands (TODO.md).
            TransportLog.FrameNotParsed(Log, Name, frame.Length, ex);
            return;
        }

        if (message is null)
        {
            TransportLog.FrameWasNotAMessage(Log, Name, frame.Length);
            return;
        }

        await WriteMessageAsync(message, _shutdown.Token).ConfigureAwait(false);
    }
}

/// <summary>Source-generated log messages for the transports.</summary>
internal static partial class TransportLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "{Transport}: a JSON-RPC message was dropped because the transport is no longer connected.")]
    public static partial void SendOnDisconnectedTransport(ILogger logger, string transport);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "{Transport}: a {Bytes}-byte frame could not be parsed and was dropped.")]
    public static partial void FrameNotParsed(ILogger logger, string transport, long bytes, Exception exception);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "{Transport}: a {Bytes}-byte frame parsed as JSON null rather than a message, and was dropped.")]
    public static partial void FrameWasNotAMessage(ILogger logger, string transport, long bytes);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "{Transport}: the peer closed its end of the connection.")]
    public static partial void EndOfStream(ILogger logger, string transport);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "{Transport}: the read loop ended with an error.")]
    public static partial void ReadLoopFailed(ILogger logger, string transport, Exception exception);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "{Transport}: the read loop faulted while shutting down.")]
    public static partial void ReadLoopFailedDuringShutdown(ILogger logger, string transport, Exception exception);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "{Transport}: started '{Command}' as pid {ProcessId} in '{WorkingDirectory}'.")]
    public static partial void ChildStarted(ILogger logger, string transport, string command, int processId, string workingDirectory);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "{Transport}: pid {ProcessId} exited with code {ExitCode}.")]
    public static partial void ChildExited(ILogger logger, string transport, int processId, int exitCode);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "{Transport}: pid {ProcessId} did not exit within {Timeout}; killing the tree.")]
    public static partial void ChildKilled(ILogger logger, string transport, int processId, TimeSpan timeout);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "{Transport}: the standard-error callback threw and its exception was discarded.")]
    public static partial void StandardErrorCallbackFailed(ILogger logger, string transport, Exception exception);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Debug,
        Message = "{Transport}: child stderr: {Line}")]
    public static partial void ChildStandardError(ILogger logger, string transport, string line);
}
