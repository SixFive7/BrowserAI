// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Protocol;

/// <summary>
/// One JSON-RPC response's <c>result</c> or <c>error</c> member, as the exact
/// bytes it arrived as.
/// </summary>
/// <param name="Json">The member's value, terminator and surrounding envelope removed.</param>
/// <param name="IsError">Whether those bytes came from <c>error</c> rather than <c>result</c>.</param>
/// <remarks>
/// <para>
/// <b>This type is the whole difference between "semantically lossless" and
/// "byte-identical".</b> Everything a proxy could otherwise change on the way
/// through — string escaping, the textual form of a number, key order, the
/// presence of a member no contract knows about — is decided by whoever
/// re-serialises the payload. Nobody re-serialises these bytes: they are read
/// out of the child's frame and written into the caller's with
/// <see cref="System.Text.Json.Utf8JsonWriter.WriteRawValue(ReadOnlySpan{byte}, bool)"/>.
/// </para>
/// <para>
/// A <c>JsonNode</c> round trip gets very close and is not the same thing. It
/// preserves order and numeric form, but the escaping is the writer's: a child
/// that emits <c>é</c> would reach the caller as a raw <c>é</c>. Identical
/// value, different bytes, and a claim of byte-identity that is true only of
/// the payloads someone happened to test.
/// </para>
/// </remarks>
internal readonly record struct VerbatimPayload(byte[] Json, bool IsError);

/// <summary>
/// Carries a <see cref="VerbatimPayload"/> alongside an outgoing message,
/// without putting anything of ours inside an SDK type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a side table rather than a field.</b> <c>JsonRpcMessage</c>'s
/// constructor is <c>private protected</c> — <i>"Prevent external
/// derivations"</i>, read from the shipped 2.2.0 source — so there is no
/// subclass to hang a payload on, and the alternative,
/// <c>JsonRpcMessage.Context.Items</c>, means writing into SDK state that
/// <c>StreamServerTransport</c> deliberately leaves null.
/// </para>
/// <para>
/// The table is keyed on the message instance and holds it weakly, so a
/// response that is never sent — a cancelled call, a disposed session — takes
/// its payload with it rather than leaving a megabyte of screenshot behind.
/// </para>
/// </remarks>
internal static class Verbatim
{
    private static readonly ConditionalWeakTable<JsonRpcMessage, byte[]> Payloads = [];

    /// <summary>Marks a message as one whose payload must be written unchanged.</summary>
    /// <param name="message">The outgoing response or error.</param>
    /// <param name="payload">The exact bytes to write as its <c>result</c> or <c>error</c>.</param>
    public static void Attach(JsonRpcMessage message, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(payload);

        Payloads.AddOrUpdate(message, payload);
    }

    /// <summary>Reads back what <see cref="Attach"/> recorded, if anything.</summary>
    /// <param name="message">The outgoing message.</param>
    /// <param name="payload">The bytes to write verbatim.</param>
    /// <returns><see langword="true"/> when this message carries a verbatim payload.</returns>
    public static bool TryGet(JsonRpcMessage message, [NotNullWhen(true)] out byte[]? payload)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Payloads.TryGetValue(message, out payload);
    }
}
