// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Interop;
using BrowserAI.Updates;

namespace BrowserAI.Relay;

/// <summary>Which kind of client started a relay, by option d's rule (2026-10-08).</summary>
/// <remarks>
/// The five the measurement told apart, 41 of 41, with Codex's two as one because both
/// need the same after an update, and a Claude Code whose mode cannot be told.
/// </remarks>
internal enum ClientKind
{
    /// <summary>A client this build does not know, or one that did not say what it is.</summary>
    Unknown,

    /// <summary>Claude Code whose mode cannot be told: option c, or a host its parent and its entrypoint do not name.</summary>
    ClaudeCode,

    /// <summary>Claude Code's terminal UI.</summary>
    ClaudeCodeTerminal,

    /// <summary>Claude Code in VS Code: one tab of a window, started by that window's extension host.</summary>
    ClaudeCodeVsCode,

    /// <summary><c>claude -p</c>.</summary>
    ClaudeCodePrint,

    /// <summary>Codex, in every mode measured.</summary>
    Codex,
}

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
/// <para>
/// <b>Added 2026-10-10 by addition: the kind is named, and the conversation is read
/// beside it.</b> The maintainer's answer that day, verbatim: <i>"1.1-2.3 I accept all
/// your recommendations"</i>. The rule above decides a <see cref="ClientKind"/> first and
/// the reconnect from the kind, with every answer unchanged, because a VS Code tab is
/// the one kind whose window the relay reads (1.5 a). <see cref="Read(string?, ParentReading?, Func{string, string?})"/> adds what the
/// background needs to name the conversation (1.1 c, 1.4 a): the places the client keeps
/// its records, from the environment the client handed the relay, and the client's own
/// creation time, from its parent.
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

    /// <summary>The variable that moves Claude Code's configuration folder away from <c>%USERPROFILE%\.claude</c>.</summary>
    public const string ConfigFolderVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>
    /// The variable Claude Code sets for every server it starts to the session it serves
    /// at that moment, 54 of 54 in the measurement of 2026-10-08, and does not change at
    /// <c>/clear</c>, 6 of 6.
    /// </summary>
    public const string SessionIdVariable = "CLAUDE_CODE_SESSION_ID";

    /// <summary>The variable that names the user's profile folder, under which both clients keep their records by default.</summary>
    public const string ProfileVariable = "USERPROFILE";

    /// <summary>The options that name a session on Claude Code's command line, each with its id after it or after an equals sign.</summary>
    private static readonly string[] SessionOptions = ["--resume", "-r", "--session-id"];

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
    internal static RelayReconnect Reconnect(string? clientName, string? parentCommandLine, string? entrypoint, bool readsTheParent) =>
        ReconnectOf(KindOf(clientName, parentCommandLine, entrypoint, readsTheParent));

    /// <summary>Which kind of client started the relay, by option d's rule, under the product's choice of option.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="parentCommandLine">The command line of the process that started the relay, or <see langword="null"/>.</param>
    /// <param name="entrypoint">The value of <see cref="EntrypointVariable"/>, or <see langword="null"/>.</param>
    /// <returns>The kind, <see cref="ClientKind.ClaudeCode"/> for a Claude Code whose mode cannot be told.</returns>
    public static ClientKind KindOf(string? clientName, string? parentCommandLine, string? entrypoint) =>
        KindOf(clientName, parentCommandLine, entrypoint, ReadsTheParent);

    /// <summary>Which kind of client started the relay, under either of the measurement's options.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="parentCommandLine">The command line of the process that started the relay, or <see langword="null"/>.</param>
    /// <param name="entrypoint">The value of <see cref="EntrypointVariable"/>, or <see langword="null"/>.</param>
    /// <param name="readsTheParent">Option d when <see langword="true"/>; option c, which tells no Claude Code mode, when <see langword="false"/>.</param>
    /// <returns>The kind.</returns>
    internal static ClientKind KindOf(string? clientName, string? parentCommandLine, string? entrypoint, bool readsTheParent)
    {
        if (string.Equals(clientName, Codex, StringComparison.Ordinal))
        {
            // exec and app-server alike: Codex never starts a server that has gone
            // again on the failure path, so its conversation needs a new one.
            return ClientKind.Codex;
        }

        if (!string.Equals(clientName, ClaudeCode, StringComparison.Ordinal))
        {
            return ClientKind.Unknown;
        }

        if (!readsTheParent)
        {
            return ClientKind.ClaudeCode;
        }

        if (parentCommandLine is { Length: > 0 })
        {
            var arguments = Split(parentCommandLine);

            // The first element is the program; only what follows it is an argument.
            var given = arguments.Skip(1).ToList();

            if (given.Exists(argument => argument is "-p" or "--print"))
            {
                return ClientKind.ClaudeCodePrint;
            }

            if (!given.Exists(argument => Array.Exists(HeadlessMarks, mark => IsTheFlag(argument, mark))))
            {
                return ClientKind.ClaudeCodeTerminal;
            }

            return string.Equals(entrypoint, "claude-vscode", StringComparison.Ordinal)
                ? ClientKind.ClaudeCodeVsCode
                : ClientKind.ClaudeCode;
        }

        // The parent could not be read: the entrypoint, knowing that an inherited
        // value can name VS Code for a terminal.
        return entrypoint switch
        {
            "cli" => ClientKind.ClaudeCodeTerminal,
            "sdk-cli" => ClientKind.ClaudeCodePrint,
            "claude-vscode" => ClientKind.ClaudeCodeVsCode,
            _ => ClientKind.ClaudeCode,
        };
    }

    /// <summary>What a kind of client needs once an update has ended its relay (H1-T a).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The answer, <see cref="RelayReconnect.Unknown"/> for a kind that cannot be told.</returns>
    public static RelayReconnect ReconnectOf(ClientKind kind) => kind switch
    {
        ClientKind.ClaudeCodeTerminal => RelayReconnect.McpReconnect,
        ClientKind.ClaudeCodeVsCode or ClientKind.ClaudeCodePrint => RelayReconnect.None,
        ClientKind.Codex => RelayReconnect.NewConversation,
        _ => RelayReconnect.Unknown,
    };

    /// <summary>
    /// Everything the relay reads about its client once the client has said who it is:
    /// what it needs after an update, where it keeps the conversation, and for a VS Code
    /// tab, its window.
    /// </summary>
    /// <remarks>
    /// <b>Called off the relay's loop, after the handshake is answered</b> (D6 a), so the
    /// client's first turn waits on none of it. It reads four variables of the
    /// environment the client handed the relay, each by name, and enumerates nothing:
    /// <see cref="EntrypointVariable"/>, <see cref="ConfigFolderVariable"/>,
    /// <see cref="SessionIdVariable"/> and <see cref="ProfileVariable"/>.
    /// </remarks>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="parent">The process that started the relay, or <see langword="null"/> when it could not be read.</param>
    /// <param name="variable">The client's environment, read one variable at a time.</param>
    /// <returns>The reading.</returns>
    public static ClientReading Read(string? clientName, ParentReading? parent, Func<string, string?> variable) =>
        Read(clientName, parent, variable, ReadsTheParent);

    /// <summary>Everything the relay reads about its client, under either of the measurement's options.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="parent">The process that started the relay, or <see langword="null"/>.</param>
    /// <param name="variable">The client's environment, read one variable at a time.</param>
    /// <param name="readsTheParent">Option d when <see langword="true"/>, option c when <see langword="false"/>.</param>
    /// <returns>The reading.</returns>
    internal static ClientReading Read(string? clientName, ParentReading? parent, Func<string, string?> variable, bool readsTheParent)
    {
        ArgumentNullException.ThrowIfNull(variable);

        var kind = KindOf(clientName, readsTheParent ? parent?.CommandLine : null, variable(EntrypointVariable), readsTheParent);

        // 1.5 a: a VS Code tab's Claude Code was started by its window's extension host,
        // so the parent's parent is the window. No other kind is grouped.
        var window = kind is ClientKind.ClaudeCodeVsCode && parent?.Parent is { } host
            ? string.Create(CultureInfo.InvariantCulture, $"{host.ProcessId}-{host.CreatedFileTime}")
            : null;

        return new ClientReading(ReconnectOf(kind), ConversationOf(clientName, parent, variable), window);
    }

    /// <summary>Where a client keeps the conversation the relay serves, as far as the relay can say.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="parent">The process that started the relay, or <see langword="null"/>.</param>
    /// <param name="variable">The client's environment, read one variable at a time.</param>
    /// <returns>The facts, or <see langword="null"/> for a client whose records BrowserAI does not read.</returns>
    internal static ConversationFacts? ConversationOf(string? clientName, ParentReading? parent, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (string.Equals(clientName, Codex, StringComparison.Ordinal))
        {
            // 1.4 a: CODEX_HOME never reaches a server, measured 21 of 21, so the home is
            // the default one.
            return ProfileOf(variable) is { } profile
                ? new ConversationFacts(null, null, null, null, Path.Combine(profile, ".codex"))
                : null;
        }

        if (!string.Equals(clientName, ClaudeCode, StringComparison.Ordinal))
        {
            return null;
        }

        return new ConversationFacts(
            ConfigFolderOf(variable),
            parent?.CreatedFileTime,
            SessionIds.Valid(variable(SessionIdVariable)),
            SessionIdOnCommandLine(parent?.CommandLine),
            CodexHome: null);
    }

    /// <summary>
    /// The session a Claude Code command line names with <c>--resume</c>, <c>-r</c> or
    /// <c>--session-id</c>, spelled with a space or an equals sign.
    /// </summary>
    /// <remarks>
    /// <b>None at all when <c>--fork-session</c> is present</b>: a fork starts a new
    /// session, and the id on its command line is the one it forked from, 6 of 6 in the
    /// measurement of 2026-10-08.
    /// </remarks>
    /// <param name="commandLine">The command line, the program first, or <see langword="null"/>.</param>
    /// <returns>The session id, or <see langword="null"/>.</returns>
    internal static string? SessionIdOnCommandLine(string? commandLine)
    {
        if (commandLine is not { Length: > 0 })
        {
            return null;
        }

        var given = Split(commandLine).Skip(1).ToList();

        if (given.Exists(argument => IsTheFlag(argument, "--fork-session")))
        {
            return null;
        }

        for (var index = 0; index < given.Count; index++)
        {
            foreach (var option in SessionOptions)
            {
                var value = string.Equals(given[index], option, StringComparison.Ordinal) && index + 1 < given.Count
                    ? given[index + 1]
                    : given[index].StartsWith(option + "=", StringComparison.Ordinal)
                        ? given[index][(option.Length + 1)..]
                        : null;

                if (SessionIds.Valid(value) is { } id)
                {
                    return id;
                }
            }
        }

        return null;
    }

    /// <summary>Claude Code's configuration folder: <see cref="ConfigFolderVariable"/>, or <c>.claude</c> in the profile.</summary>
    /// <param name="variable">The client's environment.</param>
    /// <returns>The folder, or <see langword="null"/> when neither can be read.</returns>
    private static string? ConfigFolderOf(Func<string, string?> variable)
    {
        if (variable(ConfigFolderVariable) is { Length: > 0 } configured)
        {
            try
            {
                return Path.GetFullPath(configured);
            }
            catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        return ProfileOf(variable) is { } profile ? Path.Combine(profile, ".claude") : null;
    }

    /// <summary>The user's profile folder, as the client's environment names it, or as Windows does when it does not.</summary>
    /// <param name="variable">The client's environment.</param>
    /// <returns>The folder, or <see langword="null"/>.</returns>
    private static string? ProfileOf(Func<string, string?> variable) =>
        variable(ProfileVariable) is { Length: > 0 } named ? named
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } known ? known
        : null;

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
