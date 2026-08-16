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
/// ⚠️ <b>Its answers are upstream's own, measured rather than invented.</b>
/// Measured 2026-08-16 @ Claude Code 2.1.233: a duplicate <c>add</c> exits
/// <b>1</b> with <i>"MCP server browserai already exists in user config"</i> and
/// a <c>remove</c> of an absent name exits <b>1</b> with <i>"No MCP server named
/// \"browserai\" in user scope"</i> — the same exit code as every real failure,
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

    /// <summary>What is registered, by server name, valued by the command.</summary>
    public Dictionary<string, string> Registered { get; } = new(StringComparer.Ordinal);

    /// <summary>The verbs this double was asked for, in order — <c>add</c> or <c>remove</c>.</summary>
    public IReadOnlyList<string> Verbs => [.. Invocations.Select(arguments => arguments.Count > 1 ? arguments[1] : "<none>")];

    /// <inheritdoc />
    public string? Locate(string executableName) => Executable;

    /// <inheritdoc />
    public CommandOutcome Run(string executable, IReadOnlyList<string> arguments, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(arguments);
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
        if (arguments.Count < 3)
        {
            return new CommandOutcome(1, "unrecognised arguments", TimedOut: false, null);
        }

        var verb = arguments[1];
        var name = arguments[2];

        return verb switch
        {
            "add" => Add(name, arguments[^1]),
            "remove" => Remove(name),
            _ => new CommandOutcome(1, $"unknown command {verb}", TimedOut: false, null),
        };
    }

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
}
