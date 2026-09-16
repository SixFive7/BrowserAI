// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Tests.Harness;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BrowserAI.Tests;

/// <summary>
/// The server transport, asserted on bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Byte-identity is the whole deliverable</b>, so a test that decodes both
/// sides and compares strings would pass against the transport this one exists
/// to replace: the SDK's escaping is semantically lossless and visible only in
/// the bytes.
/// </para>
/// <para>
/// The comparison against <c>StreamServerTransport</c> is deliberate and is not
/// a test of the SDK. It is what keeps the deviation honest: the day upstream
/// sets an encoder, that test fails, and the reason recorded for owning a
/// server transport has to be rewritten rather than quietly carried forward.
/// </para>
/// </remarks>
internal sealed class DirectStdioServerTransportTests
{
    /// <summary>
    /// One character from each class <c>JavaScriptEncoder.Default</c> escapes:
    /// a backtick, an apostrophe, both angle brackets, an ampersand, and
    /// characters outside ASCII.
    /// </summary>
    private const string AwkwardText = "Page URL: `x` it's <b>&amp;</b> café — ünïcødé";

    private static readonly TimeSpan Patience = TestDefaults.InProcessHang;

    [Test]
    public async Task AResultLeavesWithItsCharactersIntact()
    {
        var written = await SendThroughOursAsync(AwkwardResponse());

        // The bytes of the string itself, present in the frame verbatim.
        await Assert.That(written.AsSpan().IndexOf(Encoding.UTF8.GetBytes(AwkwardText)) >= 0).IsTrue();

        // And no escape sequence for any of them.
        await Assert.That(written.AsSpan().IndexOf("\\u0060"u8) >= 0).IsFalse();
        await Assert.That(written.AsSpan().IndexOf("\\u0027"u8) >= 0).IsFalse();
        await Assert.That(written.AsSpan().IndexOf("\\u003C"u8) >= 0).IsFalse();
        await Assert.That(written.AsSpan().IndexOf("\\u0026"u8) >= 0).IsFalse();
        await Assert.That(written.AsSpan().IndexOf("\\u00E9"u8) >= 0).IsFalse();
    }

    [Test]
    public async Task AFrameIsLfTerminatedWithNoBomAndNoCarriageReturn()
    {
        var written = await SendThroughOursAsync(AwkwardResponse());

        await Assert.That(written[0]).IsEqualTo((byte)'{');
        await Assert.That(written[^1]).IsEqualTo((byte)'\n');
        await Assert.That(written).DoesNotContain((byte)'\r');
    }

    [Test]
    public async Task TheSdkServerTransportStillEscapesTheSameResult()
    {
        var ours = await SendThroughOursAsync(AwkwardResponse());
        var theirs = await SendThroughTheSdkAsync(AwkwardResponse());

        // Their bytes do not contain the string; ours do. That is the entire
        // difference, and it is worth several hundred bytes of a model's
        // context on every result that carries a URL or a snapshot.
        await Assert.That(theirs.AsSpan().IndexOf(Encoding.UTF8.GetBytes(AwkwardText)) >= 0).IsFalse();
        await Assert.That(theirs.AsSpan().IndexOf("\\u0060"u8) >= 0).IsTrue();
        await Assert.That(ours.Length).IsLessThan(theirs.Length);

        // Semantically identical, which is why nothing else catches this.
        await Assert.That(TextOf(theirs)).IsEqualTo(TextOf(ours));
        await Assert.That(TextOf(ours)).IsEqualTo(AwkwardText);
    }

    [Test]
    public async Task AFrameFromTheCallerArrivesAsAMessage()
    {
        await using var rig = new ServerRig();

        // The é arrives as its two raw UTF-8 bytes, which is the case a
        // CP437 console decoder or a BOM-sniffing StreamReader gets wrong.
        // There is neither on this path, and this is what says so.
        await rig.SendToServerAsync("""{"jsonrpc":"2.0","id":9,"method":"probe/in","params":{"text":"café"}}""");

        var received = await rig.ReceiveAsync();

        await Assert.That(received).IsTypeOf<JsonRpcRequest>();
        await Assert.That(((JsonRpcRequest)received).Params!["text"]!.GetValue<string>()).IsEqualTo("café");
    }

    [Test]
    public async Task ABlankLineAndACrlfTerminatorAreBothTolerated()
    {
        await using var rig = new ServerRig();

        // Strict on the way out, tolerant on the way in. A caller that frames
        // with CRLF is answerable rather than mysterious.
        await rig.SendRawToServerAsync("\n\r\n"u8.ToArray());
        await rig.SendRawToServerAsync(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"probe/in\"}\r\n"));

        var received = await rig.ReceiveAsync();
        await Assert.That(((JsonRpcRequest)received).Method).IsEqualTo("probe/in");
    }

    [Test]
    public async Task AMalformedFrameIsDroppedAndTheSessionSurvivesIt()
    {
        await using var rig = new ServerRig();

        // One bad message from a caller must not end a session that is
        // otherwise healthy. It is logged at Error, because the caller is now
        // waiting for a reply that will never come and the log is the only
        // place that says why.
        await rig.SendToServerAsync("{ this is not json");
        await rig.SendToServerAsync("""{"jsonrpc":"2.0","id":2,"method":"probe/after"}""");

        var received = await rig.ReceiveAsync();
        await Assert.That(((JsonRpcRequest)received).Method).IsEqualTo("probe/after");
    }

    private static JsonRpcResponse AwkwardResponse() => new()
    {
        Id = new RequestId(1),
        Result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = AwkwardText }),
        },
    };

    private static string TextOf(byte[] frame) =>
        JsonNode.Parse(Encoding.UTF8.GetString(frame))!["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static async Task<byte[]> SendThroughOursAsync(JsonRpcMessage message)
    {
        await using var rig = new ServerRig();
        await rig.Transport.SendMessageAsync(message);
        return rig.Written();
    }

    private static async Task<byte[]> SendThroughTheSdkAsync(JsonRpcMessage message)
    {
        using var output = new MemoryStream();
        var input = new Pipe();

        var transport = new StreamServerTransport(input.Reader.AsStream(), output, "sdk");

        try
        {
            await transport.SendMessageAsync(message);
        }
        finally
        {
            await input.Writer.CompleteAsync();
            await transport.DisposeAsync();
        }

        // ToArray survives disposal; the SDK's transport closes the stream it
        // was handed.
        return output.ToArray();
    }

    /// <summary>
    /// A server transport with both ends in hand: a pipe standing in for the
    /// caller's stdin and a buffer standing in for stdout.
    /// </summary>
    /// <summary>
    /// Disposing the caller-facing transport does not wait for a read the caller
    /// is never going to end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the shipped defect of v1.0.0, reduced to the one object
    /// that caused it.</b> <c>JsonLinesTransport.DisposeAsync</c> cancelled its
    /// token, closed its own end of the channel and then awaited the read loop
    /// — on the strength of a comment that said <i>"closing the peer's end is
    /// what actually wakes a read blocked in a syscall"</i>. That is true of the
    /// child leg, where this process owns the pipe; it is false of the caller
    /// leg, where the other end of stdin belongs to somebody else. Measured
    /// 2026-09-15 on .NET 10 against <c>Console.OpenStandardInput()</c>: after a
    /// read has parked, neither cancelling the token nor disposing the stream
    /// completes it — <b>3 s each, both still <c>WaitingForActivation</c>, with
    /// a console stdin and with a pipe stdin alike</b>
    /// (<c>docs/evidence/2026-09-15-fix/consoleprobe/probe-console-a.txt</c>).
    /// </para>
    /// <para>
    /// <b>So the exit was conditional on the client, and the installer is not a
    /// client.</b> A post-install start gets a console nobody writes to; the
    /// read parked, the await never returned, and the process stood for 215
    /// seconds holding a browser server and a terminal window until it was
    /// killed by pid.
    /// </para>
    /// <para>
    /// <b>Abandoning the loop is safe, and that was measured rather than
    /// assumed.</b> The parked read sits on a thread-pool thread, which is a
    /// background thread: the same probe returned from <c>Main</c> without
    /// awaiting it and the process exited anyway.
    /// </para>
    /// <para>
    /// <b>The double is a stream rather than a console</b>, because this suite
    /// may not allocate one and does not need to: what the transport is parked
    /// on is a <c>Read</c> that has not returned, and a console is only one way
    /// to arrange that. The end-to-end console shape is
    /// <c>InstallerHandoffTests.ThePublishedBinaryExitsWhenItsLauncherIsGoneAndStdinIsAConsole</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task DisposingDoesNotWaitForAReadTheCallerWillNeverEnd()
    {
        using var caller = new ParkedStream();
        using var answers = new MemoryStream();

        var transport = new DirectStdioServerTransport(StdioChannel.Over(caller, answers));

        // The disposal below IS the measurement, so this second one is only what
        // CA2000 asks for: DisposeAsync is interlocked and the second call
        // returns at once, including on the path where the first never did.
        await using var ownership = transport.ConfigureAwait(false);

        // On the event rather than on a clock: the loop has to be genuinely
        // inside the read before disposal means anything at all.
        await Assert.That(caller.WaitUntilParked(Patience)).IsTrue();

        var released = true;

        try
        {
            await transport.DisposeAsync().AsTask().WaitAsync(Patience);
        }
        catch (TimeoutException)
        {
            released = false;
        }

        await Assert.That(released).IsTrue();

        // Let the abandoned read go, so the pool thread is not held for the rest
        // of the run.
        caller.Release();
    }

    /// <summary>
    /// A stream whose <c>Read</c> parks until it is released, the way a console
    /// standard input parks until somebody types.
    /// </summary>
    /// <remarks>
    /// <b>Disposing it does not release the read</b>, deliberately: that is the
    /// property <c>Console.OpenStandardInput()</c> has and the one the defect
    /// rested on. A double that unblocked on <c>Dispose</c> would make the
    /// broken transport pass.
    /// </remarks>
    private sealed class ParkedStream : Stream
    {
        private readonly ManualResetEventSlim _parked = new(initialState: false);
        private readonly ManualResetEventSlim _released = new(initialState: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>Waits until a read is actually parked inside this stream.</summary>
        /// <param name="patience">A hang detector, never a budget.</param>
        /// <returns>Whether one is.</returns>
        public bool WaitUntilParked(TimeSpan patience) => _parked.Wait(patience);

        /// <summary>Lets the parked read return end-of-stream.</summary>
        public void Release() => _released.Set();

        public override int Read(byte[] buffer, int offset, int count)
        {
            _parked.Set();
            _released.Wait();

            return 0;
        }

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
                _parked.Dispose();
                _released.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ServerRig : IAsyncDisposable
    {
        private readonly Pipe _input = new();
        private readonly MemoryStream _output = new();

        public ServerRig() =>
            Transport = new DirectStdioServerTransport(StdioChannel.Over(_input.Reader.AsStream(), _output));

        public DirectStdioServerTransport Transport { get; }

        public byte[] Written() => _output.ToArray();

        public async Task SendToServerAsync(string frame) =>
            await SendRawToServerAsync(Encoding.UTF8.GetBytes(frame + "\n"));

        public async Task SendRawToServerAsync(byte[] bytes)
        {
            _ = await _input.Writer.WriteAsync(bytes);
            _ = await _input.Writer.FlushAsync();
        }

        public async Task<JsonRpcMessage> ReceiveAsync()
        {
            using var deadline = new CancellationTokenSource(Patience);
            return await Transport.MessageReader.ReadAsync(deadline.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _input.Writer.CompleteAsync();
            await Transport.DisposeAsync();
            await _output.DisposeAsync();
        }
    }
}
