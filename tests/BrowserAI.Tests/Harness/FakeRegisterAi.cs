// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Registration;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// RegisterAI in process: it reads the command line BrowserAI builds, decides the way
/// RegisterAI decides, keeps the entries in memory, and answers with a schema-1
/// document. Every call is recorded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-03 with the switch to RegisterAI, in place of the double that
/// played each client's command line.</b> What RegisterAI does against real clients is
/// RegisterAI's own suite's to hold, and <c>RegisterAiTests</c> drives the real one
/// from the payload; what this models is enough of its contract for BrowserAI's side
/// to be driven: which command line goes out, and what BrowserAI makes of each answer.
/// </para>
/// <para>
/// <b>Ownership is RegisterAI's rule, reduced:</b> an entry naming exactly the command
/// given, case aside, or naming a file under an <c>--owned-root</c>, is ours; ours and
/// naming a file that is gone, or naming something other than the command, is stale;
/// anything else is foreign. Claude Code's <c>${NAME}</c> is expanded from this
/// process's environment; nothing is looked up on PATH, so a bare name is ours only by
/// naming the command, and stale.
/// </para>
/// </remarks>
internal sealed class FakeRegisterAi : IRegisterAi
{
    /// <summary>Both clients RegisterAI knows, in its fixed order.</summary>
    private static readonly string[] ClientIds = ["claude-code", "codex"];

    /// <summary>Where the fake says it is.</summary>
    public string Executable { get; set; } = @"C:\fake\current\payload\registerai\RegisterAI.exe";

    /// <summary>Every entry, by client id, scope and project (empty for user scope): the command it names.</summary>
    public Dictionary<(string Client, string Scope, string Project), string> Entries { get; } = [];

    /// <summary>Clients whose executable is not found.</summary>
    public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

    /// <summary>Clients whose configuration cannot be read.</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

    /// <summary>Clients whose write fails.</summary>
    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    /// <summary>A run that replaces the model's answer for every call, when set.</summary>
    public ToolRun? Answer { get; set; }

    /// <summary>Whether every call throws, the way a defect in the runner would.</summary>
    public bool Throws { get; set; }

    /// <summary>Every command line, in order.</summary>
    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// <summary>The verb of each call.</summary>
    public List<string> Verbs => [.. Calls.Select(call => call[0])];

    /// <summary>The user-scope entry a client has, or null.</summary>
    /// <param name="client">The client id.</param>
    /// <returns>The command.</returns>
    public string? UserEntry(string client) => Entries.GetValueOrDefault((client, "user", string.Empty));

    /// <summary>Gives a client a user-scope entry, as somebody would have registered it.</summary>
    /// <param name="client">The client id.</param>
    /// <param name="command">The command.</param>
    public void Register(string client, string command) => Entries[(client, "user", string.Empty)] = command;

    /// <summary>Gives a client a project entry.</summary>
    /// <param name="client">The client id.</param>
    /// <param name="project">The project folder.</param>
    /// <param name="command">The command.</param>
    public void RegisterIn(string client, string project, string command) => Entries[(client, "project", project)] = command;

    /// <summary>The value of one option in a recorded call, or null.</summary>
    /// <param name="call">The call.</param>
    /// <param name="option">The option, with its dashes.</param>
    /// <returns>Its value.</returns>
    public static string? Option(IReadOnlyList<string> call, string option)
    {
        ArgumentNullException.ThrowIfNull(call);

        for (var index = 0; index < call.Count - 1 && call[index] is not "--"; index++)
        {
            if (call[index] == option)
            {
                return call[index + 1];
            }
        }

        return null;
    }

    /// <summary>The command after <c>--</c> in a recorded call, or null.</summary>
    /// <param name="call">The call.</param>
    /// <returns>The command.</returns>
    public static string? Command(IReadOnlyList<string> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        var separator = call.ToList().IndexOf("--");

        return separator >= 0 && separator + 1 < call.Count ? call[separator + 1] : null;
    }

    /// <inheritdoc/>
    public ToolRun Run(IReadOnlyList<string> arguments, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Calls.Add([.. arguments]);

        if (Throws)
        {
            throw new InvalidOperationException("The fake RegisterAI was asked to throw, the way a defect in the runner would.");
        }

        if (Answer is { } canned)
        {
            return canned;
        }

        var verb = arguments[0];
        var scope = Option(arguments, "--scope") ?? "user";
        var project = Option(arguments, "--project") ?? string.Empty;
        var command = Command(arguments);
        var roots = Options(arguments, "--owned-root");
        var replace = arguments.Contains("--replace");
        var asked = Options(arguments, "--client");
        var clients = ClientIds
            .Where(client => asked.Count is 0 || asked.Contains("all") || asked.Contains(client))
            .ToList();

        var results = new JsonArray();

        foreach (var client in clients)
        {
            results.Add(Result(verb, client, scope, project, command, roots, replace));
        }

        // The version is the one the committed stamp names, so the fake reports the
        // RegisterAI the payload carries and a new release needs no edit here.
        var document = new JsonObject
        {
            ["tool"] = "registerai",
            ["version"] = ResolvedVersions.FromRegisterAiStamp(),
            ["schema"] = ToolDocuments.Schema,
            ["verb"] = verb,
            ["dryRun"] = false,
            ["exitCode"] = 0,
            ["error"] = null,
            ["server"] = new JsonObject { ["name"] = Option(arguments, "--name"), ["command"] = command, ["args"] = new JsonArray(), ["env"] = new JsonArray() },
            ["results"] = results,
            ["path"] = null,
        };

        return new ToolRun(0, document.ToJsonString(), string.Empty, TimedOut: false, null);
    }

    private static List<string> Options(IReadOnlyList<string> call, string option)
    {
        var values = new List<string>();

        for (var index = 0; index < call.Count - 1 && call[index] is not "--"; index++)
        {
            if (call[index] == option)
            {
                values.Add(call[index + 1]);
            }
        }

        return values;
    }

    private JsonObject Result(string verb, string client, string scope, string project, string? command, List<string> roots, bool replace)
    {
        var key = (client, scope, project);
        var missing = Missing.Contains(client);
        var clientPath = missing ? null : $@"C:\fake\clients\{(client is "codex" ? "codex" : "claude")}.exe";
        var config = scope is "project"
            ? (client is "codex" ? Path.Combine(project, ".codex", "config.toml") : Path.Combine(project, ".mcp.json"))
            : (client is "codex" ? @"C:\fake\profile\.codex\config.toml" : @"C:\fake\profile\.claude.json");

        var state = missing && client is "codex" ? "unknown"
            : Unreadable.Contains(client) ? "unreadable"
            : Entries.TryGetValue(key, out var entry) ? Classify(client, entry, command, roots)
            : "absent";

        var before = Entry(state, Entries.GetValueOrDefault(key), client, roots);

        if (verb is "status")
        {
            var status = new JsonObject
            {
                ["client"] = client,
                ["scope"] = scope,
                ["project"] = scope is "project" ? project : null,
                ["clientPath"] = clientPath,
                ["config"] = config,
            };

            foreach (var (name, value) in before)
            {
                status[name] = value?.DeepClone();
            }

            status["advice"] = new JsonArray();
            status["error"] = state switch
            {
                "unknown" => "codex.exe was not found, so what Codex has registered is not known.",
                "unreadable" => $"'{config}' is not readable JSON, so what is registered there is unknown.",
                _ => null,
            };

            return status;
        }

        var action = (verb, state) switch
        {
            (_, "unknown") => "client-not-found",
            (_, "unreadable") => "refused-unreadable",
            ("register", "absent") => "added",
            ("register", "ours") => replace ? "replaced" : "none",
            ("register", "ours-stale") => string.Equals(Entries[key], command, StringComparison.OrdinalIgnoreCase) && !replace ? "none" : "replaced",
            (_, "foreign") => "refused-foreign",
            ("unregister", "absent") => "none",
            _ => "removed",
        };

        if (missing && action is "added" or "replaced" or "removed")
        {
            action = "client-not-found";
        }

        var error = action switch
        {
            "client-not-found" => $"{(client is "codex" ? "codex.exe" : "claude.exe")} was not found on PATH, so nothing was run.",
            "refused-unreadable" => $"'{config}' is not readable JSON, so what is registered there is unknown. Nothing was changed.",
            "refused-foreign" => $"The entry names '{Entries.GetValueOrDefault(key)}', which is not under an owned root and is not the command given. Nothing was changed.",
            _ => null,
        };

        string? said = null;

        if (action is "added" or "replaced" or "removed" && Failing.Contains(client))
        {
            action = "failed";
            said = "the fake client failed";
            error = $"{(client is "codex" ? "codex.exe" : "claude.exe")} exited 1 while writing the entry. What it printed is in 'said'.";
        }
        else if (action is "added" or "replaced")
        {
            Entries[key] = command!;
        }
        else if (action is "removed")
        {
            _ = Entries.Remove(key);
        }

        var afterState = Entries.TryGetValue(key, out var now) ? Classify(client, now, command, roots) : "absent";

        return new JsonObject
        {
            ["client"] = client,
            ["scope"] = scope,
            ["project"] = scope is "project" ? project : null,
            ["clientPath"] = clientPath,
            ["config"] = config,
            ["before"] = before,
            ["action"] = action,
            ["after"] = action is "failed" ? before.DeepClone() : Entry(afterState, Entries.GetValueOrDefault(key), client, roots),
            ["ran"] = new JsonArray(),
            ["said"] = said,
            ["advice"] = new JsonArray(),
            ["manual"] = $"{(client is "codex" ? "codex" : "claude")} mcp {(verb is "register" ? "add" : "remove")} browserai",
            ["error"] = error,
        };
    }

    private static JsonObject Entry(string state, string? command, string client, IReadOnlyList<string>? roots = null) => new()
    {
        ["state"] = state,
        ["command"] = command,
        ["args"] = new JsonArray(),
        ["env"] = new JsonArray(),
        ["resolvesTo"] = command is null ? null : Resolve(command, client, roots),
    };

    private static string Classify(string client, string entry, string? command, List<string> roots)
    {
        var resolved = Resolve(entry, client, roots);
        var namesCommand = command is not null && string.Equals(entry, command, StringComparison.OrdinalIgnoreCase);
        var underRoot = resolved is not null && roots.Any(root =>
            resolved.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        if (!namesCommand && !underRoot)
        {
            return "foreign";
        }

        return resolved is null || !File.Exists(resolved) || (command is not null && !namesCommand) ? "ours-stale" : "ours";
    }

    /// <summary>What a command names, the way RegisterAI resolves it.</summary>
    /// <remarks>
    /// <b>A bare name resolves into an owned root's <c>current\</c> since 2026-10-10</b>, which
    /// stands in for the folder the installer puts on the user's PATH (Q294 b): RegisterAI
    /// finds a bare name on the PATH a new program gets, and the fake has no PATH of its own.
    /// It mattered once Claude Code's project entry took the bare name too, the maintainer's
    /// 30 that day; before, a bare entry read as stale here and no arm read one back.
    /// </remarks>
    private static string? Resolve(string command, string client, IReadOnlyList<string>? roots = null)
    {
        var text = command;

        if (roots is { Count: > 0 } && text.Length > 0 && text.IndexOfAny(['\\', '/', ':']) < 0)
        {
            return roots
                .Select(root => Path.Combine(root, RegistrationTarget.CurrentDirectoryName, text))
                .FirstOrDefault(File.Exists);
        }

        if (client is "claude-code" && text.Contains("${", StringComparison.Ordinal))
        {
            var open = text.IndexOf("${", StringComparison.Ordinal);
            var close = text.IndexOf('}', open);
            var name = text[(open + 2)..close];

            text = text[..open] + (Environment.GetEnvironmentVariable(name) ?? text[open..(close + 1)]) + text[(close + 1)..];
        }

        try
        {
            return Path.IsPathFullyQualified(text) ? Path.GetFullPath(text) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
