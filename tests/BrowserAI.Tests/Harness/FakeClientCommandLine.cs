// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Registration;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A scripted MCP client command line, so registration can be driven without an
/// installed Velopack layout and without touching anybody's configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it replaces is one <c>CreateProcessW</c> and a <c>PATH</c> walk.</b>
/// The intent split, the idempotence, the refusal of the execution stub, the
/// reading of the client's exit codes and every log record are on the product's
/// side of the seam and run here exactly as they do in an installer.
/// </para>
/// <para>
/// ⚠️ <b>Its answers are upstream's own, measured, not invented.</b>
/// Measured 2026-08-16 @ Claude Code 2.1.233: a duplicate <c>add</c> exits
/// <b>1</b> with <i>"MCP server browserai already exists in user config"</i> and
/// a <c>remove</c> of an absent name exits <b>1</b> with <i>"No MCP server named
/// \"browserai\" in user scope"</i> -- the same exit code as every real failure,
/// which is why the product has to read the words. A double that invented
/// friendlier exit codes would let the product's discrimination rot unnoticed,
/// so <c>RegistrationTests.TheClientStillSaysWhatTheExitCodesCannot</c> asserts
/// the same wording against the real client in the same run.
/// </para>
/// </remarks>
internal sealed class FakeClientCommandLine : IRegistrationCommand
{
    /// <summary>The path this double reports for the client executable.</summary>
    public static string DefaultExecutable => @"C:\double\claude.exe";

    /// <summary>
    /// What <see cref="Locate"/> answers. <see langword="null"/> is a machine
    /// with no MCP client on it.
    /// </summary>
    public string? Executable { get; init; } = DefaultExecutable;

    /// <summary>
    /// When set, every invocation answers this instead of modelling the client.
    /// </summary>
    public CommandOutcome? Always { get; init; }

    /// <summary>Whether every invocation throws instead of answering.</summary>
    public bool Throws { get; init; }

    /// <summary>Every argument vector this double was given, in order.</summary>
    public List<IReadOnlyList<string>> Invocations { get; } = [];

    /// <summary>
    /// The working directory each invocation was given, in the same order.
    /// </summary>
    public List<string?> Directories { get; } = [];

    /// <summary>What is registered with the SCOPED client, by server name, valued by the command.</summary>
    public Dictionary<string, string> Registered { get; } = new(StringComparer.Ordinal);

    /// <summary>What is registered with the SCOPELESS client, by server name.</summary>
    /// <remarks>
    /// ⚠️ <b>A second registry, because the two clients are two configurations
    /// and sharing one here would hide the failure that matters most</b> -- a hook
    /// that registered with one client and reported success for both. See
    /// <see cref="IsScopeless"/> for how a call is attributed, and why the
    /// attribution is a real property of the call and not a flag a test sets.
    /// </remarks>
    public Dictionary<string, string> CodexRegistered { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// What is registered with the scopeless client under a FORCED
    /// <c>CODEX_HOME</c>, by that home.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A registry per home, because for this client the home IS the
    /// scope.</b> A project registration is the same command with
    /// <c>CODEX_HOME</c> moved, so a double that kept one registry could not tell
    /// a project write from a write into the user's own configuration -- which is
    /// the one mistake that exits 0 and changes somebody's setup silently.
    /// <see cref="CodexRegistered"/> is the inherited home; every forced one is
    /// here.
    /// </remarks>
    public Dictionary<string, Dictionary<string, string>> CodexHomes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The registry a scopeless call writes, chosen by the home it was given.</summary>
    /// <param name="environment">The variables the call was run with.</param>
    /// <returns>That home's registry.</returns>
    private Dictionary<string, string> CodexRegistryFor(IReadOnlyDictionary<string, string> environment)
    {
        if (!environment.TryGetValue("CODEX_HOME", out var home) || home is not { Length: > 0 })
        {
            return CodexRegistered;
        }

        if (!CodexHomes.TryGetValue(home, out var registry))
        {
            registry = new Dictionary<string, string>(StringComparer.Ordinal);
            CodexHomes[home] = registry;
        }

        return registry;
    }

    /// <summary>The verbs this double was asked for, in order -- <c>add</c> or <c>remove</c>.</summary>
    public IReadOnlyList<string> Verbs => [.. Invocations.Select(arguments => arguments.Count > 1 ? arguments[1] : "<none>")];

    /// <inheritdoc />
    public string? Locate(string executableName) => Executable;

    /// <inheritdoc />
    /// <summary>The environment each call was given, in order.</summary>
    public List<Dictionary<string, string>> Environments { get; } = [];

    /// <inheritdoc />
    public CommandOutcome Run(string executable, IReadOnlyList<string> arguments, TimeSpan budget) =>
        Run(executable, arguments, budget, workingDirectory: null);

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b>The working directory is RECORDED and not ignored</b>, because
    /// for a project-scope registration it is the only thing that decides where
    /// the file lands, and a double that dropped it would let an arm assert a
    /// successful write into a directory nobody named.
    /// </remarks>
    public CommandOutcome Run(string executable, IReadOnlyList<string> arguments, TimeSpan budget, string? workingDirectory) =>
        Run(executable, arguments, budget, workingDirectory, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b>The environment is RECORDED for the same reason the working
    /// directory is.</b> For Codex the scope IS an environment variable -- there is
    /// no <c>--scope</c> flag -- so a double that dropped it would let an arm
    /// assert a project registration that was actually written to the user's own
    /// home.
    /// </remarks>
    public CommandOutcome Run(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan budget,
        string? workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);

        Directories.Add(workingDirectory);
        Environments.Add(new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase));

        Invocations.Add([.. arguments]);

        if (Throws)
        {
            throw new InvalidOperationException("The double was asked to throw, the way a client that cannot be started does.");
        }

        if (Always is { } scripted)
        {
            return scripted;
        }

        // ["mcp", "add", <name>, "--scope", <scope>, "--", <command>]
        // ["mcp", "remove", <name>, "--scope", <scope>]
        // ["mcp", "add", <name>, "--", <command>]      the scopeless client
        // ["mcp", "remove", <name>]                    the scopeless client
        // ["mcp", "list", "--json"]                    the scopeless client
        if (arguments.Count < 3)
        {
            return new CommandOutcome(1, "unrecognised arguments", TimedOut: false, null);
        }

        var verb = arguments[1];
        var name = arguments[2];

        if (IsScopeless(arguments))
        {
            var registry = CodexRegistryFor(environment);

            return verb switch
            {
                "add" => CodexAdd(registry, name, arguments[^1]),
                "remove" => CodexRemove(registry, name),
                "list" => new CommandOutcome(0, CodexList(registry), TimedOut: false, null),
                "get" => new CommandOutcome(0, CodexList(registry), TimedOut: false, null),
                _ => new CommandOutcome(1, $"unknown command {verb}", TimedOut: false, null),
            };
        }

        return verb switch
        {
            "add" => Add(name, arguments[^1]),
            "remove" => Remove(name),
            _ => new CommandOutcome(1, $"unknown command {verb}", TimedOut: false, null),
        };
    }

    /// <summary>
    /// Whether this call belongs to the client that has no <c>--scope</c> flag.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Attributed by what the product actually hands the client, and not by
    /// a flag an arm sets.</b> The absence of <c>--scope</c> is the structural
    /// difference between the two clients
    /// (<see cref="BrowserAI.Registration.CodexRegistration"/>): one takes the
    /// scope as an argument, the other takes it as <c>CODEX_HOME</c>. So a double
    /// that told them apart any other way would be able to stay right while the
    /// product stopped being -- and a registration that lost its <c>--scope</c>
    /// would quietly be modelled as the other client's instead of failing.
    /// </remarks>
    /// <param name="arguments">The argument vector.</param>
    /// <returns>Whether it is the scopeless client's shape.</returns>
    private static bool IsScopeless(IReadOnlyList<string> arguments) =>
        !arguments.Contains("--scope", StringComparer.Ordinal);

    private CommandOutcome Add(string name, string command)
    {
        if (Registered.ContainsKey(name))
        {
            return new CommandOutcome(
                1,
                string.Create(CultureInfo.InvariantCulture, $"MCP server {name} already exists in user config"),
                TimedOut: false,
                null);
        }

        Registered[name] = command;

        return new CommandOutcome(
            0,
            string.Create(CultureInfo.InvariantCulture, $"Added stdio MCP server {name} with command: {command}  to user config"),
            TimedOut: false,
            null);
    }

    private CommandOutcome Remove(string name) =>
        Registered.Remove(name)
            ? new CommandOutcome(0, string.Create(CultureInfo.InvariantCulture, $"Removed MCP server {name} from user config"), TimedOut: false, null)
            : new CommandOutcome(1, string.Create(CultureInfo.InvariantCulture, $"No MCP server named \"{name}\" in user scope"), TimedOut: false, null);

    /// <summary>
    /// The scopeless client's add, which is idempotent and exits zero.
    /// </summary>
    /// <remarks>
    /// <b>Measured 2026-09-24 @ codex-cli 0.155.0-alpha.9.2</b>, which is the
    /// whole reason this is a second method and not a shared one: three
    /// consecutive adds exit <b>0</b>, where the scoped client exits 1 on the
    /// second. A double that gave both clients one dialect would let the
    /// product's discrimination between them rot unnoticed.
    /// </remarks>
    /// <param name="registry">The home's registry.</param>
    /// <param name="name">The server name.</param>
    /// <param name="command">What it is registered as.</param>
    /// <returns>Exit zero, always.</returns>
    private static CommandOutcome CodexAdd(Dictionary<string, string> registry, string name, string command)
    {
        registry[name] = command;

        return new CommandOutcome(
            0,
            string.Create(CultureInfo.InvariantCulture, $"Added MCP server {name}"),
            TimedOut: false,
            null);
    }

    /// <summary>The scopeless client's remove, which exits zero even when there was nothing.</summary>
    /// <param name="registry">The home's registry.</param>
    /// <param name="name">The server name.</param>
    /// <returns>Exit zero, always.</returns>
    private static CommandOutcome CodexRemove(Dictionary<string, string> registry, string name)
    {
        _ = registry.Remove(name);

        return new CommandOutcome(
            0,
            string.Create(CultureInfo.InvariantCulture, $"Removed MCP server {name}"),
            TimedOut: false,
            null);
    }

    /// <summary>
    /// The scopeless client's <c>mcp list --json</c>, in the shape the real one
    /// prints.
    /// </summary>
    /// <remarks>
    /// <b>A bare array whose entries carry <c>transport.command</c></b>, measured
    /// first-hand on the same day. Serialised through
    /// <see cref="System.Text.Json"/> because a Windows path inside a JSON string
    /// needs its backslashes doubled, and a hand-built literal that lost the
    /// doubling reads through the product's own view as UNREADABLE -- which is a
    /// different state from the one an arm meant to arrange.
    /// </remarks>
    /// <param name="registry">The home's registry.</param>
    /// <returns>The JSON.</returns>
    private static string CodexList(Dictionary<string, string> registry) =>
        "[" + string.Join(
            ",",
            registry.Select(entry =>
                "{\"name\":" + System.Text.Json.JsonSerializer.Serialize(entry.Key)
                + ",\"enabled\":true,\"transport\":{\"command\":"
                + System.Text.Json.JsonSerializer.Serialize(entry.Value)
                + ",\"args\":[]}}")) + "]";
}
