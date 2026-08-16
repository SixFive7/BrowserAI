// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Finds a top-level member's exact byte span in a JSON-RPC frame, by token
/// offset.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is deliberately a second implementation of something the product
/// already has</b>, and that is the only reason it earns its place. The
/// assertion "the bytes the child wrote are the bytes the caller received"
/// compares a span the product sliced with a span the product sliced; if both
/// came from <c>JsonLines.TryReadPayload</c>, an off-by-one in it would move
/// both sides equally and the test would agree with the bug.
/// </para>
/// <para>
/// <b>And it is a span comparison, never a re-serialised one.</b> Parsing both
/// frames and comparing the resulting objects — or their
/// <c>ToJsonString()</c> — normalises away escaping, whitespace and numeric
/// form, which is the entire set of differences a lossless proxy has to not
/// introduce. That assertion passes while the bug ships.
/// </para>
/// </remarks>
internal static class JsonSpan
{
    /// <summary>Slices a top-level member's value out of a frame.</summary>
    /// <param name="frame">A whole JSON-RPC frame, terminator removed.</param>
    /// <param name="member">The member name, such as <c>result</c>.</param>
    /// <returns>The member's value, exactly as it appears in <paramref name="frame"/>.</returns>
    /// <exception cref="InvalidOperationException">The frame has no such top-level member.</exception>
    public static byte[] MemberOf(byte[] frame, string member)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var reader = new Utf8JsonReader(frame);

        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new InvalidOperationException($"That frame is not a JSON object: {Preview(frame)}");
        }

        while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
        {
            var found = reader.GetString() == member;

            if (!reader.Read())
            {
                break;
            }

            if (!found)
            {
                reader.Skip();
                continue;
            }

            var start = (int)reader.TokenStartIndex;
            reader.Skip();

            return frame[start..(int)reader.BytesConsumed];
        }

        throw new InvalidOperationException($"That frame has no top-level '{member}': {Preview(frame)}");
    }

    /// <summary>Whether a frame carries a top-level member at all.</summary>
    /// <param name="frame">A whole JSON-RPC frame, terminator removed.</param>
    /// <param name="member">The member name.</param>
    /// <returns><see langword="true"/> when the member is present.</returns>
    public static bool Has(byte[] frame, string member)
    {
        try
        {
            _ = MemberOf(frame, member);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string Preview(byte[] frame)
    {
        var text = FrameChannel.TextOf(frame);
        return text.Length <= 400 ? text : text[..400] + "…";
    }
}
