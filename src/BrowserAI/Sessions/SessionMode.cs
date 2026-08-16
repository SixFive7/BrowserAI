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
/// <b>One table, because four channels have to agree.</b>
/// [§C](../../plan/C-sessions.md) requires the mode list to reach the model in
/// the server <c>instructions</c>, in <c>init</c>'s description, in
/// <c>resume</c>'s replayed record and in the refusal text — and a fourth mode
/// added in one place and missed in the other three is a mode nobody picks
/// correctly, failing silently. The instructions string is
/// [step 13](../../plan/build-order.md#13-the-one-table-enforcement-and-the-model-facing-surface)'s;
/// the other three read this table today.
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
    /// The whole table as one paragraph per mode, for <c>init</c>'s description
    /// and for the refusal a bad <c>mode</c> argument produces.
    /// </summary>
    public static string Table { get; } = string.Join(
        " ",
        All.Select(mode => $"'{mode.Name}' — {mode.Grants}."));

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
