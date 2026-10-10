// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

/// <summary>Which lifecycle event is asking, and therefore what it may do.</summary>
/// <remarks>
/// <b>The three differ in exactly one judgement: whose answer wins when an entry
/// is already there.</b> Getting that wrong in either direction is a real cost --
/// re-pointing always would silently discard a user's own edits on every update,
/// and never re-pointing would leave a stale path after a
/// <c>Setup.exe --installto</c> somewhere else, which is a product that cannot be
/// launched at all.
/// </remarks>
internal enum RegistrationIntent
{
    /// <summary>
    /// A fresh install. <b>An entry of ours is made to name this install's server</b>,
    /// and an entry of ours that already does is left as it is.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-10-03 (previously "<b>This install wins:</b> any existing
    /// entry is replaced, because the path just changed and the newest install is the
    /// authority on where BrowserAI now is")</b>, by the maintainer's decision Q347,
    /// verbatim <i>"Q347 a"</i>: replacing an entry that already names this server
    /// also dropped any argument a person had added to it, and RegisterAI's register
    /// leaves such an entry alone unless <c>--replace</c> asks. An entry of ours naming
    /// anything else is still re-pointed, and since 2026-09-16 a foreign one is refused.
    /// </remarks>
    Install,

    /// <summary>
    /// An update in place. <b>An existing entry of ours wins unless it names a
    /// file that is not there.</b>
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-09-15 (previously "<c>current\</c> is replaced
    /// wholesale but its path does not move, so there is nothing to correct ...
    /// Only an <i>absent</i> entry is written, which self-heals a registration
    /// somebody removed").</b> The premise stopped being true the day the server
    /// was renamed: the path inside <c>current\</c> <i>can</i> move now, and
    /// every registration written before that day names a file the update
    /// deleted. The half that survives is the reason -- a user who added
    /// arguments or environment variables to their own registration must not
    /// have them deleted by a background update -- so an entry of ours that still
    /// resolves is left exactly as it is, and only one that resolves to nothing
    /// is re-pointed. A <c>browserai</c> entry outside our install root is
    /// somebody else's and is reported, not touched.
    /// </remarks>
    Update,

    /// <summary>An uninstall. The entry goes, and its absence is not a failure.</summary>
    Uninstall,
}

/// <summary>What one registration pass concluded.</summary>
internal enum RegistrationStatus
{
    /// <summary>The entry was written.</summary>
    Registered,

    /// <summary>An entry was already there and was deliberately left alone.</summary>
    AlreadyRegistered,

    /// <summary>The entry was removed.</summary>
    Unregistered,

    /// <summary>There was no entry to remove, which is an ordinary outcome.</summary>
    NothingToUnregister,

    /// <summary>
    /// This machine has no client command line, so there is nothing to register
    /// with. Ordinary, and logged, not failed.
    /// </summary>
    ClientNotFound,

    /// <summary>
    /// BrowserAI refused to register the path it was asked about -- the execution
    /// stub, or anything else outside <c>current\</c>.
    /// </summary>
    Refused,

    /// <summary>The client was there, ran, and did not do what was asked.</summary>
    Failed,
}

/// <summary>What a registration pass did, in the form the record file stores.</summary>
/// <param name="Status">The conclusion.</param>
/// <param name="Detail">
/// One sentence a person can act on, carrying the client's own words when it had
/// any and the manual command when the pass failed.
/// </param>
/// <param name="ClientPath">The client executable that was used, when one was found.</param>
/// <param name="Command">The path that was, or would have been, registered.</param>
/// <param name="ResolvesTo">
/// The file the entry resolves to afterwards, as RegisterAI reported it, when it
/// reported one. Added 2026-10-03, for the sentence after a Codex project
/// registration, which names what its bare name finds.
/// </param>
internal sealed record RegistrationReport(RegistrationStatus Status, string Detail, string? ClientPath, string? Command, string? ResolvesTo = null)
{
    /// <summary>
    /// Whether the pass left the machine in the state it was asked for.
    /// </summary>
    /// <remarks>
    /// <see cref="RegistrationStatus.ClientNotFound"/> counts: a machine with no
    /// MCP client is correctly configured for the client it does not have. Only
    /// <see cref="RegistrationStatus.Failed"/> and
    /// <see cref="RegistrationStatus.Refused"/> are wrong.
    /// </remarks>
    public bool IsWhatWasAskedFor => Status is not (RegistrationStatus.Failed or RegistrationStatus.Refused);
}

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
internal static class McpRegistrar
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
    public static TimeSpan ToolTimeout { get; } = ProcessBounds.RegisterAiTimeout;

    /// <summary>How long BrowserAI waits for that run: the tool's own budget and a margin to stop its clients.</summary>
    public static TimeSpan ToolBudget { get; } = ProcessBounds.RegisterAiBudget;

    /// <summary>Runs one user-scope pass for one client.</summary>
    /// <param name="who">The client.</param>
    /// <param name="intent">Which lifecycle event or click is asking.</param>
    /// <param name="imagePath">This process's own image, which decides the command.</param>
    /// <param name="tool">RegisterAI.</param>
    /// <param name="logger">Where the pass reports.</param>
    /// <param name="replace">Whether an entry of ours that already matches is rewritten.</param>
    /// <param name="commandArguments">The arguments the command is registered with, or <see langword="null"/> for the target's own, <c>--mcp</c>.</param>
    /// <returns>What happened. Never <see langword="null"/>, never throws.</returns>
    public static RegistrationReport Apply(
        RegistrationClient who,
        RegistrationIntent intent,
        string? imagePath,
        IRegisterAi tool,
        ILogger logger,
        bool replace = false,
        IReadOnlyList<string>? commandArguments = null)
    {
        ArgumentNullException.ThrowIfNull(who);

        return Apply([who], intent, imagePath, tool, logger, replace, commandArguments)[0].Report;
    }

    /// <summary>Runs one user-scope pass for several clients, with one run of RegisterAI.</summary>
    /// <param name="clients">The clients, in the order the record lists them.</param>
    /// <param name="intent">Which lifecycle event or click is asking.</param>
    /// <param name="imagePath">This process's own image, which decides the command.</param>
    /// <param name="tool">RegisterAI.</param>
    /// <param name="logger">Where the pass reports.</param>
    /// <param name="replace">Whether an entry of ours that already matches is rewritten.</param>
    /// <param name="commandArguments">The arguments the command is registered with, or <see langword="null"/> for the target's own, <c>--mcp</c>.</param>
    /// <returns>One pass per client, in the order given. Never throws.</returns>
    public static IReadOnlyList<ClientRegistration> Apply(
        IReadOnlyList<RegistrationClient> clients,
        RegistrationIntent intent,
        string? imagePath,
        IRegisterAi tool,
        ILogger logger,
        bool replace = false,
        IReadOnlyList<string>? commandArguments = null)
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
                Arguments(verb, clients, "user", project: null, target.InstallRoot, replace, pathFolder: null, command, commandArguments ?? target.Arguments),
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
                Arguments(verb, [who], "project", project, target!.InstallRoot, replace: false, pathFolder, command, target.Arguments),
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
    /// <param name="commandArguments">
    /// What the command is started with, after it: <see cref="RegistrationTarget.Arguments"/>
    /// when registering, since 2026-10-08 (D7 a, <c>--mcp</c>), and nothing for a read.
    /// </param>
    /// <returns>The arguments, one element each.</returns>
    internal static List<string> Arguments(
        string verb,
        IReadOnlyList<RegistrationClient> clients,
        string scope,
        string? project,
        string? ownedRoot,
        bool replace,
        string? pathFolder,
        string? command,
        IReadOnlyList<string>? commandArguments = null)
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
            arguments.AddRange(["--", command, .. commandArguments ?? []]);
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

                return new RegistrationReport(RegistrationStatus.Registered, $"Wrote '{file}' registering '{command}' for {who.DisplayName}.", client, command, result.After.ResolvesTo);

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
                    result.Before.Command,
                    result.Before.ResolvesTo);

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

/// <summary>Source-generated log messages for the registration path.</summary>
internal static partial class RegistrationLog
{
    /// <summary>The entry was written.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The name it was registered under.</param>
    /// <param name="command">The executable a client will now launch.</param>
    /// <param name="client">The client command line that did it.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Registered '{Server}' as a user-scoped MCP server pointing at {Command}, using {Client}. It is available in every repository and needs no file in any of them.")]
    public static partial void Registered(ILogger logger, string server, string command, string client);

    /// <summary>An entry was already present and was left alone.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The name.</param>
    /// <param name="command">What this build would have registered.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "'{Server}' is already registered at user scope, so nothing was changed. This build would have pointed it at {Command}; an update never overwrites a registration, because the path does not move and any arguments on it may not be ours.")]
    public static partial void AlreadyRegistered(ILogger logger, string server, string command);

    /// <summary>The entry was removed.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The name.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Removed '{Server}' from the client's user-scoped MCP servers.")]
    public static partial void Unregistered(ILogger logger, string server);

    /// <summary>
    /// An entry of ours naming a file that is not there any more, re-pointed.
    /// </summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="was">What the entry named.</param>
    /// <param name="now">What it names now.</param>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "Repaired the MCP registration: it named '{Was}', which is no longer there, and now names '{Now}'.")]
    public static partial void Repairing(ILogger logger, string was, string now);

    /// <summary>There was nothing registered to remove.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The name.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "There was no '{Server}' registered at user scope to remove. Nothing is wrong: this is what uninstalling a BrowserAI that was already unregistered looks like.")]
    public static partial void NothingToUnregister(ILogger logger, string server);

    /// <summary>
    /// This machine has no client command line.
    /// </summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="executable">What was looked for.</param>
    /// <param name="where">Where it was looked for.</param>
    /// <param name="manual">The line that registers by hand.</param>
    /// <remarks>
    /// <para>
    /// <b>Warning, not Information.</b> An installed BrowserAI that no
    /// client can reach is the exact state this whole mechanism exists to
    /// prevent, and the fact that it is nobody's fault does not make it a state
    /// anyone should have to guess at.
    /// </para>
    /// <para>
    /// <i>Corrected 2026-10-03 (previously the message named <c>claude mcp add</c>
    /// for every client and "on PATH or at" one folder)</i>: the manual line is the
    /// client's own, because the second client has one of its own, and where the
    /// search looked is said by whoever searched.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "No '{Executable}' was found {Where}, so BrowserAI has not registered itself with that client. It is installed and working; nothing is configured to talk to it. Register it by hand once the client is installed: {Manual}")]
    public static partial void NoClient(ILogger logger, string executable, string where, string manual);

    /// <summary>BrowserAI refused to register the path it was given.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="refusal">Which path, and why not.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "BrowserAI refused to register itself. {Refusal}")]
    public static partial void Refused(ILogger logger, string refusal);

    /// <summary>The client ran and did not do what was asked.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="verb">Register or unregister.</param>
    /// <param name="client">The client executable.</param>
    /// <param name="said">What it did, in its own words where it had any.</param>
    /// <param name="manual">The command to run by hand.</param>
    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "BrowserAI could not {Verb} itself with the MCP client. {Client} {Said}. BrowserAI is installed and working; what is missing is the client's pointer at it. Run: {Manual}")]
    public static partial void Failed(ILogger logger, string verb, string client, string said, string manual);

    /// <summary>The pass threw, which the installer must never see.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Error,
        Message = "The MCP registration pass threw. The install itself is unaffected -- a hook that throws breaks an installer, so this is caught here and reported instead.")]
    public static partial void PassFailed(ILogger logger, Exception failure);

    /// <summary>Where the registration record went, and what it says.</summary>
    /// <remarks>
    /// ⚠️ <b>Once per client since 2026-09-24, and the client is named.</b> One
    /// record now carries one outcome per client, so a single line could only
    /// have named one of them -- which is the same reduction the record itself
    /// refuses to make. The path repeats because there is still one file.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="path">The record file.</param>
    /// <param name="client">Which client this outcome is about.</param>
    /// <param name="status">The outcome it records for that client.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Information,
        Message = "MCP registration state is at {Path}: {Client}={Status}")]
    public static partial void RecordWritten(ILogger logger, string path, string client, RegistrationStatus status);

    /// <summary>The record could not be written, which is a second silence.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="path">Where it was going.</param>
    /// <param name="failure">Why it did not get there.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "The MCP registration record at {Path} could not be written. The log record above is the only account of what happened.")]
    public static partial void RecordNotWritten(ILogger logger, string path, Exception failure);
}
