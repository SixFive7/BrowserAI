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
    /// <summary>No window, no stored credentials. The workhorse.</summary>
    Headless,

    /// <summary>A window, no stored credentials. A human may type a password the agent must never capture.</summary>
    Interactive,

    /// <summary>A window and a persistent profile. Logged-in agent work.</summary>
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
/// inside it as the same Windows user. The consumer named in its place is the one
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
            "no window and no stored credentials; the default choice for automation nobody is watching"),
        new(
            SessionMode.Interactive,
            "interactive",
            Headed: true,
            Storage: false,
            "a visible window and no stored credentials, so a human can type a password this session will not keep"),
        new(
            SessionMode.Persistent,
            "persistent",
            Headed: true,
            Storage: true,
            "a visible window and a profile that keeps cookies and logins between runs"),
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
    /// <b>is</b> — a window or none, a profile that persists or one that does
    /// not — is the whole of what a model needs to choose on, and it is what
    /// <see cref="SessionModeDefinition.Grants"/> already said.
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

    /// <summary>Looks a mode up by the name recorded in <c>lock.json</c>.</summary>
    /// <param name="name">The recorded mode.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="LockFileException">The recorded mode is not one this build knows.</exception>
    public static SessionModeDefinition Recorded(string name) =>
        Find(name) ?? throw new LockFileException(
            $"This session records mode '{name}', which this build of BrowserAI does not know. Known modes are {Names}. A newer BrowserAI may have created it; use that build, or destroy the session and create it again.");
}

/// <summary>One row of the mode table.</summary>
/// <param name="Mode">The mode itself.</param>
/// <param name="Name">Its name on the wire and in <c>lock.json</c>.</param>
/// <param name="Headed">Whether a browser window appears.</param>
/// <param name="Storage">Whether the profile keeps cookies and logins between runs.</param>
/// <param name="Grants">What it gives the caller, in one clause, for a model to choose on.</param>
internal sealed record SessionModeDefinition(
    SessionMode Mode,
    string Name,
    bool Headed,
    bool Storage,
    string Grants);
