// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// The durations both executables read about a session, declared once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Moved here from the server on 2026-10-03</b>, when the configuration app's
/// sessions page began to ask the question the server's idle timer answers. Q269
/// settled that a server is busy when it answered a tool call within the
/// browser-idle period or is answering one now, and the page's warning that a
/// session <i>may be in the middle of a task</i> is that question; two copies of the
/// period would be two answers to it.
/// </para>
/// <para>
/// <b>The close cap joined it on 2026-10-04</b>, because the server's sessions and
/// the coordinator's wait for the session host both read it, and the coordinator
/// lives in the app.
/// </para>
/// </remarks>
internal static class SessionTimes
{
    /// <summary>
    /// How long a headless session's browser may go unused before it is closed:
    /// ten minutes. <c>BrowserIdleTimer.DefaultIdlePeriod</c> is this value.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The hidden default since 2026-10-08, E2</b> (previously the only
    /// period): an agent may set another per session, in whole minutes or never, and
    /// a visible window's default is <see cref="VisibleIdleMinutes"/>. Derived from
    /// <see cref="HiddenIdleMinutes"/>, so the two cannot disagree.
    /// </remarks>
    public static TimeSpan BrowserIdlePeriod { get; } = TimeSpan.FromMinutes(HiddenIdleMinutes);

    /// <summary>
    /// The idle setting a session without a window gets when the call names none:
    /// <b>ten minutes</b>.
    /// </summary>
    /// <remarks>
    /// <b>Chosen, and unchanged since the first build</b>: long enough that ordinary
    /// think-time between calls never closes a browser, short enough that a browser
    /// nobody uses does not hold memory for long. E2, the maintainer's words of
    /// 2026-10-07 verbatim: <i>"Right now browsers automatically close after 10 min. of
    /// inactivity and interactive windows never do. What if we change the never to 1
    /// hour and then allow the calling agent to change this default behaviour with a
    /// parameter?"</i> A new row for the numbers index (F3).
    /// </remarks>
    public const int HiddenIdleMinutes = 10;

    /// <summary>
    /// The idle setting a session with a window gets when the call names none:
    /// <b>sixty minutes</b>.
    /// </summary>
    /// <remarks>
    /// <b>Decided 2026-10-07 by the maintainer, E2</b>, in the words quoted on
    /// <see cref="HiddenIdleMinutes"/>: an hour where a visible window was never
    /// closed for idleness before (Q326 a of 2026-10-03, reversed by this), so a window
    /// nobody uses closes overnight and lets an update in. A person's input in the
    /// window counts as use (F4). A new row for the numbers index (F3).
    /// </remarks>
    public const int VisibleIdleMinutes = 60;

    /// <summary>
    /// How long Chromium's cookie store holds a change before it commits it to disk:
    /// <b>30 s</b>, the longest commit window measured among the stores a page writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured 2026-10-03</b>, killing a browser's whole job a set time after a
    /// page wrote: at <c>chromium-1246</c> a cookie was lost in every run killed up to
    /// 29.5 s after the write and kept from 30 s, and at <c>chromium-1247</c> lost up to
    /// 29.2 s and kept from 31.1 s, with the cookie database written 30.0 to 30.1 s
    /// after the cookie; and read in Chromium's source, where the interval is a
    /// constant of the store's batch,
    /// <c>net/extras/sqlite/sqlite_persistent_cookie_store.cc:1234-1237</c>
    /// (<see href="../../../kb/playwright/provisioning-and-timings.md#how-old-a-write-must-be-before-a-hard-kill-keeps-it----measured-2026-10-03">kb</see>,
    /// <see href="../../../kb/playwright/provisioning-and-timings.md#committing-to-disk-sooner-and-session-restore-after-a-hard-kill----measured-2026-10-03">kb</see>).
    /// </para>
    /// <para>
    /// <b>The longest, of what was measured</b>: Chromium's <c>localStorage</c> about
    /// 1 s with the switch every launch carries, its session file 2.5 s, its
    /// preferences 10 s and its cache index 20 s; Firefox's <c>localStorage</c> 5 s,
    /// its cookies at once, and its session file two 15 s timers in series while
    /// somebody is at the machine. Firefox's session file waits up to an hour once
    /// nobody is, which a clean close writes anyway. Q378, the maintainer's words of
    /// 2026-10-04, verbatim: <i>"30 sec. fits nicely in the 1 min. we set under d4.1"</i>,
    /// so nothing shortens this interval and <see cref="BrowserCloseCap"/> is derived
    /// from it.
    /// </para>
    /// </remarks>
    public static TimeSpan ChromiumCookieCommitInterval { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long every clean close BrowserAI makes gives the browser to answer its own
    /// <c>browser_close</c> before the child is ended anyway: twice
    /// <see cref="ChromiumCookieCommitInterval"/>, <b>one minute</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D4.1 and D4.2, decided 2026-10-04 by the maintainer, in his words verbatim.</b>
    /// Of the idle close's cap, thirty seconds until then: <i>"Make it a roomy 1 min. We
    /// want everything nicely saved to disk even on a slow system."</i> Of the shutdown's,
    /// one second until then: <i>"Same 1 min. under option d (lane c)"</i>. So one cap
    /// serves the idle close, the shutdown of a server a client started, the session
    /// host's shutdown before an update, and a reopen's wait for a close in flight,
    /// whether the timer or the caller sent it.
    /// </para>
    /// <para>
    /// <b>Why twice the longest commit window.</b> A clean close flushes everything as
    /// the browser shuts down, and the slowest one timed on 2026-10-03 answered in
    /// 1,163 ms, so a close that answers never meets the cap. A close that does not
    /// answer, a debugger pause armed in the browser or a machine too loaded to finish
    /// one, is ended through its child's stdin, on which <c>@playwright/mcp</c>
    /// force-kills the browser about 1 ms into its own graceful close, and a hard kill
    /// keeps a write only once the browser's own commit timer has run. At twice the
    /// longest of those timers, every write made before the close began has had that
    /// interval twice over when the cap runs out, which is the room a slow system needs,
    /// where a timer fires late. Whether a browser parked on a debugger pause goes on
    /// committing to disk was not measured.
    /// </para>
    /// <para>
    /// ⚠️ <b>Where a client ends the process first, the cap is not what decides.</b> A
    /// server a client started serves its client itself when it is not installed, when
    /// no session host answered within the front's bound, or while an update installs,
    /// and there the client's own kill can come before the cap: Claude Code 2.1.288
    /// lands its <c>taskkill /T /F</c> 0.53 to 1.15 s after it closes the server's
    /// input, and Codex ends its server's job at once, both measured 2026-10-03
    /// (<see href="../../../kb/mcp/protocol.md#what-each-client-does-to-a-stdio-server-when-the-session-ends----measured-2026-10-03">kb</see>).
    /// Nothing on this side works around that; the session host, which no client's kill
    /// reaches, is what keeps a session's browser through a client's exit.
    /// </para>
    /// </remarks>
    public static TimeSpan BrowserCloseCap { get; } = ChromiumCookieCommitInterval * 2;
}
