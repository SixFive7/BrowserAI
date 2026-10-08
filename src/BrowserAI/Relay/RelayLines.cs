// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.IO.Pipelines;

namespace BrowserAI.Relay;

/// <summary>
/// Newline-delimited framing for the relay's two streams: one frame per line,
/// handed on as the bytes that arrived.
/// </summary>
/// <remarks>
/// <b>On <see cref="PipeReader"/>, never a <see cref="StreamReader"/></b>, for the
/// reason <c>JsonLinesTransport</c> gives: a text reader would decode every frame to
/// a string, and a frame the relay passes on has to leave as the bytes it came in
/// as. A trailing carriage return is tolerated on the way in and an empty line is
/// skipped; a last frame with no newline is still delivered.
/// </remarks>
internal static class RelayLines
{
    /// <summary>The terminator every frame the relay writes ends with.</summary>
    public static ReadOnlyMemory<byte> Newline { get; } = "\n"u8.ToArray();

    /// <summary>Reads frames until the stream ends, the token fires or a read fails.</summary>
    /// <param name="source">The stream. It is not disposed here.</param>
    /// <param name="deliver">What each frame is handed to, in order.</param>
    /// <param name="cancellationToken">Ends the read.</param>
    /// <returns>A task that completes when the stream has ended; a failed read faults it.</returns>
    public static async Task ReadAsync(Stream source, Action<byte[]> deliver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(deliver);

        var reader = PipeReader.Create(source, new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (buffer.PositionOf((byte)'\n') is { } newline)
                {
                    Deliver(buffer.Slice(0, newline), deliver);
                    buffer = buffer.Slice(buffer.GetPosition(1, newline));
                }

                if (result.IsCompleted)
                {
                    Deliver(buffer, deliver);
                    reader.AdvanceTo(buffer.End);
                    return;
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Writes one frame and its newline, and flushes.</summary>
    /// <param name="destination">The stream.</param>
    /// <param name="frame">The frame, which carries no newline.</param>
    /// <param name="cancellationToken">Ends the write.</param>
    /// <returns>A task that completes once the frame is on the wire.</returns>
    public static async Task WriteAsync(Stream destination, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        await destination.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Deliver(ReadOnlySequence<byte> line, Action<byte[]> deliver)
    {
        if (line.Length > 0 && line.Slice(line.Length - 1).FirstSpan[0] is (byte)'\r')
        {
            line = line.Slice(0, line.Length - 1);
        }

        if (!line.IsEmpty)
        {
            deliver(line.ToArray());
        }
    }
}
