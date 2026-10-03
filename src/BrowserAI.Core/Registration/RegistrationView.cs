// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Registration;

/// <summary>Which of a client's two configurations an entry is in.</summary>
/// <remarks>
/// <i>Moved here 2026-10-03 from <c>McpRegistryView.cs</c>, which went with the
/// switch to RegisterAI; the type is BrowserAI's name for what RegisterAI calls a
/// scope.</i>
/// </remarks>
internal enum RegistrationScope
{
    /// <summary>
    /// The user's own configuration: <i>"for all my projects"</i>.
    /// </summary>
    User,

    /// <summary>
    /// A project's own file, committed with the repository and approved once per
    /// project by the client.
    /// </summary>
    Project,
}

/// <summary>
/// Whose registration an entry is, decided from its command and an install root.
/// </summary>
/// <remarks>
/// <para>
/// <b>The distinction the configuration app is built around.</b> An entry named
/// <c>browserai</c> that points somewhere we do not own is somebody else's -- a
/// second install, a build from source, a path a person typed -- and BrowserAI
/// neither adopts it, overwrites it nor deletes it. It says where it is and
/// refuses. Inferred intent is never acted on; only an explicit click edits
/// anything.
/// </para>
/// <para>
/// <b>RegisterAI decides it since 2026-10-03</b>, by the same rule: an entry is ours
/// when its command resolves under this install's root or names exactly the command
/// this install writes. <see cref="RegistrationReader"/> maps its words onto these.
/// </para>
/// </remarks>
internal enum RegistrationOwnership
{
    /// <summary>There is no entry of that name in that scope.</summary>
    Absent,

    /// <summary>
    /// Ours, and it names this install's server, which is there.
    /// </summary>
    /// <remarks>
    /// <b>The server, not merely a file</b> -- <i>narrowed 2026-09-16, previously
    /// "Ours, and the file it names is there".</i> A pre-split entry naming
    /// <c>current\BrowserAI.exe</c> points at a file that exists and is the
    /// configuration app. <i>Corrected 2026-10-03 (previously "The console subsystem
    /// is what decides it")</i>: RegisterAI is handed the server's own path, and an
    /// entry of ours naming any other file is stale.
    /// </remarks>
    OursAndPresent,

    /// <summary>
    /// Ours -- the command is under our install root -- and it names a file that is
    /// gone, or a file other than this install's server.
    /// </summary>
    /// <remarks>
    /// <b>Two causes, one state, because the action is the same</b> --
    /// <i>widened 2026-09-16, previously "but the file it names is not there any
    /// more. This is what an entry written by an older layout looks like after
    /// the server was renamed".</i> That sentence described half of what an
    /// older layout leaves behind: the other half is an entry naming
    /// <c>current\BrowserAI.exe</c>, which after the 2026-09-15 split is the
    /// configuration app and is still there. Both are re-pointed by the update hook
    /// and both make the window offer <i>Register</i>; what differs is the sentence a
    /// person reads, which <c>ClientState.StatusSentence</c> tells apart by asking
    /// whether the file is there at all.
    /// </remarks>
    OursAndStale,

    /// <summary>
    /// Somebody else's BrowserAI. Reported, never touched.
    /// </summary>
    Foreign,
}

/// <summary>
/// One scope's answer about <c>browserai</c>.
/// </summary>
/// <param name="Scope">Which configuration was read.</param>
/// <param name="File">The file, whether or not it exists.</param>
/// <param name="Command">The command the entry names, or <see langword="null"/>.</param>
/// <param name="Ownership">Whose it is.</param>
/// <param name="Unreadable">
/// Why the configuration could not be read, when that is the reason there is no
/// answer. <see langword="null"/> when it was read -- including when the file simply
/// is not there, which is an answer, not a failure.
/// </param>
/// <param name="ResolvesTo">
/// The file <paramref name="Command"/> resolves to, the way its client resolves it,
/// when RegisterAI found one. Added 2026-10-03.
/// </param>
internal sealed record RegistrationView(
    RegistrationScope Scope,
    string File,
    string? Command,
    RegistrationOwnership Ownership,
    string? Unreadable,
    string? ResolvesTo = null);
