// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Proxy;
using BrowserAI.Sessions;
using BrowserAI.Updates;

namespace BrowserAI.Background;

/// <summary>
/// The background's sessions as the update core reads them: each open session's
/// countdown, and a way to close them all.
/// </summary>
/// <remarks>
/// <b>Lane SESS's seam, read as it is</b>: <c>SessionManager.Countdowns</c> lists every
/// open session with the moment its countdown closes its browser, <see langword="null"/>
/// exactly when the agent set it to never (H1). Closing them all is the host's own
/// shutdown, which closes each cleanly within the minute's cap; the background exits
/// right after it, so nothing is served from a closed host.
/// </remarks>
/// <param name="host">The sessions.</param>
internal sealed class BackgroundSessions(SessionHost host) : IUpdateSessions
{
    /// <inheritdoc />
    public IReadOnlyList<ListedSession> Countdowns() =>
        [.. host.Sessions.Countdowns().Select(static countdown => new ListedSession(countdown.Directory, countdown.Purpose, countdown.Visible, countdown.ClosesAt))];

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b>Every session records the update as its reason</b>, which every later
    /// refusal, resume and <c>browserai_catch_up</c> quotes. <i>Corrected 2026-10-10
    /// (previously the host was disposed with no reason given, so each session read as
    /// shut down when its client went away)</i>, the texts review's #24.
    /// </remarks>
    public async Task CloseAllAsync(CancellationToken cancellationToken)
    {
        host.Sessions.ShuttingDownBecause(SessionCloseCause.Updating);
        await host.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
