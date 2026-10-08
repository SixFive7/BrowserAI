// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Relay;

/// <summary>
/// The frames the relay writes itself, encoded the way every BrowserAI frame is.
/// </summary>
/// <remarks>
/// <b>Through <see cref="JsonLines"/>, so the relay's own frames escape as little as
/// JSON permits</b>, exactly like the frames the old server wrote: a sentence with an
/// apostrophe in it reaches the client as an apostrophe. A frame the relay passes on
/// is never encoded here at all; it leaves as the bytes that arrived.
/// </remarks>
internal static class RelayWire
{
    /// <summary>The relay's request ids all start with this, and nothing it passes on to the client may carry one.</summary>
    public const string IdPrefix = "browserai-relay-";

    /// <summary>Encodes one message as one frame, without a newline.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The frame.</returns>
    public static byte[] Encode(JsonRpcMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = JsonLines.CreateWriter(buffer))
        {
            JsonLines.Write(writer, message);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A result.</summary>
    /// <param name="id">The request it answers.</param>
    /// <param name="result">The result.</param>
    /// <returns>The frame.</returns>
    public static byte[] Result(RequestId id, JsonNode result) =>
        Encode(new JsonRpcResponse { Id = id, Result = result });

    /// <summary>A result spliced in as the bytes it already is.</summary>
    /// <param name="id">The request it answers.</param>
    /// <param name="result">The result's JSON.</param>
    /// <returns>The frame.</returns>
    public static byte[] VerbatimResult(RequestId id, ReadOnlySpan<byte> result)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = JsonLines.CreateWriter(buffer))
        {
            JsonLines.WriteVerbatim(writer, id, result, isError: false);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A tool call's answer carrying one sentence, with <c>isError</c> set.</summary>
    /// <remarks>
    /// A tool result and not a JSON-RPC error, because a tool result is what reaches
    /// the model in both clients (measured with a stand-in on 2026-10-04: an
    /// <c>isError</c> result reached the model in Claude Code 2.1.288 and Codex 0.155
    /// and 0.160).
    /// </remarks>
    /// <param name="id">The call it answers.</param>
    /// <param name="text">The sentence.</param>
    /// <returns>The frame.</returns>
    public static byte[] ToolError(RequestId id, string text) =>
        Result(id, BrowserProxy.TextResult(text, isError: true));

    /// <summary>A JSON-RPC error.</summary>
    /// <param name="id">The request it answers.</param>
    /// <param name="code">The error code.</param>
    /// <param name="message">The message.</param>
    /// <returns>The frame.</returns>
    public static byte[] Error(RequestId id, int code, string message) =>
        Encode(new JsonRpcError { Id = id, Error = new JsonRpcErrorDetail { Code = code, Message = message } });

    /// <summary>A request of the relay's own.</summary>
    /// <param name="id">Its id, which starts with <see cref="IdPrefix"/>.</param>
    /// <param name="method">The method.</param>
    /// <param name="parameters">The parameters, or <see langword="null"/> for none.</param>
    /// <returns>The frame.</returns>
    public static byte[] Request(string id, string method, JsonNode? parameters) =>
        Encode(new JsonRpcRequest { Id = new RequestId(id), Method = method, Params = parameters });

    /// <summary>A notification.</summary>
    /// <param name="method">The method.</param>
    /// <param name="parameters">The parameters, or <see langword="null"/> for none.</param>
    /// <returns>The frame.</returns>
    public static byte[] Notification(string method, JsonNode? parameters) =>
        Encode(new JsonRpcNotification { Method = method, Params = parameters });

    /// <summary>An instant as the protocol between relay and background writes it: ISO 8601, UTC.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>Its text.</returns>
    public static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Whether an id is in the relay's own namespace.</summary>
    /// <param name="id">The id.</param>
    /// <returns><see langword="true"/> when it is a string starting with <see cref="IdPrefix"/>.</returns>
    public static bool IsRelays(RequestId id) =>
        id.Id is string text && text.StartsWith(IdPrefix, StringComparison.Ordinal);
}
