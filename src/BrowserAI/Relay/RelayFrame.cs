// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Relay;

/// <summary>What a JSON-RPC frame is, by its top-level members.</summary>
internal enum FrameKind
{
    /// <summary>A <c>method</c> and an <c>id</c>: somebody waits for an answer.</summary>
    Request,

    /// <summary>A <c>method</c> and no <c>id</c>: nobody waits.</summary>
    Notification,

    /// <summary>No <c>method</c>, and a <c>result</c> or an <c>error</c>.</summary>
    Response,

    /// <summary>Not JSON, not an object, or none of the three shapes.</summary>
    Unreadable,
}

/// <summary>
/// One frame's bytes, with what the relay routes on read off them and nothing
/// decoded that it does not need.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bytes are the frame, and they are what goes on the wire.</b> A forwarded
/// call leaves exactly as the client wrote it and an answer comes back exactly as
/// the background wrote it; the relay reads the method, the id and the slices of
/// <c>params</c>, <c>result</c> and <c>error</c> by token offset, as
/// <see cref="Protocol.JsonLines.TryReadPayload"/> does, and never re-serialises a
/// frame it passes on.
/// </para>
/// <para>
/// <b>Reading validates the whole frame</b>, because the reader has to walk every
/// value it skips: a frame that is not JSON is <see cref="FrameKind.Unreadable"/> as
/// a whole, never half read.
/// </para>
/// </remarks>
internal sealed class RelayFrame
{
    private (int Start, int Length)? _id;
    private (int Start, int Length)? _params;
    private (int Start, int Length)? _payload;

    private RelayFrame(byte[] bytes) => Bytes = bytes;

    /// <summary>The frame exactly as it arrived, without its newline.</summary>
    public byte[] Bytes { get; }

    /// <summary>What the frame is.</summary>
    public FrameKind Kind { get; private set; } = FrameKind.Unreadable;

    /// <summary>The method of a request or a notification.</summary>
    public string? Method { get; private set; }

    /// <summary>The id of a request or a response, when it is a string or an integer.</summary>
    public RequestId? Id { get; private set; }

    /// <summary>Whether a response carries an <c>error</c> and no <c>result</c>.</summary>
    public bool IsError { get; private set; }

    /// <summary>The <c>params</c> value's bytes, or nothing when the frame has none.</summary>
    public ReadOnlySpan<byte> Params => Slice(_params);

    /// <summary>A response's <c>result</c> or <c>error</c> value's bytes.</summary>
    public ReadOnlySpan<byte> Payload => Slice(_payload);

    /// <summary>Reads a frame.</summary>
    /// <param name="bytes">The frame, without its newline.</param>
    /// <returns>What it is; <see cref="FrameKind.Unreadable"/> when it is no JSON-RPC message.</returns>
    public static RelayFrame Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var frame = new RelayFrame(bytes);

        try
        {
            frame.Parse();
        }
        catch (JsonException)
        {
            frame.Kind = FrameKind.Unreadable;
        }

        return frame;
    }

    /// <summary>A string member of a JSON object's top level.</summary>
    /// <param name="json">The object's bytes.</param>
    /// <param name="name">The member's name.</param>
    /// <returns>Its value, or <see langword="null"/> when it is absent or not a string.</returns>
    public static string? StringMember(ReadOnlySpan<byte> json, ReadOnlySpan<byte> name)
    {
        if (json.IsEmpty)
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(json);

            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
            {
                var wanted = reader.ValueTextEquals(name);

                if (!reader.Read())
                {
                    return null;
                }

                if (wanted && reader.TokenType is JsonTokenType.String)
                {
                    return reader.GetString();
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // A value that cannot be read is a value that is not there.
        }

        return null;
    }

    /// <summary>A request id held in a JSON object's top-level member.</summary>
    /// <param name="json">The object's bytes.</param>
    /// <param name="name">The member's name.</param>
    /// <returns>The id, or <see langword="null"/> when it is absent or neither a string nor an integer.</returns>
    public static RequestId? IdMember(ReadOnlySpan<byte> json, ReadOnlySpan<byte> name)
    {
        if (json.IsEmpty)
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(json);

            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
            {
                var wanted = reader.ValueTextEquals(name);

                if (!reader.Read())
                {
                    return null;
                }

                if (wanted)
                {
                    return IdAt(ref reader);
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // As above.
        }

        return null;
    }

    /// <summary>Decodes a slice of the frame, for the few small values the relay reads in full.</summary>
    /// <param name="json">The slice.</param>
    /// <returns>The node, or <see langword="null"/> when the slice is empty or not JSON.</returns>
    public static JsonNode? Node(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The same frame with its <c>id</c> replaced, every other byte as it was.
    /// </summary>
    /// <remarks>
    /// What replays a client's <c>initialize</c> to a background under an id of the
    /// relay's own: the background sees the client's own parameters, byte for byte.
    /// </remarks>
    /// <param name="id">The new id, a string.</param>
    /// <returns>The frame; or <see langword="null"/> when this frame has no id to replace.</returns>
    public byte[]? WithId(string id)
    {
        if (_id is not { } at)
        {
            return null;
        }

        var encoded = JsonEncodedText.Encode(id, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).EncodedUtf8Bytes;
        var replaced = new byte[Bytes.Length - at.Length + encoded.Length + 2];

        Bytes.AsSpan(0, at.Start).CopyTo(replaced);
        var next = at.Start;
        replaced[next++] = (byte)'"';
        encoded.CopyTo(replaced.AsSpan(next));
        next += encoded.Length;
        replaced[next++] = (byte)'"';
        Bytes.AsSpan(at.Start + at.Length).CopyTo(replaced.AsSpan(next));

        return replaced;
    }

    /// <summary>The id at the reader's current value, if it is one an answer can carry.</summary>
    /// <param name="reader">A reader positioned on the value.</param>
    /// <returns>The id, or <see langword="null"/>.</returns>
    private static RequestId? IdAt(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String when reader.GetString() is { } text => new RequestId(text),
        JsonTokenType.Number when reader.TryGetInt64(out var number) => new RequestId(number),
        _ => null,
    };

    private ReadOnlySpan<byte> Slice((int Start, int Length)? range) =>
        range is { } at ? Bytes.AsSpan(at.Start, at.Length) : [];

    private void Parse()
    {
        var reader = new Utf8JsonReader(Bytes);

        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
        {
            return;
        }

        var hasResult = false;
        var hasError = false;

        while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
        {
            var member = reader.ValueTextEquals("method"u8) ? Member.Method
                : reader.ValueTextEquals("id"u8) ? Member.Id
                : reader.ValueTextEquals("params"u8) ? Member.Params
                : reader.ValueTextEquals("result"u8) ? Member.Result
                : reader.ValueTextEquals("error"u8) ? Member.Error
                : Member.Other;

            if (!reader.Read())
            {
                return;
            }

            var start = (int)reader.TokenStartIndex;

            switch (member)
            {
                case Member.Method:
                    Method = reader.TokenType is JsonTokenType.String ? reader.GetString() : null;
                    reader.Skip();
                    break;

                case Member.Id:
                    Id = IdAt(ref reader);
                    reader.Skip();
                    _id = (start, (int)reader.BytesConsumed - start);
                    break;

                case Member.Params:
                    reader.Skip();
                    _params = (start, (int)reader.BytesConsumed - start);
                    break;

                case Member.Result:
                    reader.Skip();
                    _payload = (start, (int)reader.BytesConsumed - start);
                    hasResult = true;
                    break;

                case Member.Error:
                    reader.Skip();
                    _payload ??= (start, (int)reader.BytesConsumed - start);
                    hasError = true;
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        if (reader.TokenType is not JsonTokenType.EndObject)
        {
            return;
        }

        Kind = Method is not null
            ? Id is null ? FrameKind.Notification : FrameKind.Request
            : hasResult || hasError ? FrameKind.Response : FrameKind.Unreadable;
        IsError = hasError && !hasResult;
    }

    /// <summary>The members the relay routes on.</summary>
    private enum Member
    {
        Method,
        Id,
        Params,
        Result,
        Error,
        Other,
    }
}
