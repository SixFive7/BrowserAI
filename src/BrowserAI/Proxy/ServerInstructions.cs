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
/// <b>The mode lines are rendered from
/// <see cref="SessionModes.Lines"/>, never typed here.</b> This is one of the six
/// consumers of the one table, and it is the one where a stale copy would be
/// least visible: it is read by every model on every connection and by no human
/// ever.
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

        Modes — chosen at init, permanent for the session's life:
        {SessionModes.Lines}
        'tracing: true' records the session into its output directory, and works with any of them.

        You must supply an absolute directory. The directory IS the session — its profile, screenshots, downloads and log all live there — so name it for what the work is. You must also supply a one-sentence 'purpose': another agent meeting this directory later reads it.

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
