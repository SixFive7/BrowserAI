// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Frozen;

namespace BrowserAI.Sessions;

/// <summary>
/// What a session is, bound once at <c>init</c> and never changed.
/// </summary>
/// <remarks>
/// Three modes rather than the legacy four. <c>tracing</c> was never a mode — it
/// is a boolean orthogonal to all three — and promoting it to a modifier removes
/// a row while <i>adding</i> capability: headless-with-a-trace and
/// persistent-with-a-trace arrive for free.
/// </remarks>
internal enum SessionMode
{
    /// <summary>No window, and no cookie or storage tools. The workhorse.</summary>
    Headless,

    /// <summary>A window, and no cookie or storage tools. A human can sign in and the agent cannot read the cookie jar through this server.</summary>
    Interactive,

    /// <summary>A window and the cookie and storage tools. Logged-in agent work.</summary>
    Persistent,
}

/// <summary>
/// The one mode table. Every channel that describes a mode to a model reads from
/// here.
/// </summary>
/// <remarks>
/// <para>
/// <b>One table, because six consumers have to agree.</b> The server
/// <c>instructions</c>, <c>init</c>'s description, <c>resume</c>'s result, the
/// refusal text, the generated child config and the tests all render from here —
/// and a fourth mode added in one place and missed in another is a mode nobody
/// picks correctly, failing silently. <c>ModelSurfaceTests</c> asserts each
/// consumer renders every row of this table.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-18 (previously the fifth consumer was "the
/// <c>(tool, mode)</c> enforcement decision", and this paragraph added that
/// <see cref="SessionToolPolicy"/> "refuses everything for a mode it has no
/// policy row for").</b> The permission matrix is gone: it was never a boundary
/// against the caller, who chooses the session directory and can read the profile
/// inside it as the same Windows user —
/// [measured 2026-08-18](../../../kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18),
/// after the removal rather than before it. The consumer named in its place is the one
/// that was always doing the real work — <c>BrowserConfiguration.ForSession</c>
/// turns <see cref="SessionModeDefinition.Headed"/> into upstream's
/// <c>headless</c> and <see cref="SessionModeDefinition.Storage"/> into the
/// capability set the session's own child is launched with, so a mode without
/// storage has no cookie tools <i>in its child at all</i> rather than a lookup
/// declining to forward them.
/// </para>
/// <para>
/// <b>Rows 3 and 4 of §C's eight combinations are deliberately absent.</b>
/// Headless-with-storage would grant full credential access with no visible
/// signal that anything is driving the session. A window is not a security
/// control; it is the only cue a human gets, and opening that should be a
/// decision taken on its own merits rather than a side effect of making three
/// switches orthogonal.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-19: a mode is a window and a TOOL FILTER, not a
/// window and a persistence switch.</b> Every sentence in this file used to say
/// "no stored credentials" for two of the three modes, and the code has never
/// done that: <see cref="Runtime.BrowserConfiguration.ForSession"/> sets
/// <c>browser.userDataDir</c> to <c>&lt;session&gt;\profile</c> in all three and
/// never sets upstream's <c>isolated</c>, so all three keep cookies and
/// <c>localStorage</c> for as long as the session directory exists. What
/// <see cref="SessionModeDefinition.Storage"/> decides is whether the session's
/// child is started with upstream's <c>storage</c> capability — that is, whether
/// the 17 cookie, <c>localStorage</c> and <c>storageState</c> tools exist in that
/// process at all. <b>The distinction is not academic and it points the safe
/// way</b>: a caller reading "this session will not keep the password" would have
/// left a signed-in profile on disk believing it had not, and the session
/// directory is where <c>browserai_destroy</c> exists to be pointed. The removal
/// of the <c>(tool, mode)</c> matrix on 2026-08-18 already established that this
/// is not a boundary against the caller, who owns the directory
/// ([kb](../../../kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18));
/// what it is, is a boundary against the <i>model in the loop</i> asking this
/// server for the jar. Found by
/// [the auth-transfer review](../../../docs/reviews/2026-08-19-auth-transfer-and-session-modes.md#three-corrections-to-what-this-repository-says-about-itself),
/// which measured it while asking a different question.
/// </para>
/// </remarks>
internal static class SessionModes
{
    /// <summary>Every mode, in the order they are offered to a model.</summary>
    public static IReadOnlyList<SessionModeDefinition> All { get; } =
    [
        new(
            SessionMode.Headless,
            "headless",
            Headed: false,
            Storage: false,
            "no window, and no cookie or storage tools; the default choice for automation nobody is watching"),
        new(
            SessionMode.Interactive,
            "interactive",
            Headed: true,
            Storage: false,
            "a visible window, and no cookie or storage tools, so a human can sign in and this server will not hand the cookie jar back"),
        new(
            SessionMode.Persistent,
            "persistent",
            Headed: true,
            Storage: true,
            "a visible window, plus the cookie and storage tools for reading and replaying what the profile holds"),
    ];

    /// <summary>The mode names, comma separated, for a refusal or a description.</summary>
    public static string Names { get; } = string.Join(", ", All.Select(mode => mode.Name));

    /// <summary>
    /// The whole table as one clause per mode — name and what it grants — for
    /// <c>init</c>'s description and for the refusal a bad <c>mode</c> argument
    /// produces.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-08-18 (previously each clause carried a second half,
    /// <c>SessionToolPolicy.Summary(mode)</c>, naming what the mode
    /// <i>refuses</i>).</b> There is no permission matrix left to summarise: it
    /// was removed because it was never a boundary against the caller, who owns
    /// the session directory and therefore the profile inside it. What a mode
    /// <b>is</b> — a window or none, and the cookie and storage tools or not — is
    /// the whole of what a model needs to choose on, and it is what
    /// <see cref="SessionModeDefinition.Grants"/> already said.
    /// ⚠️ <b>Corrected 2026-08-19 (previously "a profile that persists or one
    /// that does not").</b> All three profiles persist; see this type's own
    /// remarks.
    /// </remarks>
    public static string Table { get; } = string.Join(
        " ",
        All.Select(mode => $"'{mode.Name}' — {mode.Grants}."));

    /// <summary>
    /// The mode table as the server <c>instructions</c> renders it: one line per
    /// mode, and nothing else.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Table"/> because the two channels have different
    /// budgets — <c>instructions</c> is capped at 2 KB and is read by every model
    /// on every connection, while a tool description is read once by a model that
    /// has already decided to create a session. Both render every row of
    /// <see cref="All"/>, which is what the test checks.
    /// </remarks>
    public static string Lines { get; } = string.Join(
        "\n",
        All.Select(mode => $"· {mode.Name} — {mode.Grants}."));

    private static FrozenDictionary<string, SessionModeDefinition> ByName { get; } =
        All.ToFrozenDictionary(mode => mode.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks a mode up by the name a caller wrote.</summary>
    /// <param name="name">The <c>mode</c> argument, as it arrived.</param>
    /// <returns>The definition, or <see langword="null"/> if the name is not one of the three.</returns>
    public static SessionModeDefinition? Find(string? name) =>
        name is not null && ByName.TryGetValue(name, out var mode) ? mode : null;

    /// <summary>Looks a mode up by the name recorded in <c>browserai.json</c>.</summary>
    /// <param name="name">The recorded mode.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="LockFileException">The recorded mode is not one this build knows.</exception>
    public static SessionModeDefinition Recorded(string name) =>
        Find(name) ?? throw new LockFileException(
            $"This session records mode '{name}', which this build of BrowserAI does not know. Known modes are {Names}. A newer BrowserAI may have created it; use that build, or destroy the session and create it again.");
}

/// <summary>One row of the mode table.</summary>
/// <param name="Mode">The mode itself.</param>
/// <param name="Name">Its name on the wire and in <c>browserai.json</c>.</param>
/// <param name="Headed">Whether a browser window appears.</param>
/// <param name="Storage">
/// Whether this session's child is launched with upstream's <c>storage</c>
/// capability, which is what puts the 17 cookie, <c>localStorage</c> and
/// <c>storageState</c> tools in it.
/// <para>
/// ⚠️ <b>Corrected 2026-08-19 (previously "Whether the profile keeps cookies and
/// logins between runs").</b> It never decided that.
/// <see cref="Runtime.BrowserConfiguration.ForSession"/> writes
/// <c>browser.userDataDir</c> as <c>&lt;session&gt;\profile</c> in <b>all
/// three</b> modes and never writes upstream's <c>isolated</c> key at all, so
/// every mode already persists cookies and <c>localStorage</c> across a
/// <c>browserai_resume</c> — a <c>headless</c> session that signs in is still
/// signed in tomorrow. This flag is a <b>tool filter</b>, and the difference is
/// what a caller is told: <i>the agent cannot ask this server for the cookie
/// jar</i> is true, and <i>the session will not keep the password</i> was not.
/// </para>
/// </param>
/// <param name="Grants">What it gives the caller, in one clause, for a model to choose on.</param>
internal sealed record SessionModeDefinition(
    SessionMode Mode,
    string Name,
    bool Headed,
    bool Storage,
    string Grants);
