// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Proxy;

/// <summary>
/// What the client silently cuts a model-facing string at, and which of the
/// three surfaces that number is <b>documented</b> for.
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
/// exists in source, reads correctly in review, and simply never arrives. There
/// is no error, no warning and no signal of any kind. That is why the budget is
/// a <b>hard build failure at 100%</b> and deliberately has no warning tier: the
/// maintainer's position is that there is plenty of context, so a large string
/// costs nothing worth reporting, and the only interesting event is going over —
/// which is a broken state rather than a tight one.
/// </para>
/// <para>
/// ⚠️ <b>"EACH" DOES NOT SAY EACH WHAT, AND NOBODY HAS CHECKED.</b> This constant
/// is applied <b>per string</b> — one budget for the <c>instructions</c>, one per
/// tool <c>description</c>, one per parameter <c>description</c>. That is an
/// <b>assumption</b>, not something the documentation states. The competing
/// reading is that the cap is per <i>tool</i>: description plus serialized schema
/// plus every parameter description, all in one 2 KB bucket. Under that reading
/// the arithmetic here is wrong, and trimming a description to fit would merely
/// move text from one capped bucket into the same capped bucket — a maintainer of
/// another MCP server reports hitting exactly that. The per-string reading is
/// taken because it is the conservative one to be wrong about and it is cheap:
/// every string that fits per-string may still overflow a per-tool total, so this
/// gate can be too weak but never too strong. <b>An experiment to settle it
/// empirically is commissioned and has not reported.</b>
/// <c>ModelSurfaceTests.EveryModelFacingStringFitsTheClientsSilentTruncationBudget</c>
/// reports each tool's whole-entry total beside the per-string figures, so the
/// data that experiment needs is produced on every run.
/// </para>
/// <para>
/// <b>Bytes rather than characters, and both are measured.</b> It is not
/// documented whether the client counts characters or bytes, and the two diverge
/// on the first em dash. For UTF-8 the byte count is never below the character
/// count, so bytes is the conservative gate — but the test reports both, because
/// a figure nobody can see is a figure nobody can act on.
/// </para>
/// </remarks>
internal static class ClientTruncationBudget
{
    /// <summary>
    /// The documented cap, in bytes, for the server <c>instructions</c> string
    /// and for a tool <c>description</c>.
    /// </summary>
    /// <remarks>
    /// 2 KB, stated by the documentation quoted in the remarks above for exactly
    /// these two surfaces. Read the ⚠️ paragraph before treating it as a per-tool
    /// total: the documentation's <i>"each"</i> is unresolved.
    /// </remarks>
    public const int Bytes = 2048;

    /// <summary>
    /// The cap applied to a parameter <c>description</c> inside an
    /// <c>inputSchema</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ASSUMED, NOT DOCUMENTED.</b> The sentence quoted above names tool
    /// descriptions and server instructions and says nothing whatever about the
    /// strings inside a schema. This applies the same number to them because it
    /// is the only number anybody has, and because a parameter description that
    /// overflows would fail the same silent way. Do not cite it as a documented
    /// limit; it is a conservative assumption, and the surface it guards is the
    /// one where BrowserAI's own injected <c>session</c> description lands on
    /// every upstream tool at once.
    /// </remarks>
    public const int ParameterDescriptionBytes = Bytes;
}
