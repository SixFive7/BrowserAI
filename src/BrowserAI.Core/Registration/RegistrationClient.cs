// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Registration;

/// <summary>
/// One MCP client, as everything that registers with it needs to see it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-09-24 for Q258, and the shape is the point: there is ONE
/// registrar and one ownership rule, not one per client.</b> The two clients
/// disagree about almost everything -- Claude Code has a <c>--scope</c> flag and a
/// JSON file this product reads; Codex has neither and is asked through
/// <c>mcp list --json</c> -- and the thing that must not be duplicated is the
/// decision about whether a registration is OURS. Duplicating that means two
/// answers to <i>may I delete this</i>, and the cost of disagreeing is somebody
/// else's registration.
/// </para>
/// <para>
/// ⚠️ <b>EVERY PER-CLIENT DIFFERENCE IS A MEMBER HERE AND NOWHERE ELSE.</b> If a
/// third client arrives, it is one more instance of this record; if the registrar
/// needs an <c>if</c> on which client it has, the difference belongs here instead.
/// </para>
/// </remarks>
internal sealed record RegistrationClient
{
    /// <summary>What to call this client in a sentence a person reads.</summary>
    public required string DisplayName { get; init; }

    /// <summary>A short, stable key for the record on disk.</summary>
    public required string Key { get; init; }

    /// <summary>The executable, by file name only.</summary>
    public required string Executable { get; init; }

    /// <summary>The server key this client is given.</summary>
    public required string ServerName { get; init; }

    /// <summary>How long one call gets.</summary>
    public required TimeSpan Budget { get; init; }

    /// <summary>Where the client is, or null when no shape found it.</summary>
    public required Func<IRegistrationCommand, string?> Locate { get; init; }

    /// <summary>What to say when it was not found, naming every place that was looked.</summary>
    public required Func<string, string> NotFoundDetail { get; init; }

    /// <summary>The arguments that register at user scope.</summary>
    public required Func<string, IReadOnlyList<string>> AddArguments { get; init; }

    /// <summary>The arguments that unregister at user scope.</summary>
    public required Func<IReadOnlyList<string>> RemoveArguments { get; init; }

    /// <summary>What is registered at user scope, and whose it is.</summary>
    /// <remarks>
    /// ⚠️ <b>The client path is nullable, and that is not tidiness -- 2026-09-24.</b>
    /// Claude Code's reading is a file this product opens, so it answers with or
    /// without a client on the machine. Codex's reading IS the client, so with no
    /// binary there is nothing to ask and the honest answer is <i>unknown</i>,
    /// never <i>nothing is registered</i>. The configuration window reads
    /// this for every client whether or not it found one, so the difference has
    /// to be expressible here.
    /// </remarks>
    public required Func<IRegistrationCommand, string?, string?, RegistrationView> UserView { get; init; }

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
    /// a project registration at all. A second member for the second use would be
    /// two answers to <i>where does this client keep it</i>.
    /// </remarks>
    public required string ProjectFileName { get; init; }

    /// <summary>
    /// The spelling of this install's server path that resolves on every
    /// machine, for a project file meant to be committed, or
    /// <see langword="null"/> when this client offers no such spelling.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Per client because only one of them is known to expand a variable
    /// in a server command -- 2026-09-24.</b> Claude Code expands <c>${VAR}</c>
    /// inside <c>.mcp.json</c>, which is the only reason
    /// <see cref="McpClientRegistration.PortableCommandFor"/> is usable. Codex's
    /// own documentation of <c>mcp_servers.&lt;id&gt;.command</c> says nothing of
    /// expansion, and a committed command that expands on one client and not the
    /// other is a path that resolves to nothing on the second. So a Codex project
    /// entry carries this machine's absolute path, and the window says so.
    /// </remarks>
    public required Func<string, string?> PortableCommandFor { get; init; }

    /// <summary>The project registration's file, under a named repository.</summary>
    /// <param name="projectDirectory">The repository root.</param>
    /// <returns>The absolute path of the file whose existence means this folder has one.</returns>
    public string ProjectFileIn(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        return Path.Combine(projectDirectory, ProjectFileName);
    }

    /// <summary>Whether an add that failed means it was already there.</summary>
    public required Func<int, string, bool> MeansAlreadyRegistered { get; init; }

    /// <summary>Whether a remove that failed means there was nothing to remove.</summary>
    public required Func<int, string, bool> MeansNothingToRemove { get; init; }

    /// <summary>The line a person can run by hand.</summary>
    public required Func<string, string> ManualCommandFor { get; init; }

    /// <summary>
    /// How the client is told which scope to write, when that is not a flag.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The two clients answer this differently and that is the only place the
    /// difference lives.</b> Claude Code takes <c>--scope project</c> and is run in
    /// the repository; Codex takes no scope at all and writes whatever
    /// <c>CODEX_HOME</c> points at. So a project registration is
    /// <see cref="ProjectAddArguments"/> plus <see cref="ProjectEnvironment"/> plus
    /// <see cref="ProjectWorkingDirectory"/>, and a client that does not use one of
    /// the three returns nothing for it.
    /// </remarks>
    public required Func<string, string, IReadOnlyList<string>> ProjectAddArguments { get; init; }

    /// <summary>The arguments that unregister from a repository.</summary>
    public required Func<string, IReadOnlyList<string>> ProjectRemoveArguments { get; init; }

    /// <summary>The environment a repository-scoped call runs under.</summary>
    public required Func<string, IReadOnlyDictionary<string, string>> ProjectEnvironment { get; init; }

    /// <summary>Where a repository-scoped call runs, or null for the profile.</summary>
    public required Func<string, string?> ProjectWorkingDirectory { get; init; }

    /// <summary>What must exist before a repository-scoped call, or null.</summary>
    /// <remarks>
    /// Codex refuses to write into a <c>CODEX_HOME</c> that does not exist, and
    /// Claude Code needs nothing. Named and not inferred, because creating a
    /// directory in somebody's repository is an act and not a detail.
    /// </remarks>
    public required Func<string, string?> ProjectDirectoryToCreate { get; init; }

    /// <summary>What the client leaves behind in a repository, to be removed.</summary>
    public required Func<string, string?> ProjectResidue { get; init; }

    /// <summary>What is registered in a repository, and whose it is.</summary>
    public required Func<IRegistrationCommand, string, string, string?, RegistrationView> ProjectView { get; init; }

    /// <summary>Claude Code, the client this product was written against first.</summary>
    public static RegistrationClient ClaudeCode { get; } = new()
    {
        DisplayName = "Claude Code",
        Key = "claude-code",
        Executable = McpClientRegistration.ClientExecutable,
        ServerName = McpClientRegistration.ServerName,
        Budget = McpClientRegistration.Budget,
        Locate = commands => commands.Locate(McpClientRegistration.ClientExecutable),
        NotFoundDetail = command =>
            $"No '{McpClientRegistration.ClientExecutable}' was found on PATH or at '{ClientCommandLine.FallbackDirectory}', so BrowserAI has not registered itself with anything. "
            + $"Install the client and run: {McpClientRegistration.ManualCommandFor(command)}",
        AddArguments = McpClientRegistration.AddArguments,
        RemoveArguments = McpClientRegistration.RemoveArguments,
        UserView = (_, _, installRoot) => McpRegistryView.User(installRoot),
        RestartHint =
            "Claude Code reads its MCP configuration when a session starts. Sessions already open will not see this change until they are restarted.",
        ProjectHint =
            "Claude Code will ask you to approve this server the first time you open a session in that folder.",
        ProjectFileName = McpRegistryView.ProjectConfigFileName,
        PortableCommandFor = McpClientRegistration.PortableCommandFor,
        MeansAlreadyRegistered = McpClientRegistration.MeansAlreadyRegistered,
        MeansNothingToRemove = McpClientRegistration.MeansNothingToRemove,
        ManualCommandFor = McpClientRegistration.ManualCommandFor,
        ProjectAddArguments = (command, _) => McpClientRegistration.AddArguments(command, McpClientRegistration.ProjectScope),
        ProjectRemoveArguments = _ => McpClientRegistration.RemoveArguments(McpClientRegistration.ProjectScope),
        ProjectEnvironment = _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        ProjectWorkingDirectory = project => project,
        ProjectDirectoryToCreate = _ => null,
        ProjectResidue = _ => null,
        ProjectView = (_, _, project, installRoot) => McpRegistryView.Project(project, installRoot),
    };

    /// <summary>Codex, added 2026-09-24.</summary>
    public static RegistrationClient Codex { get; } = new()
    {
        DisplayName = "Codex",
        Key = "codex",
        Executable = CodexRegistration.ClientExecutable,
        ServerName = CodexRegistration.ServerName,
        Budget = CodexRegistration.Budget,
        Locate = CodexRegistration.Locate,
        NotFoundDetail = CodexRegistration.NotFoundDetail,
        AddArguments = CodexRegistration.AddArguments,
        RemoveArguments = CodexRegistration.RemoveArguments,
        UserView = (commands, client, installRoot) => client is { Length: > 0 }
            ? CodexRegistryView.Read(commands, client, installRoot, home: null, RegistrationScope.User)
            : CodexRegistryView.WithoutAClient(RegistrationScope.User, home: null),
        RestartHint =
            "Codex does not pick up this change in a thread that is already open. Start a new thread to use it.",
        ProjectHint =
            "Codex reads a project's own configuration only in a project you have trusted, so this entry does nothing in a folder Codex has not been trusted in.",
        ProjectFileName = Path.Combine(CodexRegistration.ProjectDirectoryName, CodexRegistration.ConfigFileName),
        PortableCommandFor = _ => null,
        MeansAlreadyRegistered = CodexRegistration.MeansAlreadyRegistered,
        MeansNothingToRemove = CodexRegistration.MeansNothingToRemove,
        ManualCommandFor = CodexRegistration.ManualCommandFor,
        ProjectAddArguments = (command, _) => CodexRegistration.AddArguments(command),
        ProjectRemoveArguments = _ => CodexRegistration.RemoveArguments(),
        ProjectEnvironment = project => CodexRegistryView.Environment(CodexRegistration.ProjectHome(project)),
        ProjectWorkingDirectory = _ => null,
        ProjectDirectoryToCreate = CodexRegistration.ProjectHome,
        ProjectResidue = project => Path.Combine(CodexRegistration.ProjectHome(project), CodexRegistration.ProjectResidue),
        ProjectView = (commands, client, project, installRoot) =>
            CodexRegistryView.Read(commands, client, installRoot, CodexRegistration.ProjectHome(project), RegistrationScope.Project),
    };

    /// <summary>Both clients, in the order a report lists them.</summary>
    /// <remarks>
    /// <b>Claude Code first, because it is the one the product was written against
    /// and the one whose absence used to mean the product was unusable.</b> The
    /// order is stable so that a record on disk and a dialog read the same way.
    /// </remarks>
    public static IReadOnlyList<RegistrationClient> All { get; } = [ClaudeCode, Codex];
}
