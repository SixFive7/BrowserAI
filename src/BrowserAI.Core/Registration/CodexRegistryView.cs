// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Registration;

/// <summary>
/// What Codex says is registered, asked of Codex.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>IT ASKS THE CLIENT AND NEVER READS THE TOML, AND THAT IS THE WHOLE
/// DESIGN.</b> <see cref="McpRegistryView"/> reads Claude Code's JSON because the
/// client offers no read command that answers the ownership question cheaply.
/// Codex offers one: <c>codex mcp list --json</c> and <c>codex mcp get &lt;name&gt;
/// --json</c> report the name, whether it is enabled, and the transport with its
/// command, args and env. So this product never parses
/// <c>config.toml</c> -- which keeps the charter's shape on the client where
/// direct writing would have been easiest, and it means a future Codex that
/// changes its own file format costs nothing here.
/// </para>
/// <para>
/// <b>The ownership rule is the shared one.</b>
/// <see cref="McpRegistryView.Classify"/> decides ours-by-command-path against the
/// install root, and it is called and not copied: two implementations of <i>is
/// this ours</i> are two answers waiting to disagree, and the consequence of
/// disagreeing is deleting somebody else's registration.
/// <i>Corrected 2026-09-24 by addition: the shared half is
/// <see cref="McpRegistryView.ClassifyPath"/>, the judgement of a path. How a
/// command becomes a path is each client's own, and Codex expands nothing; see
/// <see cref="Classify(string?, string?)"/>.</i>
/// </para>
/// <para>
/// ⚠️ <b>A CLIENT THAT COULD NOT BE ASKED IS UNREADABLE AND NOT ABSENT.</b> A
/// non-zero exit, a timeout or output that is not JSON all answer
/// <see cref="RegistrationOwnership.Absent"/> with <c>Unreadable</c> set, exactly
/// as a locked <c>.claude.json</c> does -- because <i>we could not find out</i>
/// and <i>there is nothing there</i> must not be the same answer to a registrar
/// that deletes things.
/// </para>
/// </remarks>
internal static class CodexRegistryView
{
    /// <summary>What Codex reports at the home it is pointed at.</summary>
    /// <param name="commands">The process runner.</param>
    /// <param name="client">The CLI, absolute.</param>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <param name="home">The <c>CODEX_HOME</c> to ask about, or null for the user's own.</param>
    /// <param name="scope">Which scope this reading is of, for the report.</param>
    /// <returns>What is registered there, and whose it is.</returns>
    /// <exception cref="ArgumentNullException">The runner is null.</exception>
    public static RegistrationView Read(
        IRegistrationCommand commands,
        string client,
        string? installRoot,
        string? home,
        RegistrationScope scope)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentException.ThrowIfNullOrWhiteSpace(client);

        var where = home is { Length: > 0 }
            ? Path.Combine(home, CodexRegistration.ConfigFileName)
            : $"{CodexRegistration.HomeVariable}'s own {CodexRegistration.ConfigFileName}";

        var outcome = commands.Run(
            client,
            CodexRegistration.ListArguments(),
            CodexRegistration.Budget,
            workingDirectory: null,
            environment: Environment(home));

        if (!outcome.Succeeded)
        {
            var why = outcome.TimedOut
                ? $"it did not answer within {CodexRegistration.Budget.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} s"
                : outcome.Failure ?? $"it exited {outcome.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            return new RegistrationView(
                scope,
                where,
                null,
                RegistrationOwnership.Absent,
                $"'{client} mcp list --json' could not be read ({why}), so what is registered in Codex is unknown. This is not the same as nothing being registered.");
        }

        return Parse(outcome.Output, where, installRoot, scope);
    }

    /// <summary>What Codex reports in a repository's own home.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A repository with no home has nothing registered, and asking Codex
    /// about it is not a way to find that out -- measured 2026-09-24 @ codex-cli
    /// 0.155.0-alpha.9.2.</b> With <c>CODEX_HOME</c> at a directory that does not
    /// exist, <c>mcp list --json</c>, <c>mcp remove</c> and <c>mcp add</c> each exit
    /// 1 with <i>CODEX_HOME points to ..., but that path does not exist</i> and
    /// create nothing. The first project registration through the registrar asked
    /// before the home existed, read that exit as UNREADABLE, and refused -- so a
    /// repository could never be registered the first time. Found by the
    /// real-client arm; the double did not model the refusal until then.
    /// </para>
    /// <para>
    /// <b>Absent and not unreadable</b>, because this one is established: the
    /// configuration file Codex would read is inside a directory that is not
    /// there.
    /// </para>
    /// </remarks>
    /// <param name="commands">The process runner.</param>
    /// <param name="client">The CLI, absolute.</param>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <param name="home">The repository's <c>.codex</c> directory.</param>
    /// <returns>What is registered there, and whose it is.</returns>
    public static RegistrationView ReadProject(IRegistrationCommand commands, string client, string? installRoot, string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);

        return Directory.Exists(home)
            ? Read(commands, client, installRoot, home, RegistrationScope.Project)
            : new RegistrationView(
                RegistrationScope.Project,
                Path.Combine(home, CodexRegistration.ConfigFileName),
                null,
                RegistrationOwnership.Absent,
                null);
    }

    /// <summary>
    /// What can be said about Codex's configuration when there is no Codex to
    /// ask.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><c>Unreadable</c> and NOT <c>Absent</c>, which is the same
    /// distinction the rest of this type is built on.</b> Nothing was read, so
    /// nothing is known -- and a reader that answered <i>nothing is registered</i>
    /// would let the configuration window offer to register over whatever is
    /// actually in that file. <i>Added 2026-09-24 with the window's per-client
    /// rows, which read every client whether or not one was found.</i>
    /// </remarks>
    /// <param name="scope">Which scope this reading would have been of.</param>
    /// <param name="home">The <c>CODEX_HOME</c> it would have asked about, or null for the user's own.</param>
    /// <returns>A reading that establishes nothing, and says so.</returns>
    public static RegistrationView WithoutAClient(RegistrationScope scope, string? home) =>
        new(
            scope,
            home is { Length: > 0 }
                ? Path.Combine(home, CodexRegistration.ConfigFileName)
                : $"{CodexRegistration.HomeVariable}'s own {CodexRegistration.ConfigFileName}",
            null,
            RegistrationOwnership.Absent,
            $"No '{CodexRegistration.ClientExecutable}' was found, so what is registered in Codex is unknown. This is not the same as nothing being registered.");

    /// <summary>The environment one call runs under.</summary>
    /// <remarks>
    /// <b>Only the one variable, and only when a home was named.</b> A project
    /// registration is the same command with <c>CODEX_HOME</c> moved
    /// (<see cref="CodexRegistration"/>), and a user registration must inherit the
    /// caller's own -- writing the default explicitly would override a
    /// <c>CODEX_HOME</c> the person set deliberately.
    /// </remarks>
    /// <param name="home">The home to force, or null to inherit.</param>
    /// <returns>The overrides, or an empty set.</returns>
    public static IReadOnlyDictionary<string, string> Environment(string? home) =>
        home is { Length: > 0 }
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [CodexRegistration.HomeVariable] = home }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads one <c>mcp list --json</c> answer.</summary>
    /// <remarks>
    /// <b>Both shapes the client has used are accepted</b>: a bare array of
    /// servers, and an object carrying one under a <c>servers</c> or
    /// <c>mcp_servers</c> key. Accepting both is not generosity -- it is the
    /// difference between a client bump that changes a wrapper and a product that
    /// silently reports nothing registered.
    /// </remarks>
    /// <param name="json">What the client printed.</param>
    /// <param name="where">The file this reading is about, for the report.</param>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <param name="scope">Which scope this reading is of.</param>
    /// <returns>What is registered, and whose it is.</returns>
    public static RegistrationView Parse(string json, string where, string? installRoot, RegistrationScope scope)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var servers = Servers(document.RootElement);

            if (servers is null)
            {
                return new RegistrationView(scope, where, null, RegistrationOwnership.Absent, null);
            }

            foreach (var server in servers.Value.EnumerateArray())
            {
                if (server.ValueKind is not JsonValueKind.Object
                    || !server.TryGetProperty("name", out var name)
                    || name.ValueKind is not JsonValueKind.String
                    || !string.Equals(name.GetString(), CodexRegistration.ServerName, StringComparison.Ordinal))
                {
                    continue;
                }

                var command = CommandOf(server);

                return new RegistrationView(scope, where, command, Classify(command, installRoot), null);
            }

            return new RegistrationView(scope, where, null, RegistrationOwnership.Absent, null);
        }
        catch (JsonException failure)
        {
            return new RegistrationView(
                scope,
                where,
                null,
                RegistrationOwnership.Absent,
                $"'codex mcp list --json' did not print readable JSON ({failure.Message}), so what is registered in Codex is unknown. BrowserAI will not act on a reading it does not have.");
        }
    }

    /// <summary>Whose a command in a Codex entry is.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>CODEX EXPANDS NOTHING, SO NEITHER DOES THIS -- 2026-09-24.</b>
    /// <i>Previously this view called <see cref="McpRegistryView.Classify"/>, which
    /// expands <c>${VAR}</c> the way Claude Code does.</i> Measured the same day at
    /// codex-cli 0.155.0-alpha.9.2: <c>${LOCALAPPDATA}</c>, <c>$LOCALAPPDATA</c>,
    /// <c>%LOCALAPPDATA%</c> and <c>~</c> in <c>command</c> started nothing in 48
    /// attempts, each failing with <i>os error 3</i>
    /// (<c>docs/evidence/2026-09-24-codex-expansion</c>). So an entry spelled the
    /// Claude way read here as ours and present while Codex could not start it, and
    /// the window would have reported a working registration that was not one.
    /// </para>
    /// <para>
    /// <b>A command that is not a fully qualified path is not ours.</b> BrowserAI
    /// never writes a variable, a tilde or a relative path into a Codex entry, so an
    /// entry carrying one was written by somebody else: reported, never touched.
    /// A fully qualified path goes to <see cref="McpRegistryView.ClassifyPath"/>,
    /// the half of the ownership rule both clients share.
    /// </para>
    /// <para>
    /// ⚠️ <b>Except a bare name, which is what a Codex PROJECT entry is since
    /// 2026-09-24 -- Q294, the maintainer's words verbatim: <i>"Q294 b"</i>.</b> A
    /// bare name is resolved the way Codex resolves it, through a PATH -- here the
    /// one a program started now would get (<see cref="UserPath.SearchDirectories"/>)
    /// -- and the file it finds first is what is judged: under this install root it
    /// is ours, anywhere else it is another install's. A bare
    /// <c>BrowserAI.Server.exe</c> that nothing on the PATH answers is the product's
    /// own spelling pointing at nothing, which is ours and stale, and a register
    /// over it writes the same name again; any other bare name that resolves to
    /// nothing is somebody else's.
    /// </para>
    /// </remarks>
    /// <param name="command">What the entry names, or <see langword="null"/>.</param>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <returns>The classification.</returns>
    public static RegistrationOwnership Classify(string? command, string? installRoot) =>
        Classify(command, installRoot, UserPath.SearchDirectories);

    /// <summary>Whose a command in a Codex entry is, given where a bare name is looked for.</summary>
    /// <param name="command">What the entry names, or <see langword="null"/>.</param>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <param name="folders">The PATH a bare name is resolved against, read only when there is one.</param>
    /// <returns>The classification.</returns>
    public static RegistrationOwnership Classify(string? command, string? installRoot, Func<IEnumerable<string>> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        if (command is not { Length: > 0 })
        {
            return RegistrationOwnership.Absent;
        }

        if (installRoot is not { Length: > 0 })
        {
            return RegistrationOwnership.Foreign;
        }

        if (UserPath.IsBareName(command))
        {
            if (UserPath.Resolve(command, folders()) is { } found)
            {
                return McpRegistryView.ClassifyPath(found, installRoot);
            }

            return string.Equals(command, RegistrationTarget.ServerFileName, StringComparison.OrdinalIgnoreCase)
                ? RegistrationOwnership.OursAndStale
                : RegistrationOwnership.Foreign;
        }

        return Path.IsPathFullyQualified(command)
            ? McpRegistryView.ClassifyPath(command, installRoot)
            : RegistrationOwnership.Foreign;
    }

    /// <summary>The array of servers, whichever wrapper the client put it in.</summary>
    /// <param name="root">The parsed root.</param>
    /// <returns>The array, or null when there is none.</returns>
    private static JsonElement? Servers(JsonElement root)
    {
        if (root.ValueKind is JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in new[] { "servers", "mcp_servers", "mcpServers" })
        {
            if (root.TryGetProperty(key, out var wrapped) && wrapped.ValueKind is JsonValueKind.Array)
            {
                return wrapped;
            }
        }

        return null;
    }

    /// <summary>The command one server entry names.</summary>
    /// <remarks>
    /// The transport carries it, and a flat <c>command</c> is accepted too for the
    /// same reason the wrapper keys are: a shape that moved must not read as an
    /// absence.
    /// </remarks>
    /// <param name="server">One entry.</param>
    /// <returns>The command, or null.</returns>
    private static string? CommandOf(JsonElement server)
    {
        if (server.TryGetProperty("transport", out var transport)
            && transport.ValueKind is JsonValueKind.Object
            && transport.TryGetProperty("command", out var nested)
            && nested.ValueKind is JsonValueKind.String)
        {
            return nested.GetString();
        }

        return server.TryGetProperty("command", out var flat) && flat.ValueKind is JsonValueKind.String
            ? flat.GetString()
            : null;
    }
}
