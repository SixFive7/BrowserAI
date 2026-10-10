// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// One open session's idle countdown, read at one moment: what the background's
/// update holds and the dashboard show.
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam the session code exposes for the background and the toasts, 2026-10-08.</b> H1, decided
/// that day: hidden browser sessions and visible windows hold a downloaded update
/// until their countdown runs out or they are closed, and the toast and the dashboard
/// show each one with its deadline. The background maps this onto
/// <c>Updates.HoldingSession(Directory, Purpose, ClosesAt)</c>, split by
/// <see cref="Visible"/>.
/// </para>
/// <para>
/// <b>A deadline and never a remaining time</b>, the contract's own rule: a reader
/// computes what is left against its own clock, so a snapshot read a second ago is
/// still right a second later.
/// </para>
/// </remarks>
/// <param name="Directory">The session directory.</param>
/// <param name="Purpose">What its record says it is for, or <see langword="null"/> when it says nothing.</param>
/// <param name="Visible">Whether its browser has a window this run.</param>
/// <param name="ClosesAt">
/// When its countdown closes its browser if no call names it first and, in a visible
/// window, the person does not use it; <see langword="null"/> exactly when the agent
/// set it to never. In the past once the countdown has run out with no browser up.
/// </param>
/// <param name="LastActivity">When a call last named it, or the person last used its window.</param>
/// <param name="BrowserIsOpen">Whether a browser is up, so a session whose browser never started can be told apart.</param>
internal sealed record SessionCountdown(
    string Directory,
    string? Purpose,
    bool Visible,
    DateTimeOffset? ClosesAt,
    DateTimeOffset LastActivity,
    bool BrowserIsOpen);
