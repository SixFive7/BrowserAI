// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// The durations both executables read about a session, declared once.
/// </summary>
/// <remarks>
/// <b>Moved here from the server on 2026-10-03</b>, when the configuration app's
/// sessions page began to ask the question the server's idle timer answers. Q269
/// settled that a server is busy when it answered a tool call within the
/// browser-idle period or is answering one now, and the page's warning that a
/// session <i>may be in the middle of a task</i> is that question; two copies of the
/// period would be two answers to it.
/// </remarks>
internal static class SessionTimes
{
    /// <summary>
    /// How long a headless session's browser may go unused before it is closed:
    /// ten minutes. <c>BrowserIdleTimer.DefaultIdlePeriod</c> is this value.
    /// </summary>
    public static TimeSpan BrowserIdlePeriod { get; } = TimeSpan.FromMinutes(10);
}
