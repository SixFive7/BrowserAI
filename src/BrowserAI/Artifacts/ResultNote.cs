// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserAI.Artifacts;

/// <summary>
/// Appends one text block to a result the child wrote, without re-serialising a
/// byte of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is where the byte-identity guarantee is made precise rather than
/// weakened.</b> Lossless passthrough requires that a <c>tools/call</c> answer
/// reach the caller as the exact bytes the child wrote; artifact routing
/// requires every routed artifact's answer to carry the path it was routed to,
/// because relocating a file while telling the model otherwise is a new silent
/// failure introduced by the fix for an old one. Both are asserted, by
/// <c>LosslessPassthroughTests</c> and <c>ArtifactRoutingTests</c>, and neither
/// may be narrowed — the second is, on its face, editing the result. The two are
/// reconciled by
/// splicing: the child's <c>content</c> array is found by token offset and one
/// element is inserted immediately before its closing bracket, so every byte the
/// child produced survives in its original order and its original escaping, and
/// exactly one array element is added.
/// </para>
/// <para>
/// So the guarantee is now: <b>BrowserAI never rewrites a byte the child wrote;
/// it appends, and only to a call whose request it rewrote.</b> A call BrowserAI
/// forwarded unchanged comes back unchanged, which is every call that names no
/// file.
/// </para>
/// <para>
/// <b>A payload with no top-level <c>content</c> array cannot be spliced</b>, and
/// that case is answered rather than absorbed: the caller gets the note through
/// the ordinary contract path and the log says the answer was rebuilt. Dropping
/// the note instead would relocate a file and tell the model otherwise, which is
/// the failure this whole section exists to avoid.
/// </para>
/// </remarks>
internal static class ResultNote
{
    private static readonly JsonWriterOptions BlockOptions = new()
    {
        // The same encoder the server transport uses, so a note that mentions a
        // Windows path does not arrive as a wall of \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>One text content block, as bytes.</summary>
    /// <param name="text">What it says.</param>
    /// <returns>The encoded block, with no surrounding punctuation.</returns>
    public static byte[] Block(string text)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, BlockOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>The same block, as a node, for the path that cannot splice.</summary>
    /// <param name="text">What it says.</param>
    /// <returns>The block.</returns>
    public static JsonObject Node(string text) =>
        new() { ["type"] = "text", ["text"] = text };

    /// <summary>
    /// Inserts a text block at the end of a raw result's <c>content</c> array.
    /// </summary>
    /// <param name="result">The child's <c>result</c> member, exactly as it arrived.</param>
    /// <param name="text">The note to append.</param>
    /// <returns>
    /// The spliced bytes, or <see langword="null"/> when the payload carries no
    /// top-level <c>content</c> array to append to.
    /// </returns>
    public static byte[]? Append(byte[] result, string text)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (EndOfContent(result) is not { } end)
        {
            return null;
        }

        var block = Block(text);
        var separator = end.Empty ? 0 : 1;
        var spliced = new byte[result.Length + separator + block.Length];

        result.AsSpan(0, end.At).CopyTo(spliced);

        if (!end.Empty)
        {
            spliced[end.At] = (byte)',';
        }

        block.CopyTo(spliced.AsSpan(end.At + separator));
        result.AsSpan(end.At).CopyTo(spliced.AsSpan(end.At + separator + block.Length));

        return spliced;
    }

    /// <summary>
    /// The byte offset of the <c>content</c> array's closing bracket, and whether
    /// the array is empty.
    /// </summary>
    private static (int At, bool Empty)? EndOfContent(byte[] result)
    {
        var reader = new Utf8JsonReader(result);

        try
        {
            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
            {
                var found = reader.ValueTextEquals("content"u8);

                if (!reader.Read())
                {
                    return null;
                }

                if (!found)
                {
                    reader.Skip();
                    continue;
                }

                if (reader.TokenType is not JsonTokenType.StartArray)
                {
                    return null;
                }

                var opened = (int)reader.TokenStartIndex;

                // From a StartArray this lands on the matching EndArray, which
                // is the one offset the whole splice turns on.
                reader.Skip();

                var closed = (int)reader.TokenStartIndex;

                return (closed, IsBlank(result.AsSpan(opened + 1, closed - opened - 1)));
            }
        }
        catch (JsonException)
        {
            // A payload the child wrote that this reader cannot walk is not a
            // payload to guess at. The caller falls back and says so.
            return null;
        }

        return null;
    }

    private static bool IsBlank(ReadOnlySpan<byte> span)
    {
        foreach (var value in span)
        {
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                return false;
            }
        }

        return true;
    }
}
