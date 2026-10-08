// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Relay;

/// <summary>
/// Every number the relay waits on, each with where it comes from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each one is a row the numbers index carries</b> (F3, decided 2026-10-08): a
/// value, what it governs and where it came from, so that it can be checked again
/// from its source. None of them was picked for a quiet machine (D3).
/// </para>
/// <para>
/// <b>One set for both clients, derived from the stricter one.</b> Codex gives a
/// tool call 300 s by default, read in its source at 0.155 and 0.160, and Claude
/// Code 30 minutes, read in its binary, both for the one-binary plan of 2026-10-04;
/// the maintainer accepted one set on 2026-10-08 ("r ok") on the ground that a
/// number inside Codex's limit is inside Claude Code's too.
/// </para>
/// </remarks>
internal static class RelayConstants
{
    /// <summary>How long a call is held while there is no background: 150 s.</summary>
    /// <remarks>
    /// <b>Half of Codex's 300 s per tool call</b> (D8 a, then R and S, 2026-10-08), so
    /// the sentence that says why reaches the model before the client gives up on the
    /// call. The background appears in that time at sign-in and after an update; a
    /// relay never starts one itself.
    /// </remarks>
    public static TimeSpan HoldBound { get; } = TimeSpan.FromSeconds(150);

    /// <summary>How long the background may leave every liveness probe unanswered before it is reported hung: 150 s.</summary>
    /// <remarks>
    /// <b>The same half of Codex's limit</b> (D10, R, 2026-10-08), which the maintainer
    /// asked to be forgiving: <i>"on a forgiving timeout"</i>. Nothing is killed and
    /// nothing restarts when it runs out; the calls are answered with a sentence that
    /// sends the person to the log.
    /// </remarks>
    public static TimeSpan HangBound { get; } = TimeSpan.FromSeconds(150);

    /// <summary>How often the relay asks the background whether it is alive while a call is outstanding: 10 s.</summary>
    /// <remarks>
    /// The interval of D3's liveness row, kept when R moved the detector itself to
    /// <see cref="HangBound"/>: fifteen probes fit in one hang bound, so one slow
    /// answer never decides anything.
    /// </remarks>
    public static TimeSpan ProbeInterval { get; } = TimeSpan.FromSeconds(10);

    /// <summary>How often the relay looks for the background while it holds a call: 500 ms.</summary>
    /// <remarks>
    /// <b>About one start of the background through the Task Scheduler</b>, measured at
    /// 514 to 674 ms (the one-binary measurement of 2026-10-04): a held call waits for
    /// the background and not for the relay's next look. Looking costs one failed pipe
    /// open.
    /// </remarks>
    public static TimeSpan LookWhileHolding { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How often the relay looks for the background while it holds nothing: 2 s.</summary>
    /// <remarks>
    /// Nothing waits on these looks: the relay answers the handshake and the tool list
    /// from the binary, so a connection made 2 s late costs no call anything. A
    /// slower pace keeps an idle relay's cost at one failed pipe open every 2 s.
    /// </remarks>
    public static TimeSpan LookWhileIdle { get; } = TimeSpan.FromSeconds(2);

    /// <summary>The relay's fixed activity countdown: 10 minutes.</summary>
    /// <remarks>
    /// <b>U1 and H1, decided 2026-10-08.</b> Every message the client sends except
    /// <c>ping</c> restarts it; while it runs, the relay holds updates. When it runs
    /// out nothing happens to the relay: in the maintainer's words, <i>"the relay is
    /// NOT terminated"</i>. It is fixed, unlike the browsers' countdowns, which an agent
    /// may change.
    /// </remarks>
    public static TimeSpan IdleCountdown { get; } = TimeSpan.FromMinutes(10);

    /// <summary>The least time between two activity reports to the background: 1 s.</summary>
    /// <remarks>
    /// The background shows each relay's countdown on the dashboard and the update
    /// toast to the second, so a second is the finest change anyone can see. A client
    /// that sends a burst costs the background one report a second, and the last
    /// value of the burst is always sent.
    /// </remarks>
    public static TimeSpan ActivityReportGap { get; } = TimeSpan.FromSeconds(1);
}
