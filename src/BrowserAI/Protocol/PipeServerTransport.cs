// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Protocol;

/// <summary>
/// The session host's caller-facing transport: one client's MCP conversation over
/// one connected instance of the host's pipe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b, 2026-10-03.</b> A client's server relays its client's bytes to the
/// session host unchanged, so the frames that arrive here are the frames the client
/// wrote, and the frames written here reach the client byte for byte. This is
/// <see cref="DirectStdioServerTransport"/> over a pipe in place of stdio, and for
/// the same reason it exists at all: a result leaves as the bytes the child produced.
/// </para>
/// <para>
/// <b>Asynchronous both ways</b>, because the stream is: the pipe instance is
/// overlapped and the stream over it is bound to the thread pool's completion port,
/// so an idle connection holds no thread.
/// </para>
/// </remarks>
internal sealed class PipeServerTransport : JsonLinesTransport
{
    private static readonly byte[] Newline = [(byte)'\n'];

    private readonly Stream _connection;

    /// <summary>Starts serving one connection.</summary>
    /// <param name="connection">The connected pipe, opened for asynchronous I/O. The transport owns it and closes it.</param>
    /// <param name="name">What the log calls this connection.</param>
    /// <param name="loggerFactory">Where the transport logs.</param>
    public PipeServerTransport(Stream connection, string name, ILoggerFactory? loggerFactory = null)
        : base(name, JsonLinesRole.CallerFacing, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _connection = connection;
        StartReading(connection);
    }

    /// <inheritdoc />
    protected override async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken)
    {
        // The base class holds the send lock across this, so frames cannot
        // interleave; the newline goes in the same lock.
        await _connection.WriteAsync(utf8Payload, cancellationToken).ConfigureAwait(false);
        await _connection.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
        await _connection.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>Closing the stream closes the instance</b>, which the client's end reads as
    /// the end of the conversation; there is nothing else to wait for.
    /// </remarks>
    protected override async ValueTask<bool> ShutdownPeerAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);

        return false;
    }
}
