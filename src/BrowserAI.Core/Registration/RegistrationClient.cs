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
    public required Func<IRegistrationCommand, string, string?, RegistrationView> UserView { get; init; }

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
        UserView = (commands, client, installRoot) =>
            CodexRegistryView.Read(commands, client, installRoot, home: null, RegistrationScope.User),
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
