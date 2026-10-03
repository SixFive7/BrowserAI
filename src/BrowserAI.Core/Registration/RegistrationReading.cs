// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Registration;

/// <summary>What RegisterAI says one client has: at user scope, and in the nearest project.</summary>
/// <param name="Client">The client.</param>
/// <param name="ClientPath">The client's executable, or <see langword="null"/> when RegisterAI found none, or could not be asked.</param>
/// <param name="UserScope">The user-scope entry.</param>
/// <param name="ProjectDirectory">The nearest folder at or above the working directory that carries this client's project file, or null.</param>
/// <param name="ProjectScope">That folder's entry, or null.</param>
/// <param name="Advice">What RegisterAI advises about the user-scope entry: restarts, a folder missing from PATH.</param>
/// <param name="Unanswered">
/// Why RegisterAI gave no answer about this client, or <see langword="null"/> when it
/// did. Set, nothing is known about the client, its executable included.
/// </param>
internal sealed record ClientReading(
    RegistrationClient Client,
    string? ClientPath,
    RegistrationView UserScope,
    string? ProjectDirectory,
    RegistrationView? ProjectScope,
    IReadOnlyList<ToolAdvice> Advice,
    string? Unanswered = null);

/// <summary>
/// Reads what every client has registered, through RegisterAI's <c>status</c>, and
/// writes nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>One run for the user scope of every client, and one per client that has a
/// project file at or above the working directory.</b> Each project run passes that
/// client's own spelling of the server, because the two clients are given different
/// commands for the same file (<see cref="RegistrationClient.ProjectCommandFor"/>):
/// an entry is judged ours by the install root, or by naming exactly what this install
/// writes.
/// </para>
/// <para>
/// ⚠️ <b>A RegisterAI that could not be asked is never <i>nothing registered</i>.</b>
/// Every client then reads as unknown, with the reason in
/// <see cref="RegistrationView.Unreadable"/> and no client path, so the window says so
/// and offers no action.
/// </para>
/// </remarks>
internal static class RegistrationReader
{
    /// <summary>Reads every client.</summary>
    /// <param name="tool">RegisterAI.</param>
    /// <param name="clients">The clients, in the order a report lists them.</param>
    /// <param name="installRoot">The install root an entry is judged ours under, or null.</param>
    /// <param name="server">This install's server, absolute, or null when there is none to name.</param>
    /// <param name="workingDirectory">Where the walk for a project file starts.</param>
    /// <returns>One reading per client, in the order given. Never throws for a tool that fails.</returns>
    public static IReadOnlyList<ClientReading> Read(
        IRegisterAi tool,
        IReadOnlyList<RegistrationClient> clients,
        string? installRoot,
        string? server,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var run = tool.Run(
            McpRegistrar.Arguments("status", clients, "user", project: null, installRoot, replace: false, pathFolder: null, server),
            McpRegistrar.ToolBudget);

        var answered = ToolDocuments.TryRead(run, out var document, out var problem);
        var readings = new List<ClientReading>(clients.Count);

        foreach (var who in clients)
        {
            var result = answered ? document!.For(who.ToolId) : null;
            var project = NearestProject(who, workingDirectory);

            if (result is null)
            {
                var unknown = Unknown(
                    RegistrationScope.User,
                    $"<{who.DisplayName}'s own configuration>",
                    tool,
                    answered ? $"its answer carried nothing about {who.DisplayName}" : problem);

                readings.Add(new ClientReading(who, null, unknown, project, null, [], unknown.Unreadable));
                continue;
            }

            readings.Add(new ClientReading(
                who,
                result.ClientPath,
                ViewOf(RegistrationScope.User, result),
                project,
                project is { Length: > 0 } && result.ClientPath is { Length: > 0 }
                    ? ReadProject(tool, who, project, installRoot, server)
                    : null,
                result.Advice));
        }

        return readings;
    }

    /// <summary>
    /// The nearest folder at or above a starting point that carries this client's
    /// project file, or <see langword="null"/>.
    /// </summary>
    /// <param name="who">The client, whose file is looked for.</param>
    /// <param name="start">Where the walk starts.</param>
    /// <returns>The folder, or null.</returns>
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

    /// <summary>One client's entry in one project.</summary>
    private static RegistrationView ReadProject(IRegisterAi tool, RegistrationClient who, string project, string? installRoot, string? server)
    {
        var command = server is { Length: > 0 } ? who.ProjectCommandFor(server, installRoot).Command : null;
        var run = tool.Run(
            McpRegistrar.Arguments("status", [who], "project", project, installRoot, replace: false, pathFolder: null, command),
            McpRegistrar.ToolBudget);

        if (!ToolDocuments.TryRead(run, out var document, out var problem))
        {
            return Unknown(RegistrationScope.Project, who.ProjectFileIn(project), tool, problem);
        }

        return document!.For(who.ToolId) is { } result
            ? ViewOf(RegistrationScope.Project, result)
            : Unknown(RegistrationScope.Project, who.ProjectFileIn(project), tool, $"its answer carried nothing about {who.DisplayName}");
    }

    /// <summary>An entry as BrowserAI names its states.</summary>
    private static RegistrationView ViewOf(RegistrationScope scope, ToolResult result)
    {
        var entry = result.Before;
        var file = result.Config ?? "<the client's own configuration>";

        return entry.State switch
        {
            "ours" => new RegistrationView(scope, file, entry.Command, RegistrationOwnership.OursAndPresent, null, entry.ResolvesTo),
            "ours-stale" => new RegistrationView(scope, file, entry.Command, RegistrationOwnership.OursAndStale, null, entry.ResolvesTo),
            "foreign" => new RegistrationView(scope, file, entry.Command, RegistrationOwnership.Foreign, null, entry.ResolvesTo),
            "unreadable" => new RegistrationView(
                scope,
                file,
                null,
                RegistrationOwnership.Absent,
                $"{result.Error ?? $"'{file}' could not be read."} What is registered there is unknown, which is not the same as nothing being registered."),
            _ => new RegistrationView(scope, file, entry.Command, RegistrationOwnership.Absent, null),
        };
    }

    /// <summary>What a client reads as when RegisterAI gave no answer about it.</summary>
    private static RegistrationView Unknown(RegistrationScope scope, string file, IRegisterAi tool, string problem) =>
        new(
            scope,
            file,
            null,
            RegistrationOwnership.Absent,
            $"BrowserAI could not ask RegisterAI at '{tool.Executable}' what is registered: {problem}. What is registered is unknown, which is not the same as nothing being registered.");
}
