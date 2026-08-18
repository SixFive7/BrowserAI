// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Protocol;

/// <summary>
/// BrowserAI's server-side transport: the caller's end of the proxy, over the
/// process's own stdin and stdout.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists so that a result leaves as the bytes the child produced.</b>
/// The SDK's <c>StreamServerTransport</c> serialises through
/// <c>McpJsonUtilities.JsonContext</c> — read from the shipped 2.2.0 source,
/// <c>JsonSerializer.SerializeToUtf8Bytes(message,
/// McpJsonUtilities.JsonContext.Default.JsonRpcMessage)</c>, with no options
/// seam anywhere on the path — so <c>JavaScriptEncoder.Default</c> re-escapes
/// every backtick, apostrophe, angle bracket and non-ASCII character on the way
/// out. Byte-identity is unobtainable through it, at any configuration.
/// </para>
/// <para>
/// The second reason is the stronger one and it is not about correctness: the
/// escaping is <b>tokens in the model's context on every single result</b>. A
/// measured unicode case grew 154 bytes to 218.
/// </para>
/// <para>
/// It writes through <see cref="StdioChannel"/> rather than owning a second
/// path to the handle, which is what keeps "nothing else in this process can
/// reach stdout" true.
/// </para>
/// </remarks>
internal sealed class DirectStdioServerTransport : JsonLinesTransport
{
    private readonly StdioChannel _channel;

    /// <summary>Starts serving over the given channel.</summary>
    /// <param name="channel">
    /// The process's protocol channel. <b>The transport takes ownership</b> and
    /// closes it on disposal: stdout belongs to the protocol, so nothing that
    /// could write to it may outlive the session that owns it.
    /// </param>
    /// <param name="loggerFactory">Where the transport logs.</param>
    public DirectStdioServerTransport(StdioChannel channel, ILoggerFactory? loggerFactory = null)
        : base("BrowserAI (stdio)", JsonLinesRole.CallerFacing, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        StartReading(channel.Input);
    }

    /// <inheritdoc />
    protected override ValueTask WriteFrameAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Synchronous, and honestly so: an async write queues the same blocking
        // call to the thread pool and reads as if it did not block. Corrected
        // 2026-08-18 (previously "the handle behind stdout is not opened for
        // overlapped I/O"). Measured 2026-08-18 on .NET 10:
        // Console.OpenStandardOutput returns a WindowsConsoleStream and not a
        // FileStream, redirected to a file and to a pipe alike, so the
        // thread-pool fallback comes from Stream's own base implementation
        // rather than from how a handle was opened. Same conclusion, different
        // mechanism.
        // The base class holds the send lock across this, so frames cannot
        // interleave.
        _channel.WriteFrame(utf8Payload.Span);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override ValueTask ShutdownPeerAsync()
    {
        _channel.Dispose();
        return ValueTask.CompletedTask;
    }
}
