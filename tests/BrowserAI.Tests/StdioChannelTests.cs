// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Protocol;

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
            channel.WriteFrame(Payload);
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
            channel.WriteFrame("{}");
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
            channel.WriteFrame("one");
            channel.WriteFrame("two");
        }

        await Assert.That(output.ToArray()).DoesNotContain((byte)'\r');
    }

    [Test]
    public async Task EachFrameIsFlushedBeforeTheCallReturns()
    {
        using var output = new MemoryStream();
        using var channel = StdioChannel.Over(Stream.Null, output);

        channel.WriteFrame("first");

        // Without an explicit flush the StreamWriter's buffer holds this until
        // dispose, and a caller that has returned would believe it had sent a
        // frame that is still in memory.
        await Assert.That(output.Length).IsGreaterThan(0);
    }
}
