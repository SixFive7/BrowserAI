// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// The one refusal BrowserAI takes on a browser call, and it is about
/// <b>liveness</b> rather than about permission.
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
/// would silently pick one nobody chose. And <see cref="AnnotateTool"/> stays
/// refused on a windowless session, for the reason on <see cref="Decide"/>.
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
    /// Decides one call. The answer is <see cref="ToolDecision.Allowed"/> for
    /// every tool but one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The refusal is a liveness guard and carries no security claim.</b>
    /// <see cref="AnnotateTool"/> opens the Playwright Dashboard and waits for a
    /// human to draw — and <b>the window appears on a headless session too</b>,
    /// so a windowless session that called it would put an unexplained window on
    /// a screen nobody is watching and then <b>hang until the run is killed</b>.
    /// Unattended overnight runs are this product's primary use, and one hung
    /// call takes the whole run with it. Nothing about this protects any secret;
    /// it stops a session deadlocking.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-18, three runs, after standing undated for the life of
    /// the decision it justified</b>
    /// ([kb](../../../kb/playwright/tools-and-artifacts.md#what-browser_annotate-actually-does--measured-2026-08-18)).
    /// Against a real child on the config this product generates for
    /// <c>headless</c>: a <b>visible</b> <c>Chrome_WidgetWin_1</c> at
    /// <c>100,100,1280x800</c> took the <b>foreground</b> within 1.2 s on every
    /// run, and the call was still silent 90 s later in the arm nothing
    /// interrupted. The window is not the session's own browser changing its
    /// mind — it is a <i>second</i>, non-headless Chromium under a detached
    /// dashboard daemon, launched headed on an upstream test variable that no
    /// session configuration reaches — so <c>launchOptions.headless</c> cannot
    /// prevent it and neither can anything else this proxy sets. The only
    /// bounded arm is the daemon failing to start, at 15 s.
    /// </para>
    /// <para>
    /// <b>It keys on <see cref="SessionModeDefinition.Headed"/> rather than on a
    /// mode name</b>, because the fact it turns on is "was a window expected" and
    /// that is the mode table's own column. A mode added to the table is
    /// therefore judged by what it promises rather than by whether anybody
    /// remembered this file.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool the caller named.</param>
    /// <param name="mode">The mode recorded for the session the caller named.</param>
    /// <returns>Permission, or a refusal a model can act on in one turn.</returns>
    public static ToolDecision Decide(string tool, SessionModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(mode);

        return !mode.Headed && string.Equals(tool, AnnotateTool, StringComparison.Ordinal)
            ? ToolDecision.Refused(SessionErrors.AnnotationWouldHangAWindowlessSession(tool, mode))
            : ToolDecision.Allowed;
    }

    /// <summary>
    /// What BrowserAI appends to one upstream tool's description, or
    /// <see langword="null"/> when it appends nothing — which is every tool but
    /// <see cref="AnnotateTool"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rewrite is append-only, and this is the whole of it.</b> Upstream's
    /// sentence is never trimmed or re-framed: a phrase dropped in a rewrite is a
    /// warning the model no longer receives, and nothing fails when that happens —
    /// the tool still works and the model simply stops being told the thing that
    /// stopped it doing something stupid. <c>ModelSurfaceTests</c> asserts a
    /// declared list of upstream phrases survives, per tool, which is the only
    /// check that catches it.
    /// </para>
    /// <para>
    /// <b>The tool is still advertised in every session</b>, because the MCP spec
    /// forbids the tool set varying per connection and SEP-2567 removed
    /// protocol-level sessions outright. Saying here which modes it works in is
    /// the only way a model can choose correctly at <c>init</c>, which is hours
    /// before the refusal would otherwise arrive.
    /// </para>
    /// </remarks>
    /// <param name="tool">The upstream tool name.</param>
    /// <returns>A sentence to append, or <see langword="null"/>.</returns>
    public static string? Note(string tool) =>
        string.Equals(tool, AnnotateTool, StringComparison.Ordinal)
            ? $"BrowserAI refuses this on a session whose mode opens no window ({Windowless}), and the reason is liveness rather than security: the dashboard window appears even there, in front of nobody, and the call then blocks until the run is killed. Create the session in {Headed} mode if a human will be at the keyboard."
            : null;

    /// <summary>The modes that open a window, as a clause.</summary>
    private static string Headed { get; } = Names(headed: true);

    /// <summary>The modes that open none, as a clause.</summary>
    private static string Windowless { get; } = Names(headed: false);

    private static string Names(bool headed) => string.Join(
        " or ",
        SessionModes.All.Where(mode => mode.Headed == headed).Select(mode => $"'{mode.Name}'"));
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
