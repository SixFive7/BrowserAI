// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;

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
/// Those three are why this is a type rather than a convention. The encoding is
/// set once, here, and <c>System.Console</c> is banned everywhere else in the
/// process by <c>BannedSymbols.txt</c> at error severity — so there is no
/// second path to the handle for a future change to get wrong.
/// </para>
/// <para>
/// <b>Bytes are the primitive, and that is deliberate.</b>
/// <see cref="DirectStdioServerTransport"/> hands this type UTF-8 it has
/// already encoded, because the whole point of owning the server transport is
/// that a result leaves byte-for-byte as the child produced it — the SDK's own
/// server transport re-escapes every backtick, apostrophe, angle bracket and
/// non-ASCII character, measured at +49.6% on a real result frame
/// ([kb](../../../kb/mcp/sdk.md#added-2026-08-16--writing-the-two-transports-at-220)).
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

    /// <summary>
    /// UTF-8 with no byte-order mark, and throwing on invalid input rather than
    /// substituting <c>U+FFFD</c>. A silently replaced character is a corrupted
    /// payload that still parses, which is the harder failure to find.
    /// </summary>
    /// <remarks>
    /// Exposed so that a pipe to a child process is encoded by the same
    /// instance rather than by a second declaration that agrees today. The one
    /// place this rule is deliberately not applied is a child's <b>stderr</b>,
    /// which carries diagnostics rather than protocol: see
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
    public static StdioChannel OpenStandardStreams()
    {
        // RS0030: System.Console is banned process-wide precisely so that this
        // is the only place the handles can be acquired. OpenStandardInput and
        // OpenStandardOutput hand back the raw Stream without installing any
        // encoding, which is exactly what is wanted -- it is Console.Out and
        // Console.In, the text-level API, that are wrong. Acquiring the handle
        // any other way would mean P/Invoking GetStdHandle for no gain.
#pragma warning disable RS0030
        return new StdioChannel(Console.OpenStandardInput(), Console.OpenStandardOutput(), ownsStreams: true);
#pragma warning restore RS0030
    }

    /// <summary>Opens the channel over caller-supplied streams, for tests.</summary>
    public static StdioChannel Over(Stream input, Stream output) => new(input, output, ownsStreams: false);

    /// <summary>
    /// Writes one newline-delimited frame of already-encoded UTF-8 and flushes
    /// it, so that a caller that returns has demonstrably put the bytes on the
    /// wire.
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose. <c>Console.OpenStandardOutput</c> hands back a
    /// stream opened without <c>FileOptions.Asynchronous</c>, so an async write
    /// against it only queues the same blocking call to the thread pool. The
    /// caller serialises frames; this does not.
    /// </remarks>
    /// <param name="utf8Payload">The frame body, already UTF-8 and free of newlines.</param>
    public void WriteFrame(ReadOnlySpan<byte> utf8Payload)
    {
        Output.Write(utf8Payload);
        Output.WriteByte((byte)'\n');
        Output.Flush();
    }

    /// <summary>Writes one newline-delimited frame, encoding it first.</summary>
    /// <param name="payload">The frame body.</param>
    public void WriteFrame(string payload) => WriteFrame(Utf8NoBom.GetBytes(payload));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsStreams)
        {
            Input.Dispose();
            Output.Dispose();
        }
    }
}
