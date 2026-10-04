// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;

namespace BrowserAI.Proxy;

/// <summary>
/// The one answer BrowserAI reads for what it says: a Chromium screenshot larger
/// than Chromium can capture, which comes back looking like a success.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q380, decided 2026-10-04 by the maintainer, in his words verbatim:</b>
/// <i>"9 d - and add a todo to the repo to track the progress of the bug for when
/// to remove our checks. Also, the refusal should mention the chromium bug
/// link."</i> His option d was <i>"Scan the answer's image height or refuse the
/// call. This reverses the 2026-08-26 rule that BrowserAI does not read
/// answers."</i>, and it does: until this, the only bytes of an answer BrowserAI
/// looked at were an error answer's install advice
/// (<c>BrowserProxy.Remediate</c>).
/// </para>
/// <para>
/// <b>What it guards against, measured 2026-10-04 at Chrome for Testing
/// 155.0.8059.12 (<c>chromium-1247</c>), <c>@playwright/mcp</c> 0.0.83:</b> a
/// full-page screenshot of a page 50,000 px tall came back as a 1920x50000 PNG
/// with <c>isError: false</c>, in which every row at or past 16,384 px repeats
/// the row 16,384 px above it, 2 of 2 runs. Taken further the same day, page by
/// page at exact sizes: a page 16,384 px tall came back whole and one 16,385 px
/// tall came back with its last row equal to its first as a PNG, and as a JPEG
/// with that row a blend of the band above it and the first, headless (2 of 2)
/// and headed (1 of 1); a page 16,385 px wide repeats its first column the same
/// way (2 of 2); and a screenshot of one element 20,001 px tall repeats from
/// 16,384 px down (1 of 1). Firefox took pages 16,385 and 32,767 px
/// tall whole and refused one 32,768 px tall with an error of its own, so it is
/// not judged here. See
/// <see href="../../../kb/playwright/tools-and-artifacts.md">kb</see>.
/// </para>
/// <para>
/// <b>It reads the image's own header and nothing else.</b> A PNG says its size
/// in its first 24 bytes and a JPEG in its first start-of-frame marker, so the
/// check costs a few bytes of the inline image, or one read of the head of the
/// file the answer names. A WebP is never larger than 16,383 px on a side, so
/// it cannot reach the limit and is not read.
/// </para>
/// <para>
/// <b>It is a check BrowserAI means to delete.</b> The Chromium bug it works
/// around is <see href="https://issues.chromium.org/issues/41347676">41347676</see>,
/// open since 2017, and <c>TODO.md</c> watches it and Playwright's
/// <see href="https://github.com/microsoft/playwright/issues/32373">#32373</see>
/// so that the check goes once a fixed Chromium ships.
/// </para>
/// </remarks>
internal static class ScreenshotLimit
{
    /// <summary>Upstream's screenshot tool, spelled as upstream spells it.</summary>
    public const string ScreenshotTool = "browser_take_screenshot";

    /// <summary>
    /// The largest side, in pixels, that a Chromium screenshot captures faithfully.
    /// </summary>
    /// <remarks>
    /// Measured 2026-10-04 at <c>chromium-1247</c>: a page exactly 16,384 px tall
    /// came back whole and one 16,385 px tall did not, headless and headed, and the
    /// same holds across: 16,384 px wide whole, 16,385 px wide not. Chromium's own
    /// issue says the limit is the compositor's largest texture.
    /// </remarks>
    public const int LargestFaithfulSide = 16_384;

    /// <summary>Chromium's own issue for the limit, which the refusal names.</summary>
    public const string ChromiumIssue = "https://issues.chromium.org/issues/41347676";

    /// <summary>Playwright's issue for the same limit, closed as a Chromium one.</summary>
    public const string PlaywrightIssue = "https://github.com/microsoft/playwright/issues/32373";

    /// <summary>How much of an image's head is read for its size.</summary>
    /// <remarks>
    /// A PNG needs 24 bytes. A JPEG's start-of-frame marker follows its tables and
    /// any application segments, which in Chromium's own encoder put it inside the
    /// first few hundred bytes; 64 KiB leaves room for a writer that adds more.
    /// </remarks>
    private const int HeadBytes = 64 * 1024;

    /// <summary>
    /// Whether a successful <c>browser_take_screenshot</c> answer from a Chromium
    /// session must be refused, and the refusal when it must.
    /// </summary>
    /// <remarks>
    /// <b>The inline image first</b>, because it is the file's own bytes and it is
    /// already in memory; the file the answer names otherwise, which is the
    /// <c>filename</c> path, and only a file inside the session's own output
    /// directory, which is the only place upstream lets a session write.
    /// </remarks>
    /// <param name="result">The child's <c>result</c> object.</param>
    /// <param name="outputDirectory">The session's output directory, absolute.</param>
    /// <returns>The refusal, or <see langword="null"/> when the answer goes back as it came.</returns>
    public static string? Refusal(JsonNode? result, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (result is not JsonObject answer || answer["content"] is not JsonArray content)
        {
            return null;
        }

        var file = LinkedFile(content, outputDirectory);
        var size = InlineImageSize(content) ?? (file is null ? null : FileImageSize(file));

        return size is { } measured && (measured.Width > LargestFaithfulSide || measured.Height > LargestFaithfulSide)
            ? SessionErrors.ScreenshotPastChromiumsLimit(measured.Width, measured.Height, file)
            : null;
    }

    /// <summary>
    /// The size a PNG or a JPEG says it is, read off the head of its bytes.
    /// </summary>
    /// <param name="head">The image's first bytes.</param>
    /// <returns>The size, or <see langword="null"/> when the bytes are neither, or too short to say.</returns>
    public static ImageSize? SizeOf(ReadOnlySpan<byte> head)
    {
        // PNG: an eight-byte signature, then the IHDR chunk, whose data starts with
        // the width and the height as big-endian 32-bit integers.
        if (head.Length >= 24
            && head[..8].SequenceEqual(PngSignature)
            && head.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            var width = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(20, 4));

            return width > int.MaxValue || height > int.MaxValue ? null : new ImageSize((int)width, (int)height);
        }

        // JPEG: segments after the start-of-image marker, each a marker and a
        // length, until a start-of-frame marker, whose data holds the precision,
        // then the height and the width as big-endian 16-bit integers.
        if (head.Length >= 4 && head[0] is 0xFF && head[1] is 0xD8)
        {
            var at = 2;

            while (at + 4 <= head.Length)
            {
                if (head[at] is not 0xFF)
                {
                    return null;
                }

                var marker = head[at + 1];

                // Fill bytes between segments.
                if (marker is 0xFF)
                {
                    at++;
                    continue;
                }

                var length = BinaryPrimitives.ReadUInt16BigEndian(head.Slice(at + 2, 2));

                if (IsStartOfFrame(marker))
                {
                    return at + 9 <= head.Length
                        ? new ImageSize(
                            BinaryPrimitives.ReadUInt16BigEndian(head.Slice(at + 7, 2)),
                            BinaryPrimitives.ReadUInt16BigEndian(head.Slice(at + 5, 2)))
                        : null;
                }

                if (length < 2)
                {
                    return null;
                }

                at += 2 + length;
            }
        }

        return null;
    }

    /// <summary>The eight bytes every PNG starts with.</summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Whether a JPEG marker is a start-of-frame: C0 to CF, less DHT, JPG and DAC.</summary>
    /// <param name="marker">The marker's second byte.</param>
    /// <returns>Whether it carries the frame's size.</returns>
    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    /// <summary>The size of the first inline image in the answer, read off its base64 head.</summary>
    /// <param name="content">The answer's content blocks.</param>
    /// <returns>The size, or <see langword="null"/> when there is no image or it says nothing readable.</returns>
    private static ImageSize? InlineImageSize(JsonArray content)
    {
        foreach (var block in content)
        {
            if (block is not JsonObject image
                || image["type"] is not JsonValue type
                || type.GetValueKind() is not JsonValueKind.String
                || !string.Equals(type.GetValue<string>(), "image", StringComparison.Ordinal)
                || image["data"] is not JsonValue data
                || data.GetValueKind() is not JsonValueKind.String)
            {
                continue;
            }

            var text = data.GetValue<string>();

            // Base64 turns every three bytes into four characters, so a head of
            // HeadBytes is this many characters, cut to a whole group of four.
            var characters = Math.Min(text.Length, HeadBytes / 3 * 4) & ~3;
            var head = new byte[characters / 4 * 3];

            return Convert.TryFromBase64Chars(text.AsSpan(0, characters), head, out var written)
                ? SizeOf(head.AsSpan(0, written))
                : null;
        }

        return null;
    }

    /// <summary>The size of an image file, read off its head.</summary>
    /// <param name="path">The file, absolute.</param>
    /// <returns>The size, or <see langword="null"/> when it cannot be read or says nothing readable.</returns>
    private static ImageSize? FileImageSize(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            var head = new byte[HeadBytes];
            var read = RandomAccess.Read(handle, head, 0);

            return SizeOf(head.AsSpan(0, read));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read is not judged: the answer goes back as
            // upstream wrote it, which is what happened before this check.
            return null;
        }
    }

    /// <summary>
    /// The image file a screenshot answer links to, when it is inside the session's
    /// output directory.
    /// </summary>
    /// <remarks>
    /// Upstream writes the link as one Markdown line, <c>- [Screenshot of full
    /// page](C:\...\name.png)</c>, with the path as it is on disk, so the target
    /// runs to the line's last closing parenthesis: a folder name may carry one of
    /// its own.
    /// </remarks>
    /// <param name="content">The answer's content blocks.</param>
    /// <param name="outputDirectory">The session's output directory, absolute.</param>
    /// <returns>The absolute path, or <see langword="null"/>.</returns>
    private static string? LinkedFile(JsonArray content, string outputDirectory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory)) + Path.DirectorySeparatorChar;

        foreach (var block in content)
        {
            if (block is not JsonObject text
                || text["text"] is not JsonValue value
                || value.GetValueKind() is not JsonValueKind.String)
            {
                continue;
            }

            foreach (var line in value.GetValue<string>().Split('\n'))
            {
                var trimmed = line.TrimEnd('\r', ' ');
                var label = trimmed.IndexOf("[Screenshot", StringComparison.Ordinal);
                var opens = label < 0 ? -1 : trimmed.IndexOf("](", label, StringComparison.Ordinal);

                if (opens < 0 || !trimmed.EndsWith(')'))
                {
                    continue;
                }

                var target = trimmed[(opens + 2)..^1];

                if (!Path.IsPathFullyQualified(target))
                {
                    continue;
                }

                var full = Path.GetFullPath(target);

                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return full;
                }
            }
        }

        return null;
    }
}

/// <summary>An image's size, in pixels, as its own header states it.</summary>
/// <param name="Width">How wide.</param>
/// <param name="Height">How tall.</param>
internal readonly record struct ImageSize(int Width, int Height);
