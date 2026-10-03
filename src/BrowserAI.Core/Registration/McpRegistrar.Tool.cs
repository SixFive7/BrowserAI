// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

/// <summary>
/// The registrar over RegisterAI: when to register and what to say about it are
/// BrowserAI's, and the registering is RegisterAI's.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q332, decided 2026-10-01 by the maintainer, verbatim: <i>"Go for only the small
/// command line program."</i></b> RegisterAI reads each client's entry, decides whether
/// it is this install's own, runs the client's own <c>mcp add</c> or <c>mcp remove</c>,
/// reads the entry back, and writes one JSON document saying so. What stays here is
/// what makes it BrowserAI's: which executable may be registered
/// (<see cref="RegistrationTarget"/>), the install root that makes an entry ours, the
/// command each client is given, the sentences a person reads and the record on disk.
/// </para>
/// <para>
/// <b>One run per hook, both clients in it.</b> RegisterAI is given
/// <see cref="ToolTimeout"/> for the whole run, which bounds both clients together;
/// two client calls of ten seconds each used to be able to outlast the fifteen the
/// update hook gets.
/// </para>
/// <para>
/// ⚠️ <b>One behaviour changed with the switch, by the maintainer's decision Q347,
/// verbatim: <i>"Q347 a"</i>.</b> An install over an entry of ours that already names
/// this server leaves it as it is, arguments a person added included; until
/// 2026-10-03 an install removed and re-added it. The window's <i>Register again</i>
/// is the explicit rewrite, and passes <c>--replace</c>.
/// </para>
/// </remarks>
internal static partial class McpRegistrar
{
    /// <summary>The name every client is given, and the prefix of every tool the model sees.</summary>
    public const string ServerName = "browserai";

    /// <summary>
    /// The budget RegisterAI is given for one run, as <c>--timeout</c>: both clients,
    /// every read and every write.
    /// </summary>
    /// <remarks>
    /// Sized against the tightest hook, <c>--veloapp-updated</c>'s fifteen seconds, and
    /// not against the measurement: a pass took 1 to 2 s in the 2026-10-01 survey.
    /// </remarks>
    public static TimeSpan ToolTimeout { get; } = TimeSpan.FromSeconds(12);

    /// <summary>How long BrowserAI waits for that run: the tool's own budget and a margin to stop its clients.</summary>
    public static TimeSpan ToolBudget { get; } = TimeSpan.FromSeconds(14);

    /// <summary>Runs one user-scope pass for one client.</summary>
    /// <param name="who">The client.</param>
    /// <param name="intent">Which lifecycle event or click is asking.</param>
    /// <param name="imagePath">This process's own image, which decides the command.</param>
    /// <param name="tool">RegisterAI.</param>
    /// <param name="logger">Where the pass reports.</param>
    /// <param name="replace">Whether an entry of ours that already matches is rewritten.</param>
    /// <returns>What happened. Never <see langword="null"/>, never throws.</returns>
    public static RegistrationReport Apply(
        RegistrationClient who,
        RegistrationIntent intent,
        string? imagePath,
        IRegisterAi tool,
        ILogger logger,
        bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(who);

        return Apply([who], intent, imagePath, tool, logger, replace)[0].Report;
    }

    /// <summary>Runs one user-scope pass for several clients, with one run of RegisterAI.</summary>
    /// <param name="clients">The clients, in the order the record lists them.</param>
    /// <param name="intent">Which lifecycle event or click is asking.</param>
    /// <param name="imagePath">This process's own image, which decides the command.</param>
    /// <param name="tool">RegisterAI.</param>
    /// <param name="logger">Where the pass reports.</param>
    /// <param name="replace">Whether an entry of ours that already matches is rewritten.</param>
    /// <returns>One pass per client, in the order given. Never throws.</returns>
    public static IReadOnlyList<ClientRegistration> Apply(
        IReadOnlyList<RegistrationClient> clients,
        RegistrationIntent intent,
        string? imagePath,
        IRegisterAi tool,
        ILogger logger,
        bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(logger);

        // No client asked about is no run at all: RegisterAI would refuse a
        // register that names none, and there is nothing to report either way.
        if (clients.Count is 0)
        {
            return [];
        }

        try
        {
            if (!RegistrationTarget.TryResolve(imagePath, out var target, out var refusal))
            {
                RegistrationLog.Refused(logger, refusal);
                return [.. clients.Select(who => new ClientRegistration(who.Key, who.DisplayName, new RegistrationReport(RegistrationStatus.Refused, refusal, null, imagePath)))];
            }

            var command = target!.Command;
            var verb = intent is RegistrationIntent.Uninstall ? "unregister" : "register";
            var run = tool.Run(
                Arguments(verb, clients, "user", project: null, target.InstallRoot, replace, pathFolder: null, command),
                ToolBudget);

            if (!ToolDocuments.TryRead(run, out var document, out var problem))
            {
                return [.. clients.Select(who => new ClientRegistration(who.Key, who.DisplayName, ToolFailed(who, tool, problem, verb, command, logger)))];
            }

            return
            [
                .. clients.Select(who => new ClientRegistration(
                    who.Key,
                    who.DisplayName,
                    document!.For(who.ToolId) is { } result
                        ? UserReport(who, intent, result, command, logger)
                        : ToolFailed(who, tool, $"its answer carried nothing about {who.DisplayName}", verb, command, logger))),
            ];
        }
#pragma warning disable CA1031 // The hook boundary. A registration failure is a log line, a record on disk and an install that still succeeds, never an exception into the installer.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            RegistrationLog.PassFailed(logger, failure);

            return
            [
                .. clients.Select(who => new ClientRegistration(
                    who.Key,
                    who.DisplayName,
                    new RegistrationReport(
                        RegistrationStatus.Failed,
                        $"The registration pass threw: {failure.Message}. BrowserAI is installed and is not registered with {who.DisplayName}; register it by hand with: {who.ManualCommandFor(imagePath ?? "<the installed BrowserAI.exe>")}",
                        null,
                        imagePath))),
            ];
        }
    }

    /// <summary>One pass against a repository's own configuration, in either direction, through RegisterAI.</summary>
    /// <param name="who">The client to write to.</param>
    /// <param name="register">Whether to add or to remove.</param>
    /// <param name="project">The repository root.</param>
    /// <param name="imagePath">This process's own image, which decides what may be registered.</param>
    /// <param name="tool">RegisterAI.</param>
    /// <param name="logger">Where the pass reports.</param>
    /// <param name="commandToRegister">
    /// What to write, or <see langword="null"/> for this client's own spelling of the
    /// server (<see cref="RegistrationClient.ProjectCommandFor"/>).
    /// </param>
    /// <returns>What happened. Never <see langword="null"/>, never throws.</returns>
    public static RegistrationReport ApplyToProject(
        RegistrationClient who,
        bool register,
        string project,
        string? imagePath,
        IRegisterAi tool,
        ILogger logger,
        string? commandToRegister = null)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            if (!RegistrationTarget.TryResolve(imagePath, out var target, out var refusal))
            {
                RegistrationLog.Refused(logger, refusal);
                return new RegistrationReport(RegistrationStatus.Refused, refusal, null, imagePath);
            }

            var command = commandToRegister is { Length: > 0 } spelled ? spelled : who.ProjectCommandFor(target!.Command, target.InstallRoot).Command;
            var verb = register ? "register" : "unregister";

            // A bare name is found through the PATH, so RegisterAI is told which
            // folder that has to be: the install's current\, which the hooks put on
            // the user's PATH (Q294 b).
            var pathFolder = register && IsBareName(command) ? Path.GetDirectoryName(target!.Command) : null;

            var run = tool.Run(
                Arguments(verb, [who], "project", project, target!.InstallRoot, replace: false, pathFolder, command),
                ToolBudget);

            if (!ToolDocuments.TryRead(run, out var document, out var problem))
            {
                return ToolFailed(who, tool, problem, verb, command, logger);
            }

            return document!.For(who.ToolId) is { } result
                ? ProjectReport(who, register, project, result, command, logger)
                : ToolFailed(who, tool, $"its answer carried nothing about {who.DisplayName}", verb, command, logger);
        }
#pragma warning disable CA1031 // Same boundary as Apply: a registration failure is a report, never an exception into a click handler.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            RegistrationLog.PassFailed(logger, failure);

            return new RegistrationReport(
                RegistrationStatus.Failed,
                $"The project registration pass threw: {failure.Message}. Nothing in '{project}' was changed by BrowserAI.",
                null,
                imagePath);
        }
    }

    /// <summary>The command line one run is given.</summary>
    /// <param name="verb">status, register or unregister.</param>
    /// <param name="clients">The clients asked about.</param>
    /// <param name="scope">user or project.</param>
    /// <param name="project">The repository, for project scope.</param>
    /// <param name="ownedRoot">The install root under which an entry is ours, or null.</param>
    /// <param name="replace">Whether an entry of ours that already matches is rewritten.</param>
    /// <param name="pathFolder">The folder a bare command is found in, or null.</param>
    /// <param name="command">The server's command, or null.</param>
    /// <returns>The arguments, one element each.</returns>
    internal static List<string> Arguments(
        string verb,
        IReadOnlyList<RegistrationClient> clients,
        string scope,
        string? project,
        string? ownedRoot,
        bool replace,
        string? pathFolder,
        string? command)
    {
        ArgumentNullException.ThrowIfNull(clients);

        var arguments = new List<string> { verb, "--name", ServerName };

        if (RegistrationClient.All.All(client => clients.Any(asked => asked.ToolId == client.ToolId)))
        {
            arguments.AddRange(["--client", "all"]);
        }
        else
        {
            foreach (var client in clients)
            {
                arguments.AddRange(["--client", client.ToolId]);
            }
        }

        arguments.AddRange(["--scope", scope]);

        if (project is { Length: > 0 })
        {
            arguments.AddRange(["--project", project]);
        }

        if (ownedRoot is { Length: > 0 })
        {
            arguments.AddRange(["--owned-root", ownedRoot]);
        }

        if (replace)
        {
            arguments.Add("--replace");
        }

        if (pathFolder is { Length: > 0 })
        {
            arguments.AddRange(["--path-folder", pathFolder]);
        }

        arguments.AddRange(["--timeout", ToolTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)]);

        if (command is { Length: > 0 })
        {
            arguments.AddRange(["--", command]);
        }

        return arguments;
    }

    /// <summary>Whether a command is a file name with no folder, which a client finds through PATH.</summary>
    /// <param name="command">The command.</param>
    /// <returns>Whether it names no folder at all.</returns>
    internal static bool IsBareName(string command) =>
        command.Length > 0 && command.IndexOfAny(['\\', '/', ':']) < 0;

    /// <summary>What one client's user-scope result amounts to, in BrowserAI's words.</summary>
    private static RegistrationReport UserReport(RegistrationClient who, RegistrationIntent intent, ToolResult result, string command, ILogger logger)
    {
        var client = result.ClientPath;

        switch (result.Action)
        {
            case "added" or "replaced":
                if (result.Before.State is "ours-stale")
                {
                    RegistrationLog.Repairing(logger, result.Before.Command ?? "<none>", command);
                }

                RegistrationLog.Registered(logger, ServerName, command, client ?? who.Executable);

                return new RegistrationReport(
                    RegistrationStatus.Registered,
                    $"Registered '{ServerName}' with {who.DisplayName} for this user, pointing at '{command}'. It is available in every repository and wrote no file into any of them.",
                    client,
                    command);

            case "removed":
                RegistrationLog.Unregistered(logger, ServerName);

                return new RegistrationReport(RegistrationStatus.Unregistered, $"Removed '{ServerName}' from {who.DisplayName} for this user.", client, command);

            case "none" when intent is RegistrationIntent.Uninstall:
                RegistrationLog.NothingToUnregister(logger, ServerName);

                return new RegistrationReport(
                    RegistrationStatus.NothingToUnregister,
                    $"There was no '{ServerName}' registered with {who.DisplayName} for this user to remove, which is what an uninstall of a BrowserAI somebody had already unregistered looks like.",
                    client,
                    command);

            case "none":
                RegistrationLog.AlreadyRegistered(logger, ServerName, command);

                return new RegistrationReport(
                    RegistrationStatus.AlreadyRegistered,
                    $"'{ServerName}' is already registered with {who.DisplayName} at '{result.Before.Command}' and was left exactly as it is.",
                    client,
                    result.Before.Command);

            default:
                return NotDone(who, intent is RegistrationIntent.Uninstall ? "unregister" : "register", result, command, logger, Foreign(who, result, intent is RegistrationIntent.Uninstall));
        }
    }

    /// <summary>What one client's project-scope result amounts to, in BrowserAI's words.</summary>
    private static RegistrationReport ProjectReport(RegistrationClient who, bool register, string project, ToolResult result, string command, ILogger logger)
    {
        var client = result.ClientPath;
        var file = result.Config ?? who.ProjectFileIn(project);

        switch (result.Action)
        {
            case "added" or "replaced":
                RegistrationLog.Registered(logger, ServerName, command, client ?? who.Executable);

                return new RegistrationReport(RegistrationStatus.Registered, $"Wrote '{file}' registering '{command}' for {who.DisplayName}.", client, command);

            case "removed":
                RegistrationLog.Unregistered(logger, ServerName);

                return new RegistrationReport(RegistrationStatus.Unregistered, $"Removed '{ServerName}' from '{file}' for {who.DisplayName}.", client, command);

            case "none" when !register:
                RegistrationLog.NothingToUnregister(logger, ServerName);

                return new RegistrationReport(
                    RegistrationStatus.NothingToUnregister,
                    $"There is no '{ServerName}' registered in '{project}' for {who.DisplayName} to remove.",
                    client,
                    command);

            case "none":
                RegistrationLog.AlreadyRegistered(logger, ServerName, command);

                return new RegistrationReport(
                    RegistrationStatus.AlreadyRegistered,
                    $"'{file}' already registers '{result.Before.Command}' for {who.DisplayName}, and it was left exactly as it is.",
                    client,
                    result.Before.Command);

            default:
                return NotDone(who, register ? "register in a project" : "unregister from a project", result, command, logger, Foreign(who, result, !register));
        }
    }

    /// <summary>The refusals, the missing client and the failure, which read the same at either scope.</summary>
    private static RegistrationReport NotDone(RegistrationClient who, string verb, ToolResult result, string command, ILogger logger, string foreign)
    {
        var client = result.ClientPath;
        var manual = who.ManualCommandFor(command);

        if (result.Action is "refused-foreign")
        {
            RegistrationLog.Refused(logger, foreign);
            return new RegistrationReport(RegistrationStatus.Refused, foreign, client, result.Before.Command);
        }

        if (result.Action is "refused-unreadable")
        {
            var unreadable = $"{result.Error ?? $"{who.DisplayName}'s configuration could not be read."} What is registered there is unknown, and BrowserAI acts on nothing it could not read.";

            RegistrationLog.Refused(logger, unreadable);
            return new RegistrationReport(RegistrationStatus.Refused, unreadable, client, command);
        }

        if (result.Action is "client-not-found")
        {
            RegistrationLog.NoClient(logger, who.Executable, "where RegisterAI looks", manual);

            return new RegistrationReport(
                RegistrationStatus.ClientNotFound,
                $"{result.Error ?? $"{who.Executable} was not found."} So BrowserAI has not registered itself with {who.DisplayName}. Install the client and run: {manual}",
                null,
                command);
        }

        var said = $"{result.Error ?? "RegisterAI reported the change as not done."}{(result.Said is { Length: > 0 } words ? $" What {who.Executable} printed: {words}" : string.Empty)}";

        RegistrationLog.Failed(logger, verb, client ?? who.Executable, said, manual);

        return new RegistrationReport(
            RegistrationStatus.Failed,
            $"BrowserAI could not {verb} itself with {who.DisplayName}. {said} BrowserAI is installed and working; what is missing is the client's pointer at it. Run: {manual}",
            client,
            command);
    }

    /// <summary>The sentence for an entry another install wrote.</summary>
    private static string Foreign(RegistrationClient who, ToolResult result, bool removing)
    {
        var advice = removing
            ? "That entry belongs to the other install, and removing it is for that install to do."
            : $"If this install is the one you want, unregister the other and register this one: {who.ManualCommandFor(result.Before.Command ?? "<this install's server>")}";

        return $"Another BrowserAI is registered at '{result.Before.Command ?? "<an entry with no local command>"}', which is not under this install root. "
            + $"Nothing was changed: BrowserAI never adopts, overwrites or removes a '{ServerName}' entry it did not write. "
            + advice;
    }

    /// <summary>A run of RegisterAI that gave no answer BrowserAI can read.</summary>
    private static RegistrationReport ToolFailed(RegistrationClient who, IRegisterAi tool, string problem, string verb, string command, ILogger logger)
    {
        var said = $"RegisterAI at '{tool.Executable}' gave no answer: {problem}.";

        RegistrationLog.Failed(logger, verb, tool.Executable, said, who.ManualCommandFor(command));

        return new RegistrationReport(
            RegistrationStatus.Failed,
            $"BrowserAI could not {verb} itself with {who.DisplayName}. {said} BrowserAI is installed and working; what is missing is the client's pointer at it. Run: {who.ManualCommandFor(command)}",
            null,
            command);
    }
}
