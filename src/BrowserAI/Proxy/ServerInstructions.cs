// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Sessions;

namespace BrowserAI.Proxy;

/// <summary>
/// The one string that reaches a model <b>before</b> it calls anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries the choice itself rather than a pointer to it.</b> Claude Code
/// loads tool <i>names</i> and the server <c>instructions</c> eagerly and defers
/// schemas, so this is the only channel that arrives before the first mistake —
/// and the first mistake after a restart is calling a browser tool with no
/// session. A sentence here that says "see <c>browserai_init</c>'s description"
/// is a sentence that arrives after the failure it was meant to prevent.
/// </para>
/// <para>
/// <b>Hard cap 2,048 UTF-16 characters, and the truncation is silent.</b> The
/// client cuts both this string and every tool description with nothing
/// reported: the tail simply does not exist, and a paragraph past the cut is a
/// paragraph nobody has ever read. <c>ModelSurfaceTests</c> gates on
/// <b>characters</b>, matching <see cref="ClientTruncationBudget"/> and
/// <see cref="MaximumCharacters"/> below.
/// <i>Corrected 2026-08-18 (previously "Hard cap 2 KB … <c>ModelSurfaceTests</c>
/// measures it in <b>bytes</b> rather than characters — <c>·</c> and <c>—</c> are
/// two and three bytes of UTF-8 apiece, so a character count would under-report
/// exactly the string that uses them").</i> That was the unit this file's own
/// constant, <c>ClientTruncationBudget</c> and <c>ModelSurfaceTests</c> were all
/// corrected away from on 2026-08-18, measured @ Claude Code 2.1.234 — the cut
/// is on <c>string.Length</c> and a byte count is never consulted. The sweep
/// changed the constants and the test and left the paragraph that argued for the
/// old reading standing twenty lines above the corrected one, which is why
/// nothing went red.
/// </para>
/// <para>
/// ⚠️ <b>The mode lines are gone, 2026-08-20 (previously three lines rendered
/// from <c>SessionModes.Lines</c>, never typed here — "one of the six consumers
/// of the one table").</b> There is no table: every capability is granted to
/// every session and headedness is a per-run argument, so there is nothing left
/// for a model to choose before it calls <c>browserai_init</c>.
/// </para>
/// <para>
/// <b>What took the space is the response-mocking warning, and it is here
/// rather than on the tool.</b> <c>browser_route</c> became reachable in the
/// same change. A rule installed with it can make a page lie to a human watching
/// a headed window — the browser renders the mock, the address bar keeps the real
/// origin, and nothing on screen says a rule is in force. Upstream's own
/// description says what the tool does and cannot say what BrowserAI knows about
/// the window it is being called against, and every upstream description passes
/// through this proxy byte for byte, so the warning has to live in the one
/// string BrowserAI writes itself. It is here rather than in
/// <c>browserai_init</c>'s description for the reason the whole file exists:
/// this arrives before the first call, and a description arrives after the model
/// has already decided to make one.
/// </para>
/// <para>
/// <b>The <c>fullPage</c> line, added 2026-08-20, is here for the same reason
/// and is a cost fact rather than a warning.</b> BrowserAI diverges from
/// upstream before <c>scaleImageToFitMessage</c> and appends what is on disk, so
/// <b>what the viewport renders is what the model receives</b>
/// ([kb](../../../kb/playwright/tools-and-artifacts.md#what-it-costs)). Measured
/// 2026-08-20: a viewport shot at the 1920x1080 default arrives as
/// <b>2,691 visual tokens</b>; the same page with <c>fullPage: true</c> over a
/// 3,637 px document leaves as 1920x3637, which is
/// <c>⌈1920/28⌉ × ⌈3637/28⌉ =</c> <b>8,970</b>, and the API downscales that to
/// its per-image ceiling of <b>4,784</b>. The break-even is a document about
/// 1,960 px tall, so <i>every full-page shot of a page long enough to want one</i>
/// costs the maximum — which is why the sentence says "any page worth using it
/// on" rather than "always": on a page that does not scroll the two are the same
/// image.
/// </para>
/// <para>
/// ⚠️ <b>It must never be appended to <c>browser_take_screenshot</c>'s
/// description instead.</b> That is where a reader's instinct sends it, and the
/// append path was <b>deleted</b> on 2026-08-18 so that every upstream
/// description passes through byte for byte — see the note above
/// <c>SessionToolSurface.InjectSession</c>. <c>ModelSurfaceTests</c> holds both
/// halves: the sentence is in this string, and
/// <c>browser_take_screenshot</c>'s description is still upstream's own bytes.
/// </para>
/// </remarks>
internal static class ServerInstructions
{
    /// <summary>
    /// What the client silently truncates at, and therefore what this string
    /// must fit inside.
    /// </summary>
    /// <remarks>
    /// One of the two surfaces <see cref="ClientTruncationBudget"/> quotes the
    /// documentation for by name, and one of the two it was measured on. The
    /// number lives there so that all three model-facing surfaces cannot drift
    /// apart. <i>Corrected 2026-08-18 (previously <c>MaximumBytes</c>, over a
    /// UTF-8 byte count).</i> The client counts UTF-16 characters and never
    /// bytes; it delivers this string inside a <c>&lt;system-reminder&gt;</c> in
    /// the messages array, cut at 2,048 with <c>"… [truncated]"</c> appended.
    /// </remarks>
    public const int MaximumCharacters = ClientTruncationBudget.Characters;

    /// <summary>The instructions sent on <c>initialize</c>.</summary>
    public static string Text { get; } =
        $"""
        BrowserAI drives a real browser. Call {SessionToolSurface.Init} first: it returns a session directory that every other tool requires as 'session'. There is no default and BrowserAI never guesses one.

        Every session gets every tool. Nothing is chosen at init that a later call has to live with: 'headed: true' opens a window, 'tracing: true' records the session, and both are per-run rather than bound to the directory.

        'fullPage: true' costs the per-image token maximum on any page worth using it on: it leaves at full document height and is downscaled to that ceiling.

        You must supply an absolute directory. The directory IS the session — its profile, screenshots, downloads and log all live there — so name it for what the work is. You must also supply a one-sentence 'purpose': another agent meeting this directory later reads it.

        Every call that NAMES a session also takes 'why', and it is required. Write why you are making the call, not what it does — the tool name already says that. It goes in the session's log, which is what lets the next agent read back what was being attempted rather than only which tools ran. {SessionToolSurface.CatchUp} reads that log back, beside what the directory actually holds now — call it when you arrive at a session you did not create, and before you destroy one.

        WARNING — browser_route and browser_network_state_set change what the page IS, not just what you see. A mocked response renders as if it came from the server: the address bar keeps the real origin and nothing on screen says a rule is in force, so a human watching a headed window is looking at something you made up. Say so in 'why' and to the human, and call browser_unroute when you are done.

        {SessionToolSurface.Init} refuses a directory that already holds a session and directs you to {SessionToolSurface.Resume}. That is deliberate rather than an obstacle: it turns an accidental collision into a stated intent. {SessionToolSurface.List} reports the sessions beneath a directory, {SessionToolSurface.Destroy} deletes one, {SessionToolSurface.SetPurpose} rewrites what one says it is for.
        """;

    /// <summary>How many characters <see cref="Text"/> costs of the budget.</summary>
    /// <remarks>
    /// The gate, because characters are what the client counts. Measured
    /// 2026-08-18 @ Claude Code 2.1.234 off the wire it actually sends.
    /// </remarks>
    public static int CharacterCount { get; } = Text.Length;

    /// <summary>
    /// How many UTF-8 bytes <see cref="Text"/> costs on the JSON-RPC wire.
    /// </summary>
    /// <remarks>
    /// Reported rather than gated: it is what the string costs to transmit, and
    /// it is <b>not</b> what the client truncates on — this string carries
    /// <c>·</c> (2 bytes) and <c>—</c> (3 bytes), so the two figures differ and
    /// the byte one is the larger and the wrong one.
    /// </remarks>
    public static int ByteCount { get; } = Encoding.UTF8.GetByteCount(Text);
}
