// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// The one tool BrowserAI keeps out of the surface, and the refusal a caller
/// that names it anyway meets. Both are about <b>liveness</b> rather than
/// permission.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-08-18 (previously "This type is the charter's security
/// trade-off made true": five <see langword="enum"/> tool classes, a written-down
/// <c>(tool, mode)</c> permission matrix, deny-by-default on an unclassified tool
/// and on a mode with no row, and a guard that refused any
/// <c>browser_get_config</c> answer containing <c>"secrets"</c>).</b> The claim
/// was false and could not have been made true at this layer. <b>The matrix was
/// never a boundary against the caller.</b> The calling agent chooses the session
/// directory; the browser profile — cookie database included — is created inside
/// it; the agent runs as the same Windows user, so DPAPI decrypts for it. An
/// agent holding any file tool reads what the matrix declined to hand back, and
/// the matrix cost a lookup on every call to make it take one extra step.
/// <b>Measured 2026-08-18 rather than argued</b>
/// ([kb](../../../kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18)):
/// against a session this product configured, a second process running as the
/// same user recovered the cookie with <c>CryptUnprotectData</c> and AES-256-GCM
/// and nothing else — no elevation, no service, no admin. App-Bound Encryption,
/// the one thing that could have made this false, is <b>not</b> in force for the
/// provisioned build: no <c>app_bound_encrypted_key</c>, and a <c>v10</c> cookie
/// rather than a <c>v20</c> one. Prompt
/// injection is real and is not solved here: an injected model smart enough to
/// want the cookies is smart enough to open the file, and a defeated motivation
/// is not answered by more execution complexity.
/// </para>
/// <para>
/// <b>Change control moved to the release gate, which already covered more than
/// this did.</b> Four golden snapshots — <c>tools-list.json</c> carrying all 69
/// tools <i>with</i> their <c>inputSchema</c>s, <c>cli-help.txt</c>,
/// <c>config-schema.d.ts</c> and <c>browsers.json</c> — are regenerated from the
/// resolved payload and diffed on every build, and
/// [`upstream-review.json`](../../../upstream-review.json) blocks a release until
/// a human adjudicates whatever moved. That catches a tool whose <i>schema</i>
/// changed and a CLI flag that appeared; deny-by-default on a tool <i>name</i>
/// caught neither.
/// </para>
/// <para>
/// <b>What survives is not a permission, and neither half is optional.</b>
/// <c>session</c> stays mandatory — a call naming none is refused rather than
/// reaching the run's own child — because that is <b>routing</b>: it is how a
/// proxy holding N children knows which one a call belongs to, and a default
/// would silently pick one nobody chose. And <see cref="AnnotateTool"/> is
/// withheld from <c>tools/list</c> — see
/// <see cref="IsWithheldFromTheSurface"/> — because the call it advertises
/// cannot return.
/// </para>
/// <para>
/// <b>Nothing here is conditional on the build.</b> No preprocessor branch, no
/// <c>[Conditional]</c>, no configuration-dependent path — the decision a
/// released artifact takes is the decision the suite took, and
/// <c>ModelSurfaceTests</c> reads this file to say so.
/// </para>
/// </remarks>
internal static class SessionToolPolicy
{
    /// <summary>
    /// Upstream's annotation tool: it opens the Playwright Dashboard and blocks
    /// until a human draws in it.
    /// </summary>
    public const string AnnotateTool = "browser_annotate";

    /// <summary>
    /// Whether a tool the child advertises is kept out of the list BrowserAI
    /// advertises. True of <see cref="AnnotateTool"/> and of nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously a refusal keyed on
    /// <c>SessionModeDefinition.Headed</c>: the tool was advertised to every
    /// session, permitted on the two modes that open a window, and refused on the
    /// one that does not).</b> The measurement that justified the refusal also
    /// took the ground out from under permitting it anywhere, so what was a
    /// <c>(tool, mode)</c> decision became a decision about the <i>surface</i>:
    /// the tool is filtered out of <c>tools/list</c> in every mode, and a caller
    /// that names it anyway is refused. <b>Filtering is in scope by the
    /// charter</b> — <i>filter, re-describe, inject <c>session</c></i> — and it
    /// is renaming that is not.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-18, three runs against a real child on the config this
    /// product generates</b>
    /// ([kb](../../../kb/playwright/tools-and-artifacts.md#what-browser_annotate-actually-does--measured-2026-08-18)).
    /// Four facts, and each is its own reason:
    /// </para>
    /// <list type="number">
    /// <item><b>It has no self-timeout.</b> The control run stood silent for the
    /// full 90 s budget; the two that returned did so in the same 40 ms tick
    /// their window disappeared, which is a human closing it. The wait is
    /// <c>await new Promise(resolve =&gt; client.on("exit", ...))</c>, unbounded
    /// by construction. The one bounded arm is the daemon failing to start at
    /// all, at 15 s.</item>
    /// <item><b>The window is a SECOND, non-headless Chromium</b>, at upstream's
    /// <c>--window-position=100,100 --window-size=1280,800</c>, and its pid owns
    /// the visible <c>Chrome_WidgetWin_1</c>. It is not the session's own browser
    /// changing its mind.</item>
    /// <item><b>There is no configuration in which it runs headless.</b> The
    /// dashboard's headedness is
    /// <c>headless: !!process.env.PWTEST_DASHBOARD_APP_BIND_TITLE</c> — an
    /// upstream <i>test</i> variable, which no session config reaches and which
    /// this build's child-environment allowlist would not pass on in any
    /// case.</item>
    /// <item><b>It escapes the session's containment.</b> The daemon is spawned
    /// <c>detached</c>, <c>stdio: "ignore"</c> and <c>unref</c>'d, is a per-USER
    /// singleton on a named pipe, and writes its profile into <c>%TEMP%</c> —
    /// outside every session directory. Of the 18-process tree captured while the
    /// call was blocked, a parent walk after the probe exited found <b>zero</b>:
    /// only the job object collected it.</item>
    /// </list>
    /// <para>
    /// <b>What it would take to put it back</b>, so that this is a reversible
    /// decision rather than a dead end. Three things, and no two of them are
    /// enough: a <b>bounded</b> call, so that no unattended run can hang on it; a
    /// dashboard <b>inside the session's own containment</b>, writing into the
    /// session directory and dying with it rather than living in <c>%TEMP%</c>
    /// under a user-wide singleton; and a <b>headless path that does not depend
    /// on an upstream test variable</b> — a real config key, upstream's or a
    /// documented flag, rather than something named <c>PWTEST_*</c>. The decision
    /// and its consequences are in
    /// [DECISIONS](../../../DECISIONS.md#licence-release-policy-and-the-tool-surface).
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool name, as the child spells it.</param>
    /// <returns>Whether BrowserAI keeps it out of the surface.</returns>
    public static bool IsWithheldFromTheSurface(string? tool) =>
        string.Equals(tool, AnnotateTool, StringComparison.Ordinal);

    /// <summary>
    /// Decides one call. The answer is <see cref="ToolDecision.Allowed"/> for
    /// every tool but one, in every mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The refusal is not decoration for a tool nobody was offered.</b> A
    /// model knows <c>@playwright/mcp</c>'s tool names from everywhere except
    /// this server's <c>tools/list</c>, and a name it remembers is a name it can
    /// send. Forwarding that call would start the daemon, put a window on a
    /// screen nobody is watching, and block until the run was killed — so
    /// withholding the tool without refusing the call would leave the hang
    /// reachable by exactly the caller most likely to reach for it. <b>The
    /// unadvertised half is what saves the model anything:</b> attention and
    /// description budget are spent on the tools in the list, and this is not one
    /// of them.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously
    /// <c>Decide(string tool, SessionModeDefinition mode)</c>, refusing only
    /// where <c>mode.Headed</c> was false).</b> The mode parameter went with the
    /// per-mode answer: the daemon lands in <c>%TEMP%</c> and outlives its parent
    /// on an <c>interactive</c> session exactly as it does on a <c>headless</c>
    /// one, and <i>a human might be at the keyboard</i> was never something a
    /// session record could know. Keeping the parameter and ignoring it would
    /// have left a signature claiming this varies by mode when it does not.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool the caller named.</param>
    /// <returns>Permission, or a refusal a model can act on in one turn.</returns>
    public static ToolDecision Decide(string tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return IsWithheldFromTheSurface(tool)
            ? ToolDecision.Refused(SessionErrors.AnnotationIsNotInTheSurface(tool))
            : ToolDecision.Allowed;
    }
}

/// <summary>Whether one call may proceed, and why not if it may not.</summary>
/// <remarks>
/// A refusal carries its text rather than a code, because the audience is a model
/// deciding what to do next and every text in the catalogue names a fix.
/// </remarks>
internal readonly record struct ToolDecision
{
    private ToolDecision(string? refusal) => Refusal = refusal;

    /// <summary>The call may proceed.</summary>
    public static ToolDecision Allowed { get; } = new(null);

    /// <summary>Why the call was refused, or <see langword="null"/> when it was not.</summary>
    public string? Refusal { get; }

    /// <summary>Whether the call may proceed.</summary>
    public bool IsAllowed => Refusal is null;

    /// <summary>Builds a refusal.</summary>
    /// <param name="refusal">The text the caller reads.</param>
    /// <returns>The decision.</returns>
    public static ToolDecision Refused(string refusal) => new(refusal);
}
