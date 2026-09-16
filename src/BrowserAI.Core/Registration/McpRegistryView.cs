// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Registration;

/// <summary>Which of the client's two configuration files an entry is in.</summary>
internal enum RegistrationScope
{
    /// <summary>
    /// <c>~/.claude.json</c>: <i>"for all my Claude Code projects"</i>.
    /// </summary>
    User,

    /// <summary>
    /// <c>.mcp.json</c> at a project's root: <i>"in this project"</i>, committed
    /// with the repository and approved once per project by the client.
    /// </summary>
    Project,
}

/// <summary>
/// Whose registration an entry is, decided from its command and an install root.
/// </summary>
/// <remarks>
/// <b>The distinction the configuration app is built around.</b> An entry named
/// <c>browserai</c> that points somewhere we do not own is somebody else's — a
/// second install, a build from source, a path a person typed — and BrowserAI
/// neither adopts it, overwrites it nor deletes it. It says where it is and
/// refuses. Inferred intent is never acted on; only an explicit click edits
/// anything.
/// </remarks>
internal enum RegistrationOwnership
{
    /// <summary>There is no entry of that name in that scope.</summary>
    Absent,

    /// <summary>
    /// Ours, and the file it names is the MCP server.
    /// </summary>
    /// <remarks>
    /// <b>The server, not merely a file</b> — <i>narrowed 2026-09-16, previously
    /// "Ours, and the file it names is there".</i> The console subsystem is what
    /// decides it, because a pre-split entry naming
    /// <c>current\BrowserAI.exe</c> points at a file that exists and is the
    /// configuration app.
    /// </remarks>
    OursAndPresent,

    /// <summary>
    /// Ours — the command is under our install root — and the file it names
    /// cannot be launched as the MCP server: it is gone, or it is not a
    /// console-subsystem binary.
    /// </summary>
    /// <remarks>
    /// <b>Two causes, one state, because the action is the same</b> —
    /// <i>widened 2026-09-16, previously "but the file it names is not there any
    /// more. This is what an entry written by an older layout looks like after
    /// the server was renamed".</i> That sentence described half of what an
    /// older layout leaves behind: the other half is an entry naming
    /// <c>current\BrowserAI.exe</c>, which after the 2026-09-15 split is the
    /// configuration app and is still there. Both are re-pointed by
    /// <c>McpRegistrar.Repair</c> and both make the window offer <i>Register</i>;
    /// what differs is the sentence a person reads, which
    /// <c>AppState.StatusSentence</c> tells apart by asking whether the file is
    /// there at all.
    /// </remarks>
    OursAndStale,

    /// <summary>
    /// Somebody else's BrowserAI. Reported, never touched.
    /// </summary>
    Foreign,
}

/// <summary>
/// One scope's answer about <c>browserai</c>.
/// </summary>
/// <param name="Scope">Which file was read.</param>
/// <param name="File">The file, whether or not it exists.</param>
/// <param name="Command">The command the entry names, or <see langword="null"/>.</param>
/// <param name="Ownership">Whose it is.</param>
/// <param name="Unreadable">
/// Why the file could not be read, when that is the reason there is no answer.
/// <see langword="null"/> when the file was read — including when it simply is
/// not there, which is an answer rather than a failure.
/// </param>
internal sealed record RegistrationView(
    RegistrationScope Scope,
    string File,
    string? Command,
    RegistrationOwnership Ownership,
    string? Unreadable);

/// <summary>
/// Reads what the client has been told about <c>browserai</c>, and never writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-09-15 with the configuration app</b>, which has to be able to
/// show a state before anybody clicks anything. Writing still goes through the
/// client's own CLI — <c>claude mcp add</c> and <c>claude mcp remove</c> — for
/// the reason it always has: the file format is the client's, it has changed
/// before, and a hand-rolled splicer is a second implementation of somebody
/// else's schema that nobody will re-derive when it moves.
/// </para>
/// <para>
/// <b>Reading is a different trade from writing and that is why it is allowed
/// here.</b> A read that misunderstands the file shows the wrong sentence in a
/// dialog; a write that misunderstands it corrupts a person's configuration. The
/// first is recoverable by looking again and the second is not, so the read is
/// ours and the write is upstream's. <c>claude mcp get</c> would have been the
/// symmetrical answer and is rejected on cost: it starts a Node process and
/// health-checks every configured server, which on this machine takes seconds
/// and would make opening a window depend on five remote endpoints.
/// </para>
/// <para>
/// ⚠️ <b>A file that cannot be read is never reported as <i>not
/// registered</i>.</b> The two are different sentences and only one of them
/// licenses an offer to register. <see cref="RegistrationView.Unreadable"/>
/// carries the reason and the caller says so.
/// </para>
/// </remarks>
internal static class McpRegistryView
{
    /// <summary>The client's user-scope configuration file.</summary>
    public const string UserConfigFileName = ".claude.json";

    /// <summary>The client's project-scope configuration file.</summary>
    public const string ProjectConfigFileName = ".mcp.json";

    /// <summary>
    /// The variable that moves the client's configuration directory, which is
    /// what makes any of this testable without touching the user's own file.
    /// </summary>
    public const string ConfigDirectoryVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>The property holding the map of servers.</summary>
    private const string ServersProperty = "mcpServers";

    /// <summary>The property holding one server's executable.</summary>
    private const string CommandProperty = "command";

    /// <summary>
    /// Where the client keeps its user-scope configuration.
    /// </summary>
    /// <returns>The file's path, whether or not it exists.</returns>
    public static string UserConfigFile() =>
        Path.Combine(
            Environment.GetEnvironmentVariable(ConfigDirectoryVariable) is { Length: > 0 } overridden
                ? overridden
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            UserConfigFileName);

    /// <summary>Where a project keeps its committed configuration.</summary>
    /// <param name="projectDirectory">The project's root.</param>
    /// <returns>The file's path, whether or not it exists.</returns>
    public static string ProjectConfigFile(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        return Path.Combine(projectDirectory, ProjectConfigFileName);
    }

    /// <summary>Reads the user-scope entry.</summary>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <returns>What is there.</returns>
    public static RegistrationView User(string? installRoot) =>
        Read(RegistrationScope.User, UserConfigFile(), installRoot);

    /// <summary>Reads a project's entry.</summary>
    /// <param name="projectDirectory">The project's root.</param>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <returns>What is there.</returns>
    public static RegistrationView Project(string projectDirectory, string? installRoot) =>
        Read(RegistrationScope.Project, ProjectConfigFile(projectDirectory), installRoot);

    /// <summary>
    /// Reads one configuration file's <c>browserai</c> entry.
    /// </summary>
    /// <param name="scope">Which scope the file is.</param>
    /// <param name="file">The file to read.</param>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <returns>What is there.</returns>
    public static RegistrationView Read(RegistrationScope scope, string file, string? installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        string text;

        try
        {
            if (!System.IO.File.Exists(file))
            {
                return new RegistrationView(scope, file, null, RegistrationOwnership.Absent, null);
            }

            // FileShare.ReadWrite | Delete: the client may be holding this file
            // open, and a dialog that could not say what is registered because
            // somebody has a session running would be a dialog nobody trusts.
            using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096);
            using var reader = new StreamReader(stream);

            text = reader.ReadToEnd();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new RegistrationView(
                scope,
                file,
                null,
                RegistrationOwnership.Absent,
                $"'{file}' could not be read ({failure.Message}), so what is registered there is unknown. This is not the same as nothing being registered.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);

            if (!document.RootElement.TryGetProperty(ServersProperty, out var servers)
                || servers.ValueKind is not JsonValueKind.Object
                || !servers.TryGetProperty(McpClientRegistration.ServerName, out var entry)
                || entry.ValueKind is not JsonValueKind.Object
                || !entry.TryGetProperty(CommandProperty, out var command)
                || command.ValueKind is not JsonValueKind.String)
            {
                return new RegistrationView(scope, file, null, RegistrationOwnership.Absent, null);
            }

            var value = command.GetString();

            return new RegistrationView(scope, file, value, Classify(value, installRoot), null);
        }
        catch (JsonException failure)
        {
            return new RegistrationView(
                scope,
                file,
                null,
                RegistrationOwnership.Absent,
                $"'{file}' is not readable JSON ({failure.Message}), so what is registered there is unknown. BrowserAI will not edit a file it cannot read.");
        }
    }

    /// <summary>
    /// Whose a command is.
    /// </summary>
    /// <param name="command">What the entry names, or <see langword="null"/>.</param>
    /// <param name="installRoot">The install root ownership is judged against.</param>
    /// <returns>The classification.</returns>
    /// <remarks>
    /// <para>
    /// <b>Environment references are expanded before the comparison, and only
    /// for it.</b> A project-scope entry is written in the portable form
    /// <c>${LOCALAPPDATA}/BrowserAI.app/current/…</c> precisely so that it is
    /// right on a teammate's machine as well as this one; unexpanded, it would
    /// classify as foreign on the very machine that wrote it. What is stored is
    /// never rewritten — the expansion exists to answer a question, not to
    /// produce a value.
    /// </para>
    /// <para>
    /// <b>Prefix, not equality.</b> An entry under our root naming a file that
    /// no longer exists is still ours, and telling that from somebody else's is
    /// the whole difference between repairing and trampling.
    /// </para>
    /// </remarks>
    public static RegistrationOwnership Classify(string? command, string? installRoot)
    {
        if (command is not { Length: > 0 })
        {
            return RegistrationOwnership.Absent;
        }

        if (installRoot is not { Length: > 0 })
        {
            // No install root to judge against. Anything present is somebody
            // else's as far as this process is concerned, which is the reading
            // that touches nothing.
            return RegistrationOwnership.Foreign;
        }

        var expanded = Expand(command);

        string full;
        string root;

        try
        {
            full = Path.GetFullPath(expanded);
            root = Path.GetFullPath(installRoot);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return RegistrationOwnership.Foreign;
        }

        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return RegistrationOwnership.Foreign;
        }

        // ⚠️ PRESENT MEANS *THE SERVER*, NOT *A FILE* — 2026-09-16. Until this
        // day the answer here was `File.Exists(full)`, and that was right for
        // exactly as long as this product shipped one executable. Every
        // registration written before the 2026-09-15 split names
        // `current\BrowserAI.exe`, which is now the CONFIGURATION APP — so in an
        // install that has been updated the file is there, existence answers
        // "ours and present", `McpRegistrar.Repair` leaves it exactly as it is
        // by design, and the client starts a window and waits forever for a
        // handshake from a process that is showing a dialog. There is nothing in
        // any log, because nothing failed.
        //
        // The subsystem is the same discriminator `RegistrationTarget` uses when
        // it composes the path in the first place, and it is the one a rename
        // cannot fake: subsystem 3 is always given a console and subsystem 2
        // never is. Anything else — the app, a text file wearing the name, a
        // file that cannot be read — is OURS AND STALE, which is the state
        // `Repair` re-points and the state the window offers to register out of.
        // Neither may be launched as an MCP server, and the difference between
        // "gone" and "wrong" changes the sentence rather than the action.
        return Runtime.PeSubsystem.IsConsole(full)
            ? RegistrationOwnership.OursAndPresent
            : RegistrationOwnership.OursAndStale;
    }

    /// <summary>
    /// Expands the <c>${VAR}</c> references the client itself expands.
    /// </summary>
    /// <param name="command">The stored command.</param>
    /// <returns>The command with references replaced by their values.</returns>
    /// <remarks>
    /// <b>Only the braced form, because that is the form the client documents
    /// and the form BrowserAI writes.</b> A bare <c>%VAR%</c> is Windows shell
    /// syntax the client does not expand, so expanding it here would classify a
    /// path the client cannot launch as one it can.
    /// </remarks>
    public static string Expand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Contains("${", StringComparison.Ordinal))
        {
            return command;
        }

        var result = new System.Text.StringBuilder(command.Length);

        for (var index = 0; index < command.Length;)
        {
            var open = command.IndexOf("${", index, StringComparison.Ordinal);

            if (open < 0)
            {
                _ = result.Append(command, index, command.Length - index);
                break;
            }

            var close = command.IndexOf('}', open + 2);

            if (close < 0)
            {
                _ = result.Append(command, index, command.Length - index);
                break;
            }

            _ = result.Append(command, index, open - index);

            var name = command[(open + 2)..close];

            _ = result.Append(Environment.GetEnvironmentVariable(name) ?? command[open..(close + 1)]);

            index = close + 1;
        }

        return result.ToString();
    }
}
