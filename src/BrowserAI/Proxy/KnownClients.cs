// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Proxy;

/// <summary>
/// The two clients BrowserAI has a different sentence for, by the name each puts
/// in <c>initialize</c>'s <c>clientInfo</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are read values, not guesses, and the measurement is the point of
/// the type.</b> Both were taken off the wire on 2026-09-24 out of
/// <c>docs/evidence/2026-09-23-client-reconnect</c>'s request captures, where
/// each client's own <c>initialize</c> frame is stored: Claude Code
/// <b>2.1.281</b> sends
/// <c>{"name":"claude-code","title":"Claude Code","version":"2.1.281",...}</c>
/// and codex-cli <b>0.155.0-alpha.9.2</b> sends
/// <c>{"name":"codex-mcp-client","title":"Codex","version":"0.155.0-alpha.9.2"}</c>.
/// The <c>name</c> is what is matched, because it is the only member of the three
/// that is a stable identifier and not a display string.
/// </para>
/// <para>
/// ⚠️ <b>Codex's name is the one a reader will get wrong.</b> It is
/// <c>codex-mcp-client</c> and not <c>codex</c>: the CLI is <c>codex</c>, the
/// <c>title</c> is <c>Codex</c>, and the identifier is neither. Nothing here is
/// derived from the product name and nothing may be.
/// </para>
/// <para>
/// <b>An unrecognised client is not a problem and is never treated as one.</b>
/// <see cref="Matches"/> is asked in order and the caller falls through to a
/// remedy written for a client this build has never met -- which is the shape
/// every other client is, and the one this project will be wrong about least
/// often. The names are <c>[FLOATS]</c>: either client may rename itself in any
/// release, and the cost of that is a generic sentence instead of a specific
/// one, never a refusal that fails.
/// </para>
/// </remarks>
internal static class KnownClients
{
    /// <summary>What Claude Code calls itself.</summary>
    public const string ClaudeCode = "claude-code";

    /// <summary>What the Codex CLI's MCP client calls itself.</summary>
    public const string Codex = "codex-mcp-client";

    /// <summary>
    /// Whether the client that handshaked is the one named.
    /// </summary>
    /// <remarks>
    /// Ordinal and case-insensitive: an identifier is compared as an identifier,
    /// and a client that shipped a capitalisation change would otherwise fall to
    /// the generic sentence for no reason a reader could see.
    /// </remarks>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, if anything.</param>
    /// <param name="known">One of the constants on this type.</param>
    /// <returns><see langword="true"/> when they are the same client.</returns>
    public static bool Matches(string? clientName, string known) =>
        clientName is { Length: > 0 } && string.Equals(clientName, known, StringComparison.OrdinalIgnoreCase);
}
