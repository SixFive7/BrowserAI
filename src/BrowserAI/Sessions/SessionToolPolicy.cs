// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Frozen;

namespace BrowserAI.Sessions;

/// <summary>
/// What kind of thing a tool can reach, which is the only property the
/// <c>(tool, mode)</c> decision is taken on.
/// </summary>
/// <remarks>
/// <b>Five classes rather than sixty-nine rules.</b> A per-tool rule is a
/// per-tool judgement that has to be re-made on every upstream bump; a class is a
/// judgement about a <i>capability</i>, and a new tool joins an existing class or
/// is refused. The classes are deliberately about what a tool can obtain, never
/// about what it is called.
/// </remarks>
internal enum ToolClass
{
    /// <summary>Drives the page. Reaches no stored credential and needs no human.</summary>
    Ordinary,

    /// <summary>
    /// Reads or writes cookies, <c>localStorage</c>, <c>sessionStorage</c> or a
    /// whole <c>storageState</c>.
    /// </summary>
    /// <remarks>
    /// Every one of these returns <c>httpOnly</c> cookies — session bearer tokens
    /// JavaScript cannot read — so any mode permitted to call them is
    /// credential-bearing by definition.
    /// </remarks>
    Storage,

    /// <summary>
    /// Executes arbitrary JavaScript in the Playwright <i>server</i> process,
    /// which is a back door to everything <see cref="Storage"/> guards.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Measured 2026-08-14 and this class exists because of it:</b> against
    /// the default 24-tool surface, with zero <c>browser_cookie_*</c> tools
    /// exposed, <c>async (page) =&gt; page.context().cookies()</c> returned an
    /// <c>httpOnly</c> bearer token. The tool is in the <c>core</c> family, which
    /// upstream ors in unconditionally, so <b>no capability setting removes
    /// it</b> — the child always has it and BrowserAI's own decision is the only
    /// thing standing between a mode and the cookies it was promised not to see.
    /// It was the only hole: <c>browser_evaluate</c> runs in the page and
    /// <c>document.cookie</c> returns <c>""</c> for an <c>httpOnly</c> cookie,
    /// and <c>browser_network_request</c> strips <c>Cookie</c> and
    /// <c>Set-Cookie</c>.
    /// </remarks>
    ArbitraryCode,

    /// <summary>Blocks until a human at the keyboard finishes with it.</summary>
    /// <remarks>
    /// <c>browser_annotate</c> opens the Playwright Dashboard and waits for
    /// somebody to draw — and <b>the window appears in headless too</b>, so a
    /// headless session that called it would put an unexplained window on a
    /// screen nobody is watching and then hang until the run is killed. Only a
    /// mode whose defining promise is that a human is present can permit it.
    /// </remarks>
    HumanPresent,

    /// <summary>Reports the child's own resolved configuration.</summary>
    /// <remarks>
    /// <c>browser_get_config</c>'s handler is
    /// <c>JSON.stringify(context.config, null, 2)</c> with no filtering, and
    /// <c>config.secrets</c> is a real key — <c>--secrets &lt;path&gt;</c> on the
    /// CLI, <c>secrets?: Record&lt;string, string&gt;</c> in
    /// <c>config.d.ts</c> — so a config carrying one would be emitted in
    /// plaintext. BrowserAI never writes that key and never passes that flag, so
    /// the tool is permitted; what closes the gap is
    /// <c>Guard</c>, which refuses the <i>answer</i> if the config that
    /// came back has secrets in it after all.
    /// </remarks>
    Configuration,
}

/// <summary>
/// The single place a <c>(tool, mode)</c> call is permitted or refused.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is the charter's security trade-off made true.</b> Under four
/// separate processes the <c>interactive</c> server ran without the
/// <c>storage</c> capability, so the storage tools <i>did not exist</i> in that
/// process. Under one server they exist and correctness depends on a lookup —
/// [the charter is explicit](../../../README.md#the-init-design-weakens-a-security-boundary)
/// that this is a demotion from <i>"the capability does not exist"</i> to
/// <i>"our code declines to use it"</i>, and that it is only acceptable if the
/// decision is centralised in exactly one place, deny-by-default, unit-tested
/// against every tool in the surface, and correct under concurrency. This is that
/// one place; there is no second.
/// </para>
/// <para>
/// <b>Deny-by-default runs in both dimensions.</b> A tool this build does not
/// classify is refused in every mode, and a <i>mode</i> this build has no policy
/// row for refuses every browser tool. Neither is derived: a permission inferred
/// for a tool or a mode nobody considered is exactly the silent failure the
/// project exists to remove, so the fallback is always refusal and the missing
/// row is a red build rather than a quiet allow.
/// </para>
/// <para>
/// <b>Nothing here is conditional on the build.</b> No <c>#if</c>, no
/// <c>[Conditional]</c>, no configuration-dependent branch — the decision a
/// released artifact takes is the decision the suite took, and
/// <c>ModelSurfaceTests</c> reads this file to say so.
/// </para>
/// <para>
/// <b>The lookup is on the hot path of every call and is immutable after
/// construction.</b> Both tables are <see cref="FrozenDictionary{TKey, TValue}"/>
/// built once in a static initialiser and never written again, and the mode a
/// decision is taken on is read off the <see cref="LiveSession"/> the caller's
/// own <c>session</c> argument resolved to — a record whose
/// <see cref="LiveSession.Mode"/> is fixed at <c>init</c>. There is therefore no
/// read-decide-act window at all: no shared cell holds "the current mode", so a
/// second session opening or closing on another thread cannot change what this
/// call is judged against. <c>SessionPolicyTests</c> drives that claim across
/// modes concurrently rather than resting on the word "frozen".
/// </para>
/// </remarks>
internal static class SessionToolPolicy
{
    /// <summary>
    /// Every tool the child can ever expose over MCP, and what it can reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a classification, not a schema.</b> The scope boundary forbids
    /// a tool <i>definition</i> in C#; what is here is a name and a judgement
    /// about it, which is BrowserAI's own policy and originates nowhere else. The
    /// schemas still come from the child's <c>tools/list</c> at runtime.
    /// </para>
    /// <para>
    /// <b>The list is exactly the 69 names
    /// [the golden snapshot](../../../upstream-snapshots/tools-list.json) records as
    /// the maximum ever exposed</b>, which is regenerated from the resolved
    /// payload and diffed on every build. A tool upstream adds therefore arrives
    /// as a snapshot diff first; once that diff is accepted the tool is missing
    /// from here and <c>SessionPolicyTests</c> is red, which is the whole point —
    /// a new upstream tool is a red build rather than a security incident. A
    /// tool upstream <i>removes</i> is equally red, because a classification for
    /// something that no longer exists is stale documentation.
    /// </para>
    /// </remarks>
    public static FrozenDictionary<string, ToolClass> Classification { get; } = new Dictionary<string, ToolClass>(StringComparer.Ordinal)
    {
        ["browser_close"] = ToolClass.Ordinary,
        ["browser_resize"] = ToolClass.Ordinary,
        ["browser_get_config"] = ToolClass.Configuration,
        ["browser_console_messages"] = ToolClass.Ordinary,
        ["browser_cookie_list"] = ToolClass.Storage,
        ["browser_cookie_get"] = ToolClass.Storage,
        ["browser_cookie_set"] = ToolClass.Storage,
        ["browser_cookie_delete"] = ToolClass.Storage,
        ["browser_cookie_clear"] = ToolClass.Storage,
        ["browser_resume"] = ToolClass.Ordinary,
        ["browser_highlight"] = ToolClass.Ordinary,
        ["browser_hide_highlight"] = ToolClass.Ordinary,
        ["browser_annotate"] = ToolClass.HumanPresent,
        ["browser_handle_dialog"] = ToolClass.Ordinary,
        ["browser_evaluate"] = ToolClass.Ordinary,
        ["browser_file_upload"] = ToolClass.Ordinary,
        ["browser_drop"] = ToolClass.Ordinary,
        ["browser_find"] = ToolClass.Ordinary,
        ["browser_fill_form"] = ToolClass.Ordinary,
        ["browser_press_key"] = ToolClass.Ordinary,
        ["browser_type"] = ToolClass.Ordinary,
        ["browser_mouse_move_xy"] = ToolClass.Ordinary,
        ["browser_mouse_click_xy"] = ToolClass.Ordinary,
        ["browser_mouse_drag_xy"] = ToolClass.Ordinary,
        ["browser_mouse_down"] = ToolClass.Ordinary,
        ["browser_mouse_up"] = ToolClass.Ordinary,
        ["browser_mouse_wheel"] = ToolClass.Ordinary,
        ["browser_navigate"] = ToolClass.Ordinary,
        ["browser_navigate_back"] = ToolClass.Ordinary,
        ["browser_network_requests"] = ToolClass.Ordinary,
        ["browser_network_request"] = ToolClass.Ordinary,
        ["browser_network_state_set"] = ToolClass.Ordinary,
        ["browser_pdf_save"] = ToolClass.Ordinary,
        ["browser_route"] = ToolClass.Ordinary,
        ["browser_route_list"] = ToolClass.Ordinary,
        ["browser_unroute"] = ToolClass.Ordinary,
        ["browser_run_code_unsafe"] = ToolClass.ArbitraryCode,
        ["browser_take_screenshot"] = ToolClass.Ordinary,
        ["browser_snapshot"] = ToolClass.Ordinary,
        ["browser_click"] = ToolClass.Ordinary,
        ["browser_drag"] = ToolClass.Ordinary,
        ["browser_hover"] = ToolClass.Ordinary,
        ["browser_select_option"] = ToolClass.Ordinary,
        ["browser_generate_locator"] = ToolClass.Ordinary,
        ["browser_storage_state"] = ToolClass.Storage,
        ["browser_set_storage_state"] = ToolClass.Storage,
        ["browser_tabs"] = ToolClass.Ordinary,
        ["browser_start_tracing"] = ToolClass.Ordinary,
        ["browser_stop_tracing"] = ToolClass.Ordinary,
        ["browser_verify_element_visible"] = ToolClass.Ordinary,
        ["browser_verify_text_visible"] = ToolClass.Ordinary,
        ["browser_verify_list_visible"] = ToolClass.Ordinary,
        ["browser_verify_value"] = ToolClass.Ordinary,
        ["browser_start_video"] = ToolClass.Ordinary,
        ["browser_stop_video"] = ToolClass.Ordinary,
        ["browser_video_chapter"] = ToolClass.Ordinary,
        ["browser_video_show_actions"] = ToolClass.Ordinary,
        ["browser_video_hide_actions"] = ToolClass.Ordinary,
        ["browser_wait_for"] = ToolClass.Ordinary,
        ["browser_localstorage_list"] = ToolClass.Storage,
        ["browser_localstorage_get"] = ToolClass.Storage,
        ["browser_localstorage_set"] = ToolClass.Storage,
        ["browser_localstorage_delete"] = ToolClass.Storage,
        ["browser_localstorage_clear"] = ToolClass.Storage,
        ["browser_sessionstorage_list"] = ToolClass.Storage,
        ["browser_sessionstorage_get"] = ToolClass.Storage,
        ["browser_sessionstorage_set"] = ToolClass.Storage,
        ["browser_sessionstorage_delete"] = ToolClass.Storage,
        ["browser_sessionstorage_clear"] = ToolClass.Storage,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// What each mode permits, written down per mode rather than derived from the
    /// mode table's <c>headed</c> and <c>storage</c> flags.
    /// </summary>
    /// <remarks>
    /// <b>Deriving these would defeat the whole step.</b> A derived rule renders a
    /// fourth mode automatically, which means a mode could be added and be
    /// live in production without anyone having decided what it may reach — a
    /// security posture arrived at by inference. Written down, a new mode has no
    /// row, refuses everything, and fails the build until somebody states its
    /// permissions. <c>SessionPolicyTests</c> then cross-checks each row against
    /// the mode table's own flags, so the two expressions of the same fact cannot
    /// drift apart in silence either.
    /// </remarks>
    private static FrozenDictionary<SessionMode, FrozenSet<ToolClass>> Permits { get; } =
        new Dictionary<SessionMode, FrozenSet<ToolClass>>
        {
            // No window, no stored credentials. Arbitrary code is permitted
            // because there is nothing here for it to steal that the caller did
            // not put there this run: the profile is the session's own and holds
            // no login a human typed. The annotation tool is refused because its
            // window appears even here, in front of nobody.
            [SessionMode.Headless] = FrozenSet.ToFrozenSet(
            [
                ToolClass.Ordinary,
                ToolClass.Configuration,
                ToolClass.ArbitraryCode,
            ]),

            // The mode a human relies on, and the only one whose guarantee is
            // about a person rather than about a file. Arbitrary code is refused
            // precisely BECAUSE the storage tools are: permitting it would leave
            // the front door locked and the back door open, and the measured back
            // door returns an httpOnly bearer token.
            [SessionMode.Interactive] = FrozenSet.ToFrozenSet(
            [
                ToolClass.Ordinary,
                ToolClass.Configuration,
                ToolClass.HumanPresent,
            ]),

            // Stored logins, deliberately reachable: an agent that asked for a
            // persistent session is entitled to the cookies in it, and refusing
            // them would leave the mode with no purpose. The annotation tool is
            // still refused — nothing about a persistent profile implies somebody
            // is sitting in front of it.
            [SessionMode.Persistent] = FrozenSet.ToFrozenSet(
            [
                ToolClass.Ordinary,
                ToolClass.Configuration,
                ToolClass.ArbitraryCode,
                ToolClass.Storage,
            ]),

        }.ToFrozenDictionary();

    /// <summary>How each class reads in a sentence a model is meant to act on.</summary>
    private static FrozenDictionary<ToolClass, string> Describes { get; } =
        new Dictionary<ToolClass, string>
        {
            [ToolClass.Ordinary] = "the ordinary page-driving tools",
            [ToolClass.Storage] = "the cookie and storage tools",
            [ToolClass.ArbitraryCode] = "browser_run_code_unsafe, which runs arbitrary code in the Playwright process and can read the same cookies",
            [ToolClass.HumanPresent] = "browser_annotate, which opens a window and waits for a human to draw in it",
            [ToolClass.Configuration] = "browser_get_config",
        }.ToFrozenDictionary();

    /// <summary>Every mode that permits a class, in the table's own order.</summary>
    /// <param name="toolClass">The class.</param>
    /// <returns>The modes, which may be empty.</returns>
    public static IReadOnlyList<SessionModeDefinition> ModesPermitting(ToolClass toolClass) =>
    [
        .. SessionModes.All.Where(mode =>
            Permits.TryGetValue(mode.Mode, out var permitted) && permitted.Contains(toolClass)),
    ];

    /// <summary>
    /// What BrowserAI appends to one upstream tool's description, or
    /// <see langword="null"/> when every mode permits it.
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
    /// A restricted tool is still <b>advertised</b> in every session, because the
    /// MCP spec forbids the tool set varying per connection and SEP-2567 removed
    /// protocol-level sessions outright. Telling a model up front which mode a
    /// tool needs is therefore the only way it can choose correctly at
    /// <c>init</c>, which is hours before the refusal would otherwise arrive.
    /// </para>
    /// </remarks>
    /// <param name="tool">The upstream tool name.</param>
    /// <returns>A sentence to append, or <see langword="null"/>.</returns>
    public static string? Note(string tool)
    {
        if (!Classification.TryGetValue(tool, out var toolClass))
        {
            return "BrowserAI refuses this tool in every session mode: it is not one this build has classified, and BrowserAI decides what a tool may reach before forwarding it.";
        }

        var permitting = ModesPermitting(toolClass);

        if (permitting.Count == SessionModes.All.Count)
        {
            return null;
        }

        if (permitting.Count is 0)
        {
            return "BrowserAI refuses this tool in every session mode.";
        }

        var refusing = SessionModes.All
            .Where(mode => !permitting.Contains(mode))
            .Select(mode => $"'{mode.Name}'");

        return $"BrowserAI: this needs a session created in {string.Join(" or ", permitting.Select(mode => $"'{mode.Name}'"))} mode, and is refused in {string.Join(" or ", refusing)}. The mode is bound at browserai_init and no session can change what it is.";
    }

    /// <summary>What one mode refuses, as a clause for a description.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>A clause beginning "refuses".</returns>
    public static string Summary(SessionMode mode)
    {
        if (!Permits.TryGetValue(mode, out var permitted))
        {
            // Deny-by-default said out loud. A mode with no policy row is not a
            // permissive mode; it is a mode this build cannot run.
            return "refuses every browser tool, because this build carries no policy for it";
        }

        var refused = Describes
            .Where(entry => !permitted.Contains(entry.Key))
            .Select(entry => entry.Value)
            .ToList();

        return refused.Count is 0
            ? "refuses nothing"
            : "refuses " + string.Join(", and ", refused);
    }

    /// <summary>
    /// Decides one call. This is the only method in the product that answers the
    /// question.
    /// </summary>
    /// <param name="tool">The tool the caller named.</param>
    /// <param name="mode">The mode recorded for the session the caller named.</param>
    /// <returns>Permission, or a refusal a model can act on in one turn.</returns>
    public static ToolDecision Decide(string tool, SessionModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(mode);

        if (!Classification.TryGetValue(tool, out var toolClass))
        {
            return ToolDecision.Refused(SessionErrors.UnclassifiedTool(tool, mode.Name));
        }

        if (Permits.TryGetValue(mode.Mode, out var permitted) && permitted.Contains(toolClass))
        {
            return ToolDecision.Allowed;
        }

        return ToolDecision.Refused(SessionErrors.ModeRefusal(
            tool,
            mode,
            Describes[toolClass],
            ModesPermitting(toolClass)));
    }

    /// <summary>
    /// Checks a <c>browser_get_config</c> answer before it reaches the caller.
    /// </summary>
    /// <remarks>
    /// <b>Not decoration, and not a redaction either.</b> Upstream's handler
    /// serialises the whole config with no filtering, so if <c>secrets</c> were
    /// ever set the answer would carry it in plaintext. BrowserAI writes no such
    /// key and passes no <c>--secrets</c> flag, so on every ordinary call this
    /// returns <see langword="null"/> and the child's own bytes go back
    /// untouched — which is what keeps the passthrough byte-identical. Rewriting
    /// the answer to blank a field would cost byte-identity on every call to buy
    /// nothing on almost all of them; refusing the one answer that would disclose
    /// something costs nothing and is triggerable, which is what makes it a
    /// mechanism rather than a gesture.
    /// </remarks>
    /// <param name="answerText">The text the child returned.</param>
    /// <returns>A refusal, or <see langword="null"/> if the answer is safe to forward.</returns>
    public static string? Guard(string? answerText) =>
        answerText is not null && answerText.Contains("\"secrets\"", StringComparison.Ordinal)
            ? SessionErrors.ConfigurationWouldDiscloseSecrets()
            : null;
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
