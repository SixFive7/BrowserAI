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
/// <b>It carries the choice itself and not a pointer to it.</b> Claude Code
/// loads tool <i>names</i> and the server <c>instructions</c> eagerly and defers
/// schemas, so this is the only channel that arrives before the first mistake --
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
/// <i>Corrected 2026-08-18 (previously "Hard cap 2 KB ... <c>ModelSurfaceTests</c>
/// measures it in <b>bytes</b> rather than characters -- <c>·</c> and <c>--</c> are
/// two and three bytes of UTF-8 apiece, so a character count would under-report
/// exactly the string that uses them").</i> That was the unit this file's own
/// constant, <c>ClientTruncationBudget</c> and <c>ModelSurfaceTests</c> were all
/// corrected away from on 2026-08-18, measured @ Claude Code 2.1.234 -- the cut
/// is on <c>string.Length</c> and a byte count is never consulted. The sweep
/// changed the constants and the test and left the paragraph that argued for the
/// old reading standing twenty lines above the corrected one, which is why
/// nothing went red.
/// </para>
/// <para>
/// ⚠️ <b>The mode lines are gone, 2026-08-20 (previously three lines rendered
/// from <c>SessionModes.Lines</c>, never typed here -- "one of the six consumers
/// of the one table").</b> There is no table: every capability is granted to
/// every session and headedness is a per-run argument, so there is nothing left
/// for a model to choose before it calls <c>browserai_init</c>.
/// </para>
/// <para>
/// <b>What took the space is the response-mocking warning, and it is here
/// and not on the tool.</b> <c>browser_route</c> became reachable in the
/// same change. A rule installed with it can make a page lie to a human watching
/// a headed window -- the browser renders the mock, the address bar keeps the real
/// origin, and nothing on screen says a rule is in force. Upstream's own
/// description says what the tool does and cannot say what BrowserAI knows about
/// the window it is being called against, and every upstream description passes
/// through this proxy byte for byte, so the warning has to live in the one
/// string BrowserAI writes itself. It is here and not in
/// <c>browserai_init</c>'s description for the reason the whole file exists:
/// this arrives before the first call, and a description arrives after the model
/// has already decided to make one.
/// </para>
/// <para>
/// <b>The <c>fullPage</c> line, added 2026-08-20, is here for the same reason
/// and is a cost fact, not a warning.</b> <b>Nothing downscales an image
/// on the way back</b> -- not BrowserAI, which appends what is on disk, and since
/// <c>playwright-core</c> 1.63.0-alpha-2026-08-31 not upstream either, which
/// deleted <c>scaleImageToFitMessage</c> outright
/// ([kb](../../../kb/playwright/tools-and-artifacts.md#what-it-costs)).
/// </para>
/// <para>
/// ⚠️ <b>REWRITTEN 2026-09-15, because the sentence had become false.</b>
/// <i>Previously: "'fullPage: true' costs the per-image token maximum on any
/// page worth using it on: it leaves at full document height and is downscaled
/// to that ceiling", justified as -- measured 2026-08-20 -- a 1920x1080 viewport
/// shot arriving as <b>2,691 visual tokens</b> against <c>fullPage: true</c>
/// over a 3,637 px document leaving as 1920x3637 =
/// <c>⌈1920/28⌉ × ⌈3637/28⌉</c> = <b>8,970</b>, downscaled to a per-image
/// ceiling of <b>4,784</b>, break-even at about 1,960 px.</i> <b>Both halves of
/// that were wrong to tell a model.</b> The first half made a ceiling sound like
/// a cap on what the call costs, when what it caps is what one image can be
/// billed as; the second told the model the image it receives has been shrunk,
/// which is the opposite of what happens. <b>Measured 2026-09-14</b> through a
/// raw child at three viewports, both page shapes and three encodings
/// (<c>docs/evidence/2026-09-14-probes-m2c/</c>): the inline block is
/// <b>byte-identical to the file in every case</b>, and bytes follow pixels with
/// no ceiling anywhere -- a <c>fullPage</c> shot of a 20,016 px document came
/// back as 1280x20016 and 3,724,372 bytes. <b>What replaced the ceiling is the
/// lever the old sentence never mentioned</b>: passing <c>filename</c>
/// suppresses the inline image entirely, so the answer is a link to a file on
/// disk and the image costs nothing at all until something opens it.
/// </para>
/// <para>
/// ⚠️ <b>Two clauses were cut on 2026-08-26 to pay for the browser-installation
/// line, and what was cut is recorded and not left to a diff.</b> The string
/// was <b>2,207</b> characters with the new sentence in and the cap is 2,048, so
/// something had to go. Cut: <i>"That is deliberate rather than an obstacle: it
/// turns an accidental collision into a stated intent"</i> -- a justification for
/// a refusal that explains itself in its own message when it fires -- and
/// <i>"which is what lets the next agent read back what was being attempted
/// rather than only which tools ran"</i>, which said in a subordinate clause
/// what the <c>browserai_catch_up</c> sentence beside it already says. Both were
/// rationale for something else in the same paragraph; nothing that tells a
/// model what to DO was touched. <b>1,993 characters, 55 of headroom</b> --
/// measured, not estimated, and the next addition has to find its own space the
/// same way.
/// </para>
/// <para>
/// ⚠️ <b>Six sentences were tightened on 2026-09-21 to pay for the
/// session-deletion line, and what changed is recorded here and not left to
/// a diff.</b> The string was <b>2,022 characters with 26 of headroom</b> --
/// measured off the published binary's own <c>initialize</c> response, not
/// estimated -- and the new clause is 105, so something had to give.
/// <b>Nothing was dropped.</b> Every rule that was in this string is still in
/// it; what moved is wording. <i>"Nothing is chosen at init that a later call
/// has to live with"</i> became <i>"Nothing chosen at init binds a later
/// call"</i>; <i>"records the session"</i> became <i>"records the run"</i>,
/// which is the more accurate of the two because tracing is per-run;
/// <i>"You must supply an absolute directory ... You must also supply a
/// one-sentence 'purpose'"</i> became one sentence asking for both;
/// <i>"takes 'why', and it is required"</i> became <i>"takes a required
/// 'why'"</i>; the mocking warning lost four words and <b>kept <i>on
/// screen</i></b>, which is the clause that makes it a warning about what a
/// human SEES and not about what a tool does; and the tool roll-call became
/// one sentence so the deletion line could follow it.
/// <b>2,026 characters, 22 of headroom</b> -- measured the same way, and the next
/// addition has to find its own space the same way too.
/// </para>
/// <para>
/// <b>The deletion line is the short half of a rule stated in three places.</b>
/// Settled 2026-09-21: BrowserAI never deletes a session on its own, so the
/// agent that created one destroys it when the work is done, and promptly when
/// it held a login. This string carries the obligation in one clause and nothing
/// else, because 22 characters is what it has; the reason -- the cookies are in
/// the profile and stay on disk until then -- is on <c>browserai_init</c>'s and
/// <c>browserai_destroy</c>'s descriptions, which had far more room.
/// <c>ModelSurfaceTests.TheAgentIsToldThatDestroyingTheSessionsItMakesIsItsOwnJob</c>
/// holds all three against the published wire.
/// </para>
/// <para>
/// ⚠️ <b>The file-upload roots sentence is deliberately NOT here, and the
/// budget is the whole reason.</b> A file a tool may name has to be inside
/// <c>&lt;session&gt;\output</c>, because that is what this project's
/// <c>allowUnrestrictedFileAccess: false</c> leaves -- and it is about
/// <c>browser_file_upload</c>, an UPSTREAM tool whose description passes through
/// byte for byte, so the instinct is to put it in the one string BrowserAI
/// writes. It went on <c>browserai_init</c>'s description instead, beside
/// <i>the directory IS the session</i>, which is the claim it qualifies. It
/// would not fit here: the sentence is 176 characters and this string has 22.
/// </para>
/// <para>
/// <b>The browser-installation line, added 2026-08-26, is a pre-emption and
/// not a fact.</b> Every published account of a broken Playwright install ends
/// in <c>npx playwright install</c>, and a model that runs it here either fails
/// or succeeds into a second browser tree in a second location BrowserAI will
/// never launch from. <see cref="Runtime.ProvisioningRemediation"/> undoes that
/// advice when it arrives <i>in an answer</i>; nothing can undo it when the
/// model supplies it from its own training, which is why the pre-emption has to
/// be in the one string that arrives before the first call. The wording is the
/// maintainer's and <c>ModelSurfaceTests</c> holds it verbatim: the two halves --
/// <i>never install any yourself</i> and <i>this is the repair</i> -- are each
/// useless without the other.
/// </para>
/// <para>
/// ⚠️ <b>It must never be appended to <c>browser_take_screenshot</c>'s
/// description instead.</b> That is where a reader's instinct sends it, and the
/// append path was <b>deleted</b> on 2026-08-18 so that every upstream
/// description passes through byte for byte -- see the note above
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
    /// the messages array, cut at 2,048 with <c>"... [truncated]"</c> appended.
    /// </remarks>
    public const int MaximumCharacters = ClientTruncationBudget.Characters;

    /// <summary>The instructions sent on <c>initialize</c>.</summary>
    public static string Text { get; } =
        $"""
        BrowserAI drives a real browser. Call {SessionToolSurface.Init} first: it returns the session directory every other tool requires as 'session'. There is no default and BrowserAI never guesses one.

        Every session gets every tool. Nothing chosen at init binds a later call: 'headed: true' opens a window and 'tracing: true' records the run, both per-run rather than bound to the directory.

        'fullPage: true' leaves at full document height and nothing downscales it: cost follows pixels, with no ceiling. Pass 'filename' for a link to the file and no inline image.

        Browsers are managed by BrowserAI -- never install any yourself (no `npx playwright install`). If the browser installation is broken, `browserai_reinstall_browser` is the repair.

        Supply an absolute directory and a one-sentence 'purpose'. The directory IS the session -- its profile, screenshots, downloads and log live there -- so name it for the work, and write the purpose for the next agent that meets it.

        Every call that NAMES a session also takes a required 'why'. Write why you are making the call, not what it does -- the tool name already says that. It goes in the session's record, and {SessionToolSurface.CatchUp} reads it back beside what the directory holds now: call it when you arrive at a session you did not create, and before you destroy one.

        WARNING -- browser_route and browser_network_state_set change what the page IS, not just what you see. A mocked response renders as if the server sent it: the address bar keeps the real origin and nothing on screen says otherwise, so a human watching a headed window is seeing something you made up. Say so in 'why' and to the human, and browser_unroute when you are done.

        {SessionToolSurface.Init} refuses a directory that is already a session and directs you to {SessionToolSurface.Resume}; {SessionToolSurface.List} reports the sessions beneath a directory, {SessionToolSurface.SetPurpose} rewrites what one says it is for, and {SessionToolSurface.Destroy} deletes one. Nothing else ever deletes a session: destroy yours when the work is done, and promptly if it held a login.
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
    /// Reported, not gated: it is what the string costs to transmit, and
    /// it is <b>not</b> what the client truncates on -- this string carries
    /// <c>·</c> (2 bytes) and <c>--</c> (3 bytes), so the two figures differ and
    /// the byte one is the larger and the wrong one.
    /// </remarks>
    public static int ByteCount { get; } = Encoding.UTF8.GetByteCount(Text);
}
