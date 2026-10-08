// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Relay;

/// <summary>
/// What answers the client's frames that need no background: <c>initialize</c>,
/// <c>notifications/initialized</c>, <c>ping</c>, and every request other than
/// <c>tools/list</c> and <c>tools/call</c>.
/// </summary>
/// <remarks>
/// <b>Never <c>tools/list</c> and never <c>tools/call</c>.</b> The engine answers the
/// first from the list built into the binary and passes the second to the
/// background; an implementation handed either refuses it with an exception, so a
/// routing mistake is loud.
/// </remarks>
internal interface IHandshake
{
    /// <summary>Answers one client frame.</summary>
    /// <param name="frame">The frame exactly as the client sent it, without its newline.</param>
    /// <param name="cancellationToken">Ends the wait when the relay ends.</param>
    /// <returns>The answer's frame, without a newline; or <see langword="null"/> for a notification, which is not answered.</returns>
    Task<byte[]?> AnswerAsync(byte[] frame, CancellationToken cancellationToken);
}
