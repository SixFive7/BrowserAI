// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Registration;

/// <summary>
/// ⚠️ <b>THE ONE PLACE THAT DECIDES HOW BROWSERAI IS REGISTERED WITH CODEX.</b>
/// The sibling of <see cref="McpClientRegistration"/>, and deliberately a
/// sibling: the two clients agree about almost nothing except that each owns its
/// own configuration and neither will let this product parse it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's ask, 2026-09-24, verbatim:</b> <i>"I want the system level
/// and repo level registration to also work for codex and not only for claude
/// code."</i> Q258, decided as <i>the recommendation minus the plugin</i>.
/// </para>
/// <para>
/// <b>The mechanism is the client's own supported command, exactly as it is for
/// Claude Code: <c>codex mcp add</c>.</b> It writes
/// <c>[mcp_servers.&lt;name&gt;]</c> into <c>$CODEX_HOME/config.toml</c> as TOML
/// literal strings, and BrowserAI never reads or merges that file --
/// <see cref="CodexRegistryView"/> asks <c>codex mcp list --json</c> instead, so
/// the charter's shape holds on this client too: <b>BrowserAI reads its own state
/// and hands the client its own command.</b>
/// </para>
/// <para>
/// ⚠️ <b>THERE IS NO SCOPE FLAG, AND THAT IS THE ONE STRUCTURAL DIFFERENCE FROM
/// CLAUDE CODE.</b> <c>codex mcp add</c> writes to whatever
/// <c>$CODEX_HOME</c> points at and takes no <c>--scope</c>. So user scope is the
/// default home and <b>project scope is the same command with the environment
/// variable moved</b> -- <c>CODEX_HOME=&lt;repo&gt;\.codex codex mcp add ...</c>,
/// which makes Codex write <c>&lt;repo&gt;\.codex\config.toml</c> itself.
/// Measured 2026-09-24 @ codex-cli 0.155.0-alpha.9.2: the directory must exist
/// first, and the run leaves a <c>&lt;repo&gt;\.codex\tmp\arg0</c> residue that
/// <see cref="ProjectResidue"/> names and the registrar removes.
/// </para>
/// <para>
/// <b>Idempotent, measured both ways.</b> Three consecutive <c>mcp add</c> calls
/// all exit 0, and <c>mcp remove</c> of a server that is not there also exits 0
/// -- so unlike Claude Code, neither operation needs an already-exists or a
/// nothing-to-remove needle. The two predicates below exist anyway and answer
/// <see langword="false"/> always, with that stated in place, because the
/// registrar asks both clients the same questions and a client that answers them
/// differently must say so here, and not leave the caller to know it.
/// </para>
/// <para>
/// <b>What was rejected, and one of them is a closer call here than it was for
/// Claude Code.</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Writing <c>config.toml</c> directly.</b> Still rejected -- but honestly, it
/// is closer for Codex: the file is small, TOML has no merge semantics to speak
/// of, and the client writes it in a shape this product could reproduce. What
/// keeps it rejected is the same thing that keeps it rejected for Claude Code and
/// one thing more: it is a file the maintainer edits, and a background installer
/// that rewrites a hand-edited file has to own TOML comment and ordering
/// preservation forever.
/// </description></item>
/// <item><description>
/// <b>A Codex plugin.</b> Rejected by the maintainer in the same decision. It
/// would put BrowserAI inside a second distribution channel with its own
/// marketplace, versioning and trust model, for a registration the CLI already
/// performs in one command.
/// </description></item>
/// <item><description>
/// <b>The app-server protocol.</b> Recorded as the migration target and not built:
/// <c>codex app-server</c> is where a host-driven <c>config/mcpServer/reload</c>
/// lives, which is the one call measured to recover a dead server, and it is what
/// a future BrowserAI that wanted to repair its own registration live would
/// speak. Nothing needs it today.
/// </description></item>
/// </list>
/// </remarks>
internal static class CodexRegistration
{
    /// <summary>The server key, and therefore the prefix the model sees.</summary>
    /// <remarks>
    /// The same name Claude Code is given, on purpose: a caller reading two
    /// clients' configurations should see one product and not two.
    /// </remarks>
    public const string ServerName = McpClientRegistration.ServerName;

    /// <summary>The client's executable, by file name only.</summary>
    /// <remarks>
    /// An <c>.exe</c> for the reason
    /// <see cref="McpClientRegistration.ClientExecutable"/> gives at length: a
    /// <c>.cmd</c> shim runs under <c>cmd.exe</c> whether or not the caller asked
    /// for a shell, and it mangles exactly the arguments a registered path
    /// carries.
    /// </remarks>
    public const string ClientExecutable = "codex.exe";

    /// <summary>The environment variable that decides which configuration is written.</summary>
    public const string HomeVariable = "CODEX_HOME";

    /// <summary>The per-repository configuration directory, relative to the repository root.</summary>
    public const string ProjectDirectoryName = ".codex";

    /// <summary>The configuration file Codex writes, inside whichever home it is pointed at.</summary>
    public const string ConfigFileName = "config.toml";

    /// <summary>
    /// The file the CLI leaves behind in a project home, which the registrar
    /// removes.
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-24 @ codex-cli 0.155.0-alpha.9.2: a run with
    /// <c>CODEX_HOME</c> pointed at a repository's <c>.codex</c> creates
    /// <c>tmp\arg0</c> under it. It is harmless and it is not ours to leave in
    /// somebody's repository, so it is cleaned up and named here, and not left for
    /// whoever next runs <c>git status</c>.
    /// <para>
    /// ⚠️ <b>Both halves of that path are DIRECTORIES, and both were empty --
    /// measured again 2026-09-24 at 08:20Z</b> against a scratch project, after an
    /// earlier run the same day had not produced them at all. So it is removed
    /// innermost first and only while empty; see
    /// <see cref="McpRegistrar.ApplyToProject"/>.
    /// </para>
    /// </remarks>
    public static string ProjectResidue { get; } = Path.Combine("tmp", "arg0");

    /// <summary>
    /// How long one registration call gets.
    /// </summary>
    /// <remarks>
    /// <b>The same budget Claude Code gets</b>, and for the same reason: every
    /// registration call happens inside a Velopack hook whose own timeout is 15 s
    /// at its tightest, so a client that has not answered in ten seconds has to be
    /// reported and not waited for.
    /// </remarks>
    public static TimeSpan Budget => McpClientRegistration.Budget;

    /// <summary>The arguments that register the server in whichever home is in force.</summary>
    /// <param name="command">The absolute path to the server executable.</param>
    /// <returns>The argument list.</returns>
    /// <exception cref="ArgumentException">The command is empty.</exception>
    public static IReadOnlyList<string> AddArguments(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        // ⚠️ THE `--` IS LOAD-BEARING AND IS THE SAME TRAP CLAUDE CODE HAS. What
        // follows it is the command and its arguments, and without it a path
        // beginning with a dash -- or any future flag Codex adds -- would be
        // parsed as one of the CLI's own options.
        return ["mcp", "add", ServerName, "--", command];
    }

    /// <summary>The arguments that unregister the server.</summary>
    /// <returns>The argument list.</returns>
    public static IReadOnlyList<string> RemoveArguments() => ["mcp", "remove", ServerName];

    /// <summary>The arguments that list what is registered, as JSON.</summary>
    /// <returns>The argument list.</returns>
    public static IReadOnlyList<string> ListArguments() => ["mcp", "list", "--json"];

    /// <summary>The arguments that report one server, as JSON.</summary>
    /// <returns>The argument list.</returns>
    public static IReadOnlyList<string> GetArguments() => ["mcp", "get", ServerName, "--json"];

    /// <summary>
    /// Whether an <c>mcp add</c> failure means the server was already there.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ALWAYS FALSE, AND MEASURED SO.</b> <c>codex mcp add</c> is idempotent
    /// -- three consecutive adds exited 0 at codex-cli 0.155.0-alpha.9.2 -- so
    /// there is no already-exists failure to recognise. This exists because the
    /// registrar asks both clients the same question, and a client whose answer is
    /// <i>that cannot happen</i> should say so here, and not
    /// leave the caller to know it.
    /// </remarks>
    /// <param name="exitCode">What the client exited with.</param>
    /// <param name="output">What it said.</param>
    /// <returns><see langword="false"/>.</returns>
    public static bool MeansAlreadyRegistered(int exitCode, string output) => false;

    /// <summary>
    /// Whether an <c>mcp remove</c> failure means there was nothing to remove.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ALWAYS FALSE, AND MEASURED SO.</b> Removing a server that is not
    /// there exits 0. See <see cref="MeansAlreadyRegistered"/> for why the
    /// predicate exists at all.
    /// </remarks>
    /// <param name="exitCode">What the client exited with.</param>
    /// <param name="output">What it said.</param>
    /// <returns><see langword="false"/>.</returns>
    public static bool MeansNothingToRemove(int exitCode, string output) => false;

    /// <summary>The command a person can run by hand, when the client was not found.</summary>
    /// <remarks>
    /// ⚠️ <b>There is no project-scope sibling, and there was one until
    /// 2026-09-24.</b> <c>ManualProjectCommandFor</c> put
    /// <c>set CODEX_HOME=&lt;repo&gt;\.codex &amp;&amp;</c> in front of this line.
    /// Nothing called it, and the line was only right in <c>cmd.exe</c>: pasted into
    /// PowerShell, <c>set</c> is <c>Set-Variable</c>, the variable never reaches
    /// <c>codex</c>, and the add writes the person's own
    /// <c>~\.codex\config.toml</c> instead of the repository's. A line that does
    /// the opposite of what it says in the shell most people here use is deleted,
    /// not fixed, because nothing needs it.
    /// </remarks>
    /// <param name="command">The absolute path to the server executable.</param>
    /// <returns>One line, copy-pasteable.</returns>
    public static string ManualCommandFor(string command) =>
        $"codex mcp add {ServerName} -- \"{command}\"";

    /// <summary>
    /// What a Codex project file is given: the server's bare name, and which file
    /// that name finds on the PATH today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Q294, decided 2026-09-24 by the maintainer, verbatim: <i>"Q294 b"</i>. A
    /// Codex project entry names <c>BrowserAI.Server.exe</c> alone, never an absolute
    /// path.</b> Codex expands no variable in a command (0 of 48, measured), so no
    /// spelling of this install's path resolves on a teammate's machine, and a bare
    /// name does: Codex resolves it with <c>which</c> over the PATH it hands the
    /// server, and every BrowserAI install puts its own folder on the user's PATH
    /// (<see cref="UserPath"/>).
    /// </para>
    /// <para>
    /// <b>The sentence names what the name finds, and that it may take a restart</b>:
    /// a Codex process started before the install carries a PATH without the folder,
    /// which follows from how the launcher builds the server's environment and was
    /// not measured.
    /// </para>
    /// </remarks>
    /// <param name="server">This install's server, absolute.</param>
    /// <param name="installRoot">This install's root, or <see langword="null"/>.</param>
    /// <returns>The command and the sentence.</returns>
    public static ProjectCommand ProjectCommandFor(string server, string? installRoot) =>
        ProjectCommandGiven(server, UserPath.Resolve(RegistrationTarget.ServerFileName, UserPath.SearchDirectories()));

    /// <summary>The same, given what the name finds on the PATH today.</summary>
    /// <param name="server">This install's server, absolute.</param>
    /// <param name="found">The file the bare name resolves to, or <see langword="null"/>.</param>
    /// <returns>The command and the sentence.</returns>
    public static ProjectCommand ProjectCommandGiven(string server, string? found)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        const string How = "The entry names BrowserAI.Server.exe and no folder, because Codex expands no variable in a command; Codex finds it on the PATH it gives the server.";

        var note = found is null
            ? $"{How} No folder on your PATH holds one yet. BrowserAI's installer puts its own there, so install BrowserAI on this machine, or put the folder that holds it on your PATH."
            : string.Equals(Path.GetFullPath(found), Path.GetFullPath(server), StringComparison.OrdinalIgnoreCase)
                ? $"{How} It finds this install. A Codex that was already running before BrowserAI was installed may need to be restarted to see it."
                : $"{How} The first one on your PATH is '{found}', which is not this install.";

        return new ProjectCommand(RegistrationTarget.ServerFileName, note);
    }

    /// <summary>The home directory a repository-scoped registration is written into.</summary>
    /// <param name="projectDirectory">The repository root.</param>
    /// <returns>The directory, which the caller must create before running the client.</returns>
    /// <exception cref="ArgumentException">The directory is empty.</exception>
    public static string ProjectHome(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        return Path.Combine(projectDirectory, ProjectDirectoryName);
    }

    /// <summary>
    /// Where the CLI is, in the order this machine's shapes were measured in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three shapes, and the middle one is the reason this method exists.</b>
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>PATH, and the same fallback directory Claude Code uses.</b> An npm or a
    /// Homebrew-style install puts <c>codex.exe</c> somewhere a process can find
    /// it, and asking the ordinary locator first means the ordinary case costs
    /// nothing.
    /// </description></item>
    /// <item><description>
    /// ⚠️ <b>The desktop app's own manifest, because a desktop install puts the CLI
    /// NOWHERE ON PATH.</b> Measured 2026-09-24:
    /// <c>%LOCALAPPDATA%\OpenAI\Codex\chrome-native-hosts-v2.json</c> carries
    /// <c>entries[].paths.codexCliPath</c>, pointing at
    /// <c>~\.codex\plugins\.plugin-appserver\codex.exe</c>. Without this shape a
    /// machine with Codex installed reports no client, which is the failure that
    /// reads as <i>BrowserAI does not support Codex</i>.
    /// </description></item>
    /// <item><description>
    /// <b>npm's own layout</b>, <c>%APPDATA%\npm\node_modules\@openai\codex</c>,
    /// for an install whose shim is not on this process's PATH.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>It returns a path and never a shim.</b> Every candidate is an
    /// <c>.exe</c>; see <see cref="ClientExecutable"/>.
    /// </para>
    /// </remarks>
    /// <param name="commands">The process runner, which owns the PATH search.</param>
    /// <returns>The absolute path, or <see langword="null"/> when no shape found one.</returns>
    /// <exception cref="ArgumentNullException">The runner is null.</exception>
    public static string? Locate(IRegistrationCommand commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Locate(ClientExecutable) is { Length: > 0 } onPath)
        {
            return onPath;
        }

        if (FromDesktopManifest() is { Length: > 0 } fromManifest)
        {
            return fromManifest;
        }

        return FromNpmLayout();
    }

    /// <summary>What to say when no shape found a client.</summary>
    /// <param name="command">The absolute path to the server executable.</param>
    /// <returns>One sentence naming every place that was looked and what to run instead.</returns>
    public static string NotFoundDetail(string command) =>
        $"No '{ClientExecutable}' was found on PATH, at '{ClientCommandLine.FallbackDirectory}', "
        + $"in the Codex desktop manifest at '{DesktopManifestFile}', or under '{NpmPackageDirectory}', "
        + $"so BrowserAI has not registered itself with Codex. Install the Codex CLI and run: {ManualCommandFor(command)}";

    /// <summary>The desktop app's native-host manifest, which names the CLI.</summary>
    public static string DesktopManifestFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "OpenAI",
        "Codex",
        "chrome-native-hosts-v2.json");

    /// <summary>Where an npm global install puts the package.</summary>
    public static string NpmPackageDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "npm",
        "node_modules",
        "@openai",
        "codex");

    /// <summary>The CLI path the desktop manifest names, if it names one that exists.</summary>
    /// <returns>The path, or <see langword="null"/>.</returns>
    private static string? FromDesktopManifest()
    {
        try
        {
            if (!File.Exists(DesktopManifestFile))
            {
                return null;
            }

            using var stream = new FileStream(
                DesktopManifestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind is not JsonValueKind.Array)
            {
                return null;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind is JsonValueKind.Object
                    && entry.TryGetProperty("paths", out var paths)
                    && paths.ValueKind is JsonValueKind.Object
                    && paths.TryGetProperty("codexCliPath", out var cli)
                    && cli.ValueKind is JsonValueKind.String
                    && cli.GetString() is { Length: > 0 } candidate
                    && File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            // A manifest this process cannot read is not a client. The refusal
            // NotFoundDetail produces names this file, so a reader who has Codex
            // installed knows where to look.
            return null;
        }
    }

    /// <summary>The CLI inside an npm global install, if one is there.</summary>
    /// <returns>The path, or <see langword="null"/>.</returns>
    private static string? FromNpmLayout()
    {
        try
        {
            if (!Directory.Exists(NpmPackageDirectory))
            {
                return null;
            }

            foreach (var candidate in Directory.EnumerateFiles(NpmPackageDirectory, ClientExecutable, SearchOption.AllDirectories))
            {
                return Path.GetFullPath(candidate);
            }

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
