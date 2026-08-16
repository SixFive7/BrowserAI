// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Protocol;

/// <summary>
/// The wire format both of BrowserAI's transports share: one JSON-RPC message
/// per line, UTF-8, LF-terminated, escaped as little as JSON permits.
/// </summary>
/// <remarks>
/// <para>
/// <b>The encoder is the whole reason this exists</b>, and it is the second of
/// the two SDK deviations build-order step 5 delivers. <c>StreamServerTransport</c>
/// serialises through <c>McpJsonUtilities.JsonContext</c>, which sets no
/// <c>Encoder</c> — so <c>JavaScriptEncoder.Default</c> re-escapes on the way
/// out, and every backtick, apostrophe, angle bracket and non-ASCII character
/// leaves as a <c>\uXXXX</c> sequence. The decoded value is unchanged; the bytes
/// are not. A proxy that claims byte-exact passthrough and reserialises
/// <c>Page URL: …</c> into escape sequences is claiming something it does not
/// do, and the inflation is paid in <b>tokens in the model's context on every
/// result</b>.
/// </para>
/// <para>
/// The escaping happens in <see cref="Utf8JsonWriter"/> rather than in the
/// contract metadata, which is what makes this fixable at all: the writer's own
/// <see cref="JsonWriterOptions.Encoder"/> governs, so the SDK's source-generated
/// <see cref="JsonTypeInfo"/> can be reused unchanged and only the escaping
/// differs. <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> is
/// "unsafe" only in the sense of embedding JSON in HTML; this goes down a pipe
/// to a JSON parser.
/// </para>
/// <para>
/// Reading is deliberately symmetric and byte-level: a frame reaches
/// <see cref="Utf8JsonReader"/> as the bytes that arrived, with no
/// <see cref="System.IO.StreamReader"/> and no intermediate string. Step 9 has
/// to assert on the exact byte span of a <c>result</c>, and a decode/re-encode
/// round trip in the middle of the transport is the thing that would make that
/// impossible to state honestly.
/// </para>
/// </remarks>
internal static class JsonLines
{
    /// <summary>
    /// The SDK's own contract for the message hierarchy, reused rather than
    /// re-declared: <c>JsonRpcMessage</c> is polymorphic and carries a custom
    /// converter, so a hand-written contract would be a second implementation of
    /// the protocol's shape that has to be kept in step across every bump.
    /// </summary>
    private static readonly JsonTypeInfo<JsonRpcMessage> MessageTypeInfo =
        McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonRpcMessage>();

    /// <summary>
    /// The contract for a request id, so that a hand-written envelope writes one
    /// the same way the SDK does — a <see cref="RequestId"/> holds either a
    /// string or a number and the difference reaches the wire.
    /// </summary>
    private static readonly JsonTypeInfo<RequestId> RequestIdTypeInfo =
        McpJsonUtilities.DefaultOptions.GetTypeInfo<RequestId>();

    /// <summary>
    /// The recovery scan accepts nesting the message parser would refuse,
    /// because it runs on frames that have <i>already</i> failed to parse and
    /// its one job is to find an <c>id</c> before it gives up. A depth limit
    /// here would turn a recoverable frame into an unanswered request, which is
    /// the failure this scan exists to remove.
    /// </summary>
    private static readonly JsonReaderOptions RecoveryOptions = new() { MaxDepth = int.MaxValue };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // A frame is one line. Indentation would put newlines inside it and
        // break the framing outright, so this is correctness rather than size.
        Indented = false,
    };

    /// <summary>
    /// Creates the writer a transport reuses for the lifetime of its session.
    /// </summary>
    /// <param name="destination">Where encoded frames accumulate.</param>
    /// <returns>A writer configured with the relaxed encoder.</returns>
    public static Utf8JsonWriter CreateWriter(IBufferWriter<byte> destination) => new(destination, WriterOptions);

    /// <summary>Encodes one message into the writer's destination.</summary>
    /// <param name="writer">A writer from <see cref="CreateWriter"/>.</param>
    /// <param name="message">The message to encode.</param>
    public static void Write(Utf8JsonWriter writer, JsonRpcMessage message)
    {
        JsonSerializer.Serialize(writer, message, MessageTypeInfo);
        writer.Flush();
    }

    /// <summary>
    /// Writes a response whose payload is spliced in as the bytes it arrived
    /// as, rather than re-serialised.
    /// </summary>
    /// <param name="writer">A writer from <see cref="CreateWriter"/>.</param>
    /// <param name="id">The id of the request being answered.</param>
    /// <param name="payload">The <c>result</c> or <c>error</c> value, exactly as the child sent it.</param>
    /// <param name="isError">Whether <paramref name="payload"/> is an <c>error</c> rather than a <c>result</c>.</param>
    /// <remarks>
    /// The envelope is written by hand because there is no seam in the SDK's
    /// contract through which a raw value can be substituted for a
    /// <c>JsonNode</c>. Member order differs from the SDK's own output and does
    /// not matter — JSON-RPC 2.0 defines an object, not a sequence.
    /// </remarks>
    public static void WriteVerbatim(Utf8JsonWriter writer, RequestId id, ReadOnlySpan<byte> payload, bool isError)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteString("jsonrpc"u8, "2.0"u8);
        writer.WritePropertyName("id"u8);
        JsonSerializer.Serialize(writer, id, RequestIdTypeInfo);
        writer.WritePropertyName(isError ? "error"u8 : "result"u8);

        // Validation is skipped because these bytes were sliced out of a frame
        // that has already been parsed in full by Parse above. Re-validating a
        // megabyte of screenshot to learn what we already know is a third pass
        // over the largest thing the proxy carries.
        writer.WriteRawValue(payload, skipInputValidation: true);
        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>Decodes one frame.</summary>
    /// <param name="frame">The frame's bytes, without its newline terminator.</param>
    /// <returns>The message, or <see langword="null"/> if the frame was JSON <c>null</c>.</returns>
    /// <exception cref="JsonException">The frame is not a JSON-RPC message.</exception>
    public static JsonRpcMessage? Parse(in ReadOnlySequence<byte> frame)
    {
        var reader = new Utf8JsonReader(frame);
        return JsonSerializer.Deserialize(ref reader, MessageTypeInfo);
    }

    /// <summary>
    /// Slices a response frame's <c>result</c> or <c>error</c> value out as
    /// bytes, by token offset.
    /// </summary>
    /// <param name="frame">A frame that has already parsed as a JSON-RPC message.</param>
    /// <param name="payload">The member's exact bytes.</param>
    /// <returns><see langword="true"/> when the frame carried one of the two members.</returns>
    /// <remarks>
    /// <b>By offset, never by re-serialising.</b>
    /// <see cref="Utf8JsonReader.TokenStartIndex"/> is the first byte of the
    /// value and <see cref="Utf8JsonReader.BytesConsumed"/> the first byte after
    /// it, so the slice between them is what the peer actually wrote —
    /// whitespace inside it included, escaping as the peer chose it.
    /// </remarks>
    public static bool TryReadPayload(in ReadOnlySequence<byte> frame, out VerbatimPayload payload)
    {
        payload = default;

        var reader = new Utf8JsonReader(frame);

        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
        {
            var isResult = reader.ValueTextEquals("result"u8);
            var isError = reader.ValueTextEquals("error"u8);

            if (!reader.Read())
            {
                return false;
            }

            if (!isResult && !isError)
            {
                reader.Skip();
                continue;
            }

            var start = reader.TokenStartIndex;
            reader.Skip();

            payload = new VerbatimPayload(frame.Slice(start, reader.BytesConsumed - start).ToArray(), isError);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Recovers a top-level <c>id</c> from a frame that failed to parse, so the
    /// sender can be answered instead of left waiting.
    /// </summary>
    /// <param name="frame">The frame's bytes, without its newline terminator.</param>
    /// <param name="id">The recovered id.</param>
    /// <returns><see langword="true"/> when an id was found before the frame stopped being readable.</returns>
    /// <remarks>
    /// The scan runs left to right and stops at the first thing it cannot read,
    /// which is why it succeeds on the common case: an <c>id</c> written before
    /// the malformed member is reached, and every well-behaved encoder puts it
    /// near the front. An <c>id</c> that is itself malformed, or that sits after
    /// the damage, is unrecoverable — the frame is then dropped and logged, as
    /// it was before this existed.
    /// </remarks>
    public static bool TryRecoverRequestId(in ReadOnlySequence<byte> frame, out RequestId id)
    {
        id = default;

        try
        {
            var reader = new Utf8JsonReader(frame, RecoveryOptions);

            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
            {
                var isId = reader.ValueTextEquals("id"u8);

                if (!reader.Read())
                {
                    return false;
                }

                if (!isId)
                {
                    reader.Skip();
                    continue;
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.String when reader.GetString() is { } text:
                        id = new RequestId(text);
                        return true;

                    case JsonTokenType.Number when reader.TryGetInt64(out var number):
                        id = new RequestId(number);
                        return true;

                    default:
                        return false;
                }
            }
        }
        catch (JsonException)
        {
            // The frame stopped being readable before an id turned up. That is
            // the case this method reports rather than throws on: it is called
            // from a catch block that is already handling a frame nobody could
            // parse.
        }

        return false;
    }
}
