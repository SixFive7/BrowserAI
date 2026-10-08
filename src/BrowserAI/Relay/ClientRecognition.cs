// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Updates;

namespace BrowserAI.Relay;

/// <summary>
/// What a relay can tell about the client that started it, and what that client
/// needs once an update has ended the relay.
/// </summary>
/// <remarks>
/// <para>
/// <b>H1-T a, the maintainer's answer of 2026-10-08:</b> before an update, the toast
/// and the dashboard name the sessions that will need a reconnect after it, a
/// terminal Claude Code session (<c>/mcp</c>, BrowserAI, Reconnect) and a Codex
/// conversation (a new one). The brief added: whether a client is terminal Claude Code
/// is decided from what the relay can observe, and when that cannot be told reliably
/// the snapshot says so instead of guessing.
/// </para>
/// <para>
/// <b>What the relay observes, measured 2026-10-08 over 41 runs</b> (Claude Code
/// 2.1.292 and 2.1.294, codex-cli 0.161.0 and the desktop app-server
/// 0.159.0-alpha.12.1): the client's <c>clientInfo.name</c> at <c>initialize</c>, the
/// command line of the process that started the relay, and
/// <c>CLAUDE_CODE_ENTRYPOINT</c>. The protocol alone cannot split Claude Code's terminal
/// UI, its VS Code transport and <c>claude -p</c>; the parent's arguments can, 41 of
/// 41, and the entrypoint variable alone reads a terminal as VS Code when the value was
/// inherited, 3 of 3 such runs. Codex's <c>exec</c> and <c>app-server</c> look the same,
/// and both need a new conversation.
/// </para>
/// <para>
/// ⚠️ <b>PROVISIONAL: the measurement's option d, pending the maintainer's
/// confirmation</b> (the root session, 2026-10-08): the parent's arguments, then the
/// entrypoint when the parent cannot be read, then unknown. His other option, c, is
/// to never guess and name both remedies for every Claude Code session; it is
/// <see cref="ReadsTheParent"/> set to <see langword="false"/>, and nothing else
/// changes. The flags read here are the client's own and not a contract, so a client
/// release can move them.
/// </para>
/// </remarks>
internal static class ClientRecognition
{
    /// <summary>What Claude Code calls itself in <c>clientInfo.name</c>, in every mode measured.</summary>
    public const string ClaudeCode = "claude-code";

    /// <summary>What Codex calls itself in <c>clientInfo.name</c>, in every mode measured.</summary>
    public const string Codex = "codex-mcp-client";

    /// <summary>The variable Claude Code sets for the processes it starts, naming how it was started.</summary>
    public const string EntrypointVariable = "CLAUDE_CODE_ENTRYPOINT";

    /// <summary>
    /// Whether the parent's command line decides a Claude Code session's answer: the
    /// measurement's option d when <see langword="true"/>, and option c when
    /// <see langword="false"/>, which answers <see cref="RelayReconnect.Unknown"/> for
    /// every Claude Code session.
    /// </summary>
    public static bool ReadsTheParent { get; } = true;

    /// <summary>The arguments that mark a Claude Code that is not the terminal UI.</summary>
    private static readonly string[] HeadlessMarks = ["-p", "--print", "--input-format", "--output-format", "--sdk-url", "--init-only"];

    /// <summary>What the client needs once an update has ended its relay.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="parentCommandLine">
    /// The command line of the process that started the relay, verified to have started
    /// no later than the relay, or <see langword="null"/> when it could not be read.
    /// </param>
    /// <param name="entrypoint">The value of <see cref="EntrypointVariable"/>, or <see langword="null"/>.</param>
    /// <returns>The answer, <see cref="RelayReconnect.Unknown"/> whenever it cannot be told.</returns>
    public static RelayReconnect Reconnect(string? clientName, string? parentCommandLine, string? entrypoint) =>
        Reconnect(clientName, parentCommandLine, entrypoint, ReadsTheParent);

    /// <summary>What the client needs once an update has ended its relay, under either of the measurement's options.</summary>
    /// <remarks>
    /// <b>The suite's way to hold option c</b>, which <see cref="ReadsTheParent"/> chooses
    /// for the product: <paramref name="readsTheParent"/> set to <see langword="false"/>
    /// answers <see cref="RelayReconnect.Unknown"/> for every Claude Code session and
    /// changes nothing else.
    /// </remarks>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="parentCommandLine">The command line of the process that started the relay, or <see langword="null"/>.</param>
    /// <param name="entrypoint">The value of <see cref="EntrypointVariable"/>, or <see langword="null"/>.</param>
    /// <param name="readsTheParent">Option d when <see langword="true"/>, option c when <see langword="false"/>.</param>
    /// <returns>The answer, <see cref="RelayReconnect.Unknown"/> whenever it cannot be told.</returns>
    internal static RelayReconnect Reconnect(string? clientName, string? parentCommandLine, string? entrypoint, bool readsTheParent)
    {
        if (string.Equals(clientName, Codex, StringComparison.Ordinal))
        {
            // exec and app-server alike: Codex never starts a server that has gone
            // again on the failure path, so its conversation needs a new one.
            return RelayReconnect.NewConversation;
        }

        if (!string.Equals(clientName, ClaudeCode, StringComparison.Ordinal) || !readsTheParent)
        {
            return RelayReconnect.Unknown;
        }

        if (parentCommandLine is { Length: > 0 })
        {
            var arguments = Split(parentCommandLine);

            // The first element is the program; only what follows it is an argument.
            var given = arguments.Skip(1).ToList();

            if (given.Exists(argument => argument is "-p" or "--print"))
            {
                return RelayReconnect.None;
            }

            if (!given.Exists(argument => Array.Exists(HeadlessMarks, mark => IsTheFlag(argument, mark))))
            {
                return RelayReconnect.McpReconnect;
            }

            return string.Equals(entrypoint, "claude-vscode", StringComparison.Ordinal)
                ? RelayReconnect.None
                : RelayReconnect.Unknown;
        }

        // The parent could not be read: the entrypoint, knowing that an inherited
        // value can name VS Code for a terminal.
        return entrypoint switch
        {
            "cli" => RelayReconnect.McpReconnect,
            "sdk-cli" or "claude-vscode" => RelayReconnect.None,
            _ => RelayReconnect.Unknown,
        };
    }

    /// <summary>Whether an argument is a flag, spelled alone or with its value after an equals sign.</summary>
    private static bool IsTheFlag(string argument, string flag) =>
        string.Equals(argument, flag, StringComparison.Ordinal)
        || argument.StartsWith(flag + "=", StringComparison.Ordinal);

    /// <summary>
    /// Splits a command line the way the C runtime and <c>CommandLineToArgvW</c> do:
    /// whitespace separates, double quotes group, and backslashes escape a quote only in
    /// front of one.
    /// </summary>
    /// <param name="commandLine">The command line.</param>
    /// <returns>Each argument, the program first.</returns>
    internal static List<string> Split(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var started = false;
        var index = 0;

        while (index < commandLine.Length)
        {
            var character = commandLine[index];

            if (character is '\\')
            {
                var slashes = 0;

                while (index < commandLine.Length && commandLine[index] is '\\')
                {
                    slashes++;
                    index++;
                }

                if (index < commandLine.Length && commandLine[index] is '"')
                {
                    _ = current.Append('\\', slashes / 2);

                    if (slashes % 2 is 1)
                    {
                        _ = current.Append('"');
                        index++;
                    }
                }
                else
                {
                    _ = current.Append('\\', slashes);
                }

                started = true;
                continue;
            }

            if (character is '"')
            {
                inQuotes = !inQuotes;
                started = true;
                index++;
                continue;
            }

            if (!inQuotes && character is ' ' or '\t')
            {
                if (started)
                {
                    arguments.Add(current.ToString());
                    _ = current.Clear();
                    started = false;
                }

                index++;
                continue;
            }

            _ = current.Append(character);
            started = true;
            index++;
        }

        if (started)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }
}
