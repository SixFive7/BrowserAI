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

    /// <summary>Decodes one frame.</summary>
    /// <param name="frame">The frame's bytes, without its newline terminator.</param>
    /// <returns>The message, or <see langword="null"/> if the frame was JSON <c>null</c>.</returns>
    /// <exception cref="JsonException">The frame is not a JSON-RPC message.</exception>
    public static JsonRpcMessage? Parse(in ReadOnlySequence<byte> frame)
    {
        var reader = new Utf8JsonReader(frame);
        return JsonSerializer.Deserialize(ref reader, MessageTypeInfo);
    }
}
