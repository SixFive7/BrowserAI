// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Text;
using System.Threading.Channels;

namespace BrowserAI.Protocol;

/// <summary>
/// The single owner of the process's raw stdin and stdout. UTF-8, LF, no BOM.
/// </summary>
/// <remarks>
/// <para>
/// stdout is the JSON-RPC protocol channel and it is <b>wrong by default in
/// three independent ways</b>, each measured: <c>Console.Out</c> writes CP437,
/// so <c>é</c> leaves as <c>0x82</c>; any <see cref="TextWriter"/> emits CRLF,
/// so a line-delimited frame gains a stray <c>\r</c>; and a hand-rolled
/// <c>new StreamWriter(stream, Encoding.UTF8)</c> emits a BOM, so the very
/// first frame of the session is unparseable. <c>Console.InputEncoding</c> is
/// CP437 in the same way on the read side.
/// </para>
/// <para>
/// Those three are why this is a type and not a convention. The encoding is
/// set once, here, and <c>System.Console</c> is banned everywhere else in the
/// process by <c>BannedSymbols.txt</c> at error severity -- so there is no
/// second path to the handle for a future change to get wrong.
/// </para>
/// <para>
/// <b>Bytes are the primitive, and that is deliberate.</b>
/// <see cref="DirectStdioServerTransport"/> hands this type UTF-8 it has
/// already encoded, because the whole point of owning the server transport is
/// that a result leaves byte-for-byte as the child produced it -- the SDK's own
/// server transport re-escapes every backtick, apostrophe, angle bracket and
/// non-ASCII character, measured at +49.6% on a real result frame
/// ([kb](../../../kb/mcp/sdk.md#writing-replacement-transports-against-the-public-surface)).
/// A UTF-16 round trip in the middle of
/// that path cannot corrupt a valid string, but it is the exact shape of the
/// thing being removed, so there is one write path and it takes bytes.
/// </para>
/// </remarks>
internal sealed class StdioChannel : IDisposable
{
    private readonly bool _ownsStreams;

    private StdioChannel(Stream input, Stream output, bool ownsStreams)
    {
        Input = input;
        Output = output;
        _ownsStreams = ownsStreams;
    }

    /// <summary>How much of standard input one read takes: the size of the buffer of a pipe BrowserAI makes.</summary>
    private const int ReadBufferBytes = Interop.NamedPipes.OutBufferBytes;

    /// <summary>
    /// UTF-8 with no byte-order mark, and throwing on invalid input instead of
    /// substituting <c>U+FFFD</c>. A silently replaced character is a corrupted
    /// payload that still parses, which is the harder failure to find.
    /// </summary>
    /// <remarks>
    /// Exposed so that a pipe to a child process is encoded by the same
    /// instance and not by a second declaration that agrees today. The one
    /// place this rule is deliberately not applied is a child's <b>stderr</b>,
    /// which carries diagnostics and not protocol: see
    /// <see cref="DirectStdioClientTransport"/>.
    /// </remarks>
    public static UTF8Encoding Utf8NoBom { get; } = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The raw stdin stream. There is no decoder in front of it: a frame is
    /// handed to <c>Utf8JsonReader</c> as bytes.
    /// </summary>
    public Stream Input { get; }

    /// <summary>The raw stdout stream, for a transport that frames its own bytes.</summary>
    public Stream Output { get; }

    /// <summary>Opens the channel over the process's real standard handles.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Each direction is driven by a thread of its own since 2026-10-11</b>, the
    /// maintainer's decision 54, in his words verbatim: <i>"Never block a worker
    /// anywhere."</i> The handles are the client's, made by the process that started this
    /// one, and none of them is overlapped: an anonymous pipe cannot be, and the end a
    /// client hands its child is opened without <c>FILE_FLAG_OVERLAPPED</c>. So every read
    /// and write on them is synchronous in the kernel, and the one choice there is, is
    /// which thread waits in it. <i>Corrected that day (previously the read loop's
    /// <c>ReadAsync</c> on the console stream, which <see cref="Stream"/>'s own base
    /// implementation serves by parking a pool thread in a synchronous read for as long as
    /// the client sends nothing, which is the relay's whole life, and a synchronous write
    /// on the caller's thread).</i>
    /// </para>
    /// <para>
    /// <b>The reading thread is the input's for the process's life</b>, and the frames it
    /// reads are awaited by whoever reads <see cref="Input"/>. A write runs on a thread made
    /// for it, in turn, and the caller awaits it. Both are listed for the maintainer's
    /// review with the batch, as the waits this tree keeps.
    /// </para>
    /// </remarks>
    /// <returns>The channel.</returns>
    public static StdioChannel OpenStandardStreams()
    {
        // RS0030: System.Console is banned process-wide precisely so that this
        // is the only place the handles can be acquired. OpenStandardInput and
        // OpenStandardOutput hand back the raw Stream without installing any
        // encoding, which is exactly what is wanted -- it is Console.Out and
        // Console.In, the text-level API, that are wrong. Acquiring the handle
        // any other way would mean P/Invoking GetStdHandle for no gain.
#pragma warning disable RS0030, CA2000 // The channel owns both pumps and disposes them, and each pump disposes its stream.
        return new StdioChannel(new InputPump(Console.OpenStandardInput()), new OutputPump(Console.OpenStandardOutput()), ownsStreams: true);
#pragma warning restore RS0030, CA2000
    }

    /// <summary>
    /// Wraps a stream no overlapped write can be made on the way <see cref="Output"/> is
    /// wrapped: a write is kept in memory, and a flush puts it on the wire from a thread
    /// made for it, in turn.
    /// </summary>
    /// <remarks>
    /// <b>For the suite's stand-ins</b>, since 2026-10-11: a probe that writes to a standard
    /// handle of its own acquires the handle itself and writes through this, so that no
    /// write of the suite's holds a pool thread either. Nothing of the product's calls it.
    /// </remarks>
    /// <param name="raw">The stream, which the wrapper disposes.</param>
    /// <returns>The wrapper, which the caller disposes.</returns>
    public static Stream PumpWrites(Stream raw) => new OutputPump(raw);

    /// <summary>Opens the channel over caller-supplied streams, for tests.</summary>
    /// <param name="input">What the channel reads.</param>
    /// <param name="output">What it writes.</param>
    /// <returns>The channel.</returns>
    public static StdioChannel Over(Stream input, Stream output) => new(input, output, ownsStreams: false);

    /// <summary>
    /// Writes one newline-delimited frame of already-encoded UTF-8 and flushes
    /// it, so that a caller whose await completes has demonstrably put the bytes on
    /// the wire.
    /// </summary>
    /// <remarks>
    /// <b>Awaited since 2026-10-11</b>, for the reason <see cref="OpenStandardStreams"/>
    /// gives. <i>Corrected that day (previously "Synchronous on purpose, and an async
    /// write here would buy nothing: the base <see cref="Stream"/> implementation of
    /// <c>WriteAsync</c> queues the same blocking call to the thread pool")</i>: true of the
    /// console stream, and the reason the write runs on a thread of its own. Measured
    /// 2026-08-18 on .NET 10: <c>Console.OpenStandardOutput</c> hands back a
    /// <c>System.ConsolePal+WindowsConsoleStream</c>, which is <b>not</b> a
    /// <c>FileStream</c> at all -- with stdout redirected to a file and to a pipe alike.
    /// The caller serialises frames; this does not.
    /// </remarks>
    /// <param name="utf8Payload">The frame body, already UTF-8 and free of newlines.</param>
    /// <param name="cancellationToken">Ends the wait for the write's turn.</param>
    /// <returns>The write.</returns>
    public async Task WriteFrameAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken = default)
    {
        await Output.WriteAsync(utf8Payload, cancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
        await Output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes one newline-delimited frame, encoding it first.</summary>
    /// <param name="payload">The frame body.</param>
    /// <returns>The write.</returns>
    public Task WriteFrameAsync(string payload) => WriteFrameAsync(Utf8NoBom.GetBytes(payload));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsStreams)
        {
            Input.Dispose();
            Output.Dispose();
        }
    }

    /// <summary>The newline that ends every frame.</summary>
    private static ReadOnlyMemory<byte> Newline { get; } = "\n"u8.ToArray();

    /// <summary>
    /// The process's standard input, read by a thread of its own into chunks that
    /// <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> awaits.
    /// </summary>
    private sealed class InputPump : Stream
    {
        private readonly Stream _raw;
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private byte[] _current = [];
        private int _offset;

        /// <summary>Starts the thread that reads the handle.</summary>
        /// <param name="raw">The handle's stream.</param>
        public InputPump(Stream raw)
        {
            _raw = raw;

            var reader = new Thread(Pump)
            {
                IsBackground = true,
                Name = "BrowserAI standard input",
            };

            reader.Start();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_offset == _current.Length)
            {
                if (!await _chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return 0;
                }

                if (_chunks.Reader.TryRead(out var next))
                {
                    _current = next;
                    _offset = 0;
                }
            }

            var count = Math.Min(buffer.Length, _current.Length - _offset);

            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;

            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _raw.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>The reading thread: one read of the handle at a time, until it ends.</summary>
        private void Pump()
        {
            var buffer = new byte[ReadBufferBytes];

            try
            {
                while (true)
                {
                    // KEPT, for the maintainer's review: a read of a handle no overlapped
                    // read can be made on, on the input's own thread.
                    var read = _raw.Read(buffer, 0, buffer.Length);

                    if (read is 0)
                    {
                        break;
                    }

                    _ = _chunks.Writer.TryWrite(buffer.AsSpan(0, read).ToArray());
                }

                _ = _chunks.Writer.TryComplete();
            }
#pragma warning disable CA1031 // The thread's boundary: the failure ends the input, and the reader meets it.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                _ = _chunks.Writer.TryComplete(failure);
            }
        }
    }

    /// <summary>
    /// The process's standard output: a write is kept in memory, and the flush puts it on
    /// the wire from a thread made for it, in turn.
    /// </summary>
    private sealed class OutputPump(Stream raw) : Stream
    {
        private readonly SemaphoreSlim _turn = new(1, 1);
        private readonly Lock _gate = new();
        private readonly ArrayBufferWriter<byte> _pending = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                _pending.Write(buffer.Span);
            }

            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await _turn.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                byte[] bytes;

                lock (_gate)
                {
                    bytes = _pending.WrittenSpan.ToArray();
                    _pending.ResetWrittenCount();
                }

                if (bytes.Length is 0)
                {
                    return;
                }

                var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var writer = new Thread(() =>
                {
                    try
                    {
                        // KEPT, for the maintainer's review: a write of a handle no
                        // overlapped write can be made on, on a thread of its own.
                        raw.Write(bytes, 0, bytes.Length);
                        raw.Flush();
                        written.TrySetResult();
                    }
#pragma warning disable CA1031 // The thread's boundary: the failure goes to the caller, who awaits it.
                    catch (Exception failure)
#pragma warning restore CA1031
                    {
                        written.TrySetException(failure);
                    }
                })
                {
                    IsBackground = true,
                    Name = "BrowserAI standard output",
                };

                writer.Start();

                await written.Task.ConfigureAwait(false);
            }
            finally
            {
                _ = _turn.Release();
            }
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                raw.Dispose();
                _turn.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
