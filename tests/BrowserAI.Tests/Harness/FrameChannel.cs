// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Newline-delimited UTF-8 framing over a pair of streams, retaining every
/// frame's raw bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both in-process doubles are built on this and neither uses a product
/// type.</b> <see cref="FakePlaywrightChild"/> stands in for the child and
/// <see cref="RawPipeClient"/> stands in for the caller; if either framed its
/// bytes with <c>BrowserAI.Protocol</c> code, a symmetric framing mistake —
/// made on the way out and again on the way in — would pass green.
/// </para>
/// <para>
/// <b>The read is buffered, and that is not a micro-optimisation.</b> Reading a
/// byte at a time through <c>PipeReader.AsStream()</c> is quadratic; measured
/// during the 2026-08-15 spike, a one-megabyte payload turned into what looked
/// like a hang. The oversized-payload capability this harness exists to offer
/// is precisely the case that would trip it.
/// </para>
/// </remarks>
/// <param name="input">Where frames are read from.</param>
/// <param name="output">Where frames are written to.</param>
internal sealed class FrameChannel(Stream input, Stream output) : IDisposable
{
    /// <summary>
    /// UTF-8 with no byte-order mark. A harness that emits one fails the
    /// product for the harness's own defect.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>Every frame written, in order, exactly as it went on the wire.</summary>
    public List<byte[]> Sent { get; } = [];

    /// <summary>Every frame read, in order, with the terminator removed.</summary>
    public List<byte[]> Received { get; } = [];

    /// <summary>Writes one frame and flushes it.</summary>
    /// <param name="json">The frame body. Must contain no newline.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the bytes are on the wire.</returns>
    public async Task WriteFrameAsync(string json, CancellationToken cancellationToken) =>
        await WriteFrameAsync(Utf8NoBom.GetBytes(json), cancellationToken);

    /// <summary>Writes one already-encoded frame and flushes it.</summary>
    /// <param name="payload">The frame body. Must contain no newline.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the bytes are on the wire.</returns>
    public async Task WriteFrameAsync(byte[] payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        await _writeLock.WaitAsync(cancellationToken);

        try
        {
            lock (Sent)
            {
                Sent.Add(payload);
            }

            await output.WriteAsync(payload, cancellationToken);
            await output.WriteAsync("\n"u8.ToArray(), cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            _ = _writeLock.Release();
        }
    }

    /// <summary>Reads one frame, or <see langword="null"/> at end of stream.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The frame body without its terminator, or <see langword="null"/>.</returns>
    public async Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);

            if (newline >= 0)
            {
                var length = newline - _start;

                // Tolerant on the way in, like the product's own read loop: a
                // peer that frames with CRLF is answerable rather than
                // mysterious.
                if (length > 0 && _buffer[_start + length - 1] is (byte)'\r')
                {
                    length--;
                }

                var frame = _buffer[_start..(_start + length)];
                _start = newline + 1;
                Record(frame);
                return frame;
            }

            Compact();

            var read = await input.ReadAsync(_buffer.AsMemory(_end), cancellationToken);

            if (read is 0)
            {
                if (_end == _start)
                {
                    return null;
                }

                // A last frame with no terminator, for the same reason the
                // product's read loop keeps one: a peer that closes mid-write
                // would otherwise have its final message vanish.
                var tail = _buffer[_start.._end];
                _start = _end;
                Record(tail);
                return tail;
            }

            _end += read;
        }
    }

    /// <summary>
    /// Closes the write end so the peer observes EOF.
    /// </summary>
    /// <remarks>
    /// Without this a dead peer is indistinguishable from a slow one, which is
    /// the shape every hang in this layer has taken.
    /// </remarks>
    public void CloseOutput()
    {
        try
        {
            output.Dispose();
        }
#pragma warning disable CA1031 // A peer that already closed its end has closed this one for us.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Decodes a frame for a message, never for an assertion on bytes.</summary>
    /// <param name="frame">The frame to decode.</param>
    /// <returns>Its text.</returns>
    public static string TextOf(byte[] frame) => Utf8NoBom.GetString(frame);

    /// <inheritdoc />
    public void Dispose() => _writeLock.Dispose();

    private void Record(byte[] frame)
    {
        lock (Received)
        {
            Received.Add(frame);
        }
    }

    private void Compact()
    {
        if (_start > 0)
        {
            Array.Copy(_buffer, _start, _buffer, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }

        if (_end == _buffer.Length)
        {
            Array.Resize(ref _buffer, _buffer.Length * 2);
        }
    }
}
