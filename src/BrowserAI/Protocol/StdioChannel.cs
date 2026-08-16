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
/// </remarks>
internal sealed class StdioChannel : IDisposable
{
    /// <summary>
    /// UTF-8 with no byte-order mark, and throwing on invalid input rather than
    /// substituting <c>U+FFFD</c>. A silently replaced character is a corrupted
    /// payload that still parses, which is the harder failure to find.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly StreamWriter _writer;
    private readonly bool _ownsStreams;

    private StdioChannel(Stream input, Stream output, bool ownsStreams)
    {
        Input = input;
        Output = output;
        _ownsStreams = ownsStreams;
        _writer = new StreamWriter(output, Utf8NoBom, leaveOpen: true)
        {
            // LF, never CRLF. StreamWriter.WriteLine would otherwise emit
            // Environment.NewLine, which is CRLF on this platform.
            NewLine = "\n",

            // Flushing is explicit, so a frame is never half-written while a
            // caller believes it was sent.
            AutoFlush = false,
        };
    }

    /// <summary>The raw stdin stream. Decoding belongs to the reader.</summary>
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
    /// Writes one newline-delimited frame and flushes it, so that a caller that
    /// returns has demonstrably put the bytes on the wire.
    /// </summary>
    public void WriteFrame(string payload)
    {
        _writer.Write(payload);
        _writer.Write('\n');
        _writer.Flush();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();

        if (_ownsStreams)
        {
            Input.Dispose();
            Output.Dispose();
        }
    }
}
