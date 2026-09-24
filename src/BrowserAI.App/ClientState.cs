// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Registration;

namespace BrowserAI.App;

/// <summary>
/// One MCP client's whole state, as the window shows it and <c>--report</c>
/// writes it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Added 2026-09-24, and it exists because separate control per client is a
/// requirement and not a rendering detail.</b> The maintainer, verbatim: <i>"I
/// easy I want separate control over system level registration between codex and
/// claude."</i> Every question the window can ask -- is there a client, what does
/// it say is registered, whose is it, may I offer to change it -- had exactly one
/// answer on <see cref="AppState"/> before this type, and one answer for two
/// clients is a window whose buttons are ambiguous.
/// </para>
/// <para>
/// <b>One state per client, and no aggregate that hides one of them.</b>
/// <see cref="AppState.StatusSentence"/> joins these and never reduces them,
/// so a client whose registration is foreign or unreadable cannot be averaged
/// away by one that is fine.
/// </para>
/// </remarks>
internal sealed record ClientState
{
    /// <summary>Which client this is, and everything that differs about it.</summary>
    public required RegistrationClient Client { get; init; }

    /// <summary>The client executable, or <see langword="null"/> when none was found.</summary>
    public required string? ClientPath { get; init; }

    /// <summary>What this client's user-scope configuration says.</summary>
    public required RegistrationView UserScope { get; init; }

    /// <summary>
    /// The nearest project registration at or above the working directory, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    public required RegistrationView? ProjectScope { get; init; }

    /// <summary>
    /// Which folder <see cref="ProjectScope"/> is about, or
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <b>Carried and not derived from the view's file path.</b> The two clients
    /// keep their project registration at different depths --
    /// <c>&lt;repo&gt;\.mcp.json</c> and
    /// <c>&lt;repo&gt;\.codex\config.toml</c> -- so walking back up from the file
    /// to the repository would be a second implementation of
    /// <see cref="RegistrationClient.ProjectFileName"/>, free to disagree with
    /// it. The unregister action needs the folder, and this is the folder that
    /// was found.
    /// </remarks>
    public required string? ProjectDirectory { get; init; }

    /// <summary>
    /// Whether this install composed a server command at all.
    /// </summary>
    /// <remarks>
    /// <b>Copied onto every client deliberately.</b> It is a fact about the
    /// install and not about the client, and it is here so that
    /// <see cref="MayRegister"/> is a whole predicate and not half of one the
    /// caller has to remember to complete. The version that lived on
    /// <see cref="AppState"/> was completed by the dialog, which is exactly the
    /// shape that goes wrong when a second call site appears.
    /// </remarks>
    public required bool ServerComposed { get; init; }

    /// <summary>Whether a client was found to talk to.</summary>
    public bool ClientFound => ClientPath is { Length: > 0 };

    /// <summary>
    /// The one sentence about this client, which is what a person reads first.
    /// </summary>
    /// <remarks>
    /// <b>The order is the order of what a person can do about it.</b> A machine
    /// with no client cannot be registered at all, so that is said before
    /// anything about registration; a foreign entry is said with its path,
    /// because the only way out of it is for a person to decide which install
    /// they meant.
    /// </remarks>
    /// <returns>The sentence.</returns>
    public string StatusSentence()
    {
        if (!ClientFound)
        {
            return $"{Client.DisplayName} was not found on this machine, so BrowserAI has not been registered with it.";
        }

        if (UserScope.Unreadable is { } unreadable)
        {
            return unreadable;
        }

        return UserScope.Ownership switch
        {
            RegistrationOwnership.OursAndPresent =>
                $"Registered for all your {Client.DisplayName} projects.",
            RegistrationOwnership.OursAndStale => StaleSentence(),
            RegistrationOwnership.Foreign =>
                $"Another BrowserAI is registered with {Client.DisplayName} at '{UserScope.Command}'. Nothing here will change it.",
            _ => $"Not registered for your {Client.DisplayName} projects.",
        };
    }

    /// <summary>
    /// The same state in two or three words, for the line under the heading.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A second rendering of one reading and never a second reading --
    /// 2026-09-24.</b> It switches on the same ownership the sentence does, in the
    /// same order, so the short form cannot say <i>registered</i> while the
    /// sentence says something else. The heading needs a form that fits two
    /// clients on one line; the sentences are in the body, where there is room for
    /// the path that makes a foreign entry actionable.
    /// </remarks>
    public string ShortStatus =>
        !ClientFound ? "not found"
            : UserScope.Unreadable is not null ? "unknown"
            : UserScope.Ownership switch
            {
                RegistrationOwnership.OursAndPresent => "registered",
                RegistrationOwnership.OursAndStale => "needs repair",
                RegistrationOwnership.Foreign => "another BrowserAI",
                _ => "not registered",
            };

    /// <summary>
    /// The two ways an entry of ours can be stale, as two sentences.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-09-16, because one of them stopped being true.</b>
    /// <c>OursAndStale</c> used to mean one thing -- the file is gone -- and the
    /// sentence said so. Since the classifier started requiring the file to be
    /// the <b>server</b> and not merely present, it also covers the state
    /// every pre-split install is in: an entry naming
    /// <c>current\BrowserAI.exe</c>, which is there and is this very
    /// application. Telling that person the file "is not there any more" is a
    /// sentence they can check and find false, which is the fastest way to lose
    /// somebody's trust in a status line.
    /// </para>
    /// <para>
    /// <b>The question asked here is whether the file exists, and that is not a
    /// second classifier.</b> Ownership has already been decided; this picks the
    /// wording for a state that has two causes and one remedy. The expansion is
    /// <see cref="McpRegistryView.Expand"/>'s, so the path this asks about is the
    /// path the classifier asked about.
    /// </para>
    /// </remarks>
    /// <returns>The sentence.</returns>
    private string StaleSentence()
    {
        var named = UserScope.Command ?? "<none>";

        return File.Exists(McpRegistryView.Expand(named))
            ? $"Registered with {Client.DisplayName} to the wrong binary: the entry names '{named}', which is not the MCP server. Register again to repair it."
            : $"Registered with {Client.DisplayName}, but the entry names '{named}', which is not there any more. Register again to repair it.";
    }

    /// <summary>
    /// Whether a user-scope <i>register</i> action is offered for this client.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>It now includes <c>OursAndPresent</c> -- 2026-09-24 -- and that is
    /// the re-register affordance the research found missing.</b> Before this
    /// there was no way at all to make BrowserAI rewrite an entry that was
    /// already correct, which is what a person needs after editing their own copy
    /// of it by hand and wanting the product's version back. What changes with
    /// the state is the LABEL, not the availability: see
    /// <see cref="ConfigurationDialog"/>. Foreign and unreadable are still
    /// refused, because neither is ours to write over.
    /// </remarks>
    public bool MayRegister =>
        ClientFound
        && ServerComposed
        && UserScope.Unreadable is null
        && UserScope.Ownership is not RegistrationOwnership.Foreign;

    /// <summary>Whether the <i>unregister</i> action is offered for this client.</summary>
    /// <remarks>
    /// <para>
    /// <b>Only for an entry we wrote.</b> A foreign one is never removed -- that
    /// is somebody else's install and removing it would be this product
    /// uninstalling another.
    /// </para>
    /// <para>
    /// ⚠️ <b>And never over a configuration that could not be read</b>, which
    /// this did not check until a test constructed the combination. The reader
    /// answers <see cref="RegistrationOwnership.Absent"/> whenever it fails, so
    /// today the two cannot co-occur and the guard is unreachable -- which is
    /// exactly the kind of guard that stops being unreachable when somebody
    /// makes the reader smarter. It is here because the symmetry is the
    /// invariant: <b>no action is offered on top of a state nobody
    /// established</b>, and <see cref="MayRegister"/> already said so.
    /// </para>
    /// </remarks>
    public bool MayUnregister =>
        ClientFound
        && UserScope.Unreadable is null
        && UserScope.Ownership is RegistrationOwnership.OursAndPresent;

    /// <summary>Whether <i>register in a project</i> is offered for this client.</summary>
    public bool MayRegisterInProject => ClientFound && ServerComposed;

    /// <summary>
    /// Whether <i>remove from this project</i> is offered for this client.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Only when a project registration OF OURS was actually found at or
    /// above the working directory, and that is why it is usually not offered at
    /// all.</b> Opened from the Start Menu the working directory is the install
    /// root, so there is no project above it and no link appears -- which is
    /// correct: there is nothing to remove and no folder to name. Offering a
    /// folder picker for the removal instead was considered and dropped, because
    /// an unregister that asks a person to find the folder is an unregister that
    /// can be pointed at the wrong one.
    /// <i>Superseded the same day by Q289 b, the maintainer's answer verbatim
    /// "Q289 b": the picker exists beside this link, as
    /// <see cref="MayUnregisterFromAProject"/>, and it is safe for the reason the
    /// drop missed -- only an entry this install wrote is ever removed, so a folder
    /// picked by mistake loses nothing that is not ours.</i>
    /// </remarks>
    public bool MayUnregisterFromProject =>
        ClientFound
        && ProjectDirectory is { Length: > 0 }
        && ProjectScope is { Unreadable: null } view
        && view.Ownership is RegistrationOwnership.OursAndPresent or RegistrationOwnership.OursAndStale;

    /// <summary>
    /// Whether <i>remove from a project</i>, with a folder picker, is offered for
    /// this client.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Q289 b, 2026-09-24.</b> Offered whenever the client is found and this
    /// install composed a server, which is when the registrar can judge ownership at
    /// all -- the same condition as <see cref="MayRegisterInProject"/>. What makes
    /// a picked folder safe is the registrar's gate, not this: an entry another
    /// install wrote is refused and reported, and a folder with none is told so.
    /// </remarks>
    public bool MayUnregisterFromAProject => ClientFound && ServerComposed;

    /// <summary>Reads one client's whole state.</summary>
    /// <param name="who">The client to read.</param>
    /// <param name="commands">The seam over starting it.</param>
    /// <param name="workingDirectory">Where the search for a project registration starts.</param>
    /// <param name="installRoot">The root ownership is judged against.</param>
    /// <param name="serverComposed">Whether this install composed a server command.</param>
    /// <returns>What is true right now for that client.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static ClientState Read(
        RegistrationClient who,
        IRegistrationCommand commands,
        string workingDirectory,
        string? installRoot,
        bool serverComposed)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var client = who.Locate(commands);
        var project = NearestProject(who, workingDirectory);

        return new ClientState
        {
            Client = who,
            ClientPath = client,
            ServerComposed = serverComposed,
            UserScope = who.UserView(commands, client, installRoot),
            ProjectDirectory = project,
            ProjectScope = project is { Length: > 0 } && client is { Length: > 0 }
                ? who.ProjectView(commands, client, project, installRoot)
                : null,
        };
    }

    /// <summary>
    /// The nearest folder at or above a directory carrying this client's project
    /// registration.
    /// </summary>
    /// <remarks>
    /// <b>Upward, because that is how the client finds one.</b> A person running
    /// this from inside a repository expects it to report that repository's
    /// file, and a person running it from the Start Menu -- whose working
    /// directory is the install root -- expects it to report nothing, which is
    /// what an upward walk from there answers.
    /// </remarks>
    /// <param name="who">The client whose file is looked for.</param>
    /// <param name="start">Where to start looking.</param>
    /// <returns>The folder, or <see langword="null"/> when there is no such file.</returns>
    public static string? NearestProject(RegistrationClient who, string start)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentException.ThrowIfNullOrWhiteSpace(start);

        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(who.ProjectFileIn(directory.FullName)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
