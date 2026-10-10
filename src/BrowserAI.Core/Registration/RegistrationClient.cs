// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Registration;

/// <summary>What a project file is given, and what a person is told about it.</summary>
/// <param name="Command">The command the entry names.</param>
/// <param name="Note">
/// The sentence that follows a registration, or <see langword="null"/> when there is
/// nothing to say beyond the client's own hints.
/// </param>
internal sealed record ProjectCommand(string Command, string? Note);

/// <summary>
/// One MCP client, as BrowserAI names it, spells its command for it and talks about
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-09-24 for Q258, and the shape is the point: there is ONE
/// registrar and one ownership rule, not one per client.</b> The two clients
/// disagree about almost everything, and the thing that must not be duplicated is
/// the decision about whether a registration is OURS. Duplicating that means two
/// answers to <i>may I delete this</i>, and the cost of disagreeing is somebody
/// else's registration.
/// </para>
/// <para>
/// ⚠️ <b>Narrowed 2026-10-03, with the switch to RegisterAI (Q332).</b> How each
/// client is found, asked and written to is RegisterAI's now, so the members that
/// said so are gone: the client search, the add and remove arguments, the readers and
/// what each client's exit codes mean. What stays is what makes the client
/// BrowserAI's to talk about: its name and key, RegisterAI's id for it, the command a
/// project file is given, the line a person can run by hand, and the sentences a
/// person reads after a change. <i>Previously this record carried every per-client
/// difference the registrar acted on.</i>
/// </para>
/// </remarks>
internal sealed record RegistrationClient
{
    /// <summary>What to call this client in a sentence a person reads.</summary>
    public required string DisplayName { get; init; }

    /// <summary>A short, stable key for the record on disk.</summary>
    public required string Key { get; init; }

    /// <summary>RegisterAI's id for this client, as its <c>--client</c> option and its documents spell it.</summary>
    public required string ToolId { get; init; }

    /// <summary>The client's executable, by file name, for a sentence.</summary>
    public required string Executable { get; init; }

    /// <summary>
    /// The sentence a person is told after a registration changed, in this
    /// client's own terms.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Per client because the unit of staleness differs.</b> Claude Code
    /// reads its MCP configuration when a <b>session</b> starts. Codex does not
    /// pick up a change inside a <b>thread</b> that is already open: an edit to
    /// its <c>config.toml</c> on disk left a live thread unchanged 3/3 (W1-W3 in
    /// <c>docs/evidence/2026-09-23-client-reconnect</c>), while a new thread in the
    /// same process dialled fresh 3/3 (N1-N3). Telling a Codex user to restart a
    /// session is telling them to do something their client has no word for.
    /// </remarks>
    public required string RestartHint { get; init; }

    /// <summary>What a person is told after a project registration is written.</summary>
    /// <remarks>
    /// <b>Two different conditions, and the Codex one is a caveat and not a
    /// courtesy.</b> Claude Code asks each person to approve a project server
    /// once. Codex reads a project's own configuration only in a project that has
    /// been trusted, so a registration written into an untrusted folder is a file
    /// that exists and does nothing.
    /// </remarks>
    public required string ProjectHint { get; init; }

    /// <summary>
    /// The project registration's file, relative to the repository root.
    /// </summary>
    /// <remarks>
    /// <b>One member, two uses:</b> it is what a dialog names to a person, and it
    /// is the marker an upward walk looks for to decide whether a folder carries
    /// a project registration at all.
    /// </remarks>
    public required string ProjectFileName { get; init; }

    /// <summary>
    /// What a project file for this install says, and the sentence that goes with it
    /// when the spelling alone decides it: from the server's absolute path and the
    /// install root.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Per client because only one of them expands a variable in a server
    /// command.</b> Claude Code expands <c>${VAR}</c> inside <c>.mcp.json</c>, which
    /// is the only reason <see cref="PortableCommandFor"/> is usable, and it is
    /// written when it expands to this install. <b>Codex expands nothing</b>:
    /// <c>${LOCALAPPDATA}</c>, <c>$LOCALAPPDATA</c>, <c>%LOCALAPPDATA%</c> and
    /// <c>~</c> started nothing in 48 attempts (measured and read 2026-09-24,
    /// <c>docs/evidence/2026-09-24-codex-expansion</c>), so by Q294, the maintainer's
    /// words verbatim <i>"Q294 b"</i>, a Codex project entry names the executable
    /// alone, found on the PATH the install puts its own folder on
    /// (<see cref="UserPath"/>): <c>BrowserAI.exe --mcp</c> since 2026-10-08, D7 a
    /// (previously <c>BrowserAI.Server.exe</c>).
    /// </remarks>
    public required Func<string, string?, ProjectCommand> ProjectCommandFor { get; init; }

    /// <summary>
    /// The sentence that follows a project registration once RegisterAI has said which
    /// file the entry resolves to, or <see langword="null"/>: from this install's server
    /// and that file.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-03.</b> Codex's sentence names what its bare name finds on the
    /// PATH, which BrowserAI used to look up itself and RegisterAI now reports as
    /// <c>resolvesTo</c>.
    /// </remarks>
    public required Func<string, string?, string?> ProjectNoteAfter { get; init; }

    /// <summary>The line a person can run by hand, from the command to register and its arguments.</summary>
    /// <remarks>
    /// ⚠️ <b>The arguments are the ones the registration itself was given, since
    /// 2026-10-10</b>, the texts review's #138 and #139: the line carried <c>--mcp</c>
    /// alone, so for an install made with a data root it set up a relay that no
    /// background serves. The dashboard's Register and Repair keep the data root the
    /// same way (the dashboard's fix of the same day).
    /// </remarks>
    public required Func<string, IReadOnlyList<string>, string> ManualCommandFor { get; init; }

    /// <summary>
    /// The entry a person puts into a project's file by hand, from the command a project
    /// entry names and its arguments, written the way that file is.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-10</b>, when a project registration that was not done was found to
    /// offer the user-scope line, <c>claude mcp add browserai --scope user</c> with
    /// <c>${LOCALAPPDATA}</c> left for a shell to expand, which registers BrowserAI for every
    /// repository and leaves the project as it was. A project's file is the project's, and
    /// no client's command line writes it the same way from every shell, so the advice is
    /// the entry itself.
    /// </remarks>
    public required Func<string, IReadOnlyList<string>, string> ProjectEntryFor { get; init; }

    /// <summary>The line a person runs to remove BrowserAI's entry for this user by hand.</summary>
    /// <remarks>
    /// <b>Added 2026-10-10, round 2 of the texts review</b>: an unregister that was not
    /// done, from the dashboard's Unregister or the uninstall hook, was given
    /// <see cref="ManualCommandFor"/>'s line, which registers BrowserAI again. The client's
    /// own removal, at the scope the hooks and the page register at.
    /// </remarks>
    public required string ManualRemoveCommand { get; init; }

    /// <summary>A value in double quotes, its backslashes and quotes escaped, the way a JSON string and a TOML basic string both write it.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The quoted value.</returns>
    internal static string Quoted(string value) =>
        "\"" + (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>Arguments as a person types them after the command: a flag as it is, anything else in quotes.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The arguments, separated by spaces.</returns>
    internal static string Typed(IReadOnlyList<string> arguments) =>
        string.Join(' ', arguments.Select(static argument => argument.StartsWith("--", StringComparison.Ordinal) ? argument : $"\"{argument}\""));

    /// <summary>Claude Code, the client this product was written against first.</summary>
    public static RegistrationClient ClaudeCode { get; } = new()
    {
        DisplayName = "Claude Code",
        Key = "claude-code",
        ToolId = "claude-code",
        Executable = "claude.exe",
        RestartHint =
            "Claude Code reads its MCP configuration when a session starts. Sessions already open will not see this change until they are restarted.",
        ProjectHint =
            "Claude Code will ask you to approve this server the first time you open a session in that folder.",
        ProjectFileName = ".mcp.json",
        ProjectCommandFor = ClaudeProjectCommandFor,
        ProjectNoteAfter = (_, _) => null,
        ManualCommandFor = static (command, arguments) => $"claude mcp add {McpRegistrar.ServerName} --scope user -- \"{command}\" {Typed(arguments)}",
        ProjectEntryFor = static (command, arguments) =>
            $"\"{McpRegistrar.ServerName}\": {{ \"command\": {Quoted(command)}, \"args\": [{string.Join(", ", arguments.Select(Quoted))}] }}, inside \"mcpServers\"",
        ManualRemoveCommand = $"claude mcp remove {McpRegistrar.ServerName} --scope user",
    };

    /// <summary>Codex, added 2026-09-24.</summary>
    public static RegistrationClient Codex { get; } = new()
    {
        DisplayName = "Codex",
        Key = "codex",
        ToolId = "codex",
        Executable = "codex.exe",
        RestartHint =
            "Codex does not pick up this change in a thread that is already open. Start a new thread to use it.",
        ProjectHint =
            "Codex reads a project's own configuration only in a project you have trusted, so this entry does nothing in a folder Codex has not been trusted in.",
        ProjectFileName = Path.Combine(".codex", "config.toml"),
        ProjectCommandFor = (_, _) => new ProjectCommand(RegistrationTarget.AppFileName, null),
        ProjectNoteAfter = CodexProjectNote,
        ManualCommandFor = static (command, arguments) => $"codex mcp add {McpRegistrar.ServerName} -- \"{command}\" {Typed(arguments)}",
        ProjectEntryFor = static (command, arguments) =>
            $"mcp_servers.{McpRegistrar.ServerName} = {{ command = {Quoted(command)}, args = [{string.Join(", ", arguments.Select(Quoted))}] }}",
        ManualRemoveCommand = $"codex mcp remove {McpRegistrar.ServerName}",
    };

    /// <summary>Both clients, in the order a report lists them.</summary>
    /// <remarks>
    /// <b>Claude Code first, because it is the one the product was written against
    /// and the one whose absence used to mean the product was unusable.</b> The
    /// order is stable so that a record on disk and a dialog read the same way, and
    /// it is RegisterAI's order too.
    /// </remarks>
    public static IReadOnlyList<RegistrationClient> All { get; } = [ClaudeCode, Codex];

    /// <summary>The project registration's file, under a named repository.</summary>
    /// <param name="projectDirectory">The repository root.</param>
    /// <returns>The absolute path of the file whose existence means this folder has one.</returns>
    public string ProjectFileIn(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        return Path.Combine(projectDirectory, ProjectFileName);
    }

    /// <summary>
    /// The portable form of an installed server's path, for a committed
    /// <c>.mcp.json</c>.
    /// </summary>
    /// <param name="packId">The Velopack pack id, which is the install folder.</param>
    /// <returns>The command, with the environment reference unexpanded.</returns>
    /// <remarks>
    /// <para>
    /// <b>Claude Code expands <c>${VAR}</c> inside <c>.mcp.json</c> and this is the
    /// only reason the form is usable.</b> A committed absolute path under one
    /// person's user profile is wrong on every teammate's machine and right on exactly
    /// one, which makes committing it worse than committing nothing.
    /// </para>
    /// <para>
    /// <b>Forward slashes</b>, because JSON is where this lands and a backslash is an
    /// escape there; the client and Windows both accept them. <i>Moved here 2026-10-03
    /// from <c>McpClientRegistration</c>, unchanged.</i>
    /// </para>
    /// </remarks>
    public static string PortableCommandFor(string packId) =>
        $"${{LOCALAPPDATA}}/{packId}/{RegistrationTarget.CurrentDirectoryName}/{RegistrationTarget.AppFileName}";

    /// <summary>
    /// What a Claude Code project file is given: the portable spelling when it
    /// expands to this install, and the absolute path with the reason otherwise.
    /// </summary>
    /// <param name="server">This install's server, absolute.</param>
    /// <param name="installRoot">This install's root, or <see langword="null"/>.</param>
    /// <returns>The command and the sentence.</returns>
    /// <remarks>
    /// ⚠️ <b>The portable form is only written when it expands to the install this
    /// process is running out of.</b> A non-default install root -- <c>Setup.exe</c>
    /// with an install-to argument -- does not sit under <c>%LOCALAPPDATA%</c>, and
    /// writing this form there would commit a path that resolves to nothing on the
    /// very machine that wrote it.
    /// </remarks>
    public static ProjectCommand ClaudeProjectCommandFor(string server, string? installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        var folder = installRoot is { Length: > 0 } root
            ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : "BrowserAI.app";

        var expanded = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            folder,
            RegistrationTarget.CurrentDirectoryName,
            RegistrationTarget.AppFileName);

        return string.Equals(Path.GetFullPath(expanded), Path.GetFullPath(server), StringComparison.OrdinalIgnoreCase)
            ? new ProjectCommand(PortableCommandFor(folder), null)
            : new ProjectCommand(
                server,
                "This install is not at its default location, so the entry carries its absolute path and will not resolve on another machine.");
    }

    /// <summary>
    /// What a person is told after a Codex project registration: that the entry names
    /// the server alone, and which file that name finds on the PATH.
    /// </summary>
    /// <param name="server">This install's server, absolute.</param>
    /// <param name="found">The file the bare name resolves to, as RegisterAI reported it, or <see langword="null"/>.</param>
    /// <returns>The sentence.</returns>
    /// <remarks>
    /// <b>The sentence names what the name finds</b>, and a Codex process started before
    /// the install carries a PATH without the folder, which follows from how the launcher
    /// builds the server's environment. <i>Corrected 2026-10-10 (previously "and was not
    /// measured")</i>: measured 2026-09-25, where such a Codex never started the server, 3
    /// of 3, and on 2026-10-10 at codex-cli 0.162.0-alpha.2, where it started it 0 of 6
    /// (<c>kb/mcp/protocol.md</c>). <i>Moved here 2026-10-03 from
    /// <c>CodexRegistration.ProjectCommandGiven</c>, with its wording unchanged.</i>
    /// ⚠️ <i>Corrected 2026-10-10 a second time, the texts polish, page #90 (previously
    /// "The sentence names what the name finds, and that it may take a restart", and the
    /// sentence ended "A Codex that was already running before BrowserAI was installed may
    /// need to be restarted to see it.")</i>: the restart is said once, by the page's Codex
    /// section, <c>PageContent.CodexStartedBeforeTheInstall</c>, without the hedge the
    /// measurement above took away.
    /// </remarks>
    public static string CodexProjectNote(string server, string? found)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        const string How = "The entry names BrowserAI.exe with --mcp and no folder, because Codex expands no variable in a command; Codex finds it on the PATH it gives the server.";

        return found is null
            ? $"{How} No folder on your PATH holds one yet. BrowserAI's installer puts its own there, so install BrowserAI on this machine, or put the folder that holds it on your PATH."
            : string.Equals(Path.GetFullPath(found), Path.GetFullPath(server), StringComparison.OrdinalIgnoreCase)
                // The texts polish, 2026-10-10, page #90 (previously "It finds this install.
                // A Codex that was already running ... may need to be restarted to see it." and
                // "..., which is not this install."): the page's Codex section says the restart,
                // and the other ending says what follows.
                ? $"{How} It finds this install."
                : $"{How} The first one on your PATH is '{found}', which is not this install, so Codex starts that one.";
    }
}
