// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Tests.Harness;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests;

/// <summary>
/// Asserts the protocol channel's encoding <b>on bytes</b>.
/// </summary>
/// <remarks>
/// Round-tripping through a decoder would pass against every one of the three
/// defaults this type exists to defeat: CP437 output, CRLF line endings and a
/// leading BOM all decode back to the string that was written. Only the bytes
/// say which one went on the wire.
/// </remarks>
internal sealed class StdioChannelTests
{
    /// <summary>
    /// One character from each failure mode: <c>é</c> is <c>0x82</c> under
    /// CP437, the backtick and the angle brackets are what the SDK's own server
    /// transport escapes to <c>\uXXXX</c>, and the embedded newline is what a
    /// CRLF writer turns into <c>0x0D 0x0A</c>.
    /// </summary>
    private const string Payload = "é`<>\n";

    [Test]
    public async Task AFrameIsWrittenAsUtf8WithLfAndNoBom()
    {
        using var output = new MemoryStream();

        using (var channel = StdioChannel.Over(Stream.Null, output))
        {
            await channel.WriteFrameAsync(Payload);
        }

        byte[] expected =
        [
            0xC3, 0xA9,  // é, UTF-8. CP437 would be the single byte 0x82.
            0x60,        // `
            0x3C, 0x3E,  // <>
            0x0A,        // the payload's own newline, unescaped and un-CR'd
            0x0A,        // the frame terminator
        ];

        await Assert.That(output.ToArray()).IsEquivalentTo(expected);
    }

    [Test]
    public async Task NoByteOrderMarkPrecedesTheFirstFrame()
    {
        using var output = new MemoryStream();

        using (var channel = StdioChannel.Over(Stream.Null, output))
        {
            await channel.WriteFrameAsync("{}");
        }

        // A hand-rolled StreamWriter over Encoding.UTF8 emits EF BB BF here,
        // and the very first frame of the session is then unparseable.
        await Assert.That(output.ToArray()[0]).IsEqualTo((byte)'{');
    }

    [Test]
    public async Task NoCarriageReturnIsEverEmitted()
    {
        using var output = new MemoryStream();

        using (var channel = StdioChannel.Over(Stream.Null, output))
        {
            await channel.WriteFrameAsync("one");
            await channel.WriteFrameAsync("two");
        }

        await Assert.That(output.ToArray()).DoesNotContain((byte)'\r');
    }

    [Test]
    public async Task EachFrameIsFlushedBeforeTheCallReturns()
    {
        using var output = new MemoryStream();
        using var channel = StdioChannel.Over(Stream.Null, output);

        await channel.WriteFrameAsync("first");

        // Without an explicit flush the StreamWriter's buffer holds this until
        // dispose, and a caller that has returned would believe it had sent a
        // frame that is still in memory.
        await Assert.That(output.Length).IsGreaterThan(0);
    }

    /// <summary>
    /// Every flush of the output is written by one thread for the output's life, which is
    /// no pool worker, and the frames leave in the order they were written.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-12 for the maintainer's 78 b</b>, in his words verbatim:
    /// <i>"78-82 your recommendation"</i>: one long-lived output thread fed by a queue,
    /// instead of a new thread per frame, with the frame order and the flush kept exactly.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryFlushIsWrittenByTheOutputsOneThreadInTheOrderTheFramesWereWritten()
    {
        var recorder = new RecordingStream();

        await using (var output = StdioChannel.PumpWrites(recorder))
        {
            for (var frame = 0; frame < 5; frame++)
            {
                await output.WriteAsync(Encoding.UTF8.GetBytes($"frame {frame.ToString(CultureInfo.InvariantCulture)}\n"));
                await output.FlushAsync();

                // The flush completes once its bytes are written: the frame is there now.
                await Assert.That(recorder.Text).EndsWith($"frame {frame.ToString(CultureInfo.InvariantCulture)}\n");
            }
        }

        await Assert.That(recorder.Text).IsEqualTo("frame 0\nframe 1\nframe 2\nframe 3\nframe 4\n");
        await Assert.That(recorder.Writers.Distinct().Count()).IsEqualTo(1).Because("the flushes were written by more than one thread");
        await Assert.That(recorder.Names.Distinct().Single()).IsEqualTo("BrowserAI standard output");
        await Assert.That(recorder.Pooled.Any(static pooled => pooled)).IsFalse().Because("a flush was written by a pool worker");
    }

    /// <summary>
    /// A frame written while a client that stopped reading holds the last flush on the wire
    /// waits with its caller, and the output keeps nothing behind the stuck bytes; once the
    /// client reads again, both frames arrive whole and in order.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-12 for the maintainer's 78 b</b>, whose condition is <i>"make sure a
    /// stuck client cannot grow the queue without bound: decide and state a bound"</i>. The
    /// bound is one flush's bytes, as <c>OutputPump</c>'s remarks state. The client is a real
    /// pipe of the shape both clients hand, a synchronous end that the output writes and an
    /// overlapped end the arm reads, so the stuck write waits in the kernel and the arm's own
    /// read holds no thread.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFrameWrittenWhileAStuckClientHoldsTheLastWaitsWithItsCaller()
    {
        var (parent, child) = NamedPipes.CreateChildPipe(childReads: false);

        using var childEnd = new SafeFileHandle(child, ownsHandle: true);
        await using var client = new FileStream(parent, FileAccess.Read, bufferSize: 0, isAsync: true);
        await using var writing = new FileStream(childEnd, FileAccess.Write, bufferSize: 0, isAsync: false);
        await using var output = StdioChannel.PumpWrites(writing);

        // Far more than a pipe buffers, so the write waits until the client reads.
        var stuck = new byte[1024 * 1024];
        Array.Fill(stuck, (byte)'a');
        stuck[^1] = (byte)'\n';
        var after = "the frame after\n"u8.ToArray();

        await output.WriteAsync(stuck);
        var first = output.FlushAsync();
        var second = output.WriteAsync(after).AsTask();

        await Assert.That(first.IsCompleted).IsFalse().Because("the client read nothing, so the flush cannot have finished");
        await Assert.That(second.IsCompleted).IsFalse().Because("the output kept a second frame behind the one a stuck client was not reading");

        // The client reads again: the stuck frame arrives whole, then the one after it.
#pragma warning disable CA2025 // The read is awaited below, before the client is disposed: the writes it lets through are awaited first.
        var read = ReadAsync(client, stuck.Length + after.Length);
#pragma warning restore CA2025

        await first.WaitAsync(TestDefaults.InProcessHang);
        await second.WaitAsync(TestDefaults.InProcessHang);
        await output.FlushAsync().WaitAsync(TestDefaults.InProcessHang);

        var received = await read.WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(received.AsSpan(0, stuck.Length).SequenceEqual(stuck)).IsTrue();
        await Assert.That(received.AsSpan(stuck.Length).SequenceEqual(after)).IsTrue();
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes from a stream opened for overlapped reads.</summary>
    private static async Task<byte[]> ReadAsync(Stream stream, int count)
    {
        var received = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await stream.ReadAsync(received.AsMemory(offset));

            if (read is 0)
            {
                break;
            }

            offset += read;
        }

        return received[..offset];
    }

    /// <summary>A stand-in for a handle: it keeps what is written, and which thread wrote it.</summary>
    private sealed class RecordingStream : Stream
    {
        private readonly Lock _gate = new();
        private readonly List<byte> _bytes = [];

        public List<int> Writers { get; } = [];

        public List<string?> Names { get; } = [];

        public List<bool> Pooled { get; } = [];

        public string Text
        {
            get
            {
                lock (_gate)
                {
                    return Encoding.UTF8.GetString([.. _bytes]);
                }
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                Writers.Add(Environment.CurrentManagedThreadId);
                Names.Add(Thread.CurrentThread.Name);
                Pooled.Add(Thread.CurrentThread.IsThreadPoolThread);
                _bytes.AddRange(buffer.AsSpan(offset, count));
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
