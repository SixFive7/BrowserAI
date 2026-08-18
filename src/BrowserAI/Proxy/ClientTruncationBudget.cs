// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Proxy;

/// <summary>
/// What the client silently cuts a model-facing string at, in what unit, and
/// which surfaces it applies to — <b>measured</b>, not read off documentation.
/// </summary>
/// <remarks>
/// <para>
/// <b>The documented sentence, verbatim</b>, from
/// <see href="https://code.claude.com/docs/en/mcp">Claude Code's MCP
/// documentation</see>:
/// </para>
/// <para>
/// <i>"Claude Code truncates tool descriptions and server instructions at 2KB
/// each. Keep them concise to avoid truncation, and put critical details near
/// the start."</i>
/// </para>
/// <para>
/// <b>The truncation is POSITIONAL, not semantic.</b> It cuts wherever the limit
/// falls — mid-sentence, mid-word — and everything after it is never seen by the
/// model. So an over-long description fails in the worst way available: the text
/// exists in source, reads correctly in review, and simply never arrives. That is
/// why the budget is a <b>hard build failure at 100%</b> and deliberately has no
/// warning tier: the maintainer's position is that there is plenty of context, so
/// a large string costs nothing worth reporting, and the only interesting event
/// is going over — which is a broken state rather than a tight one.
/// </para>
/// <para>
/// <b>"EACH" MEANS EACH STRING. Measured 2026-08-18 @ Claude Code 2.1.234.</b>
/// <i>Corrected 2026-08-18 (previously "⚠️ EACH DOES NOT SAY EACH WHAT, AND
/// NOBODY HAS CHECKED … the per-string reading is taken because it is the
/// conservative one to be wrong about … an experiment to settle it empirically is
/// commissioned and has not reported").</i> The experiment reported. Claude Code
/// was pointed at a local capture endpoint through <c>ANTHROPIC_BASE_URL</c> and
/// the <c>tools</c> array it sends to the Messages API was read byte-for-byte, so
/// the finding is what the model receives rather than what a model recalls:
/// </para>
/// <list type="bullet">
/// <item><b>Per string, never per tool.</b> A probe tool whose whole serialized
/// entry was <b>4,578 bytes</b> — a 1,500-character description and four
/// 700-character parameter descriptions, every string under the cap — arrived
/// <b>completely intact</b>. Entries of <b>17 KB</b> and <b>20 KB</b> also
/// arrived whole. There is no per-tool bucket, so
/// <c>browserai_init</c>'s 3,360-byte entry is not truncated and never
/// was.</item>
/// <item><b>2,048 UTF-16 characters, not 2,048 bytes.</b> A description of 2,048
/// characters that was <b>6,004 bytes</b> of em dashes arrived whole. Bytes are
/// not counted at all.</item>
/// <item><b>The predicate is <c>&gt; 2048</c>.</b> 2,047 intact, 2,048 intact,
/// 2,049 cut. Measured as a triple in one run.</item>
/// <item><b>UTF-16 code units, not code points</b> — 1,539 code points spread
/// over 3,000 units was truncated. The cut is surrogate-aware: where unit 2,048
/// would split a pair it backs off to <b>2,047</b> and the result stays
/// well-formed.</item>
/// <item><b>Parameter descriptions are not truncated at all.</b> A
/// <c>inputSchema.properties[*].description</c> of <b>20,000</b> characters
/// arrived whole. See <see cref="ParameterDescriptionCharacters"/> for why this
/// type still publishes a cap for them.</item>
/// <item><b>No total budget.</b> 202 tools totalling <b>348,314 bytes</b> of tool
/// entries went in one request with nothing dropped and nothing cut.</item>
/// <item><b>The cut is visible to the model and invisible to us.</b> The client
/// appends the literal <c>"… [truncated]"</c> — U+2026, a space, and
/// <c>[truncated]</c>, 13 characters — so a truncated string arrives at
/// <b>2,061</b> characters. A server cannot see this; it happens after the
/// JSON-RPC response has left. Nothing about it reaches BrowserAI, which is
/// exactly why the gate is a build failure rather than a run-time check.</item>
/// </list>
/// <para>
/// <b>The same cap applies to the server <c>instructions</c></b>, which the
/// client delivers to the model inside a <c>&lt;system-reminder&gt;</c> block in
/// the <i>messages</i> array rather than in the system prompt — cut at 2,048
/// characters with the same suffix. BrowserAI's own is 1,261 characters.
/// </para>
/// <para>
/// <b>It floats.</b> Every figure above is a client-version fact this project
/// does not control. It is recorded in <c>kb/mcp/protocol.md</c> with a row in
/// <c>kb/re-verification.md</c>, and the probe that establishes it is described
/// there well enough to re-run.
/// </para>
/// </remarks>
internal static class ClientTruncationBudget
{
    /// <summary>
    /// The cap, in UTF-16 characters, for the server <c>instructions</c> string
    /// and for a tool <c>description</c>.
    /// </summary>
    /// <remarks>
    /// 2,048. Stated by the documentation quoted in the remarks above for exactly
    /// these two surfaces, and measured there as a <c>&gt; 2048</c> cut on
    /// <see cref="string.Length"/> rather than on a byte count.
    /// <i>Corrected 2026-08-18 (previously named <c>Bytes</c>, and applied to a
    /// UTF-8 byte count).</i> The rename is the point: a byte gate fails a
    /// 2,000-character string that happens to carry em dashes, which the client
    /// delivers whole.
    /// </remarks>
    public const int Characters = 2048;

    /// <summary>
    /// The cap this project holds a <c>description</c> inside an
    /// <c>inputSchema</c> to.
    /// </summary>
    /// <remarks>
    /// <b>The client does not truncate these — measured, 20,000 characters
    /// through intact.</b> <i>Corrected 2026-08-18 (previously "⚠️ ASSUMED, NOT
    /// DOCUMENTED … this applies the same number to them because it is the only
    /// number anybody has").</i> The number is kept, and kept enforced, for two
    /// reasons that are not the old one: it is a client-version fact that floats
    /// and could be tightened by any release, and this is the surface BrowserAI
    /// is most exposed on — one injected <c>session</c> description lands on
    /// fifty-nine upstream tools at once, so the day the client does start
    /// cutting schemas, one string becomes fifty-nine silent truncations.
    /// <b>It is a self-imposed house limit, not a client limit</b>, and must not
    /// be cited as one.
    /// </remarks>
    public const int ParameterDescriptionCharacters = Characters;
}
