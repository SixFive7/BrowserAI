// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// The durations BrowserAI reads about a session, declared once.
/// </summary>
/// <remarks>
/// <para>
/// <i>Corrected 2026-10-09 (previously "The durations both executables read about a
/// session")</i>: one executable since D7 a, 2026-10-08, and the paragraphs below are
/// the record of why the class exists from the days of two.
/// </para>
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

    /// <summary>
    /// How often a visible session's browser window is checked for a person's keyboard
    /// or mouse input: every <b>two seconds</b>, by one timer for every visible session
    /// in the process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>CHOSEN 2026-10-08, and not measured.</b> <i>"Every few seconds"</i> is the
    /// maintainer's own phrase for the check (root, 2026-10-08, F4), and the root's
    /// step-0 measurement of the bare reads that day was taken at exactly this cadence,
    /// one check every two seconds for five minutes. Two seconds is short against the
    /// shortest idle countdown an agent can set, one minute, so input is counted at most
    /// an interval and its tolerance after it was made; and it is more than one second,
    /// which is what Microsoft's power guidance asks of a periodic timer: <i>"set the
    /// interval to a value greater than one second"</i>
    /// (<see href="https://learn.microsoft.com/windows/win32/sync/waitable-timer-objects">Waitable Timer Objects</see>).
    /// </para>
    /// <para>
    /// ⚠️ <b>Two rows of the numbers index of decision F3</b>, this and
    /// <see cref="VisibleInputCheckTolerance"/>, in
    /// <see href="../../../kb/numbers.md">kb/numbers.md</see> since 2026-10-09
    /// (previously "a new number for the numbers index of decision F3, which does not
    /// exist yet").
    /// </para>
    /// <para>
    /// What a check costs, and what the timer costs over more than ten minutes against a
    /// process with none, was measured on 2026-10-08 and is in
    /// <see href="../../../kb/windows/processes.md">kb/windows/processes.md</see>.
    /// </para>
    /// </remarks>
    public static TimeSpan VisibleInputCheckInterval { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How late Windows may run each visible-input check so that its wake-up can share
    /// one with another timer's: <b>one second</b>, half the interval.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>CHOSEN 2026-10-08, and not measured.</b> It is the tolerable delay handed to
    /// <c>SetWaitableTimerEx</c>, which is what lets Windows fold the check's wake-up
    /// into another timer's; <c>SetTimer</c>, <c>CreateTimerQueueTimer</c> and
    /// <c>Sleep</c> offer no such room
    /// (<see href="https://learn.microsoft.com/windows-hardware/test/assessments/results-for-the-idle-energy-efficiency-assessment#issues">Idle Energy Efficiency Assessment</see>).
    /// With one second of room, two checks are at most three seconds apart on the
    /// timer's own terms, a twentieth of the shortest countdown.
    /// </para>
    /// <para>
    /// ⚠️ <b>A row of the numbers index of decision F3</b>, beside
    /// <see cref="VisibleInputCheckInterval"/>'s, in
    /// <see href="../../../kb/numbers.md">kb/numbers.md</see> since 2026-10-09
    /// (previously "which does not exist yet").
    /// </para>
    /// </remarks>
    public static TimeSpan VisibleInputCheckTolerance { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a session's child gets to answer <c>initialize</c> before it is called
    /// hung: 10 minutes. The value of <c>ChildConnection.ChildInitializationHang</c>,
    /// whose remarks say why.
    /// </summary>
    public static TimeSpan ChildInitializationHang { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long the stderr reader of a child is given to drain after the child exits:
    /// 2 s. The value of <c>ChildProcessSession.StandardErrorDrainTimeout</c>.
    /// </summary>
    public static TimeSpan ChildStandardErrorDrain { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a child gets to exit after its stdin closes before its job is closed:
    /// 5 s. The value of <c>ChildProcessOptions.DefaultShutdownTimeout</c>.
    /// </summary>
    public static TimeSpan ChildShutdown { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long asking a browser build for its headed user agent may take before
    /// BrowserAI launches with its own: 30 s. The value of <c>HeadedUserAgent.AskBound</c>.
    /// </summary>
    public static TimeSpan HeadedUserAgentAskBound { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How recently an instance directory may have been touched and still be spared by
    /// a sweep: 5 minutes. The value of <c>InstanceDirectory.YoungEnoughToStillBeStarting</c>.
    /// </summary>
    public static TimeSpan InstanceDirectoryYoungAge { get; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often a headed session whose client went is asked whether its browser is
    /// still up: 15 s. The value of <c>LiveSession.DetachedWindowLook</c>.
    /// </summary>
    public static TimeSpan DetachedWindowLook { get; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a reader waits out a rename that is replacing the file it opens: 30 s.
    /// The value of <c>RenameWindow.Budget</c>.
    /// </summary>
    public static TimeSpan RenameBudget { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The first pause of the loop that waits out a rename, in milliseconds: 5, doubled
    /// after every refusal. Read by <c>RenameWindow</c>.
    /// </summary>
    public const int RenameRetryFirstDelayMilliseconds = 5;

    /// <summary>
    /// The longest pause of that loop, in milliseconds: 100.
    /// </summary>
    public const int RenameRetryLongestDelayMilliseconds = 100;

    /// <summary>
    /// How long a rename of a session-index entry keeps retrying: 500 ms. The value of
    /// <c>SessionIndex.MoveBudget</c>.
    /// </summary>
    public static TimeSpan IndexMoveBudget { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The first pause between two tries of that rename, in milliseconds: 5, doubled
    /// after every refusal. Read by <c>SessionIndex</c>.
    /// </summary>
    public const int IndexMoveRetryFirstDelayMilliseconds = 5;

    /// <summary>
    /// The longest pause between two tries of that rename, in milliseconds: 50.
    /// </summary>
    public const int IndexMoveRetryLongestDelayMilliseconds = 50;

    /// <summary>
    /// How old the session index's own rename litter must be before a sweep clears it:
    /// 1 hour. The value of <c>SessionIndex.LitterAge</c>.
    /// </summary>
    public static TimeSpan IndexLitterAge { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The first pause of the loop that waits out a transient refusal to open a
    /// session's record, in milliseconds: 5, doubled after every refusal. Read by
    /// <c>SessionLock.ReadRecord</c>.
    /// </summary>
    public const int RecordReadRetryFirstDelayMilliseconds = 5;

    /// <summary>
    /// The longest pause of that loop, in milliseconds: 100.
    /// </summary>
    public const int RecordReadRetryLongestDelayMilliseconds = 100;

    /// <summary>
    /// How long BrowserAI waits for a page's own tool to answer: 60 s. The value of
    /// <c>SessionToolSurface.PageToolBudget</c>.
    /// </summary>
    public static TimeSpan PageToolBudget { get; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Upstream's own idle timeout, in milliseconds: one hour, which no launch writes
    /// since 2026-10-08. The value of <c>BrowserConfiguration.IdleTimeoutMilliseconds</c>.
    /// </summary>
    public const int UpstreamIdleTimeoutMilliseconds = 3_600_000;

    /// <summary>
    /// How long provisioning may make no progress at all before its job is closed: 10
    /// minutes. The default of <c>ProvisioningTimers.StallCap</c>.
    /// </summary>
    public static TimeSpan ProvisioningStallCap { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long everything after a browser's own directory first appears may take: 10
    /// minutes. The default of <c>ProvisioningTimers.ExtractionCap</c>.
    /// </summary>
    public static TimeSpan ProvisioningExtractionCap { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often provisioning's phase watcher looks: 1 s. The default of
    /// <c>ProvisioningTimers.Poll</c>.
    /// </summary>
    public static TimeSpan ProvisioningPoll { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Playwright's own per-socket stall timeout on a browser download, which BrowserAI
    /// never sets: 30 s. The value of <c>BrowserProvisioner.UpstreamStallTimeout</c>.
    /// </summary>
    public static TimeSpan UpstreamDownloadStallTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The bounded wait on a session directory's own gate: 120 s. The value of
    /// <c>LockScopes.PerDirectoryGate</c>, whose remarks say why.
    /// </summary>
    public static TimeSpan PerDirectoryGate { get; } = TimeSpan.FromSeconds(120);

    // ⚠️ DELETED 2026-10-10, by the maintainer's decision "9 a": LiveInstanceGate,
    // 5 s, the value of LockScopes.LiveInstanceGate, the bounded wait on the
    // live-instance set's own gate. LiveInstances.Join and LiveInstances.Census took
    // it, nothing called either after the one-binary build of 2026-10-08, and the
    // three went together. The reclaim that is left takes that gate at
    // LockScopes.NeverWaits. Its row left kb/numbers.md the same day.
}